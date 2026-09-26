using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kinesthetic
{
    public class PoseReplay : MonoBehaviour
    {
        public PoseRig rig;
        public Renderer target;
        public Camera sceneCamera;
        public float testTargetDegrees = 60;
        public PoseFrame CurrentFrame { get; private set; }
        public bool ShoulderValid { get; private set; }
        public bool TargetReached { get; private set; }
        public string LastStatus { get; private set; } = "Load a local capture export.";
        PoseCapture capture;
        double positionMs;
        bool playing;
        int frameIndex;
        Label statusLabel, trackingLabel, angleLabel, sourceLabel;
        Slider timeline;
        TextField path;
        Button playButton;
        Button liveButton;
        UIDocument document;
        LivePoseClient live;
        long liveTicks, liveSequence = -1;
        string liveSession;
        MaterialPropertyBlock targetProperties;
        public bool LiveConnected => live?.Connected == true;

        void Start()
        {
            Application.runInBackground = true;
            rig.Initialize();
            rig.Apply(null);
            document = GetComponent<UIDocument>();
            var root = document.rootVisualElement;
            statusLabel = root.Q<Label>("status"); trackingLabel = root.Q<Label>("tracking");
            angleLabel = root.Q<Label>("angles"); sourceLabel = root.Q<Label>("source");
            timeline = root.Q<Slider>("timeline"); path = root.Q<TextField>("capturePath");
            playButton = root.Q<Button>("play");
            root.Q<Button>("load").clicked += () => LoadFile(path.value);
            playButton.clicked += () => { if (capture == null || live != null) return; playing = !playing; UpdateUI(); };
            liveButton = root.Q<Button>("live");
            liveButton.clicked += () => { if (live == null) ConnectLive(); else DisconnectLive(); };
            root.Q<Button>("restart").clicked += () => Seek(0);
            root.Q<Button>("view").clicked += ToggleView;
            timeline.RegisterValueChangedCallback(e => { if (capture != null && live == null) Seek(e.newValue); });
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-captureJson") { path.value = args[i + 1]; LoadFile(args[i + 1]); }
            UpdateUI();
        }
        public void LoadFile(string file)
        {
            DisconnectLive(); capture = null; playing = false; ApplyFrame(null);
            try
            {
                if (path != null) path.SetValueWithoutNotify(file);
                var candidate = ReadRecording(file);
                if (candidate?.schemaVersion != "kinesthetic.pose-capture.v1" || candidate.frames == null || candidate.frames.Length == 0)
                    throw new InvalidDataException("Expected a nonempty Kinesthetic pose export.");
                for (int i = 0; i < candidate.frames.Length; i++)
                {
                    var f = candidate.frames[i];
                    if (f == null || double.IsNaN(f.sourceMediaTimeMs) || double.IsInfinity(f.sourceMediaTimeMs) ||
                        (i > 0 && f.sourceMediaTimeMs <= candidate.frames[i - 1].sourceMediaTimeMs))
                        throw new InvalidDataException("Replay requires increasing source timestamps; export a continuous clip.");
                }
                capture = candidate; frameIndex = 0; positionMs = 0; playing = true;
                if (timeline != null) timeline.highValue = (float)DurationMs;
                LastStatus = "Recorded capture · original source timing";
                ApplyAtTime(); UpdateUI();
            }
            catch (Exception e) { LastStatus = "Could not load: " + e.Message; playing = false; UpdateUI(); }
        }
        static PoseCapture ReadRecording(string file)
        {
            if (!file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                return PoseJson.Read<PoseCapture>(File.ReadAllText(file));
            var frames = new List<PoseFrame>();
            string sessionId = null;
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var envelope = PoseJson.Read<PoseEnvelope>(line);
                if (envelope?.schemaVersion != "kinesthetic.session.v1" || envelope.type != "pose.frame" || envelope.payload == null)
                    throw new InvalidDataException("Invalid recorded pose envelope.");
                sessionId ??= envelope.sessionId;
                if (sessionId != envelope.sessionId) throw new InvalidDataException("Recording contains multiple sessions.");
                frames.Add(envelope.payload);
            }
            return new PoseCapture {schemaVersion="kinesthetic.pose-capture.v1",sessionID=sessionId,frames=frames.ToArray()};
        }
        double DurationMs => capture == null ? 0 : capture.frames[^1].sourceMediaTimeMs - capture.frames[0].sourceMediaTimeMs;
        public void Seek(double milliseconds)
        {
            if (capture == null || live != null) return;
            positionMs = Math.Clamp(milliseconds, 0, DurationMs);
            frameIndex = 0; ApplyAtTime(); UpdateUI();
        }
        void Update()
        {
            if (live != null) { UpdateLive(); return; }
            if (capture == null || !playing) return;
            positionMs = Math.Min(DurationMs, positionMs + Time.unscaledDeltaTime * 1000);
            ApplyAtTime();
            if (positionMs >= DurationMs) playing = false;
            UpdateUI();
        }
        public void ConnectLive()
        {
            DisconnectLive(); playing = false; liveTicks = 0; liveSequence = -1; liveSession = null;
            ApplyFrame(null);
            live = new LivePoseClient("ws://127.0.0.1:8766/pose?role=viewer");
            LastStatus = live.Status; UpdateUI();
        }
        public void DisconnectLive()
        {
            live?.Dispose(); live = null; liveTicks = 0;
            ApplyFrame(null); LastStatus = "Pose bridge disconnected"; UpdateUI();
        }
        void OnDestroy() { live?.Dispose(); }
        void UpdateLive()
        {
            if (live.Take(out var text, out var received))
            {
                try
                {
                    var envelope = PoseJson.Read<PoseEnvelope>(text);
                    if (envelope?.schemaVersion != "kinesthetic.session.v1") throw new InvalidDataException("Unknown pose schema");
                    if (envelope.type == "pose.frame" && envelope.payload != null)
                    {
                        if (liveSession != envelope.sessionId) { liveSession = envelope.sessionId; liveSequence = -1; }
                        if (envelope.sequence > liveSequence)
                        {
                            liveSequence = envelope.sequence; liveTicks = received;
                            ApplyFrame(LivePoseClient.Fresh(received) ? envelope.payload : null);
                        }
                    }
                    else { liveTicks = 0; ApplyFrame(null); }
                }
                catch (Exception e) { liveTicks = 0; ApplyFrame(null); Debug.LogWarning("Pose packet rejected: " + e.Message); }
            }
            if (!live.Connected || !LivePoseClient.Fresh(liveTicks)) ApplyFrame(null);
            LastStatus = !live.Connected ? live.Status : CurrentFrame == null ?
                "Waiting for fresh pose · target paused" : "Receiving timestamped pose observations";
            UpdateUI();
        }
        void ApplyAtTime()
        {
            double sourceTime = capture.frames[0].sourceMediaTimeMs + positionMs;
            while (frameIndex + 1 < capture.frames.Length && capture.frames[frameIndex + 1].sourceMediaTimeMs <= sourceTime) frameIndex++;
            var frame = capture.frames[frameIndex];
            // Do not fill a processing gap with a frozen "tracked" body.
            bool gap = sourceTime - frame.sourceMediaTimeMs > 250;
            ApplyFrame(gap ? null : frame);
            LastStatus = gap ? "Recording gap · motion unavailable" : "Recorded capture · original source timing";
        }
        public void ApplyFrame(PoseFrame frame)
        {
            CurrentFrame = frame;
            rig.Apply(frame);
            ShoulderValid = PoseMath.Shoulder(frame, false, out float shoulder);
            TargetReached = ShoulderValid && shoulder >= testTargetDegrees;
            if (target)
            {
                targetProperties ??= new MaterialPropertyBlock();
                targetProperties.SetColor("_BaseColor", !ShoulderValid ? Palette.Slate40 :
                    TargetReached ? Palette.Good : Palette.Target);
                target.SetPropertyBlock(targetProperties);
            }
        }
        bool sideView;
        void ToggleView()
        {
            sideView = !sideView;
            sceneCamera.transform.position = sideView ? new Vector3(0, 1.45f, -3.5f) : new Vector3(1.5f, 1.3f, -3.3f);
            sceneCamera.transform.LookAt(sideView ? new Vector3(0, 1.15f, 0) : new Vector3(0, .69f, 0));
        }
        void UpdateUI()
        {
            if (statusLabel == null) return;
            statusLabel.text = LastStatus;
            sourceLabel.text = live != null ? (CurrentFrame == null ? "BRIDGE · no fresh observation" :
                $"{(CurrentFrame.source == "camera" ? "LIVE CAMERA" : "RECORDED INPUT")} · frame {CurrentFrame.frameID}") :
                capture == null ? "No recording loaded" : $"REPLAY · {capture.frames.Length} frames · {positionMs / 1000:F1} / {DurationMs / 1000:F1} s";
            trackingLabel.text = $"Right arm: {(rig.RightArmTracked ? "tracked" : "unavailable")}   Left arm: {(rig.LeftArmTracked ? "tracked" : "unavailable")}";
            var shoulderText = PoseMath.Shoulder(CurrentFrame, false, out var shoulder) ? $"{shoulder:F0}°" : "unavailable — hip/arm reference missing";
            var elbowText = PoseMath.Elbow(CurrentFrame, false, out var elbow) ? $"{elbow:F0}°" : "unavailable";
            angleLabel.text = $"Estimated right shoulder: {shoulderText}\nEstimated right elbow bend: {elbowText}\nTest target: {testTargetDegrees:F0}° · {(TargetReached ? "reached" : ShoulderValid ? "below target" : "paused")}";
            timeline?.SetValueWithoutNotify((float)positionMs);
            if (playButton != null) playButton.text = playing ? "Pause" : "Play";
            if (liveButton != null) liveButton.text = live == null ? "Connect camera bridge" : "Disconnect camera bridge";
            timeline?.SetEnabled(live == null && capture != null);
        }
    }
}
