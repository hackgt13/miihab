using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using Kinesthetic;
using Kinesthetic.UI;

namespace Kinesthetic.Menu
{
    /// <summary>
    /// The friends surface on the landing page: who you follow, a thread with each
    /// of them, quick encouragements, notes and photos.
    ///
    /// Everything comes from the local bridge on 8766. Nothing here shows another
    /// person's measurements — two patients at different stages are not comparable,
    /// and a shoulder angle next to someone's name invites exactly the comparison
    /// that makes people stop coming back. Activity is shared; numbers are not.
    ///
    /// Added at runtime by MainMenuController, so the generated menu scene does not
    /// need to change.
    /// </summary>
    public sealed class FriendsPanel : MonoBehaviour
    {
        const string Bridge = SocialBridge.Url;

        // JsonUtility assigns these by reflection, which the compiler cannot see.
#pragma warning disable 0649
        [Serializable] class Person { public string id, displayName; public int mii; public bool sample, following, followsMe; public int unread; }
        [Serializable] class Roster { public Person me; public Person[] friends; }
        [Serializable] class Message { public string id, from, to, at, kind, text, photoId; }
        [Serializable] class Thread { public Person person; public Message[] messages; }
        [Serializable] class Activity {
            public int streakDays, bestStreakDays, weekSessionsDone, weekSessionsGoal, programDay, programTotalDays;
            public string[] daysActive;
        }
        [Serializable] class ProfileView { public Person person; public Activity activity; public int lastActiveDays; public string[] goalComponents; }
        [Serializable] class Code { public string code; }
        [Serializable] class PhotoId { public string photoId; }
        [Serializable] class ActivityLine { public string id, line; }
        /// The coordinator's read on who to surface and what each person has been
        /// up to. Null until it arrives, and it may never arrive — every use site
        /// falls back to what the panel did before.
        [Serializable] class SpotlightInfo { public string choose; public ActivityLine[] activity; }
        [Serializable] class Recap { public string recap; }
        [Serializable] class Draft { public string draft; }
#pragma warning restore 0649

        static VisualElement Face(int variant, string cssClass)
        {
            var face = new KMiiFace { variant = variant };
            face.AddToClassList(cssClass);
            return face;
        }

        VisualElement root, overlay, facesRow, list, threadView;
        // Faces are KMiiFace elements rather than painted into plain ones, so the headset's replica draws them too.
        KMiiFace threadFace, spotlightFace, profileFace;
        ScrollView threadScroll;
        VisualElement spotlight;
        Label badge, threadName, threadHint, inviteCode, notice, spotlightName, spotlightLine;
        Button spotlightReply;
        Button openButton, closeButton, inviteButton, acceptButton, sendButton, photoButton;
        TextField codeField, composer;
        ActivityNavigation navigation;

        Roster roster;
        string selectedId;
        string pendingPhotoId;
        string spotlightId;
        /// A first draft of what to say to the spotlit friend, written by Muse while the menu is up so the press
        /// that opens the thread already has it. It only ever lands in this person's own composer.
        string draftFor, draftText;
        SpotlightInfo insight;
        IntroductionsCard introductions;
        MilestoneCard milestone;
        VisualElement pageMessages, pageMeet, profileCard, profileStats, threadHead;
        KDays profileDays;   // four weeks of turning up; a component, so the headset's replica draws it too
        Button tabMessages, tabMeet;

