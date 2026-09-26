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

    public sealed class ActivityEntry
    {
        public string Id, DisplayName, Tagline, Category, Scene, Venue;
        public string LoadingMessage, Music, HelpTitle;
        public HelpStep[] HelpSteps = new HelpStep[0];
        public string[] ExerciseKinds = new string[0];
        public string[] Requires = new string[0];
        public int Subjects = 1;
        public bool Prescribable;
        public bool UsesSharedNavigation = true;
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
                        Scene = (string)item["scene"], Venue = (string)item["venue"],
                        ExerciseKinds = item["exerciseKinds"]?.Select(k => (string)k).ToArray() ?? new string[0],
                        Requires = item["requires"]?.Select(k => (string)k).ToArray() ?? new string[0],
                        Subjects = (int?)item["subjects"] ?? 1,
                        Prescribable = (bool?)item["prescribable"] ?? false,
                        UsesSharedNavigation = (string)item["navigation"] != "activity",
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

        /// <summary>Forget the parsed copy, so a catalog re-sync is picked up without a domain reload.</summary>
        public static void Invalidate() => entries = null;
    }
}
