# Building this fork with .NET 10

The solution targets **.NET 10 for Windows x64**. Install the stable .NET 10 SDK
for development, or the .NET 10 Desktop Runtime to run a framework-dependent build.
The SDK includes the desktop runtime. .NET 6 is no longer required for the application.

`Source/global.json` selects a stable .NET 10 SDK and allows newer .NET 10 feature
bands. It does not select .NET 11 previews. In Rider, open `Source/ORTS.sln` and use
the .NET CLI/MSBuild from the installed .NET 10 SDK under **Settings > Build,
Execution, Deployment > Toolset and Build**. Restart Rider if it was open during
SDK installation and has not detected the SDK.

From `Source`:

```powershell
dotnet --version
dotnet build ORTS.sln
dotnet test Tests/Tests.csproj --no-build --no-restore
```

Application binaries and content remain in `Program`. Test binaries are isolated
under `Source/Tests/bin/<Configuration>` to keep test-runner dependencies out of
the application directory. For Release tests, pass `--configuration Release`.

The existing MonoGame 3.8.1.303 runtime and matching shader compiler are retained
to preserve rendering behavior and compiled-effect compatibility. Its compiler
permits rolling forward to a newer .NET runtime; all seven custom shaders have
also been compiled and loaded explicitly on .NET 10. A MonoGame upgrade is a
separate change. This runtime migration does not add native Linux/macOS execution.

## Replay compatibility

.NET 10 no longer implements BinaryFormatter. New `.replay` files therefore use
versioned JSON containing command types and their instance fields. Loading does
not run command constructors, replay actions, or serialization callbacks, and
static receivers are never stored. Only application simulation/viewer commands
and scalar field values are accepted.

Old binary recordings remain readable through Microsoft's NRBF record reader.
Tests cover all 155 pre-migration command types and legacy brake-log compaction.
Saving a loaded recording writes the new format; older versions of Open Rails
cannot read these new replay files. Keep original recordings when compatibility
with older builds is needed. The simulator's separate save-state format has not
been changed by this migration.

## Validation

In addition to existing tests, migration coverage exercises old recordings, new
replay round trips, invalid replay data, runtime script compilation, and embedded
menu/viewer resources. A DirectX HiDef smoke test loaded all seven custom effects
and presented a frame on an NVIDIA RTX 5070 using .NET 10.0.14.

Four pre-existing `MultiPositionControllerTests` cases still fail their brake-ramp
timing assertions, as confirmed on the .NET 6 baseline. They are not excluded or
weakened by this migration. Full route/gameplay testing remains necessary before
shipping the experimental branch.