        public void Attach(VisualElement tree, ActivityNavigation nav)
        {
            root = tree; navigation = nav;
            overlay = root.Q("friends-overlay");
            openButton = root.Q<Button>("friends");
            facesRow = root.Q("friends-faces");
            badge = root.Q<Label>("friends-badge");
            closeButton = root.Q<Button>("friends-close");
            list = root.Q<ScrollView>("friends-list")?.contentContainer;
            threadScroll = root.Q<ScrollView>("thread");
            threadView = threadScroll?.contentContainer;
            threadFace = root.Q<KMiiFace>("thread-face");
            threadName = root.Q<Label>("thread-name");
            inviteButton = root.Q<Button>("friends-invite");
            inviteCode = root.Q<Label>("invite-code");
            codeField = root.Q<TextField>("friends-code");
            acceptButton = root.Q<Button>("friends-accept");
            notice = root.Q<Label>("friends-notice");
            threadHint = root.Q<Label>("thread-hint");
            spotlight = root.Q("friend-spotlight");
            spotlightFace = root.Q<KMiiFace>("spotlight-face");
            spotlightName = root.Q<Label>("spotlight-name");
            spotlightLine = root.Q<Label>("spotlight-line");
            spotlightReply = root.Q<Button>("spotlight-reply");
            pageMessages = root.Q("page-messages");
            pageMeet = root.Q("page-meet");
            tabMessages = root.Q<Button>("tab-messages");
            tabMeet = root.Q<Button>("tab-meet");
            profileCard = root.Q("friend-profile");
            threadHead = root.Q(className: "thread-head");
            profileFace = root.Q<KMiiFace>("profile-face");
            profileStats = root.Q("profile-stats");
            profileDays = root.Q<KDays>("profile-days");
            composer = root.Q<TextField>("composer-text");
            sendButton = root.Q<Button>("composer-send");
            photoButton = root.Q<Button>("composer-photo");
            // The pane carries no modal shade, no open button and, since the Back
            // buttons went, no close button either: the ring turns to this screen
            // and MainMenu owns the button that turns it. All three were required
            // here before the panes landed, so Attach returned early on Friends.uxml
            // and nothing at all was wired — no roster, no chips, no send. The list
            // is the one thing this screen cannot do without.
            if (list == null) return;

            if (openButton != null) openButton.clicked += Open;
            if (closeButton != null) closeButton.clicked += Close;
            inviteButton.clicked += () => StartCoroutine(Invite());
            acceptButton.clicked += () => StartCoroutine(Accept());
            sendButton.clicked += () => StartCoroutine(Send(null));
            photoButton.clicked += () => StartCoroutine(AttachPhoto());
            if (spotlightReply != null)
                spotlightReply.clicked += () => { Open(); if (spotlightId != null) { SelectPerson(spotlightId); StartCoroutine(OfferDraft(spotlightId)); } };
            composer.RegisterCallback<KeyDownEvent>(e =>
            { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { StartCoroutine(Send(null)); e.StopPropagation(); } });
            if (overlay != null)
                root.RegisterCallback<NavigationCancelEvent>(e =>
                { if (!overlay.ClassListContains("hidden")) { Close(); e.StopPropagation(); } });

            root.Q<Button>("profile-close")?.RegisterCallback<ClickEvent>(_ => CloseProfile());
            if (tabMessages != null) tabMessages.clicked += () => ShowPage(false);
            if (tabMeet != null) tabMeet.clicked += () => ShowPage(true);

            // Mounted only where the tree offers a home for it, so a screen that
            // has not adopted the card is not broken by its absence.
            var introMount = root.Q("introductions");
            if (introMount != null)
                introductions = new IntroductionsCard(this, introMount, () => StartCoroutine(LoadRoster()), ShowMeetEmpty);
            var milestoneMount = root.Q("milestone");
            if (milestoneMount != null)
                milestone = new MilestoneCard(this, milestoneMount, () => StartCoroutine(LoadRoster()));
            StartCoroutine(LoadRoster());
        }

        /// One page at a time. The tabs are KButtons, so the look of the chosen one
        /// is a tone rather than a class this screen paints.
        void ShowPage(bool meet)
        {
            if (pageMessages == null || pageMeet == null) return;
            navigation?.PlaySelect();
            pageMessages.EnableInClassList("hidden", meet);
            pageMeet.EnableInClassList("hidden", !meet);
            if (tabMessages is KButton a) a.tone = meet ? KButton.Tone.Quiet : KButton.Tone.Primary;
            if (tabMeet is KButton b) b.tone = meet ? KButton.Tone.Primary : KButton.Tone.Quiet;
            // Ask again on arrival: someone may have become a friend since the last look.
            if (meet) introductions?.Refresh();
        }

        /// The meet page is a column of its own, so it says when there is nobody
        /// rather than leaving the space blank — unlike the card on a shared page,
        /// which simply goes away.
        void ShowMeetEmpty(bool empty)
        {
            root?.Q<Label>("meet-empty")?.EnableInClassList("hidden", !empty);
        }

