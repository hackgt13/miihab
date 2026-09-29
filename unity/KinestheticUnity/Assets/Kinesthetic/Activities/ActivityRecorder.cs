using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Kinesthetic.Activities
{
    /// <summary>One subject's contribution to a session. Golf records two; bowling and therapy record one.</summary>
    public struct ActivitySubject
    {
        public string SubjectId;
        public bool IsCompanion;
        /// <summary>What the plan asked for, when it asked for anything.</summary>
        public int? Prescribed;
        /// <summary>Attempts made, and how many of them counted. Attempted must not be below Valid.</summary>
        public int Attempted, Valid;
        /// <summary>The activity's own headline number: strokes, pins, degrees.</summary>
        public string MetricName, MetricUnit;
        public double? MetricValue;
    }

    /// <summary>
    /// Builds and posts the one session record every activity produces.
    ///
    /// This exists because bowling "joining the paradigm" initially meant copying golf's recorder
    /// verbatim -- the two POST coroutines were identical apart from a noun in a log line, and both
    /// hand-built the same envelope. That is the duplication the shared record was supposed to remove,
    /// so it lives here once instead.
    ///
    /// What an activity supplies is only what genuinely differs: who the subjects were, how much they
    /// did, and an opaque payload of its own figures. Everything structural -- the schema, the session
    /// id, timestamps, duration clamping, the shared flag vocabulary, the venue and exercise kinds from
    /// the catalog -- belongs to every activity equally and is decided here.
    ///
    /// Deliberately absent: anything scored. A stroke count and a rep count are not comparable numbers,
    /// so activity figures travel in Payload behind a versioned discriminant that nothing upstream reads.
    /// </summary>
    public static class ActivityRecorder
    {
        public const string Schema = "kinesthetic.activity.v1";
        public const string DefaultBridge = "http://127.0.0.1:8766";
        const int MaxDurationMs = 86_400_000;

        /// <summary>
        /// Post one session: finished, or (completed false) left partway, which still carries what was done. The caller owns the session id so it can raise Completed with it
        /// immediately, without waiting on the network. Simulated: any of the patient's motion came from a simulator.
        /// </summary>
        public static IEnumerator Send(
            string bridge, string activityId, string sessionId, DateTime startedUtc,
            IReadOnlyList<ActivitySubject> subjects, int lossEvents,
            string payloadKind, object payloadData, Action<bool> finished = null, bool completed = true,
            bool simulated = false)
        {
            var entry = ActivityCatalog.ById(activityId);
            if (entry == null)
            {
                // A session naming an unknown activity joins to no prescription, so the coordinator
                // would reject it anyway. Fail here, where the message can name the activity.
                Debug.LogError($"Cannot record a session for unknown activity \"{activityId}\".");
                finished?.Invoke(false);
                yield break;
            }

            var endedUtc = DateTime.UtcNow;
            var rows = new object[subjects.Count];
            for (int i = 0; i < subjects.Count; i++)
            {
                var s = subjects[i];
                rows[i] = new {
                    subjectId = s.SubjectId,
                    role = s.IsCompanion ? "companion" : "patient",
                    dose = new { prescribed = s.Prescribed, attempted = s.Attempted, valid = s.Valid },
                    primaryMetric = s.MetricName == null ? null : new {
                        name = s.MetricName, value = s.MetricValue, unit = s.MetricUnit ?? "" },
                };
            }

            var envelope = new {
                schema = Schema,
                activitySessionId = sessionId,
                activityId = entry.Id,
                exerciseKinds = entry.ExerciseKinds,
                venueId = entry.Venue,
                patientId = (string)null,        // a real patient entity arrives with plan schema v2
                planVersion = (int?)null,
                startedAt = startedUtc.ToString("o"),
                endedAt = endedUtc.ToString("o"),
                durationMs = (int)Mathf.Clamp((float)(endedUtc - startedUtc).TotalMilliseconds, 0, MaxDurationMs),
                completed,
                subjects = rows,
                trackingQuality = new { validFrameRatio = (double?)null, lossEvents },
                flags = Flags(lossEvents, completed, simulated),
                payload = new { kind = payloadKind, schemaVersion = "1", data = payloadData },
            };

            var body = Newtonsoft.Json.JsonConvert.SerializeObject(envelope);
            using var request = new UnityWebRequest(bridge + "/activity/session", "POST") {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(), timeout = 5 };
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            bool ok = request.result == UnityWebRequest.Result.Success;
            // A failed post must never interrupt play: the session is over and the person is done.
            if (!ok) Debug.LogWarning($"{entry.DisplayName} session was not recorded: " +
                                      $"{request.error} {request.downloadHandler?.text}");
            finished?.Invoke(ok);
        }

        static string[] Flags(int lossEvents, bool completed, bool simulated)
        {
            var flags = new List<string>();
            if (lossEvents > 0) flags.Add("tracking_lost");
            if (!completed) flags.Add("not_completed");   // left before the round or game ended
            if (simulated) flags.Add("simulated");        // played on sim-demo's motion: never the patient's session
            return flags.ToArray();
        }
    }
}
