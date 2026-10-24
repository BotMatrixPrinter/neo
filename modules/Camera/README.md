# Camera

The chase camera that follows the player's plane (plane 0).

## Contract (`Contract/`, namespace `Neo.Camera`)

`CameraPose` (`Vector3 Position`, `Quaternion Orientation`, float `FovDegrees`; `Forward`;
`CameraPose.Lerp(a, b, t)` to interpolate). World space, meters, Y up; the camera looks
along -Z rotated by `Orientation` (Godot's convention). Not published on the bus: the view
reads it from the Core.

## Input

Subscribes to `Neo.Flight.PlaneState` (modules/Flight/Contract) and uses the latest one for
`PlaneId` 0; others are ignored. No pose until one arrives (`HasPose` false).

## Core (`Core/`)

`CameraModule` (the `IModule`), each Tick:

- **Spot**: 14 m behind and 4 m above the plane along its orientation (`Back`, `Up`), so a
  banked or inverted plane takes the camera with it.
- **Look**: at a point 12 m ahead of the plane's nose (`LookAhead`); the camera's up comes
  from its lagged rotation.
- **Lag**: the rotation follows the plane's at 6/s (`RotationRate`), divided by
  `1 + 0.35 * roll rate (rad/s)` so rolls lag more. The position is carried with a lagged
  copy of the plane's velocity (3/s) and closes its gap to the spot at 5/s: a steady plane is
  followed exactly; speeding up stretches the camera back, turns swing it out, then it
  settles. The first `PlaneState` places the camera on its spot with no lag.
- **Field of view**: 70 degrees at 25 m/s and below, 80 at 60 m/s and above, linear between
  (`FovFor`), followed at 2/s.
- `Pose` (after the latest tick) and `PreviousPose` (after the one before) for the view.
  No allocations per Tick.

## Godot (`Godot/Main.tscn`, `CameraView.cs`)

Adds one `Camera3D` (near 0.3 m, far 8 km) and makes it current, so
`GetViewport().GetCamera3D()` (used by the Input module for mouse aim) returns it. Every frame
it sets it from `CameraPose.Lerp(PreviousPose, Pose, Engine.GetPhysicsInterpolationFraction())`.
In the game it draws nothing else. Opened on its own (no module bound, as `screenshot.sh`
does) it runs a preview: its own Core following a stand-in cartoon biplane on a banked
300 m circle at 50 m/s, over a sea with islands and clouds.

Screenshot (not committed): `./screenshot.sh modules/Camera/Godot/Main.tscn` (`Camera.png`).

## Tests (`Tests/`)

`CameraModuleTests` (with a fake bus): no pose before the plane, other planes ignored; sits
behind and above a level and a pitched, banked, turned plane; looks slightly ahead; no drift
at steady speed; lags when the plane speeds up and through a sharp turn, then settles; more
rotation lag rolling than yawing; fov 70 to 80 with speed, smoothly; previous pose kept for
interpolation.

Test it alone: `./test.sh Camera`.
