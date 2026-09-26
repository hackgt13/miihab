using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using Kinesthetic.Activities;
using Kinesthetic.UI;

namespace Kinesthetic.Menu
{
    /// <summary>
    /// Group therapy: the choice before an activity — alone, with friends who are playing it now, or with
    /// an open group — and, once in a group, the room itself: who is in it (the Tab list, and a fuller list
    /// with "Add friend"), and a chat in the same fixed vocabulary as the friends pane.
    ///
    /// Lives on the ActivityNavigation object, which outlives scene loads, so the room chosen in the menu
    /// is still there when the venue opens. The coordinator (coordinator/groups.ts) is the authority on
    /// who is in which room; this only asks and draws.
    ///
    /// Like the friends pane, nothing here shows anyone's measurements. A room shares presence and
    /// encouragement, never reps or degrees.
    ///
    /// Screen-space for now, like the rest of the navigation layer it mounts into: it draws on the Mac and
    /// not yet on the headset, and it answers the pointer and the keyboard, not the gaze.
    /// </summary>
    public sealed class GroupPanel : MonoBehaviour
    {
        // JsonUtility assigns these by reflection, which the compiler cannot see.
#pragma warning disable 0649
        [Serializable] class Face { public string id, displayName; public int mii; public bool sample; }
        [Serializable] class Summary { public string id, activityId, title; public bool open, sample; public int size, capacity, minutes; public Face[] friends; }
        [Serializable] class Member { public string id, displayName; public int mii; public bool sample, isMe, host, friend; }
        [Serializable] class Line { public string id, from, name, at, @event, kind, text; }
        [Serializable] class Room { public string id, activityId, title; public bool open, sample; public int capacity; public Member[] members; public Line[] messages; }
        [Serializable] class Lobby { public Summary[] friendsPlaying, open; public Room current; }
        [Serializable] class RoomReply { public Room group; }
        [Serializable] class Befriended { public Face person; public Room group; }
#pragma warning restore 0649

        const float PollSeconds = 3f, TickerSeconds = 6f;

        /// Activities that are not something you do alongside other patients. The therapist's visit is a
        /// one-to-one appointment.
        static readonly HashSet<string> SoloOnly = new() { "therapist.visit" };

        public static GroupPanel Instance { get; private set; }

        ActivityNavigation navigation;
        VisualElement lobby, choose, others, roomLayer, tablist, tablistRows, roomList, groupsList, cheers, ticker;
        ScrollView thread;
        KText lobbyTitle, lobbyNotice, roomTitle, roomNotice, tickerLine, lobbyEmpty;
        KEyebrow lobbyEyebrow, roomCount, tablistTitle;
        KTag roomSample;
        KButton openButton, hostButton;
        KField field;

        ActivityEntry pending;
        Action proceed;
        Room room;
        string lastSeen;         // the newest message id already shown, so the ticker only announces new ones
        string lastMembers;      // who was in the room, and who was a friend, when it was last drawn
        float nextPoll, tickerUntil;
        bool inActivity;
        bool othersOpen, roomOpen;   // what was up, so a rebuilt tree can put it back (see Mount)

        /// A layer of this panel is up and should hold the activity (GolfScreens pauses its countdown on this).
        public bool Showing => Visible(lobby) || Visible(roomLayer);
        public bool InRoom => room != null;

        /// Someone in the room with you, as a venue draws them: a name, a Mii, and whether they are a sample.
        public readonly struct Person
        {
            public readonly string Id, Name; public readonly int Mii; public readonly bool Sample;
            public Person(string id, string name, int mii, bool sample) { Id = id; Name = name; Mii = mii; Sample = sample; }
        }

        /// The other person a venue shows beside you: the first member of the room who is not you. Null out of a
        /// room, or alone in one. A studio shows one partner; the rest of the room is on the Tab list.
        public Person? Partner
        {
            get
            {
                if (room?.members == null) return null;
                foreach (var m in room.members) if (!m.isMe) return new Person(m.id, m.displayName, m.mii, m.sample);
                return null;
            }
        }

