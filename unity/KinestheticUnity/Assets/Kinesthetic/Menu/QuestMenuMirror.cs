using Kinesthetic.Shell;
using Kinesthetic.UI.Remote;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Kinesthetic.Menu
{
    /// The headset's ring follows the Mac's. The panes here are replicas — their trees arrive over /ui —
    /// but the ring they stand in is local geometry, and a press that turned the Mac's ring would leave
    /// this one where it was. So the Mac says which slot it is facing (UiCue.Face) and this turns to match.
    public sealed class QuestMenuMirror : MonoBehaviour
    {
        PaneCarousel carousel;

        void OnEnable() { UiCue.Received += Cue; }
        void OnDisable() { UiCue.Received -= Cue; }

        void Cue(JObject message)
        {
            if ((string)message["kind"] != UiCue.Face) return;
            if (!carousel) carousel = FindAnyObjectByType<PaneCarousel>();
            string slot = (string)message["slot"];
            if (carousel && !string.IsNullOrEmpty(slot) && carousel.Current.id != slot) carousel.Show(slot);
        }
    }
}
