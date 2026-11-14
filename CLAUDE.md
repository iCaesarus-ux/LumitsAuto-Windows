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
- The owner calls the list of picked clicks the app's "steps". While their `AutoClicker.exe` runs it's locked and a
  second copy won't start (single-instance mutex): build to another folder with build.cmd's compile line.

## Where things stand (14 November 2026)

- **Radiants only** (`CatchOptions.RadiantsOnly`, ticked in Catching…; made in LumitsAuto after the split and brought
  over): the owner makes the radiant check's step (`NameStep`) the Run button (the boot, bottom left, about 237, 939
  in the 1920 x 1025 layout). Every Lumit that isn't a radiant gets that step, ticked ones and 2v2s too; a radiant in
  a 1v1 is caught with Strike and Capture. Names are judged once the fight menu is lit (up to 5 s). After a run the
  map's step follows at once (`ranCycle`), and any jump back to the first step after the fight's step ends the pass
  (`EndPass`): before, the next fight wasn't counted and a 2v2's `duoCycle` carried over. Not run in the game yet.
- **2v2s on the PC** (read from the owner's live game window with `PrintWindow`, no input): two cards a side, one
  above the other, not the phone's four in a row. The 2v2 spots used to be the phone's, so on the PC a 2v2 was never
  seen and neither wild name was checked. Now: lead's card lower left (Infibex; Minoking, second in the team, upper
  left); the wild Lumits' upper card is the left one on the field, the lower card the right one (seen with Bonwing
  above Musingens). A 2v2 needs both your bars and a wild one, red from their left ends (`CatchSpots.DuoShowing`):
  in 1v1 pictures the lead's spot catches the 1v1 card's red frame and the lower wild spot a red radiant's portrait.
  A radiant in a 2v2 stops the run untouched, naming which one (`CatchOptions.CatchDuoRadiants`, off): 2v2 target
  clicks and the second Lumit's moves haven't been checked on the PC. Pictures: `tests/pictures/pc/duo-*.png`.
- The owner's saved settings had the radiant check off (`alert=0`) on 14 November.