        /// The room, or who is in it, changed.
        public event Action RoomChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static bool Visible(VisualElement e) => e != null && !e.ClassListContains("hidden");
        static void Show(VisualElement e, bool on) => e?.EnableInClassList("hidden", !on);

        /// Called by ActivityNavigation once its tree exists. Idempotent: a second call finds the layer.
        public void Mount(VisualElement root, ActivityNavigation nav)
        {
            navigation = nav;
            if (root == null || root.Q("group-layer") != null) return;
            // The navigation document rebuilds its tree on some scene changes, and a fresh copy of this layer
            // comes up with everything hidden. Whatever was open before is put back at the end of Mount.
            bool lobbyWasOpen = pending != null, othersWereOpen = othersOpen, roomWasOpen = roomOpen;
            var tree = Resources.Load<VisualTreeAsset>("Menu/Group");
            if (!tree) { Debug.LogError("Group session layout missing at Resources/Menu/Group.uxml"); return; }
            tree.CloneTree(root);

            lobby = root.Q("group-lobby"); choose = root.Q("lobby-choose"); others = root.Q("lobby-others");
            lobbyTitle = root.Q<KText>("lobby-title"); lobbyNotice = root.Q<KText>("lobby-notice");
            lobbyEyebrow = root.Q<KEyebrow>("lobby-eyebrow");
            groupsList = root.Q<ScrollView>("lobby-groups").contentContainer;
            lobbyEmpty = root.Q<KText>("lobby-empty");
            hostButton = root.Q<KButton>("lobby-host");

            roomLayer = root.Q("group-room"); roomTitle = root.Q<KText>("room-title");
            roomCount = root.Q<KEyebrow>("room-count"); roomNotice = root.Q<KText>("room-notice");
            roomSample = root.Q<KTag>("room-sample");
            roomList = root.Q<ScrollView>("room-list").contentContainer;
            thread = root.Q<ScrollView>("room-thread");
            cheers = root.Q("room-cheers"); field = root.Q<KField>("room-text");
            openButton = root.Q<KButton>("group-open");
            ticker = root.Q("group-ticker"); tickerLine = root.Q<KText>("group-ticker-line");
            tablist = root.Q("group-tablist"); tablistRows = root.Q("tablist-rows");
            tablistTitle = root.Q<KEyebrow>("tablist-title");

            root.Q<KOption>("lobby-solo").clicked += () => StartCoroutine(Solo());
            root.Q<KOption>("lobby-together").clicked += ShowOthers;
            root.Q<KButton>("lobby-back").clicked += Back;
            hostButton.clicked += () => StartCoroutine(Host());
            openButton.clicked += OpenRoom;
            root.Q<KButton>("room-close").clicked += CloseRoom;
            root.Q<KButton>("room-leave").clicked += () => StartCoroutine(Leave(true));
            root.Q<KButton>("room-send").clicked += () => StartCoroutine(Send(null));
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) { StartCoroutine(Send(null)); e.StopPropagation(); }
            });
            foreach (var (kind, label) in SocialBridge.Encouragements)
            {
                var cheer = new KButton { text = label, tone = KButton.Tone.Secondary, size = KButton.Size.Small };
                cheer.clicked += () => StartCoroutine(Send(kind));
                cheers.Add(cheer);
            }

            if (lobbyWasOpen) { lobbyEyebrow.text = pending.DisplayName.ToUpperInvariant(); ShowChoose(); Show(lobby, true); if (othersWereOpen) ShowOthers(); }
            if (roomWasOpen && room != null) OpenRoom();

            // Someone may already be in a room from before this layer existed (a scene opened directly).
            StartCoroutine(Refresh());
        }

        // ---- before an activity ----------------------------------------------------------------------

        /// Stand between a launch and the activity. False when this activity is not a group one, or the
        /// layer is not mounted, and the caller should simply go.
        public bool Intercept(ActivityEntry entry, Action go)
        {
            if (lobby == null || entry == null || SoloOnly.Contains(entry.Id)) return false;
            // A press that lands behind the open lobby (the menu's panes are picked by their own ray, not
            // through this layer) must not swap the activity out from under the person choosing.
            if (Visible(lobby)) return true;
            pending = entry; proceed = go;
            lobbyEyebrow.text = entry.DisplayName.ToUpperInvariant();
            ShowChoose();
            Show(lobby, true);
            navigation?.PlaySelect();
            lobby.schedule.Execute(() => lobby.Q<KOption>("lobby-together")?.Focus());
            return true;
        }

        void ShowChoose()
        {
            lobbyTitle.text = "How would you like to play?";
            lobbyNotice.text = "";
            othersOpen = false;
            Show(choose, true); Show(others, false); Show(hostButton, false);
        }

        void ShowOthers()
        {
            navigation?.PlaySelect();
            lobbyTitle.text = "Pick a group";
            othersOpen = true;
            Show(choose, false); Show(others, true); Show(hostButton, true);
            groupsList.Clear();
            Show(lobbyEmpty, false);
            lobbyNotice.text = "Looking for groups…";
            StartCoroutine(SocialBridge.Get("/api/groups?activity=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(pending.Id),
                PaintLobby,
                () => lobbyNotice.text = "The local bridge is not running, so there is nobody to join. You can still play solo."));
        }

        void Back()
        {
            navigation?.PlayBack();
            if (Visible(others)) { ShowChoose(); lobby.Q<KOption>("lobby-together")?.Focus(); return; }
            CloseLobby();
        }

        void CloseLobby() { Show(lobby, false); pending = null; proceed = null; othersOpen = false; }

        /// One list. Groups with a friend in them come first and are highlighted, with that friend's face
        /// on the row; the coordinator already sends them apart (groups.ts `lobby`), so the order is theirs.
        void PaintLobby(string json)
        {
            var reply = JsonUtility.FromJson<Lobby>(json);
            lobbyNotice.text = "";
            groupsList.Clear();
            foreach (var group in reply?.friendsPlaying ?? new Summary[0]) groupsList.Add(GroupRow(group));
            foreach (var group in reply?.open ?? new Summary[0]) groupsList.Add(GroupRow(group));
            Show(lobbyEmpty, groupsList.childCount == 0);
        }

        /// A group, pressed as a whole to join it. Friends are named by face: you know them already. The
        /// strangers in a group are only its size until you are in it (groups.ts), so they have no faces.
        VisualElement GroupRow(Summary group)
        {
            bool friends = group.friends is { Length: > 0 };
            string id = group.id;
            var row = new KOption(() => StartCoroutine(Join(id))) { tone = friends ? KOption.Tone.Highlight : KOption.Tone.Plain };
            bool full = group.size >= group.capacity;
            row.SetEnabled(!full);
            row.tooltip = full ? "This group is full" : "Join " + group.title;

            var copy = new VisualElement { pickingMode = PickingMode.Ignore };
            copy.AddToClassList("group-row-copy");
            copy.Add(new KText { text = group.title, size = KText.Size.Body });
            var meta = new VisualElement { pickingMode = PickingMode.Ignore };
            meta.AddToClassList("group-row-meta");
            meta.Add(new KText
            {
                text = full ? $"Full · {group.size} of {group.capacity}"
                    : $"{group.size} of {group.capacity} · going for {Minutes(group.minutes)}",
                size = KText.Size.Caption, tone = KText.Tone.Soft,
            });
            if (group.sample) meta.Add(new KTag("SAMPLE"));
            copy.Add(meta);
            row.Add(copy);

            if (friends)
            {
                var tags = new VisualElement { pickingMode = PickingMode.Ignore };
                tags.AddToClassList("group-row-friends");
                foreach (var friend in group.friends)
                    tags.Add(new KMiiTag { variant = friend.mii, text = friend.displayName });
                row.Add(tags);
            }

            // Where the eye lands to see that the row goes somewhere. Words, not a button: the row is the button.
            var go = new KText { text = full ? "" : "Join  ›", size = KText.Size.Caption, tone = KText.Tone.Ink };
            go.AddToClassList("group-row-go");
            row.Add(go);
            return row;
        }

        static string Minutes(int m) => m < 1 ? "a moment" : m == 1 ? "a minute" : m < 60 ? m + " minutes" : (m / 60) + "h " + (m % 60) + "m";

        IEnumerator Solo()
        {
            // Solo means no room: leave whatever one was left over from before. A bridge that is not
            // running has no room to leave, so a failure here is not a reason to stop.
            yield return SocialBridge.Post("/api/groups/leave", "{}");
            SetRoom(null);
            Go();
        }

        IEnumerator Join(string id)
        {
            lobbyNotice.text = "Joining…";
            yield return SocialBridge.Post("/api/groups/join", "{\"id\":\"" + SocialBridge.Escape(id) + "\"}",
                json => { SetRoom(JsonUtility.FromJson<RoomReply>(json)?.group); Go(); },
                reason => lobbyNotice.text = reason ?? "That did not go through. Is the bridge running?");
        }

        IEnumerator Host()
        {
            lobbyNotice.text = "Starting a group…";
            yield return SocialBridge.Post("/api/groups",
                "{\"activityId\":\"" + SocialBridge.Escape(pending.Id) + "\",\"open\":true}",
                json => { SetRoom(JsonUtility.FromJson<RoomReply>(json)?.group); Go(); },
                reason => lobbyNotice.text = reason ?? "That did not go through. Is the bridge running?");
        }

        void Go()
        {
            var go = proceed;
            Show(lobby, false); pending = null; proceed = null; othersOpen = false;
            go?.Invoke();
        }

        // ---- during an activity ----------------------------------------------------------------------

        /// ActivityNavigation calls this on every scene change. The room shows only inside the activity it
        /// is for; the menu has its own friends pane.
        public void SceneChanged(bool menu)
        {
            inActivity = !menu;
            if (menu) { roomOpen = false; Show(roomLayer, false); Show(tablist, false); Show(ticker, false); }
            PaintChrome();
            nextPoll = 0;
        }

        /// Back to the menu leaves the room: the session was the activity, and the activity is over.
        public IEnumerator LeaveOnReturn()
        {
            if (room == null) yield break;
            yield return SocialBridge.Post("/api/groups/leave", "{}");
            SetRoom(null);
        }

        void OpenRoom()
        {
            if (room == null) return;
            navigation?.PlaySelect();
            roomNotice.text = "";
            PaintRoom();
            Show(roomLayer, true); Show(ticker, false); roomOpen = true;
            field.schedule.Execute(() => field.Focus());
        }

        /// Escape, routed here by ActivityNavigation before it considers its own dialog.
        public void Cancel()
        {
            if (Visible(roomLayer)) CloseRoom();
            else if (Visible(lobby)) Back();
        }

        void CloseRoom() { Show(roomLayer, false); roomOpen = false; navigation?.PlayBack(); openButton?.Focus(); }

        IEnumerator Leave(bool sound)
        {
            yield return SocialBridge.Post("/api/groups/leave", "{}",
                _ => { SetRoom(null); Show(roomLayer, false); roomOpen = false; if (sound) navigation?.PlayBack(); },
                reason => roomNotice.text = reason ?? "That did not go through. Is the bridge running?");
        }

        IEnumerator Send(string kind)
        {
            if (room == null) yield break;
            string text = (field.value ?? "").Trim();
            if (kind == null && text.Length == 0) { roomNotice.text = "Write a note, or tap an encouragement."; yield break; }
            var body = new StringBuilder("{");
            if (kind != null) body.Append("\"kind\":\"").Append(kind).Append('"');
            else body.Append("\"text\":\"").Append(SocialBridge.Escape(text)).Append('"');
            body.Append('}');
            yield return SocialBridge.Post("/api/groups/message", body.ToString(),
                json => { if (kind == null) field.value = ""; roomNotice.text = ""; SetRoom(JsonUtility.FromJson<RoomReply>(json)?.group); },
                reason => roomNotice.text = reason ?? "That did not go through. Is the bridge running?");
        }

        IEnumerator Befriend(string id)
        {
            yield return SocialBridge.Post("/api/groups/befriend", "{\"id\":\"" + SocialBridge.Escape(id) + "\"}",
                json =>
                {
                    var reply = JsonUtility.FromJson<Befriended>(json);
                    SetRoom(reply?.group);
                    roomNotice.text = $"You now follow {reply?.person?.displayName}. They are on your Friends pane.";
                    navigation?.PlaySelect();
                },
                reason => roomNotice.text = reason ?? "That did not go through. Is the bridge running?");
        }

        IEnumerator Refresh()
        {
            yield return SocialBridge.Get("/api/groups/current",
                json => SetRoom(JsonUtility.FromJson<RoomReply>(json)?.group));
        }

        /// JsonUtility fills a null object in with defaults rather than leaving it null, so an empty id is
        /// how "not in a room" arrives.
        void SetRoom(Room next)
        {
            if (next != null && string.IsNullOrEmpty(next.id)) next = null;
            string newest = next?.messages is { Length: > 0 } m ? m[m.Length - 1].id : null;
            bool changed = newest != lastSeen;
            string members = next == null ? null : string.Join(",", Array.ConvertAll(next.members ?? new Member[0], m => m.id + (m.friend ? "+" : "")));
            bool moved = members != lastMembers;

            // Announce only what someone else said while the room was closed.
            if (changed && next != null && room != null && room.id == next.id && !Visible(roomLayer) && inActivity)
            {
                var last = next.messages[next.messages.Length - 1];
                if (!IsMine(next, last.from))
                {
                    tickerLine.text = Describe(last, withName: true);
                    tickerUntil = Time.unscaledTime + TickerSeconds;
                }
            }
            bool roomMoved = moved || (room?.id != next?.id);
            room = next; lastSeen = newest; lastMembers = members;
            if (roomMoved) RoomChanged?.Invoke();
            PaintChrome();
            // The poll lands every few seconds; redraw only for something new, so the list does not flicker.
            if (Visible(roomLayer)) { if (room == null) { Show(roomLayer, false); roomOpen = false; } else if (changed || moved) PaintRoom(changed); }
            if (Visible(tablist)) PaintTablist();
        }

        static bool IsMine(Room r, string id)
        {
            foreach (var m in r.members) if (m.id == id) return m.isMe;
            return false;
        }

        /// The button over play: the room's name and how many are in it. Only in the activity the room is for.
        void PaintChrome()
        {
            bool here = inActivity && room != null && room.activityId == ActivityNavigation.Current()?.Id;
            Show(openButton, here);
            if (here) openButton.text = $"Group · {room.members?.Length ?? 0}";
            if (!here) { Show(ticker, false); Show(tablist, false); }
        }

        void PaintRoom(bool scroll = true)
        {
            roomTitle.text = room.title;
            Show(roomSample, room.sample);
            roomCount.text = $"IN THE ROOM · {room.members.Length} OF {room.capacity}";
            roomList.Clear();
            foreach (var member in room.members) roomList.Add(MemberRow(member));

            var content = thread.contentContainer;
            content.Clear();
            bool crowd = room.members.Length > 2;
            foreach (var line in room.messages ?? new Line[0])
            {
                if (!string.IsNullOrEmpty(line.@event))
                {
                    content.Add(new KMessage { side = KMessage.Side.Room, text = Describe(line, withName: true) });
                    continue;
                }
                bool mine = IsMine(room, line.from);
                content.Add(new KMessage
                {
                    side = mine ? KMessage.Side.Mine : KMessage.Side.Theirs,
                    author = mine || !crowd ? "" : line.name,
                    said = string.IsNullOrEmpty(line.kind) ? "" : SocialBridge.LabelFor(line.kind),
                    text = line.text,
                    time = SocialBridge.ShortTime(line.at),
                });
            }
            if (scroll) ScrollToEnd();
        }

        /// A long room ends at its newest line. The balloons have no layout on the frame they are added, so
        /// this waits for the geometry pass, as the friends thread does.
        void ScrollToEnd()
        {
            var content = thread.contentContainer;
            thread.scrollOffset = new Vector2(0, content.layout.height);
            content.UnregisterCallback<GeometryChangedEvent>(Settle);
            content.RegisterCallback<GeometryChangedEvent>(Settle);
        }

        void Settle(GeometryChangedEvent _)
        {
            var content = thread.contentContainer;
            content.UnregisterCallback<GeometryChangedEvent>(Settle);
            thread.scrollOffset = new Vector2(0, content.layout.height);
        }

        VisualElement MemberRow(Member member)
        {
            var row = new VisualElement();
            row.AddToClassList("member-row");
            var face = new KMiiFace { variant = member.mii };
            face.AddToClassList("member-face");
            row.Add(face);

            var copy = new VisualElement { pickingMode = PickingMode.Ignore };
            copy.AddToClassList("member-copy");
            copy.Add(new KText { text = member.displayName, size = KText.Size.Body });
            var tags = new VisualElement { pickingMode = PickingMode.Ignore };
            tags.AddToClassList("member-tags");
            foreach (var tag in Tags(member)) tags.Add(tag);
            copy.Add(tags);
            row.Add(copy);

            // Following, from here, is the same act as on the friends pane: one-way, and only for someone
            // actually in the room with you.
            if (!member.isMe && !member.friend)
            {
                var add = new KButton { text = "Add friend", tone = KButton.Tone.Secondary, size = KButton.Size.Small };
                string id = member.id;
                add.clicked += () => StartCoroutine(Befriend(id));
                row.Add(add);
            }
            return row;
        }

        static IEnumerable<KTag> Tags(Member member)
        {
            if (member.isMe) yield return new KTag("YOU") { tone = KTag.Tone.Info };
            if (member.host) yield return new KTag("HOST");
            if (member.friend) yield return new KTag("FRIEND") { tone = KTag.Tone.Good };
            if (member.sample) yield return new KTag("SAMPLE");
        }

        void PaintTablist()
        {
            if (room == null) return;
            tablistTitle.text = $"{room.title.ToUpperInvariant()} · {room.members.Length} OF {room.capacity}";
            tablistRows.Clear();
            foreach (var member in room.members)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("tablist-row");
                var face = new KMiiFace { variant = member.mii };
                face.AddToClassList("tablist-face");
                row.Add(face);
                row.Add(new KText { text = member.displayName, size = KText.Size.Body, tone = KText.Tone.OnDark });
                foreach (var tag in Tags(member)) { tag.AddToClassList("tablist-tag"); row.Add(tag); }
                tablistRows.Add(row);
            }
        }

        static string Describe(Line line, bool withName)
        {
            if (line.@event == "joined") return $"{line.name} joined";
            if (line.@event == "left") return $"{line.name} left";
            string said = !string.IsNullOrEmpty(line.kind) ? SocialBridge.LabelFor(line.kind) : line.text;
            return withName ? $"{line.name}: {said}" : said;
        }

        void Update()
        {
            if (lobby == null) return;

            // Hold Tab for the list, as in any game with other people in it. Not while typing a message:
            // Tab there belongs to the field.
            bool typing = field != null && field.focusController?.focusedElement == field;
            bool holdTab = inActivity && room != null && !Visible(roomLayer) && !typing
                && Keyboard.current?.tabKey.isPressed == true;
            if (holdTab != Visible(tablist)) { Show(tablist, holdTab); if (holdTab) PaintTablist(); }

            if (Visible(ticker) != (Time.unscaledTime < tickerUntil && inActivity && room != null))
                Show(ticker, !Visible(ticker));

            if (room != null && inActivity && Time.unscaledTime >= nextPoll)
            {
                nextPoll = Time.unscaledTime + PollSeconds;
                StartCoroutine(Refresh());
            }
        }
    }
}
