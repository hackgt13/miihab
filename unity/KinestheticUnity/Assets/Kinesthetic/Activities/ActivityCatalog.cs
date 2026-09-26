using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Activities
{
    /// <summary>
    /// The activity catalog, read from the generated copy of coordinator/activities.json.
    ///
    /// The coordinator owns the source file because it has to validate prescriptions and session
    /// records against the same list; a C#-only ScriptableObject could not serve that half. An editor
    /// script copies the file into Resources, the way QuestSceneSetup generates QuestHostConfig
    /// rather than anyone hand-editing it in the Inspector.
    ///
    /// Parsed with JObject rather than reflection-based deserialisation because IL2CPP strips the
    /// reflection that JsonConvert.DeserializeObject&lt;T&gt; depends on, and this has to work on the
    /// headset as well as the Mac.
    /// </summary>
    public sealed class HelpStep { public string Step, Copy; }

    /// <summary>
    /// How a movement looks on a body (coordinator/exercises.ts, BodyModel): which segment moves, and the directions
    /// it sweeps between, in the patient's own frame — x out to the working side, y up, z forward. Anatomy only;
    /// PoseRig.ApplyMovement maps it onto the skeleton, so the avatar, the mirror's ghosts and the target band all
    /// draw the joint the AirPod actually measures, at the angle it measured.
    /// </summary>
    public sealed class BodyModel
    {
        public string Segment;          // arm | forearm | head | trunk | thigh | shank | leg
        public Vector3 Rest, Toward;
        public bool Roll;               // turns about Rest instead; Toward is then the thumb
        public readonly Dictionary<string, Vector3> Hold = new();   // upperArm | forearm | thigh | shank | legs

        /// <summary>The moving segment's direction at this angle, in the patient's frame (for a roll, the thumb's).</summary>
        public Vector3 At(float deg)
        {
            var rest = Rest.normalized;
            var toward = Vector3.ProjectOnPlane(Toward, rest).normalized;
            float r = deg * Mathf.Deg2Rad;
            return Roll ? toward * Mathf.Cos(r) + Vector3.Cross(rest, toward) * Mathf.Sin(r)
                        : rest * Mathf.Cos(r) + toward * Mathf.Sin(r);
        }

        static Vector3 Vec(JToken t) => t is JArray a && a.Count == 3 ? new Vector3((float)a[0], (float)a[1], (float)a[2]) : Vector3.zero;
        public static BodyModel Parse(JToken t)
        {
            if (t is not JObject o) return null;
            var model = new BodyModel { Segment = (string)o["segment"], Rest = Vec(o["rest"]), Toward = Vec(o["toward"]), Roll = (bool?)o["roll"] ?? false };
            if (o["hold"] is JObject hold) foreach (var h in hold) model.Hold[h.Key] = Vec(h.Value);
            return model;
        }
    }

    public sealed class ActivityEntry
    {
        public string Id, DisplayName, Tagline, Category, Scene, Venue;
        /// <summary>The headset's render-only copy of Scene, or null when the headset has none.</summary>
        public string QuestScene;
        public string LoadingMessage, Music, HelpTitle;
        /// <summary>The gallery card: every card is built from these two, so none is weighted above another.
        /// Null CardTag keeps the activity out of the gallery.</summary>
        public string CardTag, CardCaption;
        /// <summary>Where the tracker goes, in the patient's words, or null.</summary>
        public string Wear;
        public BodyModel Body;
        public HelpStep[] HelpSteps = new HelpStep[0];
        public string[] ExerciseKinds = new string[0];
        public string[] Requires = new string[0];
        public int Subjects = 1;
        public bool Prescribable;
        public bool UsesSharedNavigation = true;
        /// <summary>"movement": one exercise kind as its own gallery tile, generated from the exercise library.</summary>
        public string Group;
        public bool IsMovement => Group == "movement";
        /// <summary>The one kind a movement measures, or null.</summary>
        public string MovementKind => IsMovement && ExerciseKinds.Length == 1 ? ExerciseKinds[0] : null;
        public bool NeedsPose => Requires.Contains("pose");
        public bool NeedsImu => Requires.Contains("imu");
    }

    public static class ActivityCatalog
    {
        const string ResourcePath = "Activities/activities";
        static ActivityEntry[] entries;

        public static ActivityEntry[] All
        {
            get
            {
                if (entries != null) return entries;
                var asset = Resources.Load<TextAsset>(ResourcePath);
                if (!asset)
                {
                    Debug.LogError($"Activity catalog missing at Resources/{ResourcePath}. " +
                                   "Run Kinesthetic/Activities/Sync catalog from coordinator.");
                    return entries = new ActivityEntry[0];
                }
                var list = new List<ActivityEntry>();
                foreach (var item in (JArray)JObject.Parse(asset.text)["activities"])
                    list.Add(new ActivityEntry {
                        Id = (string)item["id"], DisplayName = (string)item["displayName"],
                        Tagline = (string)item["tagline"] ?? "", Category = (string)item["category"],
                        Scene = (string)item["scene"], QuestScene = (string)item["questScene"], Venue = (string)item["venue"],
                        ExerciseKinds = item["exerciseKinds"]?.Select(k => (string)k).ToArray() ?? new string[0],
                        Requires = item["requires"]?.Select(k => (string)k).ToArray() ?? new string[0],
                        Subjects = (int?)item["subjects"] ?? 1,
                        Prescribable = (bool?)item["prescribable"] ?? false,
                        UsesSharedNavigation = (string)item["navigation"] != "activity",
                        Group = (string)item["group"],
                        CardTag = (string)item["card"]?["tag"], CardCaption = (string)item["card"]?["caption"] ?? "",
                        Wear = (string)item["wear"],
                        Body = BodyModel.Parse(item["body"]),
                        LoadingMessage = (string)item["loadingMessage"] ?? "",
                        Music = (string)item["music"],
                        HelpTitle = (string)item["help"]?["title"] ?? "",
                        HelpSteps = item["help"]?["steps"]?
                            .Select(h => new HelpStep { Step = (string)h["step"], Copy = (string)h["copy"] })
                            .ToArray() ?? new HelpStep[0],
                    });
                return entries = list.ToArray();
            }
        }

        public static ActivityEntry ById(string id) => All.FirstOrDefault(a => a.Id == id);

        /// <summary>Scene name for an activity, so no caller hardcodes one.</summary>
        public static string SceneOf(string id) => ById(id)?.Scene;

        public static bool Knows(string id) => ById(id) != null;

        /// <summary>Everything the gallery shows, in catalog order: the authored activities, then every movement.</summary>
        public static ActivityEntry[] Gallery => All.Where(a => a.CardTag != null).ToArray();

        /// <summary>The movement tile that measures this exercise kind, so a studio opened on its own still draws the
        /// right joint and says where the tracker goes.</summary>
        public static ActivityEntry MovementFor(string exerciseKind) => All.FirstOrDefault(a => a.MovementKind == exerciseKind);

        /// <summary>The headset scene that renders a Mac scene, or null: the catalog's questScene, by scene name.</summary>
        public static string QuestSceneOf(string macScene) => All.FirstOrDefault(a => a.Scene == macScene)?.QuestScene;

        /// <summary>Forget the parsed copy, so a catalog re-sync is picked up without a domain reload.</summary>
        public static void Invalidate() => entries = null;
    }
}
