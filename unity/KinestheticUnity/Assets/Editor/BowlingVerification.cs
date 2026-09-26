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
        return "PASS: 10 scoring cases; mounted IMU calibration, heading, release, latch, freshness and motion rejection.";
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
