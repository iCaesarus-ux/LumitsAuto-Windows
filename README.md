# Auto Clicker

A Windows auto clicker that runs a list of steps. Each step can click a random spot inside a box,
wait until the screen shows what it's waiting for, then react after a human-like delay.
It's a single `AutoClicker.exe`, with no install needed.

## Setting up a loop (example: stone → attack → Continue)

Do this once per screen, while the game is showing that screen:

1. **Map screen:** click **Pick on screen…**. The screen freezes and dims. Drag a box over the
   stone, then right-click or press Esc.
2. **In a fight, when you can choose an action:** **Pick on screen…** again, and drag a box over
   the attack button.
3. **On the Victory screen, once Continue shows:** **Pick on screen…**, and drag a box over Continue.
4. Press **F6** (or **Start**). Press **F6** again to stop.

Each box you drag becomes a step that:
- **waits** until that area looks like it did when you picked it, by day or by night,
- waits a **reaction delay** (300–900 ms by default; short waits are more common than long ones),
- glides the mouse there and clicks a **random spot inside the box**, usually near the middle.

**Day and night:** the game's night darkens its world (the map, a fight's field, the Lumits and the names
over them), not its buttons, cards or minimap. It multiplies each colour: red by about 0.40, green 0.52 and
blue 0.79 (measured on 14 November 2026 on the same stone and rock by day and by night). So a step's
picture is also compared as the night would show it, and part of the way there for dusk and dawn, whether it
was picked by day or at night: one picture works for both. That's only tried when the area has changed the
way the night changes things, keeping much more of its blue than its red; a button dimmed while the game
animates goes darker evenly, so buttons score as they always did.

A plain click in the picker (no drag) adds an exact-spot step that doesn't wait. **F7** does the same
from any window.

**Lasso:** in the picker, press **L** to draw around something instead of dragging a box, for things
that aren't box-shaped. Hold the button down while you draw; letting go closes the outline. The step
then clicks only inside the outline and, while waiting, compares only what's inside it. **L** again
goes back to boxes. The next picker starts the way you left this one.

**Starting mid-loop:** when you press Start, the app first checks which step's screen is showing
and begins there. Start on the Victory screen and it clicks Continue first; start mid-fight and it
attacks first.

**Getting back on track:** while waiting for a step, the app keeps checking which screen the game
is really on:
- if another step's screen stays up for 5 seconds (say a Continue click didn't register and
  Victory is still showing), it carries on from that step. When that's the first step (the map)
  after the fight's step, the fight is over, and the next one counts as a new fight;
- if the previous step's screen comes back (the enemy survived), it clicks it again right away;
- if nothing it knows shows up for one of the step's time-outs (30 s by default), the game may
  still be on the first step's screen (the map), only looking different, say with someone standing
  on the watched spot. So the app retakes step 1's picture from the screen, as its *Retake snapshot
  from screen* button would, and clicks step 1. It does that only once the screen has held still
  for a second, never in a radiant's fight, and only if another step has a picture to wait for;
- if after that no other step's screen comes up within two more time-outs (60 s by default), the
  game is somewhere the steps don't know: the run stops and always alerts your phone ("Stuck on an
  unknown screen", with a screenshot), whatever *Also when a run stops by itself* says. Note that
  this means step 1's spot is clicked once on whatever that screen is (a captcha too);
- the new picture goes into your steps only once the map has come back looking like it and not
  like the old one. If the old one fits again (whoever stood there walked off), your steps stay as
  they were. For the rest of the run both count;
- when step 1's picture can't be retaken (the screen keeps changing, or step 1 doesn't wait for a
  screen), it keeps looking for three of the step's time-outs (about 90 s by default) and only then
  stops, sending the reason and a screenshot to your phone.

## Alerts (radiants and stops)

Click **Alerts…** (under Game window). The phone settings there serve two alerts: a radiant
appearing, and (if *Also when a run stops by itself* is ticked) any run that stops on its own,
with the reason and a screenshot. To check for radiants, tick *Alert me when an enemy has a gold
(radiant) name*. The **Enemy name card** settings are shared with the lumits counter:

- **Check before clicking step:** pick the attack step. Before every attack, the app looks at the
  enemy's name card. When a name is gold (with the ✦ star), the app catches the radiant, as
  described under **Catching radiants**. With that turned off, it stops the run *before* it attacks,
  so the radiant waits for you. The alert names it ("Radiant Coalython spotted!") and, with the
  counter on, says how many lumits it took.
- **Name area:** **Lumits default** fits a 1920 × 1025 game window. Otherwise use **Pick area…**
  during a fight and drag over the enemy's name, without the portrait. **Test now** shows the
  name it reads and how much of the area is gold (normal names 0%, a radiant ≈ 11%; it alerts
  at 2% or more). During a 2v2 it reads the two wild Lumits' own cards instead, as a run does,
  upper card (the left one on the field) first; the name area is empty in a 2v2.
- The app itself stays silent: its taskbar button flashes and the status line says what happened.
- **Phone:** sends an urgent notification with a screenshot of the game. Choose one:
  - **ntfy (simplest):** install the free *ntfy* app on your phone, add a subscription with a
    hard-to-guess topic name (anyone who knows the name can read it), and type the same name here.
  - **Discord:** in a server of your own, Edit Channel → Integrations → Webhooks → New Webhook →
    Copy Webhook URL, and paste it here. The Discord app on your phone then notifies you.

  Press **Send test** to check it arrives.

## Lumits counter

With **Count lumits** ticked, each battle's lumit is counted once, just before the first attack.
Its name is read off the enemy name card set in **Alerts…**, using the text recognition built
into Windows.
- The main window shows the total, this run, how many since the last radiant, and the last one met.
- **List and rules…** shows each lumit with how many you've met, its radiants, catches and when you
  last met it. **Copy** puts the table on the clipboard, and **Reset counts…** starts from zero
  (your catch ticks and keep rules stay).
- **It doesn't slow the loop.** The name is read on the side (a few milliseconds) while the click
  goes ahead. With the radiant check on, it reuses the picture that check already takes. With the
  check off, it takes one extra picture of the window (about 25 ms) per battle.
- **Misreads:** a letter read wrong ("Coatython") still counts as the right lumit. Each entry
  shows the spelling read most often. To fix a name yourself, double-click it in the list and
  change *Name in fights*. Giving it a name that's already listed merges the two.
- A second attack on the same enemy, or a Continue that has to be clicked again, isn't counted twice.
- The counts are kept in `lumits.txt`, next to the settings.

## Catching

**Choosing what to catch:** open **List and rules…**.
- **Tick** a lumit to catch it. Unticked lumits are attacked with your attack step to kill them,
  as before.
- **Double-click** a lumit to set its keep rules:
  - **IV 25:** a catch is kept if any stat you tick has IV 25.
  - **Natures:** a catch is also kept if its nature is one you tick. The list shows what each
    nature raises and lowers.
  - Either rule is enough. A catch matching neither is released.
- **Perfect Lumits:** each rule is ticked off once a catch surely meets it, since you have one now:
  - The list shows what each Lumit **still needs**. The catch log notes what was ticked off and
    what's left.
  - Later catches that only have a found stat or nature are released.
  - A Lumit with no rules left is **perfect**: it's unticked and attacked instead of caught. The
    perfect ones are listed in green, named at the top of the list, and counted on the main window.
  - A Lumit that never had rules counts as perfect too, unless you tick it with no rules, which
    catches it and keeps every catch.
  - An IV that only some readings call 25 is kept, but not ticked off; its picture goes in `unread`.
  - To look for another of something already found, tick that rule again.
- Lumits appear under their fight name. After the first catch, the caught name is added
  ("Tzik → Axel"; Axel is the first evolution, Tzik the second).

**Turning it on:** click **Catching…**.
- Tick *Catch the Lumits ticked in the Lumits list*.
- Leave **Practice mode** on at first. It always presses Keep and only notes what it would have
  released. Compare the catch log with the game, then turn practice mode off.

**What happens in a fight with a lumit you're catching:**
1. **Heat Siphon** first, always.
2. The app measures the enemy's HP bar before and after, and remembers how much of it Heat Siphon
   usually takes against that lumit. It uses the middle value, so crits don't count.
