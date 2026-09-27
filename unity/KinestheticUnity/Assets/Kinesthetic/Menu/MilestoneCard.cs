using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Kinesthetic.UI;

namespace Kinesthetic.Menu
{
    /// <summary>
    /// "7 days in a row. Tell your friends?"
    ///
    /// A milestone of turning up (coordinator/milestones.ts), never of how far
    /// anyone reached: friends see that you showed up, not your numbers. Muse
    /// words it in the patient's voice from the fact and their own goal; the
    /// words are theirs to send or not, and nothing reaches a friend until they
    /// press Share. Either answer is final, so the card never nags.
    ///
    /// Sits above the friends list and is gone when there is nothing to share,
    /// the same way the introduction card is: an empty box here would read as
    /// something broken. Built from the UI library, so layout is all this adds.
    /// </summary>
    public sealed class MilestoneCard
    {
        const string Bridge = "http://127.0.0.1:8766";

#pragma warning disable 0649
        [Serializable] class Milestone { public string id, fact, line; }
        [Serializable] class Shared { public int sharedWith; }
#pragma warning restore 0649

        readonly MonoBehaviour owner;
        readonly VisualElement mount;
        readonly Action shared;          // the share is a message in every thread; the list must catch up

        KSurface card;
        KTag fact;
        KText line;
        KButton share, later;
        Milestone showing;

        public MilestoneCard(MonoBehaviour owner, VisualElement mount, Action shared)
        {
            this.owner = owner; this.mount = mount; this.shared = shared;
            Build();
            Hide();
        }

        void Build()
        {
            mount.Clear();
            card = new KSurface { tone = KSurface.Tone.Paper, density = KSurface.Density.Comfortable };
            card.AddToClassList("milestone-card");

            var head = new VisualElement();
            head.AddToClassList("milestone-head");
            fact = new KTag { tone = KTag.Tone.Good };
            head.Add(new KEyebrow { text = "WORTH SHARING" }); head.Add(fact);

            line = new KText { size = KText.Size.Body, tone = KText.Tone.Ink };
            line.style.whiteSpace = WhiteSpace.Normal;

            var actions = new VisualElement();
            actions.AddToClassList("milestone-actions");
            later = new KButton(() => Answer(false)) { text = "Not now", tone = KButton.Tone.Quiet, size = KButton.Size.Small };
            share = new KButton(() => Answer(true)) { text = "Share with friends", tone = KButton.Tone.Primary, size = KButton.Size.Small };
            actions.Add(later); actions.Add(share);

            card.Add(head); card.Add(line); card.Add(actions);
            mount.Add(card);
        }

        void Hide() { card.AddToClassList("hidden"); showing = null; }

        public void Refresh() => owner.StartCoroutine(Load());

        IEnumerator Load()
        {
            using var request = UnityWebRequest.Get(Bridge + "/api/friends/milestone");
            request.timeout = 12;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var next = JsonUtility.FromJson<Milestone>(request.downloadHandler.text);
            if (next == null || string.IsNullOrEmpty(next.id)) { Hide(); yield break; }

            showing = next;
            fact.text = next.fact;
            line.text = next.line;
            card.RemoveFromClassList("hidden");
        }

        void Answer(bool yes)
        {
            if (showing == null) return;
            var answering = showing;
            Hide();
            owner.StartCoroutine(Send(answering, yes));
        }

        IEnumerator Send(Milestone which, bool yes)
        {
            var body = "{\"id\":\"" + SocialBridge.Escape(which.id) + "\",\"share\":" + (yes ? "true" : "false")
                + ",\"text\":\"" + SocialBridge.Escape(which.line) + "\"}";
            using var request = new UnityWebRequest(Bridge + "/api/friends/milestone/answer", "POST")
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 10,
            };
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var answered = JsonUtility.FromJson<Shared>(request.downloadHandler.text);
            if (answered != null && answered.sharedWith > 0) shared?.Invoke();
        }
    }
}
