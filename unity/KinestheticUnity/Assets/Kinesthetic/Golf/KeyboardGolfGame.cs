using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kinesthetic.Golf
{
    // Keyboard-only golf loop. Swing sensing can later call Shoot() with its own speed/direction.
    public sealed class KeyboardGolfGame : MonoBehaviour
    {
        [Serializable] public struct ClubSpec
        {
            public string name;
            public float speed, loft;
            public ClubSpec(string name, float speed, float loft) { this.name = name; this.speed = speed; this.loft = loft; }
        }

        public Rigidbody ball;
        public Camera spectatorCamera;
        public Transform cup;
        public Transform flag;
        public Transform aimArrow;
        public Transform clubVisual;
        public int hole = 1;
        public int par = 4;
        public Vector3 teePosition = new Vector3(0, .17f, -38);
        public Vector3 cupPosition = new Vector3(0, .04f, 42);
        public float aimDegrees;
        public int strokes;
        public bool completed;
        public int clubIndex;
        public float charge;
        public string status = "Aim at the flag, then hold SPACE to swing.";

        // The source repo's club order and relative power/loft, scaled for this shorter course.
        readonly ClubSpec[] clubs = {
            new("Driver", 20, 9), new("Wood", 18, 8.5f), new("3 Iron", 16, 7.5f),
            new("5 Iron", 14.5f, 6.8f), new("7 Iron", 13, 6), new("9 Iron", 11.5f, 5.3f),
            new("Wedge", 9, 6.5f), new("Putter", 7, 0)
        };
        float lastShotAt = -10;
        bool charging;
        Vector3 lastShotPosition;
        Vector3 cameraVelocity;
        Texture2D panelTexture, fillTexture, trackTexture;
        GUIStyle titleStyle, valueStyle, hintStyle, smallStyle;
        float clubSwingUntil;
        Quaternion clubRestRotation;

        public ClubSpec CurrentClub => clubs[Mathf.Clamp(clubIndex, 0, clubs.Length - 1)];
        public bool Ready => !completed && Time.time - lastShotAt > .45f && ball &&
            ball.linearVelocity.magnitude < .3f && Grounded();

        void Awake()
        {
            if (!ball || !cup || !spectatorCamera) { enabled = false; Debug.LogError("Keyboard golf scene is missing ball, cup, or camera."); return; }
            ball.position = teePosition;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            lastShotPosition = teePosition;
            if (clubVisual) clubRestRotation = clubVisual.localRotation;
            panelTexture = Solid(Palette.Prussian70.At(.92f));
            fillTexture = Solid(Palette.Cerulean40);
            trackTexture = Solid(Palette.Prussian80);
        }

        static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1); texture.SetPixel(0, 0, color); texture.Apply(); return texture;
        }
        static GUIStyle Style(int size, Color color, FontStyle weight)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight };
            style.normal.textColor = color; style.wordWrap = true; return style;
        }

        void Update()
        {
            var keys = Keyboard.current;
            if (keys == null) return;
            if (keys.rKey.wasPressedThisFrame) { Restart(); return; }
            if (completed) return;
            if (ball.position.y < -2 || Mathf.Abs(ball.position.x) > 58 || Mathf.Abs(ball.position.z) > 89)
                RecoverBall("Out of bounds — ball returned to the previous shot.");

            if (Ready)
            {
                float turn = (keys.rightArrowKey.isPressed || keys.dKey.isPressed ? 1 : 0) -
                    (keys.leftArrowKey.isPressed || keys.aKey.isPressed ? 1 : 0);
                aimDegrees += turn * 42f * Time.deltaTime;
                if (keys.upArrowKey.wasPressedThisFrame) clubIndex = (clubIndex + clubs.Length - 1) % clubs.Length;
                if (keys.downArrowKey.wasPressedThisFrame) clubIndex = (clubIndex + 1) % clubs.Length;
                bool swingPressed = keys.spaceKey.wasPressedThisFrame || keys.kKey.wasPressedThisFrame;
                bool swingHeld = keys.spaceKey.isPressed || keys.kKey.isPressed;
                if (swingPressed) { charging = true; charge = .25f; }
                if (charging && swingHeld) charge = Mathf.Min(1, charge + Time.deltaTime * .58f);
                if (charging && !swingHeld) { Shoot(charge); charging = false; charge = 0; }
            }
            else if (charging) { charging = false; charge = 0; }

            if (aimArrow)
            {
                aimArrow.gameObject.SetActive(Ready);
                aimArrow.position = ball.position + Vector3.up * .08f;
                aimArrow.rotation = Quaternion.Euler(0, aimDegrees, 0);
            }
            if (clubVisual)
            {
                float phase = Mathf.Clamp01((clubSwingUntil - Time.time) / .6f);
                clubVisual.localRotation = clubRestRotation * Quaternion.Euler(0, 0, Mathf.Sin((1 - phase) * Mathf.PI) * 52f);
            }
            var offset = Quaternion.Euler(0, aimDegrees, 0) * new Vector3(0, 3.8f, -8.5f);
            var desired = ball.position + offset;
            spectatorCamera.transform.position = Vector3.SmoothDamp(spectatorCamera.transform.position, desired, ref cameraVelocity, .2f);
            spectatorCamera.transform.LookAt(ball.position + Vector3.up * .55f + AimDirection() * 3.5f);

            var flatDistance = Vector2.Distance(new Vector2(ball.position.x, ball.position.z), new Vector2(cup.position.x, cup.position.z));
            if (strokes > 0 && flatDistance < .85f && ball.position.y < .42f && ball.linearVelocity.magnitude < 5f)
            {
                completed = true; ball.linearVelocity = Vector3.zero; ball.angularVelocity = Vector3.zero;
                status = $"HOLED OUT in {strokes} {((strokes == 1) ? "stroke" : "strokes")}! Press R to play again.";
            }
        }

        void FixedUpdate()
        {
            if (!ball || !Grounded()) return;
            var velocity = ball.linearVelocity;
            var horizontal = new Vector3(velocity.x, 0, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, Vector3.zero, 10f * Time.fixedDeltaTime);
            ball.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        Vector3 AimDirection() => Quaternion.Euler(0, aimDegrees, 0) * Vector3.forward;
        bool Grounded() => Physics.Raycast(ball.position, Vector3.down, .19f, ~0, QueryTriggerInteraction.Ignore);
        public void Shoot(float powerFraction)
        {
            if (!Ready) return;
            powerFraction = Mathf.Clamp(powerFraction, .25f, 1f);
            var club = CurrentClub;
            lastShotPosition = ball.position;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            ball.AddForce(AimDirection() * club.speed * powerFraction + Vector3.up * club.loft * powerFraction, ForceMode.Impulse);
            strokes++;
            lastShotAt = Time.time;
            clubSwingUntil = Time.time + .6f;
            status = $"Stroke {strokes} · {club.name} · {powerFraction * 100:0}% power";
        }
        void RecoverBall(string message)
        {
            ball.position = lastShotPosition;
            ball.linearVelocity = Vector3.zero;
            ball.angularVelocity = Vector3.zero;
            status = message;
            lastShotAt = Time.time;
        }
        public void Restart()
        {
            ball.position = teePosition; ball.rotation = Quaternion.identity;
            ball.linearVelocity = Vector3.zero; ball.angularVelocity = Vector3.zero;
            strokes = 0; clubIndex = 0; charge = 0; charging = false; completed = false;
            lastShotPosition = teePosition; lastShotAt = Time.time - 1;
            aimDegrees = Mathf.Atan2(cup.position.x - teePosition.x, cup.position.z - teePosition.z) * Mathf.Rad2Deg;
            status = "New round. Aim at the flag, then hold SPACE to swing.";
        }

        void OnGUI()
        {
            if (!enabled) return;
            if (titleStyle == null)
            {
                titleStyle = Style(23, Palette.Sand10, FontStyle.Bold);
                valueStyle = Style(18, Palette.Sand00, FontStyle.Bold);
                hintStyle = Style(15, Palette.Sand30, FontStyle.Normal);
                smallStyle = Style(13, Palette.Cerulean40, FontStyle.Bold);
            }
            float scale = Mathf.Min(Screen.width / 1400f, Screen.height / 900f);
            var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(Vector3.one * scale);
            GUI.DrawTexture(new Rect(28, 28, 390, 212), panelTexture);
            GUI.Label(new Rect(48, 44, 350, 25), "KINESTHETIC  /  GOLF", smallStyle);
            GUI.Label(new Rect(48, 76, 350, 32), $"HOLE {hole}    PAR {par}    STROKES {strokes}", titleStyle);
            GUI.Label(new Rect(48, 121, 350, 28), $"{CurrentClub.name}  •  {Vector2.Distance(new Vector2(ball.position.x, ball.position.z), new Vector2(cup.position.x, cup.position.z)):0} m to pin", valueStyle);
            GUI.Label(new Rect(48, 155, 350, 43), status, hintStyle);
            GUI.DrawTexture(new Rect(48, 210, 350, 8), trackTexture);
            GUI.DrawTexture(new Rect(48, 210, 350 * charge, 8), fillTexture);
            GUI.DrawTexture(new Rect(28, 789, 820, 81), panelTexture);
            GUI.Label(new Rect(48, 801, 780, 26), "A / D or ← / →  AIM     ↑ / ↓  CLUB     HOLD SPACE or K  POWER + SWING", hintStyle);
            GUI.Label(new Rect(48, 834, 780, 24), "R  RESTART     ·     Keyboard prototype — sensor swing input comes next", smallStyle);
            GUI.matrix = old;
        }
    }
}