        /// Clicking a face opens the person, not the conversation. Everything here
        /// is activity — whether they turned up, and how often — because friends.ts
        /// keeps one patient's measurements away from another and a degree on this
        /// card would be exactly the comparison it warns about.
        IEnumerator LoadProfile(string id)
        {
            if (profileCard == null) yield break;
            using var request = UnityWebRequest.Get(
                Bridge + "/api/friends/profile?id=" + UnityWebRequest.EscapeURL(id));
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var view = JsonUtility.FromJson<ProfileView>(request.downloadHandler.text);
            if (view?.person == null) yield break;
            PaintProfile(view);
        }

        void PaintProfile(ProfileView view)
        {
            root.Q<Label>("profile-name").text = view.person.displayName
                + (view.person.sample ? "  ·  sample friend" : "");
            root.Q<Label>("profile-goal").text = view.goalComponents is {Length: > 0}
                ? "Working toward " + string.Join(" and ", view.goalComponents)
                : "";

            if (profileFace != null) profileFace.variant = view.person.mii;

            profileStats.Clear();
            var note = root.Q<Label>("profile-note");
            if (view.activity == null)
            {
                // Someone whose coordinator we cannot reach. Say so rather than
                // drawing zeroes, which would read as "they have done nothing".
                note.text = view.lastActiveDays == 0 ? "Here today. Nothing else to show yet."
                    : "Nothing to show yet. Last here " + Days(view.lastActiveDays) + ".";
                if (profileDays != null) profileDays.levels = "";
                ShowProfile(true);
                return;
            }

            var a = view.activity;
            Stat(a.streakDays.ToString(), a.streakDays == 1 ? "day streak" : "day streak");
            Stat(a.bestStreakDays.ToString(), "best so far");
            Stat(a.weekSessionsDone + "/" + a.weekSessionsGoal, "this week");
            Stat(a.programDay.ToString(), "of " + a.programTotalDays + " days");
            note.text = view.lastActiveDays == 0 ? "Here today." : "Last here " + Days(view.lastActiveDays) + ".";

            // Four weeks of turning up, one square a day. No numbers on it: this is
            // a rhythm, not a score.
            var active = new HashSet<string>(a.daysActive ?? new string[0]);
            if (profileDays != null) profileDays.levels = Days28(active);
            ShowProfile(true);
        }

        /// The profile and the conversation are two views of one person, so only
        /// one is up at a time.
        void ShowProfile(bool on)
        {
            profileCard?.EnableInClassList("hidden", !on);
            threadHead?.EnableInClassList("hidden", on);
            threadScroll?.EnableInClassList("hidden", on);
            root.Q(className: "composer")?.EnableInClassList("hidden", on);
        }

        static string Days(int n) => n == 0 ? "today" : n == 1 ? "yesterday" : n + " days ago";

        void Stat(string value, string caption)
        {
            var tile = new VisualElement();
            tile.AddToClassList("profile-stat");
            var v = new KText { text = value, size = KText.Size.Title, tone = KText.Tone.Ink };
            var c = new KText { text = caption, size = KText.Size.Caption, tone = KText.Tone.Soft };
            tile.Add(v); tile.Add(c);
            profileStats.Add(tile);
        }

        /// Four weeks back, oldest first, a '1' for a day they moved — what KDays draws as a strip.
        static string Days28(HashSet<string> active)
        {
            const int Days = 28;
            var text = new System.Text.StringBuilder(Days);
            var today = DateTime.Now.Date;
            for (int i = 0; i < Days; i++)
                text.Append(active.Contains(today.AddDays(-(Days - 1 - i)).ToString("yyyy-MM-dd")) ? '1' : '0');
            return text.ToString();
        }

        void CloseProfile()
        {
            navigation?.PlayBack();
            ShowProfile(false);
        }

        public void Open()
        {
            navigation?.PlaySelect();
            overlay?.RemoveFromClassList("hidden");
            (closeButton ?? inviteButton)?.Focus();
            StartCoroutine(LoadRoster());
        }

        void Close()
        {
            overlay?.AddToClassList("hidden");
            navigation?.PlayBack();
            openButton?.Focus();
        }

