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
- **Night (14 November, measured in the owner's game with `PrintWindow`, no input):** the game multiplies its
  world's colours at night (no offset), not its interface: fitted pixel by pixel on the same standing stone (red
  0.40, green 0.55, blue 0.79) and rock (0.38, 0.49, 0.73) on `tests/pictures/pc/map-day.png` and `map-night.png`.
  `Snapshot.Similarity` also tries the snapshot (or the live area, for one taken at night) through that tint at 0.1
  to 1.3 of its strength, for dusk and dawn, but only when the area's light changed like the night's
  (`Snapshot.LightChange`): at least 15% darker or lighter, and blue kept over red against red lost between 0.35
  and 1 (the tint gives 0.65 at any strength, the stone and rock 0.61 to 0.66, dimmed buttons -0.43 to -0.04).
  Without that, dimmed buttons look much the same darkened by the tint (a lit Strike label scored 76% against a
  dimmed one through it, 41% as taken). Real pairs went from 11 to 25% to 90%; every dimmed button scores as before.
  Dusk and dawn are assumed to lie on the way to the night's tint; not seen yet.
- **Stuck on an unknown screen (14 November, asked by the owner):** after one time-out with no step's screen,
  `ClickEngine.RetakeFirstStep` retakes step 1's picture from the screen (only a screen held still for 1 s, not in a
  radiant's fight, only with another waiting step) and returns step 1 to click. Another step's screen (or a caught or
  fainted screen) after it is `SomethingHappened`; none within two of the awaited step's time-outs throws `StillStuck`
  (alert title "Stuck on an unknown screen", always sent). The old picture still counts for the rest of the run
  (`firstStepOwn`); `FirstStepSeen` raises `FirstStepRetaken` (MainForm puts it in the steps and saves) only once the
  map fits the new picture and not the old in a later pass than the one where something happened. Healing waits until the retake proved right. On a
  captcha this clicks step 1's spot there once before stopping (the owner was told). Tests: `StuckOnAScreen`. Not run in
  the game yet.
- The owner's saved settings had the radiant check off (`alert=0`) on 14 November.
- **Settings not saving (not solved):** `%APPDATA%\AutoClicker\settings.txt` was last written on 30 October, in an
  older format (no `catchRadiants`), while `lumits.txt` beside it was written during a run on 14 November. Saving
  swallowed any error then. Loading a copy and saving it elsewhere with the same code worked. Now `MainForm.SaveSettings`
  shows a failure (title "Auto Clicker — settings not saved", and the status line) and `AppSettings.NoteSaveProblem`
  appends it with its stack trace to `save-error.txt` beside the settings: read that file once the owner runs this.
