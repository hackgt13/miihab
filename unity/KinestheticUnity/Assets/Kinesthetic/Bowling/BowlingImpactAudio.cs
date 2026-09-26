using UnityEngine;

namespace Kinesthetic.Bowling
{
    // A short synthesized wooden clack shared by the ball and pins.
    public sealed class BowlingImpactAudio : MonoBehaviour
    {
        static AudioClip clip;
        static float last;
        AudioSource source;
        void Awake()
        {
            if (!clip)
            {
                const int rate = 22050; var data = new float[6600]; var random = new System.Random(49);
                for (int i = 0; i < data.Length; i++)
                {
                    float t = i / (float)rate;
                    data[i] = Mathf.Exp(-23 * t) * (.22f * (float)(random.NextDouble() * 2 - 1) + .25f * Mathf.Sin(t * 1800) + .14f * Mathf.Sin(t * 3700));
                }
                clip = AudioClip.Create("Bowling pin impact", data.Length, 1, rate, false); clip.SetData(data, 0);
            }
            source = gameObject.AddComponent<AudioSource>(); source.spatialBlend = .35f; source.maxDistance = 35; source.playOnAwake = false;
        }
        void OnCollisionEnter(Collision other)
        {
            float strength = other.relativeVelocity.magnitude;
            if (strength < .4f || !other.rigidbody || Time.unscaledTime - last < .035f) return;
            last = Time.unscaledTime; source.pitch = Random.Range(.85f, 1.15f); source.PlayOneShot(clip, Mathf.Clamp(strength * .14f, .12f, .65f));
        }
    }
}