        // ---- data ------------------------------------------------------------

        IEnumerator LoadRoster()
        {
            using var request = UnityWebRequest.Get(Bridge + "/api/friends");
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                SetNotice("The local bridge is not running. Start it with scripts/start_demo_services.sh.");
                yield break;
            }
            roster = JsonUtility.FromJson<Roster>(request.downloadHandler.text);
            // Opening onto an empty panel reads as "nothing here". Start on someone.
            if (selectedId == null && roster.friends != null && roster.friends.Length > 0)
                selectedId = roster.friends[0].id;
            PaintFaces();
            PaintList();
            PaintSpotlight();
            if (selectedId != null) yield return LoadThread(selectedId);
            // Deliberately not awaited: a model call is seconds, and the panel is
            // already correct without it. It repaints if and when it lands.
            StartCoroutine(LoadInsight());
            // After the roster, so anyone already a friend is excluded from it.
            introductions?.Refresh();
            milestone?.Refresh();
        }

        /// Asks the coordinator who deserves the spotlight and what each person has
        /// been doing. Any failure leaves `insight` null, which every reader treats
        /// as "no opinion today".
        IEnumerator LoadInsight()
        {
            using var request = UnityWebRequest.Get(Bridge + "/api/friends/spotlight");
            request.timeout = 20;   // a model call, not a file read
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var next = JsonUtility.FromJson<SpotlightInfo>(request.downloadHandler.text);
            if (next == null || string.IsNullOrEmpty(next.choose)) yield break;
            insight = next;
            PaintList();
            PaintSpotlight();
        }

        /// The narrated line for one person, or null when there is nothing to say.
        string ActivityFor(string id)
        {
            if (insight == null || insight.activity == null) return null;
            foreach (var entry in insight.activity)
                if (entry.id == id && !string.IsNullOrEmpty(entry.line)) return entry.line;
            return null;
        }

        IEnumerator LoadThread(string id)
        {
            using var request = UnityWebRequest.Get(Bridge + "/api/friends/thread?id=" + UnityWebRequest.EscapeURL(id));
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            ShowProfile(false);
            PaintThread(JsonUtility.FromJson<Thread>(request.downloadHandler.text));
            yield return LoadRecap(id);
        }

        /// One line describing what this conversation has been about, shown where
        /// the message count normally sits. Short threads and an unreachable model
        /// both leave the count in place.
        IEnumerator LoadRecap(string id)
        {
            if (threadHint == null) yield break;
            using var request = UnityWebRequest.Get(
                Bridge + "/api/friends/recap?id=" + UnityWebRequest.EscapeURL(id));
            request.timeout = 20;   // a model call, not a file read
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var body = JsonUtility.FromJson<Recap>(request.downloadHandler.text);
            // Guard the id: the user can pick someone else while this is in flight.
            if (body != null && !string.IsNullOrEmpty(body.recap) && id == selectedId)
                threadHint.text = body.recap;
        }

        IEnumerator Post(string path, string body, Action<string> done = null)
            => SocialBridge.Post(path, body, done, _ => SetNotice("That did not go through. Is the bridge running?"));

        IEnumerator Invite()
        {
            yield return Post("/api/friends/invite", "{}", text =>
            {
                var code = JsonUtility.FromJson<Code>(text);
                inviteCode.text = code.code;
                inviteCode.RemoveFromClassList("hidden");
                SetNotice("Share this code. They enter it below to connect.");
            });
        }

        IEnumerator Accept()
        {
            string code = (codeField.value ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0) { SetNotice("Enter the code your friend gave you."); yield break; }
            yield return Post("/api/friends/accept", "{\"code\":\"" + Escape(code) + "\"}", _ =>
            {
                codeField.value = "";
                SetNotice("Connected.");
            });
            yield return LoadRoster();
        }

