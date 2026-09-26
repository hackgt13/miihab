using System;
using System.Collections;
using UnityEngine;

namespace Kinesthetic.Visit
{
    /// How the therapist says a line. The visit hands over one line at a time and waits; `progress` reports
    /// how much of it has been said, 0 to 1, which is what the speech balloon types out in step with.
    ///
    /// Today the only voice is CaptionVoice, which paces text as if it were spoken. The ElevenLabs voice goes
    /// here as another implementation — a MonoBehaviour on the therapist that plays `line.Audio` (or asks the
    /// voice server for it) and reports `source.time / clip.length` — and TherapistVisit picks it up with no
    /// other change, because it looks for any component implementing this before falling back to captions.
    public interface ITherapistVoice
    {
        /// True while sound is coming out, for the mouth. A caption voice "speaks" while it types.
        bool Speaking { get; }
        IEnumerator Say(VisitScript.Line line, Action<float> progress);
        /// Finish the current line now: the patient pressed Next.
        void Skip();
    }

    /// Text with no sound, paced at a relaxed speaking rate so a caption reads like speech rather than
    /// appearing all at once, then held long enough to finish reading.
    public sealed class CaptionVoice : ITherapistVoice
    {
        const float CharactersPerSecond = 16f, Hold = 1.4f;
        bool skipped;
        public bool Speaking { get; private set; }
        public void Skip() => skipped = true;

        public IEnumerator Say(VisitScript.Line line, Action<float> progress)
        {
            skipped = false; Speaking = true;
            float duration = Mathf.Max(.8f, line.Text.Length / CharactersPerSecond);
            for (float t = 0; t < duration && !skipped; t += Time.deltaTime) { progress(t / duration); yield return null; }
            progress(1); Speaking = false;
            // A Next while the line was typing finishes the line; the next Next moves on, so a press never
            // skips words nobody has had the chance to read.
            skipped = false;
            for (float t = 0; t < Hold && !skipped; t += Time.deltaTime) yield return null;
            skipped = false;
        }
    }
}