3. If another Heat Siphon would usually kill it, it uses **Capture**. Otherwise it uses Heat
   Siphon again. If Heat Siphon is greyed out (under 7 MP), it uses Capture.
   - After a few Heat Siphons your attack button (Pyrecrest Assault) greys out for lack of MP.
     That's fine: while catching, your turn is also recognised by the menu lighting up (Capture
     plus Heat Siphon or Strike, which needs no MP). Capture alone isn't enough, since it lights
     up first while the Lumits are still being sent out.
4. If a capture fails, it tries Capture again.
5. On the "New Lumit caught" screen, it reads the name, nature and the six IVs, then presses
   **Keep**, or **Release** and the confirmation. It confirms only if the confirmation names the
   same lumit.
6. **Only a 25 counts** in the stats you ticked; the other IV numbers don't matter. It keeps a
   Lumit to be safe if the nature can't be read (when you picked natures), or if none of the
   six IV labels could be read at all.

**Catch log:** **Catch log** opens `catches.txt`, which lists every catch with its IVs, nature and
what was done. Pictures are saved in folders next to it:
- `released`: each released (or would-be-released) catch;
- `unread`: each catch kept only because an IV or the nature couldn't be read.

**Catching radiants:** this is on by default, as *Catch radiants by itself* in **Catching…**. It needs
the radiant check in **Alerts…** and a chosen game window; without a game window, a radiant stops the
run instead. It works whether or not that lumit is ticked, and even with catching turned off.
1. **Strike**, the top-left move, is the only attack. Strike's damage is measured in each fight from how
   far that radiant's HP bar drops, so it's right for each lumit. Until a hit has been measured, it only
   attacks from 60% HP or more.
2. It attacks only while the radiant's HP is above 1.8 times that damage: 1.5 for a crit, plus 20%. If
   Strike does 20, it stops attacking at about 36 HP. At that HP or lower it uses **Capture** until caught.
3. The catch is always kept, even in practice mode, and the run carries on. Your phone gets a message with
   a screenshot when a radiant is spotted and when it's caught.
4. If Infibex faints, the stand-in only uses **Capture**, since its Strike hasn't been measured.
5. If the fight ends without a catch, the run stops and your phone is told.
6. **In a 2v2** both wild Lumits' names are checked. A radiant there stops the run before anything is
   clicked, and your phone is told which one it is (the left one on the field has the upper card). The app
   doesn't fight 2v2 radiants itself yet: its clicks on a 2v2's targets haven't been seen working on the PC,
   and a click on the wrong Lumit, or your second Lumit's move, could kill it.

**2v2s on the PC** look different from the phone: two cards a side, one above the other, instead of four
in a row. Your lead's card is the lower one on the left. The wild Lumits' are on the right: the upper card
is the left one on the field. The app's 2v2 spots were measured in the PC game on 14 November 2026 (before
that they were the phone's, and on the PC a 2v2 wasn't seen at all, so neither wild name was checked). A
2v2 needs both your bars and a wild one, each red from its left end, so a 1v1's red card frame or a red
radiant's portrait can't pass for one.

`tests/run-radiant-test.cmd` tests this in a pretend game window placed off-screen, without moving the
real pointer. In 720,000 simulated fights of that decision, with Strike doing 6 to 60 damage, varying by
up to 13% per hit, and 15% crits, one radiant died. It hasn't caught a real radiant yet.

**Radiants only (hunting radiants):** no fighting. Every Lumit that isn't a radiant is run from, and a
radiant is caught.
1. In a fight, on your turn, use **Pick on screen…** to drag a box over the **Run** button (the boot,
   bottom left). Put it in place of the attack step, so the steps are stone → Run → Continue.
2. In **Alerts…**, tick the radiant check and set **Check before clicking step** to the Run step.
3. In **Catching…**, tick **Radiants only**, and choose the game under **Game window**. Start won't go
   without the radiant check and a game window.

Then in each fight:
- **Not a radiant:** it clicks Run. Lumits ticked for catching are run from too, and so are 2v2s. It
  first waits for the fight's menu to light up, so the name cards have surely faded in.
- **A radiant in a 1v1:** caught with Strike and Capture, as above, and kept. Then the Continue step ends
  the fight, so keep it in the list. With *Catch radiants by itself* off, the run stops before anything
  is clicked. Either way your phone is told.
- **A radiant in a 2v2** (either wild Lumit): the run stops before anything is clicked, and your phone is
  told which one it is, for you to catch it.
