Cab camera displays
==================

`ORTSCamera` adds a live feed to a 2D cab or a textured screen in a 3D cab.
Place two items beside each other for a split screen, or assign different screen
pages for manual left/right selection. Existing controls can continue using
`ORTS_SCREEN_SELECT`; `ORTSCameraSelect` also provides a hotspot over existing
artwork without needing a separate button texture.

Example camera item inside `CabViewControls`:

```text
ORTSCamera (
    Position ( 100 300 80 60 )
    ORTSDisplay ( 3 ) ORTSScreenPage ( cameras )
    CameraPosition ( -1.6 2.8 10.2 )
    CameraDirection ( 180 -10 0 )
    RearCameraPosition ( 1.6 2.8 -10.2 )
    RearCameraDirection ( 0 -10 0 )
    FieldOfView ( 65 ) ImageSize ( 160 240 ) UpdateRate ( 8 )
    DisabledIfCabPowerSupplyOff ( 1 )
)
```

`Position` uses the usual 640 × 480 cab coordinates. Camera positions are metres
in the vehicle's model coordinates: X right, Y up, Z forward. The mount follows
the vehicle's translation, rotation, and reversal. `CameraDirection` is yaw to
the right, pitch up, and clockwise roll, in degrees. Zero yaw looks forward;
180 looks backward. `FieldOfView` is the vertical angle.

`CarOffset` defaults to zero (the cab vehicle); positive offsets select later
vehicles in the consist list, negative offsets earlier vehicles. An unavailable
vehicle produces a black feed. Rear camera overrides are optional and selected
when the locomotive uses its rear cab. Without overrides, the same physical
camera is used. Separate front/rear CVFs can also define distinct viewpoints.
Configure left/right labels relative to the driving end explicitly.
Switching to another vehicle's front cab (as on an EMU with a driving vehicle at
each end) continues to use `CameraPosition` and `CameraDirection`, even if that
vehicle is reversed in the consist. Its world transform already accounts for the
reversal. The rear overrides apply to using the rear cab of the same vehicle.

`ImageSize` defaults to 320 × 240 and is limited to 16–1024 pixels per dimension.
`UpdateRate` defaults to 10 frames/second and is limited to 1–30. `Mirror ( 1 )`
flips the image horizontally. A hidden or unpowered feed does not render another
scene. Feed updates use real elapsed time, without advancing simulation time.

For a screen viewed at an angle, `ScreenCorners` specifies top-left, top-right,
bottom-right, and bottom-left, in the same cab coordinates as `Position`:

```text
ScreenCorners ( 121.76 324.55 151.25 317.50 157.36 371.51 127.66 379.63 )
```

Without corners, `Position` defines a rectangle and `ORTSAngle` can rotate it.
For 3D cabs, give the camera items a `Graphic` matching the screen's texture.
Items sharing that graphic are composed into one texture, allowing two feeds
on one mesh. Items on different pages can occupy the same rectangle. A 2D
background such as `disp4.ace` is a separate ordinary cab control on the camera
page; it can retain the monitor frame and buttons around the images.

Automatic switching belongs to the display and is configured once, alongside
the camera items:

```text
ORTSCameraDisplay (
    ORTSDisplay ( 3 ) CameraPage ( cameras )
    ActivateOnDoors ( 1 ) HideAboveSpeed ( 5km/h )
)
```

Opening any train door selects the camera page below the cutoff speed. An automatically
opened camera page stays selected through door closing, then returns to the previous page when
the absolute speed reaches the cutoff, including reverse movement. Slowing down
does not reactivate it; another door-opening event is required. A driver's page
selection cancels the automatic return, including reselecting the current camera
page. Manually selected camera pages can be opened and remain visible above the
cutoff. Opening doors while already on a manual camera page preserves that manual
selection. `ReturnPage ( default )` can select a fixed automatic return page instead.

Optional `ActivateBelowSpeed ( 15km/h )` also selects the camera page when slowing
from at least 15 km/h to below it. Starting at rest or below the threshold does
not activate cameras; the train must first reach the threshold. This works without
`ActivateOnDoors` and uses absolute speed, including reverse movement. Omitting
the option or setting it to zero disables this trigger, preserving door-only
behavior. Use `HideAboveSpeed` at or above the activation threshold: it remains
the cutoff for all automatically opened views. For example:

```text
ORTSCameraDisplay (
    ORTSDisplay ( 3 ) CameraPage ( cameras )
    ActivateOnDoors ( 1 ) ActivateBelowSpeed ( 15km/h )
    HideAboveSpeed ( 45km/h )
)
```

This opens cameras when slowing through 15 km/h or on a door-opening event, then keeps them open
until reaching 45 km/h. If the driver selects another page below 15 km/h, it stays
selected until speed reaches at least 15 km/h and drops below it again, or another
door-opening event occurs. A manually selected camera page still overrides the
automatic cutoff. Set both speed options to 15 km/h to close an automatically
opened view upon reaching that speed.

Speed values without units are metres/second. There
are eight display IDs, 0 through 7. A duplicate or invalid display policy is
ignored with a log warning.

Normal-page instruments and backgrounds should specify the same display ID and
`ORTSScreenPage ( default )`, so they disappear when the camera page is selected.
The display policy does not count as a cab control; camera items and selection
hotspots do count.

Example selection hotspot:

```text
ORTSCameraSelect (
    Position ( 587 357 14 12 ) ORTSDisplay ( 1 )
    ORTSScreenPage ( default ) TargetPage ( camera_left ) Label ( "Cameras" )
)
```

`DrawLabel ( 1 )` draws a small label; otherwise the existing artwork is used.
Multiple `ORTSScreenPage` entries can make a hotspot available on several pages.
3D cab buttons should use the existing `ORTS_SCREEN_SELECT` animated controls;
the hotspot geometry uses 2D cab coordinates.

Validation build
----------------

The normal build still compiles the runtime shaders and runs the usual post-build
actions. For an isolated build with existing matching shaders, set
`SkipRuntimeShaderCompilation=true` and `SkipPostBuildActions=true` and supply an
output directory inside the workspace. Copy matching compiled `.mgfx` files into
its `Content` directory before running the game. These camera controls themselves
use MonoGame's built-in BasicEffect and do not add a custom shader.

Camera passes currently omit precipitation particles, other particle effects,
and distant mountains to reduce their cost. The ordinary cab/world rendering
retains those features. Actual camera mount coordinates and the cutoff speed
must be calibrated against the train model and intended behavior. The examples
for the supplied cabs use an initial 8 fps and 5 km/h cutoff.
