using System;
using System.Linq;
using Kinesthetic.Menu;
using Kinesthetic.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// Drives the group therapy layer one step at a time, in Play mode from MainMenu, with the coordinator
/// running on 8766. Each step presses what a person would press and checks what should be true after it;
/// capture the game view between steps to see it.
public static class GroupSessionVerification
{
    static VisualElement Root()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Run in Play mode.");
        var nav = ActivityNavigation.Instance ?? throw new InvalidOperationException("No ActivityNavigation.");
        return nav.GetComponent<UIDocument>().rootVisualElement.Q("group-layer")
            ?? throw new InvalidOperationException("The group layer is not mounted.");
    }

    static bool Shown(VisualElement e) => e != null && !e.ClassListContains("hidden");

    static void Press(Button button)
    {
        if (button == null) throw new InvalidOperationException("Nothing to press.");
        using var submit = NavigationSubmitEvent.GetPooled();
        submit.target = button;
        button.SendEvent(submit);
    }

    [MenuItem("Kinesthetic/Social/1 Open the lobby for golf (Play mode)")]
    public static string OpenLobby()
    {
        var root = Root();
        ActivityNavigation.Instance.LoadActivity("golf.adaptive");
        if (!Shown(root.Q("group-lobby")) || !Shown(root.Q("lobby-choose")))
            throw new Exception("The lobby did not stand between the launch and the activity.");
        return "Lobby up on its first page.";
    }

    [MenuItem("Kinesthetic/Social/2 Play with others (Play mode)")]
    public static string Others()
    {
        var root = Root();
        Press(root.Q<KOption>("lobby-together"));
        if (!Shown(root.Q("lobby-others"))) throw new Exception("The groups page did not open.");
        return "Groups page up; the list fills when the coordinator answers. Run step 3 after a moment.";
    }

    [MenuItem("Kinesthetic/Social/3 Check the group list (Play mode)")]
    public static string CheckList()
    {
        var rows = Root().Q<ScrollView>("lobby-groups").contentContainer.Children().ToList();
        if (rows.Count == 0) throw new Exception("No groups listed. Is the coordinator running?");
        if (rows.Any(r => r is not KOption)) throw new Exception("Every group should be one KOption.");
        // The reason this list exists in this shape: nothing pressable inside a pressable row.
        foreach (var row in rows)
            if (row.Query<Button>().ToList().Any(b => b != row)) throw new Exception("A button sits inside a group row.");
        var lit = rows.Cast<KOption>().Where(r => r.tone == KOption.Tone.Highlight).ToList();
        if (lit.Any(r => r.Q<KMiiTag>() == null)) throw new Exception("A highlighted row has no friend's tag.");
        if (rows.Cast<KOption>().Any(r => r.tone == KOption.Tone.Plain && r.Q<KMiiTag>() != null))
            throw new Exception("A friend's tag is on a row that is not highlighted.");
        int first = rows.FindIndex(r => ((KOption)r).tone == KOption.Tone.Plain);
        if (first >= 0 && rows.Skip(first).Any(r => ((KOption)r).tone == KOption.Tone.Highlight))
            throw new Exception("Groups with friends should come first.");
        return $"{rows.Count} groups, {lit.Count} with friends, no nested buttons.";
    }

    [MenuItem("Kinesthetic/Social/4 Join the first group (Play mode)")]
    public static string JoinFirst()
    {
        var row = Root().Q<ScrollView>("lobby-groups").contentContainer.Children().OfType<KOption>().FirstOrDefault()
            ?? throw new Exception("No group to join.");
        Press(row);
        return "Joining; the activity loads once the coordinator answers. Run step 5 in the venue.";
    }

    [MenuItem("Kinesthetic/Social/5 Open the room (Play mode)")]
    public static string OpenRoom()
    {
        var root = Root();
        if (!Shown(root.Q("group-open"))) throw new Exception("No Group button in the venue: not in a room?");
        Press(root.Q<KButton>("group-open"));
        if (!Shown(root.Q("group-room"))) throw new Exception("The room did not open.");
        int members = root.Q<ScrollView>("room-list").contentContainer.childCount;
        return $"Room up with {members} members.";
    }

    [MenuItem("Kinesthetic/Social/6 Send an encouragement (Play mode)")]
    public static string Cheer()
    {
        var root = Root();
        Press(root.Q("room-cheers").Q<KButton>());
        return "Sent; it appears with the next reply.";
    }
}
