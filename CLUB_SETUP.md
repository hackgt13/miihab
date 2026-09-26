# Fixed physical club setup

## Decision

Use the short green-grip driver with the broad silver head (middle of the three
green-grip clubs in IMG_3334). Keep its telescoping length fixed and secure. Choose
the length at which the seated player can comfortably address the mat without
reaching or leaning. There is no universal required shaft length or hand height.
A standing player can have a separate comfortable setup and address calibration.

Keep the AirPod attached throughout play. Unity selects the virtual driver, iron,
or putter. Camera-based club identification and physical club swapping are out
of the current demo flow. The existing camera pipeline estimates body pose; it
does not recognize club models or locate the mat automatically.

## AirPod mount

- Place it on the shaft approximately 3–5 cm below the end of the grip, clear of
  the hands. Use a consistent side of the shaft, facing away from the fingers.
- Put a thin piece of black foam between the AirPod housing and shaft; point the
  stem toward the clubhead. This is a reproducible mounting convention, not a
  manufacturer-specified motion-sensor orientation.
- Wrap snugly around the housing and shaft. The foam must not let the AirPod rock
  or rotate independently. Do not load the silicone ear tip or use it as the
  fastening point. Add a retaining loop if the wrap does not fully capture it.
- Mark the mount position and telescoping length so both can be reproduced.
- Check that off-ear samples still increase with the final wrap in place. Hold
  still at address to calibrate; remounting or changing length requires a new
  calibration. Initial tests use short, slow, ball-free swings with clear space.

The measured inputs are orientation/angular rate, not tracked clubhead position
or actual impact. Sensor placement near the grip is practical for retention;
there is no need to put it near the head to acquire club rotation.

## Mat and player reference

Yellow center dot: address/virtual strike reference. Long yellow centerline:
intended forward direction. Put the mat on the floor and preserve its position
for the session. The physical ball can demonstrate the dot during setup, then
be removed for virtual swings. A real ball strike is not required.

The current calibration is relative to the player's held club orientation. It
does not solve the mat's world position from the image. Manual mat alignment and
later camera/Quest registration are separate from this orientation calibration.

## Automatic virtual club

At the start of each turn, Unity chooses from horizontal distance and the actual
surface collider beneath the ball:

- Rough/bunker: iron.
- Otherwise within 8 m of cup: putter.
- Otherwise 65 m or farther: driver.
- Otherwise: iron.

These are game presets for this demo hole, not golf coaching or visual club
recognition. Club arrows override selection until the next turn. The setup panel
also has an Auto-select button. Selection resets the swing gate, so choose before
calibrating. Nothing changes clubs during a backswing or flight.

The current physical connection remains one Mac-hosted motion stream with explicit
patient/friend assignment. Two simultaneously independent AirPod sources have not
yet been established; two earbuds in one paired set must not be presented as two
independent club trackers. One mounted controller can be passed between players
for the initial demo without remounting the sensor.