- After a run, the map coming back means the fight is over, so it goes straight on to the stone without
  waiting for Continue. If your turn comes back instead, it clicks Run again.
- Counting and healing with Bark Tea work as usual.

**Healing:**
- In **Catching…**, use the slider to choose the HP below which your lumit gets healed (0 = never).
  Also set its **Max HP** (Infibex: 182).
- After a fight that ended below that HP, on the map, the app:
  1. double-clicks Infibex's icon to open its panel;
  2. opens the **Food** tab;
  3. drags Bark Tea onto Infibex three times (it stops early if HP is already full);
  4. closes the panel.
- If dragging doesn't work in background mode, tick *drag with the real pointer*.

**If Infibex faints** in a fight:
1. The game shows your other lumits where the moves usually are. The app picks the first one. If
   that one faints too, the next one shown first gets picked.
2. The stand-in only uses **Capture** until the fight ends, since its own moves could kill.
   - A lumit you ticked for catching is kept or released by its rules as usual.
   - Any other lumit is released, since it was only caught to end the fight.
3. Back on the map, Infibex gets Bark Tea (three), even if healing is set to "never".
4. Your phone gets a message, and the run carries on.

**Phone alerts** go out when **Infibex faints** (the run carries on, as above), when you **run out
of Bark Tea**, when Infibex **couldn't be healed**, and for radiants: when one is spotted, caught or
lost. Other catches don't send anything.

**Where things are:** **Catching…** lists every button, number and bar it uses, already placed
for Lumits at any window size.
- Select one and press **Test** while that screen shows in the game. It shows the spot and what
  it reads there.
- If a spot is off, use **Pick…** to place it yourself. **Use default** puts it back.

**Reading the IVs:** the "IV n" labels are small and faint. The app reads each one 28 ways
(several zoom levels, with and without a contrast stretch) and counts what each reading found.
- **If any reading saw 25** in a stat you ticked, the Lumit is kept.
- On the game's own "IV 25" labels at 1920 × 1025, a 25 was seen every time (63 of 63), and no
  other number was ever read as 25 (0 of 135).
- For the log, a number is shown when at least 2 readings found it and it outnumbers all the other
  numbers together at least 4 to 1; otherwise it shows "?".
- If yours often come out as "?" in the log, a bigger game window helps. The pictures in `unread`
  show what it saw, and **Test** in **Catching…** shows the readings for one label.

## Background mode (keep using your mouse)

1. Under **Game window**, choose the game (e.g. *Lumits (myGame)*). Step positions are then measured
   inside that window, so moving the window doesn't break them. Existing steps are converted
   automatically.
2. Tick **Background mode (mouse stays free)**.

Now the app reads the game window directly (it can be covered by other windows) and sends the
glide, press and release straight to it as window messages. Your real pointer never moves.

- **Clicks on the game world** (like the stone on the map): edit that step and tick
  *Background mode: use the real pointer for this click*. The game finds world clicks by asking
  Windows where the real pointer is, so without this the character runs towards *your* pointer.
  With it, the pointer jumps to the spot for about a quarter of a second, then comes back.
  Buttons such as Attack and Continue don't need it.
- Don't minimize the game, since a minimized window stops drawing.
- Keep your own pointer off the game window while it runs, or the game sees two pointers.
- "Stop if I move the mouse" is switched off in this mode, because the mouse is yours.

## Editing a step

Double-click a step to:
- pick its click area again with **Pick on screen…**: drag a box, press **L** for a lasso, or click once
  for an exact spot. If the step watches its click area, that's picked again too, with its snapshot
  taken from the same screen, so get the game to that step's screen first;
- change the box with its numbers (they move and stretch a lasso too), or turn a lasso back into its
  box with **Make it a box**;
- change the button (left/right/middle) and single/double click;
- watch a **different area** than the one clicked (**Pick area…**, where **L** draws a lasso too). This
  helps when the button itself animates. Small, steady things like a button's text work best;
- **Test now**: shows how closely the screen matches right now (e.g. "Now 97% — matches"), and says so
  when it matched in the night's light (or, for a picture taken at night, lighter now);
- set **Match needed** (default 80%). Lower it if a step never fires. Raise it if it fires too early,
  e.g. while a button is still fading in;
