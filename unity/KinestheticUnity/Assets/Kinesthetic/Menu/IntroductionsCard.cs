using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Kinesthetic.UI;

namespace Kinesthetic.Menu
{
    /// <summary>
    /// "Someone else is working toward the same thing. Want to meet?"
    ///
    /// One card, one person, two buttons. Deliberately not a list: a list of
    /// strangers is a queue to triage, and the answer to each is a yes or a no
    /// with nothing to compare against. Answering makes the next one appear, and
    /// when there is nobody left the card is gone rather than showing an empty
    /// state — this sits above the friends list, and an empty box there would
    /// read as something being broken.
    ///
    /// No name, no face, no numbers. The coordinator will not send them until
    /// both sides have said yes, so there is nothing here to leak; what a person
    /// decides on is the reason line and how far along the other is.
    ///
    /// Built from the UI library rather than this screen's own classes, so it
    /// inherits the one button, the one tag and the one type scale. World-space
    /// panels never fire :hover — see KButton — so nothing here depends on it.
    /// </summary>
    public sealed class IntroductionsCard
    {
        const string Bridge = "http://127.0.0.1:8766";

#pragma warning disable 0649
        [Serializable] class Introduction { public string id, kind, reason; public bool waitingOnThem; }
        [Serializable] class Feed { public Introduction[] introductions; }
        [Serializable] class Answered { public bool joined; }
#pragma warning restore 0649

        readonly MonoBehaviour owner;
        readonly VisualElement mount;
        readonly Action roster;          // a mutual yes adds a friend; the list must catch up

        KSurface card;
        KTag kind;
        KText reason;
        KButton yes, no;
        Introduction showing;

        public IntroductionsCard(MonoBehaviour owner, VisualElement mount, Action roster)
        {
            this.owner = owner; this.mount = mount; this.roster = roster;
            Build();
            Hide();
        }

        void Build()
        {
            // Attach can run again — a pane rebinding its tree, a second call from
            // the menu — and each run built another card on top of the last, so an
            // empty one sat above the live one.
            mount.Clear();

            card = new KSurface { tone = KSurface.Tone.Paper, density = KSurface.Density.Comfortable };
            card.AddToClassList("introduction-card");

            kind = new KTag { tone = KTag.Tone.Info };
            reason = new KText { size = KText.Size.Heading, tone = KText.Tone.Ink };
            reason.style.whiteSpace = WhiteSpace.Normal;

            var eyebrow = new KEyebrow { text = "SOMEONE LIKE YOU" };

            var actions = new VisualElement();
            actions.AddToClassList("introduction-actions");
            yes = new KButton(() => Answer(true)) { text = "Yes, introduce us", tone = KButton.Tone.Primary, size = KButton.Size.Medium };
            no = new KButton(() => Answer(false)) { text = "Not now", tone = KButton.Tone.Quiet, size = KButton.Size.Medium };
            actions.Add(no); actions.Add(yes);

            var head = new VisualElement();
            head.AddToClassList("introduction-head");
            head.Add(eyebrow); head.Add(kind);

            card.Add(head); card.Add(reason); card.Add(actions);
            mount.Add(card);
        }

        void Hide() { card.AddToClassList("hidden"); showing = null; }

        /// Fetches peers first and only asks for mentors when there are none, so
        /// the two never compete for the one card.
        public void Refresh() => owner.StartCoroutine(Load());

        IEnumerator Load()
        {
            yield return Fetch("peer");
            if (showing == null) yield return Fetch("mentor");
        }

        IEnumerator Fetch(string which)
        {
            using var request = UnityWebRequest.Get(Bridge + "/api/friends/introductions?kind=" + which);
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var feed = JsonUtility.FromJson<Feed>(request.downloadHandler.text);
            if (feed?.introductions == null) yield break;

            foreach (var next in feed.introductions)
            {
                // One already waiting on the other side is not a decision to make.
                if (next == null || next.waitingOnThem || string.IsNullOrEmpty(next.reason)) continue;
                Show(next);
                yield break;
            }
        }

        void Show(Introduction next)
        {
            showing = next;
            // "Further along" rather than "mentor": nobody wants to be told they
            // are the one who needs mentoring.
            kind.text = next.kind == "mentor" ? "Further along" : "Same stage";
            kind.tone = next.kind == "mentor" ? KTag.Tone.Good : KTag.Tone.Info;
            reason.text = next.reason;
            card.RemoveFromClassList("hidden");
        }

        void Answer(bool said)
        {
            if (showing == null) return;
            var answering = showing;
            // Take the card away on the press. Waiting on a round trip to
            // acknowledge a yes or a no is what makes a UI feel unsure.
            Hide();
            owner.StartCoroutine(Send(answering, said));
        }

        IEnumerator Send(Introduction which, bool said)
        {
            var body = "{\"id\":\"" + which.id + "\",\"yes\":" + (said ? "true" : "false") + "}";
            using var request = new UnityWebRequest(Bridge + "/api/friends/introductions/answer", "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                var answered = JsonUtility.FromJson<Answered>(request.downloadHandler.text);
                if (answered != null && answered.joined) roster?.Invoke();
            }
            // Either way, see whether anyone else is waiting.
            yield return Load();
        }
    }
}
