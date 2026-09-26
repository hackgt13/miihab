using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Kinesthetic.UI;

namespace Kinesthetic.Menu
{
    /// <summary>
    /// Who is in there already, under the card that takes you in.
    ///
    /// Choosing an activity alone and choosing one three friends are in are not
    /// the same choice, and the gallery is where that is decided. So the faces go
    /// on the card rather than in a list somewhere else: by the time you are
    /// reading a list of who is online you have already picked.
    ///
    /// Only friends appear. The coordinator will not report a stranger's presence,
    /// and it drops anyone whose machine has gone quiet rather than leaving them
    /// lit — "online" that nobody clears is worse than nothing, because it sends
    /// someone into an empty room expecting company.
    ///
    /// A row with nobody in it is hidden outright. An empty strip under a card
    /// reads as a thing that failed to load.
    /// </summary>
    public sealed class GalleryPresence : MonoBehaviour
    {
        const string Bridge = "http://127.0.0.1:8766";
        const float RefreshSeconds = 30f;

        /// Card element name -> catalog activity id. The same pairing
        /// MainMenuController launches by, kept here so a card that gains a friend
        /// row and a card that launches something cannot disagree.
        static readonly (string card, string activityId)[] Cards =
        {
            ("golf-presence", "golf.adaptive"),
            ("studio-presence", "rehab.studio"),
            ("bowling-presence", "bowling.adaptive"),
        };

#pragma warning disable 0649
        [Serializable] class Who { public string id, displayName; public int mii; }
        [Serializable] class InActivity { public string activityId; public Who[] people; }
        [Serializable] class Feed { public InActivity[] activities; }
#pragma warning restore 0649

        VisualElement root;

        public void Attach(VisualElement tree)
        {
            root = tree;
            StopAllCoroutines();
            StartCoroutine(Poll());
        }

        /// Presence is the one thing here that is only true for a moment, so it is
        /// asked for again while the gallery is up rather than read once on open.
        IEnumerator Poll()
        {
            var wait = new WaitForSeconds(RefreshSeconds);
            while (true)
            {
                yield return Refresh();
                yield return wait;
            }
        }

        IEnumerator Refresh()
        {
            if (root == null) yield break;
            using var request = UnityWebRequest.Get(Bridge + "/api/friends/presence");
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var feed = JsonUtility.FromJson<Feed>(request.downloadHandler.text);
            var byActivity = new Dictionary<string, Who[]>();
            foreach (var entry in feed?.activities ?? new InActivity[0])
                if (!string.IsNullOrEmpty(entry?.activityId)) byActivity[entry.activityId] = entry.people;

            foreach (var (card, activityId) in Cards)
                Paint(root.Q(card), byActivity.TryGetValue(activityId, out var people) ? people : null);
        }

        void Paint(VisualElement row, Who[] people)
        {
            if (row == null) return;
            row.Clear();
            if (people == null || people.Length == 0) { row.AddToClassList("hidden"); return; }

            // Four faces at most, then a count. A row that grows without limit
            // pushes the card's own words off it.
            const int Shown = 4;
            var faces = new VisualElement();
            faces.AddToClassList("presence-faces");
            faces.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < people.Length && i < Shown; i++)
                faces.Add(MiiFace.Portrait(people[i].mii, 26f, "friend-face"));
            row.Add(faces);

            row.Add(new KText
            {
                text = Caption(people),
                size = KText.Size.Caption,
                tone = KText.Tone.Soft,
            });
            row.RemoveFromClassList("hidden");
        }

        /// Names while they fit, because "Maya is in here" is an invitation and
        /// "3 friends" is a statistic.
        static string Caption(Who[] people)
        {
            if (people.Length == 1) return people[0].displayName + " is playing";
            if (people.Length == 2) return people[0].displayName + " and " + people[1].displayName + " are playing";
            return people[0].displayName + " and " + (people.Length - 1) + " others are playing";
        }
    }
}