- set the **reaction delay** range and what happens if the screen **doesn't appear in time**:
  keep looking, then stop (default), skip the step, or click anyway.

## Main options

- **Delay between clicks**: wait after each click before the next step. *Vary delays by ±%*
  randomizes it.
- **Natural mouse movement and clicks**: curved, eased movement that sometimes overshoots and corrects,
  and a real press-and-hold on each click. Turn it off for very fast clicking.
- **Stop if I move the mouse**: grab the mouse and the run stops instead of fighting you.
- **Breaks**: every so often (randomly around the set minutes), pause between cycles for a random time.
- **Redo a step if its screen comes back**: if an enemy survives and the attack buttons light up
  again while it's waiting for Continue, it attacks again.
- **Save… / Load…** store the steps and settings in a `.clicklist` file. Everything is also
  remembered automatically in `%APPDATA%\AutoClicker\settings.txt`. If that can't be saved, the title
  bar says "settings not saved", the status line says why, and `save-error.txt` beside it has the details.

## Notes

- Without a game window chosen, the game has to stay in the same place and visible (not covered
  or minimized) for screen waiting to work. Choosing the game window removes both limits, except
  minimizing.
- Windows blocks simulated clicks into programs running as administrator. If clicks don't register,
  run Auto Clicker as administrator too.
- If an online game's rules forbid automation, human-like input lowers the chance of being flagged
  but doesn't remove it.

## Click Recorder

A separate little app, `recorder\ClickRecorder.exe` (build it with `recorder\build.cmd`), that records mouse
clicks during a run: where each landed, which button, and the time between clicks. It sees every press on the
PC, yours and other programs' alike, so it records Auto Clicker's clicks and your own.

- **F9** (or the button) starts and stops a recording from any window. Name it first, e.g. "Auto Clicker" or
  "Me". Clicks on Click Recorder's own windows are left out.
- Recordings stay in memory until you close the app. **Save CSV…** writes the ticked ones to a file (one line per
  click: time, gap since the one before, position, button).
- **Show graphs** (it also opens when a recording stops), for the ticked recordings:
  - **Time between clicks:** a histogram per recording, all on the same millisecond scale so they can be compared,
    with the number of gaps, mean, standard deviation, median, shortest and longest beside each. Hover a column
    for its range and count. **Column width** and **Show up to** set the scale (0 picks them); a few long pauses
    (between screens, say) are left off the scale and counted as such.
  - **Where you clicked:** a dot per click over a faded picture of the screen as the recording started, up to
    three recordings at once. Hover a dot for which click it was.
  - **Table:** every click.
  - **Left button only** (on by default) leaves other buttons out of all three. **Save picture…** saves a graph.
- Auto Clicker's **background mode** sends its clicks straight to the game window as messages, so they don't pass
  through Windows' mouse input and can't be recorded. Its normal clicks can.

## Building

Close the app, then run `build.cmd`. It uses the C# compiler that ships with Windows
(.NET Framework 4), so no SDK or Visual Studio is needed. Source is in `src/`. Click Recorder builds the same
way with `recorder\build.cmd`, from `recorder\src`.

## Tests

Both tests play against the real `ClickEngine` in a pretend game window placed off-screen, so the real pointer
doesn't move. The pictures they use are in `tests/pictures`.

- **Radiant catching**, from the repository's folder:
  `tests\run-radiant-test.cmd tests\pictures\pc\radiant.jpg tests\pictures\pc\game-turn-lowmp.png tests\pictures\pc\game-caught-sdoodo.png tests\pictures\pc\game-fainted-choice.png`
  It also covers radiants only (runs, a failed run, a radiant after runs) and the PC's 2v2s, from the
  `duo-*.png` pictures beside `radiant.jpg`: the spots on them, and a pretend PC 2v2 with and without a radiant.
- **2v2 fights:** `tests\DuoTest.cs` takes the folder `tests\pictures\forest`. Build it the way
  `run-radiant-test.cmd` does, with `/main:AutoClicker.DuoTest` and `tests\DuoTest.cs` in place of the radiant test.
  Those pictures are the phone's, so it uses the phone's 2v2 spots (four cards in a row). Two of its checks
  failed before radiants only was added and still do: catches put down to the right Lumit by their caught name
  (7 of 16), and "no stray clicks and no stops" (fails about two runs in three).
