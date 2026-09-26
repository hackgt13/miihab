using System;
using UnityEngine;

namespace Kinesthetic.UI.Boards
{
    /// One standing place for a board, around a seated patient: a direction from their eyes and a distance
    /// along it. A value, because a venue names its stations rather than placing boards by coordinates, and
    /// a station has to mean the same thing in the studio, on the course and at the lanes.
    ///
    /// Yaw is degrees clockwise from the seat's facing, so a negative yaw is to the patient's left; pitch is
    /// degrees above the horizontal. Every station sits within about ±30° of yaw: the people using this are
    /// seated, often in a chair that does not swivel, and nothing is ever placed where turning is required.
    ///
    /// Density follows the distance. A board lays out at `DensityConstant / distance` pixels per metre, so a
    /// 19px body line subtends the same angle at every station — a figure two metres away is not smaller to
    /// the eye than one at 1.2 m, it is just further.
    [Serializable]
    public struct Station
    {
        /// Pixel-metres: pixels per metre times metres of distance. 1000 makes the 1.4 m Focus station lay
        /// out at 714 px/m, near the density the menu's 3.7 m board was tuned at, and 19px body text about
        /// one degree tall everywhere.
        public const float DensityConstant = 1000f;

        /// The widest yaw any station may stand at, either side.
        public const float MaxYawDegrees = 30f;

        public string name;
        public float yawDegrees, pitchDegrees, distanceMetres;

        /// How far the board's face leans back from square-on to the eye, positive turning it upward. A
        /// board mounted in a room is square to whoever reads it, and every station here is zero for that
        /// reason. A page in your hands is not square to anything: it lies back toward you, and the few
        /// degrees of difference are most of what separates paper from a panel hanging in the air.
        public float tiltDegrees;

        public Station(string name, float yawDegrees, float pitchDegrees, float distanceMetres, float tiltDegrees = 0)
        {
            this.name = name; this.yawDegrees = yawDegrees; this.pitchDegrees = pitchDegrees;
            this.distanceMetres = distanceMetres; this.tiltDegrees = tiltDegrees;
        }

        public float PixelsPerMetre => DensityConstant / Mathf.Max(.01f, distanceMetres);

        /// The same station on the other side of the facing. A venue that reserves a side — rehab's mirror
        /// stands front-left — takes the mirror image of a left-hand station rather than inventing a fifth.
        public Station Mirrored => new(name + " (mirrored)", -yawDegrees, pitchDegrees, distanceMetres, tiltDegrees);

        /// The direction from the eye point, in the seat's frame: pitch first, then yaw about the vertical.
        public Quaternion Heading => Quaternion.Euler(-pitchDegrees, yawDegrees, 0);

        public override string ToString() => $"{name}: yaw {yawDegrees:0}°, pitch {pitchDegrees:0}°, {distanceMetres:0.0} m"
            + (Mathf.Abs(tiltDegrees) > .01f ? $", tilted {tiltDegrees:0}°" : "");
    }

    /// The named stations every venue places against. The set is deliberately small: what to do now, a
    /// modal in front, a score off to one side, live figures off to the other, and one reading distance
    /// inside arm's reach that only a document passing through ever occupies.
    public static class Stations
    {
        /// What to do now: the cue, the status, the primary button and the connection chip. Low and near, so
        /// it is in the lower field of view like a lectern rather than covering the room.
        public static readonly Station Dock = new("Dock", 0, -22, 1.2f);

        /// Modals only — a summary, a setup — straight ahead at reading distance, shown and hidden.
        public static readonly Station Focus = new("Focus", 0, 0, 1.4f);

        /// The scoreboard: up and to the left, further off, glanced at rather than read.
        public static readonly Station Score = new("Score", -25, 12, 2.0f);

        /// Live figures — a ring, an angle, a meter — to the right, a little below the eye line.
        public static readonly Station Measure = new("Measure", 25, -5, 1.5f);

        /// Where a page is held, not where a card is mounted: 48 cm out, 40° down, leaning 5° back toward
        /// the face, because nobody holds paper square to their own eye. Its board is a sheet of US
        /// Letter, 216 x 279 mm, at its real size — moving it out is what makes it smaller, since the
        /// paper stays paper.
        ///
        /// Together the pitch and the tilt set the rake, and 45° off vertical is as far as this goes
        /// before a page starts to read as a slope. The pitch is to the middle of the sheet, and a held
        /// sheet is large: even at 40° down its top edge reaches to 24° below the eye. Aiming the centre
        /// where the top edge belongs is what put the first version of this in the reader's face.
        ///
        /// A briefing is handed to you, so it arrives here and leaves again. Nothing lives at this
        /// distance: it is inside your reach and across the room you are looking at.
        public static readonly Station Reading = new("Reading", 0, -40, .48f, tiltDegrees: 5);

        public static readonly Station[] All = { Dock, Focus, Score, Measure, Reading };
    }
}
