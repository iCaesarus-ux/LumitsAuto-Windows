# Lumits Auto Clicker (Windows)

A Windows auto clicker for the game **Lumits**: `AutoClicker.exe`, built from `src/` by `build.cmd`. `README.md`
says how it's used. Split from LumitsAuto, which also holds the Android app (not in this repository).

## Rules from the owner

- Never kill the owner's running `AutoClicker.exe`, and never type passwords or credentials for them.
- Never put the owner's Discord webhook or ntfy topic in this repository. The app keeps them in
  `%APPDATA%\AutoClicker`, with its other settings.
- Tests must not move the real pointer.

## Building and code

- `build.cmd` uses the C# compiler that ships with Windows (.NET Framework 4, so C# 5 at most). No SDK needed.
  Close the app first. `AutoClicker.exe` isn't committed.
- Comments and on-screen text are in plain words, matching the code around them.
- Tests: README's Tests section.
