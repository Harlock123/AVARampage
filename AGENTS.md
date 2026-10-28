# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A city-smashing monster game in the style of Bally Midway's 1986 arcade game Rampage, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and
ARM). Everything in it is original: the monsters and cities are the game's own, drawn in code, and the sounds and
tunes are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVARampage.slnx                      # the SDK version is in global.json (10.0.400 or later)
dotnet test AVARampage.slnx                       # every test runs headless: no window opens
dotnet run --project src/AVARampage               # play it; add -- --no-audio for silence
RAMPAGE_SHOTS=$PWD/Images dotnet test tests/AVARampage.Tests --filter ScreenshotTests   # refresh the README's pictures
RAMPAGE_PROBE=1 dotnet test tests/AVARampage.Tests --filter RampageProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                      # rebuild src/AVARampage/Sounds (standard-library Python only)
```

The probe reports how the computer monsters get on over five minutes, for tuning. Without the variable it does
nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVARampage/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, monster select, attract, play, pause, game over, initials, high
    scores) and the fixed-step loop; `GameEngine.Play.cs` has the play itself: the three monsters (the player's
    first, then two computer rivals), the buildings, the army and the debris.
  - `Entities.cs` has `Controls`, the monsters (`Monsters`), buildings and their windows, the army and police.
  - `City.cs` makes each day's street of buildings and what waits behind their windows.
  - `Geometry.cs` has `Vec` and the screen measures (`Field`, including `Field.Step`).
  - `MonsterBrain.cs` is the computer monster: the rivals, the attract mode and the probe.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepad) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVARampage/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024x768 picture scaled to the
  window and runs the 60 Hz timer.
- `src/AVARampage/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVARampage/Sounds/`: the generated WAV files.
- `tests/AVARampage.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`,
  `ShiftReleaseTests` and `RampageProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame rate, so
  tests can play whole days deterministically from a seed. Draw random numbers from the engine's seeded `Random`
  (`new GameEngine(seed, table)`; reseeded from the seed and the chosen monster when a game starts), never from
  `Random.Shared`. Each `MonsterBrain` gets its own seed from that `Random`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** Up/down on keys and stick must produce the same `dy` sign the engine
  expects (negative is up the screen, for climbing). Test new controls through `InputHandler`, not by building
  `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **Test hooks.** `GameEngine.Calm` (internal, for tests) keeps the army away and the rivals at a distance.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVARampage`). A
  `GameEngine` built without a table loads that file, so tests load the table from a temporary folder and set
  `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