        IEnumerator Send(string kind)
        {
            if (selectedId == null) { SetNotice("Pick someone first."); yield break; }
            string text = (composer.value ?? "").Trim();
            if (kind == null && text.Length == 0 && pendingPhotoId == null)
            { SetNotice("Add a note, a photo, or tap an encouragement."); yield break; }

            var body = new StringBuilder("{\"to\":\"").Append(Escape(selectedId)).Append('"');
            if (kind != null) body.Append(",\"kind\":\"").Append(kind).Append('"');
            if (text.Length > 0) body.Append(",\"text\":\"").Append(Escape(text)).Append('"');
            if (pendingPhotoId != null) body.Append(",\"photoId\":\"").Append(Escape(pendingPhotoId)).Append('"');
            body.Append('}');

            yield return Post("/api/friends/message", body.ToString(), _ =>
            {
                composer.value = ""; pendingPhotoId = null;
                photoButton.text = "Photo"; SetNotice("");
            });
            yield return LoadThread(selectedId);
        }

        /// Reads an image the person already has. No camera capture here: a photo
        /// should be something they chose to share, not something taken of them.
        IEnumerator AttachPhoto()
        {
            string path = UnityEditorPathHack();
            if (string.IsNullOrEmpty(path)) { SetNotice("Put a .jpg or .png in the project folder to attach it."); yield break; }
            byte[] bytes;
            try { bytes = System.IO.File.ReadAllBytes(path); }
            catch { SetNotice("Could not read that image."); yield break; }

            string type = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png"
                        : path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : "image/jpeg";
            using var request = new UnityWebRequest(Bridge + "/api/friends/photo", "POST");
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", type);
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { SetNotice("The photo did not upload."); yield break; }
            pendingPhotoId = JsonUtility.FromJson<PhotoId>(request.downloadHandler.text).photoId;
            photoButton.text = "Photo ✓";
            SetNotice("Photo attached. Add a note if you like, then Send.");
        }

