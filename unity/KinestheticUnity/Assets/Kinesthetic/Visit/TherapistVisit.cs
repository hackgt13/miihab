using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kinesthetic.Golf;
using Kinesthetic.UI;
using Kinesthetic.UI.Boards;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Kinesthetic.Visit
{
    /// The visit: the therapist stands beside a whiteboard and talks the patient through their program
    /// updates. Each line comes up in a balloon over the therapist's head, and the update it is about is
    /// written up on the board while it is said, with the therapist turned to the board as it writes.
    ///
    /// Everything said and written comes from the coordinator (GET /api/visit, coordinator/visit.ts), which
    /// builds it from the plan's version history and the notes the therapist left. Nothing is authored here
    /// except what the therapist says when that cannot be reached.
    ///
    /// Speech goes through ITherapistVoice. Today that is captions paced as speech; a voice component on the
    /// therapist (ElevenLabs, later) takes over by being there, without this file changing.
    public sealed class TherapistVisit : MonoBehaviour
    {
        const string VisitUrl = "http://127.0.0.1:8766/api/visit";
        const float WriteCharactersPerSecond = 30f, TurnDegreesPerSecond = 120f;

        public BoardSet boards;
        /// The standing therapist, turned between the patient and the board. Faces its own +Z.
        public Transform therapist;
        public Transform whiteboard;
        public SeatRig seat;

        VisitScript script;
        ITherapistVoice voice;
        MiiIdleLife face;
        float[] written = new float[0];     // how much of each board update is written up, 0 to 1
        string saying = ""; float said;     // the balloon: the current line and how much of it is out
        bool writing, writeSkip, finished;
        int boundGeneration = -1;
        KButton boundSkip, boundReplay;
        float nextMouth;
        Coroutine running;

        void Start()
        {
            voice = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude)
                .OfType<ITherapistVoice>().FirstOrDefault() ?? new CaptionVoice();
            face = therapist ? therapist.GetComponentInChildren<MiiIdleLife>() : null;
            running = StartCoroutine(Run(reload: true));
        }

        void Update()
        {
            Bind();
            if (voice != null && voice.Speaking && face && Time.time >= nextMouth)
            {
                face.Surprise(.1f);                            // the mouth opens on each syllable-ish beat
                nextMouth = Time.time + Random.Range(.16f, .26f);
            }
            Turn();
        }

        /// Face the patient while talking; turn most of the way to the board while writing on it.
        void Turn()
        {
            if (!therapist || !seat) return;
            var toPatient = Vector3.ProjectOnPlane(seat.EyePoint - therapist.position, Vector3.up);
            var look = toPatient;
            if (writing && whiteboard)
            {
                var toBoard = Vector3.ProjectOnPlane(whiteboard.position - therapist.position, Vector3.up);
                look = Vector3.Slerp(toPatient.normalized, toBoard.normalized, .7f);
            }
            if (look.sqrMagnitude < 1e-6f) return;
            therapist.rotation = Quaternion.RotateTowards(therapist.rotation, Quaternion.LookRotation(look, Vector3.up), TurnDegreesPerSecond * Time.deltaTime);
        }

        IEnumerator Run(bool reload)
        {
            while (!Bind()) yield return null;
            if (reload || script == null) { Status("Your therapist is getting ready…"); yield return Load(); }
            written = new float[script.Updates.Length];
            finished = false; saying = ""; said = 0;
            BuildRows(); Render();
            Status(script.Live ? $"{script.TherapistName} is going over your program." : "Your program couldn't be reached on this Mac.");

            int revealed = 0;
            foreach (var line in script.Lines)
            {
                Coroutine board = null;
                int reveal = Mathf.Clamp(line.Reveal, 0, written.Length);
                if (reveal > revealed) { board = StartCoroutine(Write(revealed, reveal)); revealed = reveal; }
                saying = line.Text; said = 0; Render();
                yield return voice.Say(line, p => { said = p; Render(); });
                if (board != null) yield return board;   // the board catches up before the next line
            }
            // Anything the script never talked about still goes up, so the board is never missing an update.
            if (revealed < written.Length) yield return Write(revealed, written.Length);
            saying = ""; finished = true; Render();
            Status("That's everything for today. Hear it again, or head back to the menu when you're ready.");
            running = null;
        }

        IEnumerator Load()
        {
            using var request = UnityWebRequest.Get(VisitUrl);
            request.timeout = 3;
            yield return request.SendWebRequest();
            script = null;
            if (request.result == UnityWebRequest.Result.Success)
                try { script = VisitScript.FromCoordinator(JObject.Parse(request.downloadHandler.text)); } catch { }
            script ??= VisitScript.Offline();
        }

        /// Write up updates [from, to) one after another, a marker's pace per character.
        IEnumerator Write(int from, int to)
        {
            writing = true; writeSkip = false;
            for (int i = from; i < to; i++)
            {
                var u = script.Updates[i];
                float seconds = Mathf.Max(.4f, (u.Heading.Length + u.Detail.Length) / WriteCharactersPerSecond);
                for (float t = 0; t < seconds && !writeSkip; t += Time.deltaTime) { written[i] = Mathf.Max(.001f, t / seconds); Render(); yield return null; }
                written[i] = 1; Render();
            }
            writing = false;
        }

        void Skip() { voice?.Skip(); writeSkip = true; }

        void Replay()
        {
            if (running != null) StopCoroutine(running);
            StopAllCoroutines();
            voice?.Skip(); writing = false;
            running = StartCoroutine(Run(reload: true));   // reload: the therapist may have left a note since
        }

        bool Bind()
        {
            if (!boards || !boards.Live) return false;
            if (boundGeneration == boards.Generation) return true;
            boundGeneration = boards.Generation;
            // Generation moves when any board rebuilds, so only a button that is new gets the handler.
            var skip = boards.Q<KButton>("visit-skip");
            var replay = boards.Q<KButton>("visit-replay");
            if (skip != null && skip != boundSkip) { skip.clicked += Skip; boundSkip = skip; }
            if (replay != null && replay != boundReplay) { replay.clicked += Replay; boundReplay = replay; }
            if (script != null) { BuildRows(); Render(); }
            return true;
        }

        void Status(string text) { var label = boards ? boards.Q<Label>("visit-status") : null; if (label != null) label.text = text; }

        static readonly Dictionary<string, (string label, KTag.Tone tone)> Tags = new()
        {
            ["added"] = ("NEW", KTag.Tone.Good),
            ["changed"] = ("CHANGED", KTag.Tone.Info),
            ["removed"] = ("RESTING", KTag.Tone.Neutral),
            ["program"] = ("PLAN", KTag.Tone.Info),
            ["note"] = ("NOTE", KTag.Tone.Neutral),
        };

        void BuildRows()
        {
            var list = boards.Q("board-updates");
            if (list == null || script == null) return;
            list.Clear();
            foreach (var u in script.Updates)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("visit-update"); row.AddToClassList("unrevealed");
                var tagSlot = new VisualElement { pickingMode = PickingMode.Ignore };
                tagSlot.AddToClassList("visit-update-tag");
                if (!Tags.TryGetValue(u.Kind, out var tag)) tag = ("NOTE", KTag.Tone.Neutral);
                tagSlot.Add(new KTag { text = tag.label, tone = tag.tone });
                var copy = new VisualElement { pickingMode = PickingMode.Ignore };
                copy.AddToClassList("visit-update-copy");
                copy.Add(new KText { name = "update-heading", size = KText.Size.Body });
                copy.Add(new KText { name = "update-detail", size = KText.Size.Caption, tone = KText.Tone.Soft });
                row.Add(tagSlot); row.Add(copy);
                list.Add(row);
            }
        }

        void Render()
        {
            if (!boards || script == null) return;
            var speech = boards.Q("speech");
            speech?.EnableInClassList("quiet", string.IsNullOrEmpty(saying));
            var name = boards.Q<Label>("speech-name");
            if (name != null) name.text = script.TherapistName.ToUpperInvariant();
            var line = boards.Q<Label>("speech-line");
            if (line != null) line.text = Typed(saying, said);

            var attribution = boards.Q<Label>("board-attribution");
            if (attribution != null) attribution.text = script.Attribution.ToUpperInvariant();
            var title = boards.Q<Label>("board-title");
            if (title != null) title.text = script.Title;
            var date = boards.Q<Label>("board-date");
            if (date != null) date.text = finished ? script.UpdatedCaption() : "";

            var rows = boards.Q("board-updates");
            if (rows == null) return;
            for (int i = 0; i < rows.childCount && i < written.Length; i++)
            {
                var row = rows[i]; var u = script.Updates[i];
                row.EnableInClassList("unrevealed", written[i] <= 0);
                // The heading is written first, then the detail, as one stroke of the marker.
                float total = Mathf.Max(1, u.Heading.Length + u.Detail.Length);
                float headingShare = u.Heading.Length / total;
                float headingFraction = headingShare <= 0 ? 1 : Mathf.Clamp01(written[i] / headingShare);
                float detailFraction = headingShare >= 1 ? 1 : Mathf.Clamp01((written[i] - headingShare) / (1 - headingShare));
                var heading = row.Q<Label>("update-heading"); if (heading != null) heading.text = Typed(u.Heading, headingFraction);
                var detail = row.Q<Label>("update-detail"); if (detail != null) detail.text = Typed(u.Detail, detailFraction);
            }
        }

        /// The first `fraction` of `text`, with the rest still laid out but transparent, so a line that is
        /// being written never re-wraps as it grows. Angle brackets are swapped out: they would open a tag.
        public static string Typed(string text, float fraction)
        {
            text = (text ?? "").Replace('<', '‹').Replace('>', '›');
            int shown = Mathf.Clamp(Mathf.CeilToInt(text.Length * Mathf.Clamp01(fraction)), 0, text.Length);
            return shown >= text.Length ? text : text.Substring(0, shown) + "<alpha=#00>" + text.Substring(shown);
        }
    }
}
