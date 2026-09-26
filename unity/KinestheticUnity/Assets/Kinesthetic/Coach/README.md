# Trainer coach

A seated trainer beside the patient in `Rehab.unity` who demonstrates the prescribed exercise to follow.

- **Model:** Wii Fit male trainer (`Art/Coach/trainerM*.dae`, supplied by the team; Nintendo asset — not for public redistribution).
  `Kinesthetic ▸ Coach ▸ Build trainer coach prefab` orients it (+Y up, facing +Z), scales it to 1.6 m (Mii-world scale: seated shoulders ~0.93 m vs the patient Mii's 0.72 m), attaches the
  face to the head bone, rebuilds its materials for URP and seats it on a teal chair with a backrest → `Resources/Coach/TrainerCoach.prefab`.
- **Joins the rehab scene automatically** at runtime (beside the patient, facing the camera); the scene file is not edited.
- **Motion:** real rehab motion capture — UI-PRMD (University of Idaho), Vicon, public domain (ODC-PDDL 1.0).
  `Resources/CoachMotions/shoulder_scaption.json` is the median of 90 right-arm scaption repetitions from 10 adults
  (bell-shaped raise, soft 15–24° elbow, scapular plane ~40°); `shoulder_abduction.json` likewise from 80 repetitions.
  Source analysis: `research/ui-prmd/`.
- **Plan-driven:** peak = physician's target + 5° (inside the counting band), hold = plan hold, tempo slowed ×1.4 for control.
  It mirrors the patient (patient's right arm → coach's left, facing them) and shows ideal form: recorded trunk sway is not reproduced.

- **Follows the session** (`/exercise` stream): loops before Start, sits still during calibration, then leads each rep —
  raise, hold until the patient reaches the target (+ plan hold, max 2.5 s), lower, wait for the patient's rep — pauses when
  tracking is lost, nods when a rep counts, and celebrates (both arms up) only if at least one rep counted.
- **Life:** breathing and a slight forward lean through the spine, head follows the patient (clamped ±55°), blinks with
  the trainer's own open/half/closed eye textures.

## Mii looks (all scenes)

- Head scale 0.64 (`PoseRig.MiiHeadScale`) — friendlier proportions, less direct Mii-Maker likeness.
- Eyes are drawn shapes (`Art/Mii/Resources/MiiEyes`): patient **O O** blinking **⌒ ⌒**, friend **^ ^** blinking **— —**
  (`MiiIdleLife`, chosen automatically: seated → round, standing → happy).
- The joined-hands idle grip is golf-only (`PoseRig.golfGrip`, on in the golf scenes); elsewhere untracked arms rest on their own side.
