using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinesthetic
{
    /// Owns which camera the Mac looks through. Camera.main and AudioListener are both effectively
    /// singletons — Camera.main returns the first *enabled* MainCamera-tagged camera, and Unity warns on a
    /// second listener — so exactly one view is enabled, tagged and listening at a time.
    ///
    /// DevFreeLook attaches on scene load only, so it is re-attached after every swap; without that the
    /// newly live camera would have no look control.
    public sealed class ViewDirector : MonoBehaviour
    {
        public Key toggle = Key.V;

        Camera[] views;
        int index;

        public static void Register(params Camera[] cameras)
        {
            var live = Array.FindAll(cameras, c => c);
            if (live.Length < 2) return;                        // nothing to toggle between
            var director = new GameObject("View director").AddComponent<ViewDirector>();
            director.views = live;
            director.Show(0);                                   // the scene's own view stays the default
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggle].wasPressedThisFrame) Show((index + 1) % views.Length);
        }

        void Show(int next)
        {
            index = next;
            for (int i = 0; i < views.Length; i++)
            {
                bool live = i == index;
                if (!views[i]) continue;
                views[i].enabled = live;
                views[i].tag = live ? "MainCamera" : "Untagged";
                var listener = views[i].GetComponent<AudioListener>();
                if (listener) listener.enabled = live;
            }
            DevFreeLook.Attach();                               // Camera.main just changed underneath it
        }
    }
}
