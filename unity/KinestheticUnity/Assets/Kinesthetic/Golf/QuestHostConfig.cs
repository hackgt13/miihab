using UnityEngine;

namespace Kinesthetic.Golf
{
    /// Relay address and pairing token baked into a headset build by
    /// Kinesthetic → Quest → Write host config.
    ///
    /// A Resources asset rather than StreamingAssets on purpose: on Android, StreamingAssets lives
    /// inside the compressed APK, so Application.streamingAssetsPath is a jar: URL and File.Exists
    /// against it is always false. Reading it that way left the headset silently falling back to
    /// loopback and never finding the Mac.
    public sealed class QuestHostConfig : ScriptableObject
    {
        /// Path for Resources.Load; the asset lives at Assets/Kinesthetic/Golf/Resources/Golf/.
        public const string ResourcePath = "Golf/QuestHostConfig";

        public string host = "127.0.0.1";
        public int port = 8767;
        public string token = "";
    }
}
