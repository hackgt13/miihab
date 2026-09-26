using UnityEngine;

namespace Kinesthetic.Menu
{
    /// Who the board is greeting. Stored in PlayerPrefs because it is a display preference on this
    /// machine, not clinical data: the coordinator owns the patient record, and when a real patient
    /// entity lands on the plan schema this becomes a cache of that name rather than the source.
    ///
    /// Deliberately not a file in persistentDataPath — on Android that path is reachable but
    /// streamingAssets is not, and PlayerPrefs behaves the same on both targets with no IO to get
    /// wrong on the headset.
    public static class MenuProfile
    {
        const string NameKey = "rehabmii.patient.displayName";
        public const string DefaultName = "Alex";

        public static string Name
        {
            get
            {
                var stored = PlayerPrefs.GetString(NameKey, "").Trim();
                return stored.Length > 0 ? stored : DefaultName;
            }
            set
            {
                var clean = (value ?? "").Trim();
                if (clean.Length > 24) clean = clean[..24];
                PlayerPrefs.SetString(NameKey, clean);
                PlayerPrefs.Save();
            }
        }

        public static bool HasName => PlayerPrefs.GetString(NameKey, "").Trim().Length > 0;
        public static void Forget() { PlayerPrefs.DeleteKey(NameKey); PlayerPrefs.Save(); }
    }
}
