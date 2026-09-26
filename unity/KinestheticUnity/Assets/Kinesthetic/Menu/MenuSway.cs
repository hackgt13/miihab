using UnityEngine;

namespace Kinesthetic.Menu
{
    // Wind in the menu plaza: palm crowns and the pin flag breathe on a slow offset sine.
    // Scenery only — this never touches the camera, which stays fixed at the plaza Viewpoint
    // and is driven by whatever look input the menu installs on it.
    public sealed class MenuSway : MonoBehaviour
    {
        public Transform[] sway = new Transform[0];
        public float swayDegrees = 2.4f;
        public float swaySeconds = 6.5f;

        Quaternion[] rest;

        void Awake()
        {
            rest = new Quaternion[sway.Length];
            for (int i = 0; i < sway.Length; i++) if (sway[i]) rest[i] = sway[i].localRotation;
        }

        void LateUpdate()
        {
            // Unscaled, so the plaza keeps breathing whatever an activity left Time.timeScale at.
            float time = Time.unscaledTime;
            for (int i = 0; i < sway.Length; i++)
            {
                if (!sway[i]) continue;
                float phase = time * Mathf.PI * 2 / Mathf.Max(1, swaySeconds) + i * .7f;
                sway[i].localRotation = rest[i] * Quaternion.Euler(
                    Mathf.Sin(phase) * swayDegrees, 0, Mathf.Cos(phase * .73f) * swayDegrees * .6f);
            }
        }
    }
}
