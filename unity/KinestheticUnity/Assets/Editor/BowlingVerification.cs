using System;
using System.Collections.Generic;
using Kinesthetic.Bowling;
using UnityEditor;
using UnityEngine;

public static class BowlingVerification
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("Bowling verification: " + message); }
    static BowlingScore Game(params int[] rolls) { var score = new BowlingScore(); foreach (int roll in rolls) score.Add(roll); return score; }
    [MenuItem("Kinesthetic/Bowling/Verify scoring and motion")]
    public static string Rules()
    {
        var perfect = Game(10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10);
        Check(perfect.Complete && perfect.Total == 300, "perfect game");
        var spare = new BowlingScore(); for (int i = 0; i < 10; i++) { spare.Add(5); spare.Add(5); } spare.Add(5);
        Check(spare.Total == 150 && spare.Complete, "spare bonuses");
        var open = new BowlingScore(); for (int i = 0; i < 10; i++) { open.Add(3); open.Add(4); }
        Check(open.Total == 70 && open.Complete, "open frames");
        var gutter = new BowlingScore(); for (int i = 0; i < 20; i++) gutter.Add(0);
        Check(gutter.Complete && gutter.Total == 0, "gutter game");
        var pending = Game(10, 7); Check(pending.Cumulative(0) == null, "pending strike"); pending.Add(2); Check(pending.Total == 28, "resolved strike");
        foreach (var final in new[] { new[] { 10, 7, 3, 20 }, new[] { 9, 1, 5, 15 }, new[] { 10, 10, 6, 26 } })
        {
            var score = new BowlingScore(); for (int i = 0; i < 18; i++) score.Add(0);
            score.Add(final[0]); score.Add(final[1]); score.Add(final[2]);
            Check(score.Complete && score.Total == final[3], "tenth frame bonus");
        }
        var bad = Game(7); bool rejected = false; try { bad.Add(4); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "too many pins accepted");
        Check(Game(10, 7, 3).Marks(1) == "7  /", "spare mark");

        var swing = new BowlingSwing(); var mount = Quaternion.Euler(47, -26, 31); double time = 0;
        void Sample(float tilt, float yaw, float rate, bool expected)
        {
            time += .04;
            var q = Quaternion.AngleAxis(yaw, Vector3.forward) * Quaternion.AngleAxis(tilt, Vector3.right) * mount;
            bool released = swing.Sample(q, new Vector3(rate, 0, 0), time, true, out float aim, out float power);
            Check(released == expected, "release timing");
            if (released) { Check(Mathf.Abs(aim + 3.5f) < .1f, "relative heading across arbitrary mount"); Check(power > .25f && power < .8f, "power mapping"); }
        }
        for (int i = 0; i < 30; i++) Sample(0, 0, 0, false);
        Check(swing.Calibrated, "still calibration");
        foreach (float angle in new[] { 8f, 18, 26, 34, 30, 24, 17 }) Sample(angle, 3.5f, 2.5f, false);
        Sample(9, 3.5f, 2.5f, true);
        for (int i = 0; i < 5; i++) Sample(4, 3.5f, 3, false);
        swing.Rearm(); Sample(25, 0, 2, false); time += .5; Sample(5, 0, 2, false);
        Check(!swing.Calibrated, "packet gap must invalidate calibration");
        for (int i = 0; i < 40; i++) Sample(i * 5, 0, .1f, false);
        Check(!swing.Calibrated, "drifting attitude must not calibrate");
        float gentle = DeliveryPower(1.1f), medium = DeliveryPower(2.5f), strong = DeliveryPower(4f);
        Check(gentle < medium && medium < strong && strong == 1, "gentle/medium/strong power ordering");
        Check(Mathf.Abs(DeliveryPower(2.5f, true) - medium) < .001f, "a single gyro spike inflated power");
        Check(BowlingSwing.BallSpeed(gentle) >= 4.2f && BowlingSwing.BallSpeed(strong) == 9, "playable bounded ball speed");
        Check(BowlingSwing.MapPower(20) == 1, "power cap");
        return "PASS: scoring; mounted IMU calibration, heading, release, latch, freshness; gentle/medium/strong power, spike rejection and speed cap.";
    }

    static float DeliveryPower(float speed, bool spike = false)
    {
        var swing = new BowlingSwing(); double time = 0;
        for (int i = 0; i < 30; i++) swing.Sample(Quaternion.identity, Vector3.zero, time += .04, true, out _, out _);
        var angles = new[] { 8f, 18, 26, 34, 30, 24, 17, 9 };
        for (int i = 0; i < angles.Length; i++)
            if (swing.Sample(Quaternion.AngleAxis(angles[i], Vector3.right), Vector3.right * (spike && i == 4 ? 45 : speed), time += .04, true, out _, out float power))
                return power;
        throw new Exception("Bowling verification: expected delivery at " + speed + " rad/s");
    }

    public static string Avatar()
    {
        var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>();
        Check(game && game.avatar, "Mii missing");
        var avatar = game.avatar; avatar.Prepare();
        Vector3 release = avatar.BallGrip;
        Check(Mathf.Abs(release.x) < .01f && Mathf.Abs(release.z - .1f) < .01f, "release not centered on lane");
        Check(release.y > BowlingGame.BallRadius && release.y < .8f, "release height");
        avatar.DrawPose(-55, 0, 0, true);
        Check(avatar.BallGrip.z < release.z - .25f, "backswing does not move ball behind player");
        Check(Vector3.Distance(game.ball.transform.position, avatar.BallGrip) < .001f, "ball separated from hand");
        avatar.Release(0);
        Check(Vector3.Distance(game.ball.transform.position, release) < .001f, "release ball continuity");
        avatar.DrawPose(60, 1, 0, false);
        Check(avatar.BallGrip.z > release.z + .2f, "follow-through does not point down lane");
        avatar.Prepare();
        return $"PASS: Mii grip, backswing, release continuity, follow-through; release {release:F3}.";
    }

    public static string AvatarStream()
    {
        var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>();
        game.avatar.DrawPose(-45, 0, 3, true);
        var expected = game.avatar.joints[4].localRotation;
        var snapshot = game.GetComponent<BowlingStatePublisher>().Snapshot();
        var fixture = new GameObject("Synthetic Quest client fixture");
        try
        {
            var client = fixture.AddComponent<BowlingStateClient>(); client.game = game;
            client.hud = new GameObject("HUD").AddComponent<TextMesh>(); client.hud.transform.SetParent(fixture.transform);
            client.scoreboard = new GameObject("Score").AddComponent<TextMesh>(); client.scoreboard.transform.SetParent(fixture.transform);
            game.avatar.Prepare();
            Check(client.Apply(snapshot), "Quest rejected avatar snapshot");
            Check(Quaternion.Angle(expected, game.avatar.joints[4].localRotation) < .01f, "Quest pose differs from Mac pose");
            Check(!client.Apply(snapshot), "duplicate state accepted");
            var broken = Newtonsoft.Json.Linq.JObject.Parse(game.GetComponent<BowlingStatePublisher>().Snapshot());
            broken["avatar"][0]["r"] = new Newtonsoft.Json.Linq.JArray(0, 0, 0, 0);
            Check(!client.Apply(broken.ToString()), "invalid avatar quaternion accepted");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); game.avatar.Prepare(); }
        return "PASS: Quest renders the Mac's Mii pose; duplicate and malformed snapshots rejected.";
    }

    public static string PowerPhysicsStatus { get; private set; } = "Not run";
    static int powerCase;
    static readonly float[] PowerCases = { 0, .5f, 1 };
    static readonly List<int> PowerHits = new();
    public static string StartPowerPhysics()
    {
        Check(EditorApplication.isPlaying, "Play mode required");
        powerCase = 0; PowerHits.Clear(); deadline = EditorApplication.timeSinceStartup + 45;
        PowerPhysicsStatus = "Checking gentle, medium and full-power center deliveries.";
        StartPowerRoll(); EditorApplication.update += PowerTick;
        return PowerPhysicsStatus;
    }
    static void StartPowerRoll()
    {
        var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>(); game.RestartRound(); Time.timeScale = 4;
        Roll(0, PowerCases[powerCase]);
        Check(Mathf.Abs(game.ball.linearVelocity.magnitude - BowlingSwing.BallSpeed(PowerCases[powerCase])) < .01f, "launch speed mismatch");
    }
    static void PowerTick()
    {
        try
        {
            Check(EditorApplication.isPlaying && EditorApplication.timeSinceStartup < deadline, "power physics timeout");
            var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>(); if (game.Phase != "Result") return;
            Check(game.LastPins > 0, "center delivery failed to reach pins at power " + PowerCases[powerCase]);
            PowerHits.Add(game.LastPins);
            if (++powerCase < PowerCases.Length) { StartPowerRoll(); return; }
            PowerPhysicsStatus = "PASS: 0/50/100% deliveries reached pins at 4.2/6.6/9 m/s; hits " + string.Join("/", PowerHits) + ".";
            game.RestartRound(); EditorApplication.update -= PowerTick;
        }
        catch (Exception e) { PowerPhysicsStatus = "FAIL: " + e.Message; Time.timeScale = 1; EditorApplication.update -= PowerTick; }
    }

    // Explicit synthetic fixture; never feeds the native bridge or coordinator recordings.
    public static string Roll(float aim, float power)
    {
        Check(EditorApplication.isPlaying, "enter Play mode first");
        var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>();
        Check(game && !game.renderOnly, "Mac bowling scene required");
        typeof(BowlingGame).GetProperty(nameof(BowlingGame.Phase)).SetValue(game, "Ready");
        Check(game.Launch(aim, power), "launch refused");
        return "Launched synthetic physics fixture.";
    }
    public static object State()
    {
        var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>();
        var pins = new List<object>(); foreach (var pin in game.pins) pins.Add(new { name = pin.name, p = pin.position.ToString("F3"), up = Vector3.Dot(pin.transform.up, Vector3.up), active = pin.gameObject.activeSelf });
        return new { phase = game.Phase, frame = game.Score.Frame + 1, ball = game.Score.BallNumber, knocked = game.LastPins, result = game.Result,
            position = game.ball.position.ToString("F3"), speed = game.ball.linearVelocity.magnitude, pins };
    }
    public static string PhysicsStatus { get; private set; } = "Not run";
    static int checkedRolls;
    static bool awaitingResult, centerCheck;
    static double deadline;
    public static string StartPhysics()
    {
        Check(EditorApplication.isPlaying, "Play mode required");
        UnityEngine.Object.FindAnyObjectByType<BowlingGame>().RestartRound();
        checkedRolls = 0; awaitingResult = centerCheck = false; deadline = EditorApplication.timeSinceStartup + 100;
        PhysicsStatus = "Running 20 gutter deliveries, frame progression, pin reset and a center hit.";
        EditorApplication.update -= PhysicsTick; EditorApplication.update += PhysicsTick;
        return PhysicsStatus;
    }
    static void PhysicsTick()
    {
        try
        {
            Check(EditorApplication.isPlaying && EditorApplication.timeSinceStartup < deadline, "physics run interrupted or timed out");
            var game = UnityEngine.Object.FindAnyObjectByType<BowlingGame>();
            if (awaitingResult)
            {
                if (game.Phase != "Result" && game.Phase != "Complete") return;
                if (centerCheck)
                {
                    Check(game.LastPins > 0, "center ball failed to collide with pins");
                    int knocked = game.LastPins; game.RestartRound();
                    foreach (var pin in game.pins) Check(pin.gameObject.activeSelf && pin.position.z > 18 && Vector3.Dot(pin.transform.up, Vector3.up) > .99f, "rack reset did not restore an inactive pin");
                    PhysicsStatus = $"PASS: 20 left/right gutter rolls scored zero, all 10 frames completed, center roll hit {knocked} pins, replay restored all pins.";
                    Time.timeScale = 1; EditorApplication.update -= PhysicsTick; return;
                }
                Check(game.LastPins == 0, "gutter shot or unstable pin scored a hit on roll " + (checkedRolls + 1));
                checkedRolls++; awaitingResult = false;
                if (checkedRolls == 20)
                {
                    Check(game.Score.Complete && game.Score.Total == 0, "round completion");
                    game.RestartRound(); centerCheck = true;
                }
                else return;
            }
            if (game.Phase != "Setup" && game.Phase != "Ready") return;
            Time.timeScale = 4;
            Roll(centerCheck ? 0 : checkedRolls % 2 == 0 ? 8 : -8, centerCheck ? .6f : .4f);
            awaitingResult = true;
        }
        catch (Exception e) { PhysicsStatus = "FAIL: " + e.Message; Time.timeScale = 1; EditorApplication.update -= PhysicsTick; }
    }
}
