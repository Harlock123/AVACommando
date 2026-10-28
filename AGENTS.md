# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A run-and-gun war game in the style of Capcom's 1985 arcade game Commando, in C# on .NET 10 and Avalonia 12, drawn
with Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and
ARM). Everything in it is original: the graphics are drawn in code, and the sounds and music are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVACommando.slnx         # the SDK version is in global.json (10.0.400 or later)
dotnet test AVACommando.slnx          # every test runs headless: no window opens
dotnet run --project src/AVACommando  # play it; --no-audio for silence
COMMANDO_SHOTS=$PWD/Images dotnet test tests/AVACommando.Tests --filter ScreenshotTests   # refresh the README's pictures
COMMANDO_PROBE=1 dotnet test tests/AVACommando.Tests --filter PilotProbe   # how the computer player gets on, for tuning
python3 tools/make_sounds.py          # rebuild src/AVACommando/Sounds (standard-library Python only)
```

The probe prints its findings to the test output; `COMMANDO_PROBE=lazy` for one that just runs up the screen
firing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVACommando/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, pause, high scores) and steps the game;
    `GameEngine.Play.cs` plays an area: the march up the battlefield and the fight at the fort's gate.
  - `Entities.cs` has `Controls`, `Field` (the screen and the fixed step), the areas and the things that move.
  - `CommandoPilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVACommando/Views/`: `GameCanvas` (split over `GameCanvas.cs` and `GameCanvas.Draw.cs`) draws the frame and
  runs the 60 Hz timer.
- `src/AVACommando/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVACommando/Sounds/`: the WAV files that `tools/make_sounds.py` writes.
- `tests/AVACommando.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`.
  - `GameplayTests` play areas from a seed; `ScreenshotTests` render frames; `ShiftReleaseTests` covers the Shift
    key; `PilotProbe` only runs when `COMMANDO_PROBE` is set.
- `Images/`: the README's screenshots.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` feeds the frame time into fixed 1/60 s ticks (`Field.Step`),
  independent of the frame rate, so tests can play whole games deterministically from a seed. Draw
  random numbers from the engine's `_rng` (a `Random` made from the game's seed), never from
  `Random.Shared`. Each area's layout comes from its own fixed-seed `Random`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the `Dy` sign the engine expects.
  In this game up the screen is **+1** (see `Controls` in `Entities.cs`), the opposite of several sister games.
  Test new controls through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVACommando`); tests load
  the table from a temporary folder and set `Silent` on the engine.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
