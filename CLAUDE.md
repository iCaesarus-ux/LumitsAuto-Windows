# Lumits Auto Clicker (Windows)

A Windows auto clicker for the game **Lumits**: `AutoClicker.exe`, built from `src/` by `build.cmd`. `README.md`
says how it's used. Split from LumitsAuto, which also holds the Android app (not in this repository). Since
15 November 2026 this repository is the only one being worked on: leave LumitsAuto and the phone app alone.

## Rules from the owner

- Never kill the owner's running `AutoClicker.exe`, and never type passwords or credentials for them.
- Never put the owner's Discord webhook or ntfy topic in this repository. The app keeps them in
  `%APPDATA%\AutoClicker`, with its other settings.
- Tests must not move the real pointer.
- Commit finished pieces as they land, and as soon as a usage-limit warning shows, commit and push `main` to
  github.com/iCaesarus-ux/LumitsAuto-Windows before anything else, with "Where things stand" below kept current:
  several Claude accounts work on this through that repository.
- Cap every test run at 5 minutes (`timeout 300 ...` from Git Bash), one test at a time, not chained in one command.
  If one hits the cap, say so rather than rerunning it for longer. The radiant test takes about 100 s, the 2v2 test
  about 10 s.

## Building and code

- `build.cmd` uses the C# compiler that ships with Windows (.NET Framework 4, so C# 5 at most). No SDK needed.
  Close the app first. `AutoClicker.exe` isn't committed.
- Comments and on-screen text are in plain words, matching the code around them.
- Tests: README's Tests section.
- The owner calls the list of picked clicks the app's "steps". While their `AutoClicker.exe` runs it's locked and a
  second copy won't start (single-instance mutex): build to another folder with build.cmd's compile line.

## Where things stand (15 November 2026)

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
- **2v2 radiant checks without catching, and the PC 2v2 test (15 November):** the owner no longer uses the phone
  app, so `tests/DuoTest.cs` (`run-duo-test.cmd`, about 10 s) now uses only PC pictures and checks radiants in 2v2s;
  the phone screenshots (`tests/pictures/forest`) and the phone 2v2 catching checks are gone, and the PC 2v2 parts of
  the radiant test moved there. It found that with catching off (no `CatchOptions`) a 2v2 was never seen: only the
  1v1 name area was read, which covers 0% and 3% of the 2v2 name cards. Now `RunOptions.DuoSpots` (from MainForm
  whenever there's a game window) lets `CheckEnemy` see a 2v2 without catching (`CheckDuoNames`: both names checked
  and counted, a radiant stops the run untouched, otherwise the step is clicked). `StartDuo` no longer skips the gold
  check of a card whose bar reads empty. Still assumed: a 2v2 needs both your bars, so one with your second Lumit
  fainted would be taken for a 1v1 (not seen whether the game makes such 2v2s).
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
  (alert title "Stuck on an unknown screen", always sent). Healing waits until the retake proved right. On a captcha
  this clicks step 1's spot there once before stopping (the owner was told). Tests: `StuckOnAScreen`.
- **Stuck after a run in radiants only (15 November, owner's screenshot: "step 3 hasn't shown up, still looking
  (3/3)"):** read-only picture of the game: the map, with the character standing on the bush that step 1 watches
  (907, 498, 116 × 70, the same area it clicks), so its picture didn't fit. The retake then didn't come: after one had
  worked, `retakeToSave` blocked any other until the map fitted the old or new picture again, which a character in a
  new spot each time never did. Now retaken pictures are extras for the run only (`firstStepExtra`, up to 8, counted in
  `Matches` and `Likeness`), never put into the steps (`FirstStepRetaken` and MainForm's `KeepRetakenPicture` are
  gone), and any stuck spell can retake once the last retake was shown right. After a run (`afterRun` in
  `WaitForScreen`) the retake comes after 10 s (`RetakeAfterRunMs`), and the status says "waiting for the map (step 1)
  after the run". While nothing fits, the status and the stop say the closest step (`Closest`: "step 1 closest, 64% of
  80%") and why there was no retake. The owner was advised to have step 1 watch the map's bottom bar (Team … Settings,
  map only, interface so the night doesn't change it). Not run in the game yet.
- **The owner's settings save fine; Claude's tools see an old copy (15 November).** The Claude desktop app is a
  Windows package, and Windows gives packaged apps a private AppData: a `settings.txt` written from inside Claude on
  30 October lives in `%LOCALAPPDATA%\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\AutoClicker\`, and tools run
  from Claude read that copy at `%APPDATA%\AutoClicker\settings.txt` (same file ID). Files with no such copy, like
  `lumits.txt`, read through to the real ones. The real `settings.txt` (the owner's app runs from Explorer) was
  saved on 15 November at 11:46, 20,529 bytes, 3 steps. To read the real folder, start a command outside the package,
  e.g. PowerShell `Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = 'cmd.exe
  /c ...' }` writing to a file, and never print `alertPhone` (the webhook). Anything run from Claude that saves
  settings (the app itself, a test) writes into the package's copy, not the owner's. 14 November's "settings not
  saving" and `alert=0` came from that old copy; `MainForm.SaveSettings` still reports a real failure (title "Auto
  Clicker — settings not saved", status line, `save-error.txt` beside the settings).