        static string UnityEditorPathHack()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorUtility.OpenFilePanel("Choose a photo", "", "jpg,jpeg,png,webp");
#else
            return null;
#endif
        }

        // ---- painting ---------------------------------------------------------

        void PaintFaces()
        {
            // The stacked faces and the count live on the button that opens this, which
            // the pane does not have.
            facesRow?.Clear();
            int unread = 0, shown = 0;
            foreach (var person in roster.friends)
            {
                unread += person.unread;
                if (shown++ >= 3) continue;
                facesRow?.Add(Face(person.mii, "friend-face"));
            }
            if (badge == null) return;
            if (unread > 0) { badge.text = unread.ToString(); badge.RemoveFromClassList("hidden"); }
            else badge.AddToClassList("hidden");
        }

        void PaintList()
        {
            list.Clear();
            foreach (var person in roster.friends)
            {
                var row = new VisualElement();
                row.AddToClassList("friend-row");
                row.EnableInClassList("selected", person.id == selectedId);

                // The face opens the person; the rest of the row opens the thread.
                var portrait = Face(person.mii, "friend-row-face");
                portrait.pickingMode = PickingMode.Position;
                portrait.tooltip = "See how " + person.displayName + " is doing";
                string portraitId = person.id;
                // Down, not click: the row selects on PointerDown and repaints the
                // whole list, so this element is gone before a click could finish on
                // it. Stopping here also keeps the press from selecting the person.
                portrait.RegisterCallback<PointerDownEvent>(e =>
                { StartCoroutine(LoadProfile(portraitId)); e.StopPropagation(); });
                row.Add(portrait);

                var copy = new VisualElement();
                copy.AddToClassList("friend-row-copy");
                var name = new Label(person.displayName);
                name.AddToClassList("friend-row-name");
                // Deliberately not a score or an angle: only whether they are around.
                // The narrated line says more than the follow state when there is one.
                var meta = new Label(ActivityFor(person.id)
                    ?? (person.sample ? "Sample friend"
                    : person.followsMe && person.following ? "You follow each other"
                    : person.following ? "You follow them" : "Follows you"));
                meta.AddToClassList("friend-row-meta");
                copy.Add(name); copy.Add(meta);
                row.Add(copy);

                var dot = new VisualElement();
                dot.AddToClassList("friend-row-dot");
                dot.EnableInClassList("hidden", person.unread == 0);
                row.Add(dot);

                var follow = new Button { text = person.following ? "Following" : "Follow" };
                follow.AddToClassList("follow-pill");
                follow.EnableInClassList("following", person.following);
                string id = person.id; bool now = person.following;
                follow.clicked += () => StartCoroutine(Follow(id, !now));
                row.Add(follow);

                var message = new Button { text = person.unread > 0 ? "Read" : "Message" };
                message.AddToClassList("message-pill");
                message.clicked += () => { SelectPerson(id); composer.Focus(); };
                row.Add(message);

                row.RegisterCallback<PointerDownEvent>(_ => SelectPerson(id));
                list.Add(row);
            }
        }

        /// <summary>
        /// One friend on the landing page, cheering you on.
        ///
        /// If they actually sent something, it is shown verbatim. If they have not,
        /// this states the friendship instead of inventing a quote — a fabricated
        /// "Maya is proud of you" would be a real betrayal of someone at a low
        /// point, and there is a local language model in this repo that could
        /// produce one convincingly.
        /// </summary>
        void PaintSpotlight()
        {
            if (spotlight == null || roster?.friends == null || roster.friends.Length == 0)
            {
                spotlight?.AddToClassList("hidden");
                return;
            }

            // The model saw the unread counts and the gaps and was asked to weigh
            // them, so its pick wins when there is one. Without it, prefer someone
            // actually waiting on you and take any of them at random.
            Person chosen = null;
            if (insight != null)
                foreach (var person in roster.friends)
                    if (person.id == insight.choose) chosen = person;
            if (chosen == null)
            {
                var waiting = new List<Person>();
                foreach (var person in roster.friends) if (person.unread > 0) waiting.Add(person);
                var pool = waiting.Count > 0 ? waiting : new List<Person>(roster.friends);
                chosen = pool[UnityEngine.Random.Range(0, pool.Count)];
            }
            spotlightId = chosen.id;

            if (spotlightFace != null) spotlightFace.variant = chosen.mii;

            spotlightName.text = chosen.displayName + (chosen.sample ? " · sample friend" : "");
            spotlight.RemoveFromClassList("hidden");
            StartCoroutine(FillSpotlightLine(chosen));
        }

        IEnumerator FetchDraft(string id)
        {
            using var request = UnityWebRequest.Get(Bridge + "/api/friends/draft?id=" + UnityWebRequest.EscapeURL(id));
            request.timeout = 15;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var body = JsonUtility.FromJson<Draft>(request.downloadHandler.text);
            if (body == null || string.IsNullOrEmpty(body.draft)) yield break;
            draftFor = id; draftText = body.draft;
        }

        /// Puts the draft in the composer, selected, so one press sends it and any key replaces it. Never over
        /// something the person already started typing, and never sent from here.
        IEnumerator OfferDraft(string id)
        {
            if (draftFor != id) yield return FetchDraft(id);
            if (draftFor != id || selectedId != id || composer == null) yield break;
            if (!string.IsNullOrWhiteSpace(composer.value)) yield break;
            composer.value = draftText;
            composer.Focus();
            composer.SelectAll();
        }

        IEnumerator FillSpotlightLine(Person person)
        {
            // Asked each time the spotlight paints; the coordinator caches on the thread's tail, so it is new only when the thread is.
            StartCoroutine(FetchDraft(person.id));
            // Narration, not speech: "returned after three days" is the app talking.
            // A message they actually sent replaces it verbatim below.
            var activity = ActivityFor(person.id);
            // The name label sits directly above, so the line is the predicate only:
            // "Maya" / "returned after three days."
            spotlightLine.text = activity != null ? activity + "." : "is in your corner today.";
            spotlightReply.text = "Say hello";

            using var request = UnityWebRequest.Get(
                Bridge + "/api/friends/thread?id=" + UnityWebRequest.EscapeURL(person.id));
            request.timeout = 5;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;

            var thread = JsonUtility.FromJson<Thread>(request.downloadHandler.text);
            if (thread?.messages == null) yield break;

            // Their most recent message to me, quoted exactly as they wrote it.
            Message latest = null;
            foreach (var message in thread.messages)
                if (message.from == person.id) latest = message;
            if (latest == null) yield break;

            string said = !string.IsNullOrEmpty(latest.kind) ? LabelFor(latest.kind) : latest.text;
            if (string.IsNullOrEmpty(said) && !string.IsNullOrEmpty(latest.photoId)) said = "sent you a photo";
            if (string.IsNullOrEmpty(said)) yield break;

            spotlightLine.text = "\u201c" + said + "\u201d";
            spotlightReply.text = "Say something back";
        }

        IEnumerator Follow(string id, bool following)
        {
            yield return Post("/api/friends/follow",
                "{\"id\":\"" + Escape(id) + "\",\"following\":" + (following ? "true" : "false") + "}");
            yield return LoadRoster();
        }

        void SelectPerson(string id)
        {
            selectedId = id;
            navigation?.PlayHover();
            PaintList();
            StartCoroutine(LoadThread(id));
        }

        void PaintThread(Thread thread)
        {
            threadName.text = thread.person.displayName;
            if (threadFace != null) threadFace.variant = thread.person.mii;
            threadView.Clear();

            if (threadHint != null)
                threadHint.text = thread.messages == null || thread.messages.Length == 0
                    ? "" : thread.messages.Length + (thread.messages.Length == 1 ? " message" : " messages");

            if (thread.messages == null || thread.messages.Length == 0)
            {
                var empty = new Label($"Nothing here yet.\nTap an encouragement below — one word is plenty.");
                empty.AddToClassList("thread-empty");
                threadView.Add(empty);
                return;
            }

            foreach (var message in thread.messages)
            {
                var bubble = new KMessage
                {
                    side = message.from == roster.me.id ? KMessage.Side.Mine : KMessage.Side.Theirs,
                    said = string.IsNullOrEmpty(message.kind) ? "" : LabelFor(message.kind),
                    text = message.text,
                    time = ShortTime(message.at),
                };
                if (!string.IsNullOrEmpty(message.photoId))
                {
                    var photo = new VisualElement();
                    photo.AddToClassList("bubble-photo");
                    bubble.Attachments.Add(photo);
                    StartCoroutine(LoadPhoto(message.photoId, photo));
                }
                threadView.Add(bubble);
            }
            threadView.schedule.Execute(() => threadView.parent?.Focus());
            ScrollToEnd();
        }

        /// A repaint leaves the view where it was, which on a long thread is the
        /// top — so the message you just sent lands off-screen below. End at the
        /// newest one instead.
        ///
        /// Past the end clamps to the end, so the content's own height is "the
        /// bottom" and the maximum never has to be worked out. The bubbles have no
        /// layout on the frame they are added, though, so this also waits for the
        /// geometry pass: that is the signal a delay would only be guessing at.
        void ScrollToEnd()
        {
            if (threadScroll == null || threadView == null) return;
            threadScroll.scrollOffset = new Vector2(0, threadView.layout.height);
            threadView.UnregisterCallback<GeometryChangedEvent>(SettleToEnd);
            threadView.RegisterCallback<GeometryChangedEvent>(SettleToEnd);
        }

        void SettleToEnd(GeometryChangedEvent _)
        {
            threadView.UnregisterCallback<GeometryChangedEvent>(SettleToEnd);
            threadScroll.scrollOffset = new Vector2(0, threadView.layout.height);
        }

        IEnumerator LoadPhoto(string photoId, VisualElement target)
        {
            using var request = UnityWebRequestTexture.GetTexture(
                Bridge + "/api/friends/photo/" + UnityWebRequest.EscapeURL(photoId));
            request.timeout = 10;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) yield break;
            var texture = DownloadHandlerTexture.GetContent(request);
            target.style.backgroundImage = new StyleBackground(texture);
            // The photo lands after the scroll and grows the thread beneath it, so
            // the newest message slips below the fold again unless we follow it.
            // The thread may have been repainted for someone else while this was in
            // flight, which is why the last bubble is re-read rather than captured.
            if (threadView != null && threadView.childCount > 0
                && target.parent?.parent == threadView[threadView.childCount - 1]) ScrollToEnd();
        }

        static string LabelFor(string kind) => SocialBridge.LabelFor(kind);
        static string ShortTime(string iso) => SocialBridge.ShortTime(iso);

        void SetNotice(string text) { if (notice != null) notice.text = text; }

        static string Escape(string value) => SocialBridge.Escape(value);
    }
}
