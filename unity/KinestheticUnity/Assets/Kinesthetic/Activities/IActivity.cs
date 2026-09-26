using System;
using System.Collections;

namespace Kinesthetic.Activities
{
    /// <summary>
    /// A patient-facing activity the shell can drive without knowing what it is.
    ///
    /// Golf, bowling and a therapy session are not the same kind of thing — one is a two-player game
    /// with a physics ball, another is a measured clinical set — so this interface deliberately does
    /// not try to unify what they do. It unifies only what the shell needs: which catalog entry this
    /// is, whether it is mid-session, and how to leave without losing a record.
    ///
    /// Scoring is absent on purpose. Measurement lives on the coordinator, versioned and replayable
    /// from recorded frames; an activity emits events and reads back what the server decided.
    /// </summary>
    public interface IActivity
    {
        /// <summary>Catalog id, e.g. "golf.adaptive". Must resolve in <see cref="ActivityCatalog"/>.</summary>
        string ActivityId { get; }

        /// <summary>True while a session or round is in progress.</summary>
        bool IsRunning { get; }

        /// <summary>True while a start or stop is in flight and the activity must not be disturbed.</summary>
        bool IsBusy { get; }

        /// <summary>
        /// Raised once when the activity reaches its own terminal condition, carrying the recorded
        /// session id. An empty string means the session ended but was not recorded.
        /// </summary>
        event Action<string> Completed;

        /// <summary>
        /// Leave the activity, flushing anything that must be recorded first.
        ///
        /// This is allowed to fail, and that is the whole reason it is a coroutine rather than a
        /// method: a therapy session round-trips /exercise/stop and must refuse to leave if the
        /// measurement service does not answer, or the set is silently lost. A callback of false
        /// means "still here, do not unload me".
        /// </summary>
        IEnumerator RequestExit(Action<bool> succeeded);
    }
}
