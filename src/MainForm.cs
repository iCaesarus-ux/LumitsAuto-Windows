using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace AutoClicker
{
    public class MainForm : Form
    {
        const int HotkeyToggleId = 1;
        const int HotkeyAddId = 2;
        const int DesignListWidth = 500;
        const string ListFileFilter = "Click list (*.clicklist)|*.clicklist|All files (*.*)|*.*";
        const string NoWindowText = "None — positions are on the whole screen";

        static readonly Color ReadyColor = Color.FromArgb(80, 80, 80);
        static readonly Color RunningColor = Color.FromArgb(0, 128, 0);
        static readonly Color WarningColor = Color.FromArgb(190, 80, 0);
        static readonly Color RadiantColor = Color.FromArgb(200, 120, 0);

        readonly AppSettings settings;
        readonly List<ClickPoint> points;
        readonly ClickEngine engine = new ClickEngine();
        /// <summary>The Lumits of the area being farmed; switching areas swaps it.</summary>
        LumitCounter counter;
        Action counterChanged;
        bool fillingAreas;
        bool modalOpen;
        bool toggleHotkeyActive;
        bool runCounts;

        GroupBox grpSteps, grpWindow, grpCounter, grpTiming, grpHuman, grpRepeat, grpHotkeys;
        ComboBox cbWindow, cbArea;
        CheckBox chkBackground, chkCount;
        CheckBox chkHid;
        ComboBox cbHidPort;
        Button btnHidRefresh;
        Label lblAlert, lblCounter, lblCounterMore, lblCatch;
        ListView lvSteps;
        Button btnPick, btnAdd, btnEdit, btnRemove, btnUp, btnDown, btnClear, btnSave, btnLoad, btnStart, btnStop, btnCatching;
        Label lblStepsHint, lblCps, lblRandomRange, lblStatus;
        NumericUpDown numInterval, numRandom, numJitter, numCycles, numBreakEvery, numBreakMin, numBreakMax;
        CheckBox chkRandom, chkJitter, chkHumanMouse, chkStopOnMouse, chkBreaks, chkRedo, chkTopMost;
        RadioButton rbForever, rbCycles;
        HotkeyBox hkToggle, hkAdd;
        ToolTip tips;

        public MainForm()
        {
            settings = AppSettings.LoadOrDefault(AppSettings.DefaultPath);
            points = settings.Points;
            counter = LumitCounter.Load(LumitCounter.PathFor(settings.Area, settings.Areas));
            BuildUi();
            ApplySettingsToControls();
            RefreshList(-1);
            ShowReadyStatus();
            engine.Progress += OnEngineProgress;
            engine.Finished += OnEngineFinished;
            engine.Notice += (title, message, picture) => PostToUi(delegate { SendPhoneAlert(title, message, picture); });
            counterChanged = delegate { PostToUi(UpdateCounterLabel); };
            counter.Changed += counterChanged;
        }

        // ---------------------------------------------------------------- layout

        void BuildUi()
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Auto Clicker";
            Icon = LoadAppIcon();
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(680, 872);
            tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400 };

            // Steps
            grpSteps = AddGroup(this, "Steps", 12, 8, 656, 272);
            lvSteps = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                Location = new Point(14, 22),
                Size = new Size(DesignListWidth, 206)
            };
            foreach (string col in new[] { "#", "Click", "Button", "Wait for screen", "Reaction", "Delay after" }) lvSteps.Columns.Add(col);
            lvSteps.SelectedIndexChanged += delegate { UpdateStepButtons(); };
            lvSteps.DoubleClick += delegate { EditSelected(); };
            lvSteps.KeyDown += OnListKeyDown;
            grpSteps.Controls.Add(lvSteps);

            lblStepsHint = new Label
            {
                AutoSize = false,
                Location = new Point(14, 232),
                Size = new Size(DesignListWidth, 34),
                ForeColor = SystemColors.GrayText
            };
            grpSteps.Controls.Add(lblStepsHint);

            const int bx = 526, bw = 118, bh = 25;
            btnPick = AddButton(grpSteps, "Pick on screen…", bx, 22, bw, bh, delegate { PickOnScreen(); });
            btnAdd = AddButton(grpSteps, "Add manually…", bx, 50, bw, bh, delegate { AddManually(); });
            btnEdit = AddButton(grpSteps, "Edit…", bx, 78, bw, bh, delegate { EditSelected(); });
            btnRemove = AddButton(grpSteps, "Remove", bx, 106, bw, bh, delegate { RemoveSelected(); });
            btnUp = AddButton(grpSteps, "Move up", bx, 134, bw, bh, delegate { MoveSelected(-1); });
            btnDown = AddButton(grpSteps, "Move down", bx, 162, bw, bh, delegate { MoveSelected(1); });
            btnClear = AddButton(grpSteps, "Clear all", bx, 190, bw, bh, delegate { ClearAll(); });
            btnSave = AddButton(grpSteps, "Save…", bx, 232, 57, bh, delegate { SaveList(); });
            btnLoad = AddButton(grpSteps, "Load…", bx + 61, 232, 57, bh, delegate { LoadList(); });
            tips.SetToolTip(btnPick, "Freezes the screen. Drag a box over a button to add a step that waits for it and\nclicks a random spot inside it, or click once for an exact spot. Right-click or Esc when done.");
            tips.SetToolTip(btnAdd, "Type in exact coordinates.");
            tips.SetToolTip(btnSave, "Save these steps and settings to a file.");
            tips.SetToolTip(btnLoad, "Replace the current steps and settings with a saved list.");

            // Game window
            grpWindow = AddGroup(this, "Game window", 12, 288, 656, 88);
            cbWindow = new ComboBox { Location = new Point(14, 22), Width = 380, DropDownStyle = ComboBoxStyle.DropDownList };
            grpWindow.Controls.Add(cbWindow);
            chkBackground = AddCheck(grpWindow, "Background mode (mouse stays free)", 410, 24);
            lblAlert = AddLabel(grpWindow, "", 14, 58);
            AddButton(grpWindow, "Alerts…", 526, 52, 116, 26, delegate { OpenAlertSettings(); });
            tips.SetToolTip(cbWindow, "Step positions are measured inside this window, so moving the window doesn't break them.\nThe app also reads this window directly, so it can be covered by other windows.");
            tips.SetToolTip(chkBackground, "Clicks go straight to the game window as messages, so your real mouse pointer\nstays free. The game must not be minimized; keep your own pointer off it while running.");

            // Lumits counter (stays usable during a run, so the list can be opened)
            grpCounter = AddGroup(this, "Lumits", 12, 384, 656, 92);
            chkCount = AddCheck(grpCounter, "Count lumits", 14, 27);
            cbArea = new ComboBox { Location = new Point(14, 56), Width = 108, DropDownStyle = ComboBoxStyle.DropDownList };
            cbArea.SelectedIndexChanged += delegate { AreaPicked(); };
            grpCounter.Controls.Add(cbArea);
            FillAreas();
            lblCounter = AddLabel(grpCounter, "", 130, 19);
            lblCounter.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            lblCounterMore = AddHint(grpCounter, "", 130, 39);
            lblCatch = AddLabel(grpCounter, "", 130, 63);
            var btnCounterList = AddButton(grpCounter, "List and rules…", 526, 18, 116, 26, delegate { OpenCounterList(); });
            btnCatching = AddButton(grpCounter, "Catching…", 526, 52, 116, 26, delegate { OpenCatching(); });
            tips.SetToolTip(chkCount, "Before each battle's first attack, reads the enemy's name off its name card\n(the one set in Alerts…) and counts it. Names are read on the side, so the loop isn't slowed.");
            tips.SetToolTip(btnCounterList, "How many of each lumit you've met. Tick the ones to catch, and choose\nwhich catches to keep (IV 25 in the stats you pick, or the natures you pick).");
            tips.SetToolTip(btnCatching, "Turn catching on, practice mode, healing with Bark Tea, and where things are on screen.");
            tips.SetToolTip(cbArea, "The area you're farming. Each has its own Lumits, with their catch ticks, keep rules and attacks,\nso a Lumit found in two areas has its own settings in each.");
            tips.SetToolTip(lblCatch, "Practice: always presses Keep and only notes what it would have released.\n" +
                                      "Perfect: Lumits with no keep rules left. Each rule is ticked off when a catch meets it.");

            // Timing
            grpTiming = AddGroup(this, "Timing", 12, 484, 322, 112);
            AddLabel(grpTiming, "Delay between clicks:", 14, 27);
            numInterval = AddNumber(grpTiming, 145, 24, 80, 1, 3600000);
            numInterval.ThousandsSeparator = true;
            AddLabel(grpTiming, "ms", 229, 27);
            lblCps = AddHint(grpTiming, "", 253, 27);
            tips.SetToolTip(numInterval, "Wait after each click before the next step.\nSteps that wait for the screen also wait for it to appear, plus their reaction delay.");

            chkRandom = AddCheck(grpTiming, "Vary delays by ±", 14, 55);
            numRandom = AddNumber(grpTiming, 145, 53, 50, 1, 100);
            AddLabel(grpTiming, "%", 199, 56);
            lblRandomRange = AddHint(grpTiming, "", 217, 56);

            chkJitter = AddCheck(grpTiming, "Offset spots by ±", 14, 84);
            numJitter = AddNumber(grpTiming, 145, 82, 50, 1, 100);
            AddLabel(grpTiming, "px", 199, 85);
            tips.SetToolTip(chkJitter, "Only for exact-spot steps. Steps with a box already click a random spot inside it.");

            // Human-like
            grpHuman = AddGroup(this, "Human-like", 346, 484, 322, 112);
            chkHumanMouse = AddCheck(grpHuman, "Natural mouse movement and clicks", 14, 24);
            chkStopOnMouse = AddCheck(grpHuman, "Stop if I move the mouse", 14, 53);
            chkBreaks = AddCheck(grpHuman, "Breaks every", 14, 82);
            numBreakEvery = AddNumber(grpHuman, 108, 80, 44, 1, 240);
            AddLabel(grpHuman, "min for", 155, 83);
            numBreakMin = AddNumber(grpHuman, 203, 80, 44, 1, 900);
            AddLabel(grpHuman, "–", 249, 83);
            numBreakMax = AddNumber(grpHuman, 261, 80, 44, 1, 900);
            AddLabel(grpHuman, "s", 307, 83);
            tips.SetToolTip(chkHumanMouse, "The mouse glides along a slight curve, speeding up and slowing down, sometimes\novershooting a little, and each click is held for a natural moment.\nTurn off for very fast clicking.");
            tips.SetToolTip(chkStopOnMouse, "Stops the run if you take over the mouse, so it doesn't fight you.");
            tips.SetToolTip(chkBreaks, "Every so often (randomly around this many minutes), pause between cycles\nfor a random time in this range.");

            // Repeat
            grpRepeat = AddGroup(this, "Repeat", 12, 604, 322, 112);
            rbForever = AddRadio(grpRepeat, "Until stopped", 14, 24);
            rbCycles = AddRadio(grpRepeat, "Stop after", 14, 53);
            numCycles = AddNumber(grpRepeat, 104, 51, 70, 1, 1000000);
            AddLabel(grpRepeat, "cycles", 178, 54);
            chkRedo = AddCheck(grpRepeat, "Redo a step if its screen comes back", 14, 82);
            tips.SetToolTip(rbCycles, "One cycle = one pass through every step\n(or one click, when the list is empty).");
            tips.SetToolTip(chkRedo, "While waiting for a step, if the previous step's screen shows up again\n(say the enemy survived and the attack buttons lit up), click it again.");

            // Hotkeys
            grpHotkeys = AddGroup(this, "Hotkeys (work from any window)", 346, 604, 322, 112);
            AddLabel(grpHotkeys, "Start / Stop:", 14, 27);
            hkToggle = new HotkeyBox { Location = new Point(140, 24), Width = 168 };
            grpHotkeys.Controls.Add(hkToggle);
            AddLabel(grpHotkeys, "Add mouse position:", 14, 57);
            hkAdd = new HotkeyBox { Location = new Point(140, 54), Width = 168 };
            grpHotkeys.Controls.Add(hkAdd);
            AddHint(grpHotkeys, "Click a box, then press keys. Delete clears it.", 14, 86);

            // Mouse output
            var grpHid = AddGroup(this, "Mouse output", 12, 728, 656, 56);
            chkHid = AddCheck(grpHid, "Use Pico HID mouse (looks like a real mouse)", 14, 22);
            cbHidPort = new ComboBox { Location = new Point(330, 20), Width = 196, DropDownStyle = ComboBoxStyle.DropDownList };
            grpHid.Controls.Add(cbHidPort);
            btnHidRefresh = AddButton(grpHid, "Refresh", 532, 19, 104, 25, delegate { FillHidPorts(); });
            tips.SetToolTip(chkHid, "Sends movement and clicks through the Pico so Windows sees a real mouse instead of\nsynthetic input. Turn background mode off while this is on.");
            tips.SetToolTip(cbHidPort, "The Pico's data port (the second of its two COM ports).");

            // Start / stop
            var bigFont = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnStart = AddButton(this, "", 12, 792, 170, 40, delegate { StartRun(true); });
            btnStart.Font = bigFont;
            btnStop = AddButton(this, "", 190, 792, 170, 40, delegate { engine.Stop(); });
            btnStop.Font = bigFont;
            btnStop.Enabled = false;
            chkTopMost = AddCheck(this, "Always on top", 566, 803);

            lblStatus = new Label
            {
                AutoSize = false,
                Location = new Point(12, 842),
                Size = new Size(656, 22),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            Controls.Add(lblStatus);

            numInterval.ValueChanged += delegate { UpdateTimingLabels(); };
            numRandom.ValueChanged += delegate { UpdateTimingLabels(); };
            chkRandom.CheckedChanged += delegate { UpdateEnabledStates(); UpdateTimingLabels(); };
            chkJitter.CheckedChanged += delegate { UpdateEnabledStates(); };
            chkBreaks.CheckedChanged += delegate { UpdateEnabledStates(); };
            rbCycles.CheckedChanged += delegate { UpdateEnabledStates(); };
            chkTopMost.CheckedChanged += delegate { TopMost = chkTopMost.Checked; };
            chkBackground.CheckedChanged += delegate
            {
                if (chkBackground.Checked) chkHid.Checked = false;
                UpdateEnabledStates();
            };
            chkHid.CheckedChanged += delegate
            {
                if (chkHid.Checked) chkBackground.Checked = false;
                UpdateEnabledStates();
            };
            chkCount.CheckedChanged += delegate
            {
                settings.CountLumits = chkCount.Checked;
                UpdateCounterLabel();
            };
            cbWindow.DropDown += delegate { FillWindowList(); };
            cbWindow.SelectionChangeCommitted += delegate { OnWindowChosen(); };
            hkToggle.HotkeyChanged += OnHotkeyEdited;
            hkAdd.HotkeyChanged += OnHotkeyEdited;

            ResumeLayout(false);
            PerformLayout();
        }

        static GroupBox AddGroup(Control parent, string text, int x, int y, int w, int h)
        {
            var g = new GroupBox { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            parent.Controls.Add(g);
            return g;
        }

        static Label AddLabel(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(l);
            return l;
        }

        static Label AddHint(Control parent, string text, int x, int y)
        {
            Label l = AddLabel(parent, text, x, y);
            l.ForeColor = SystemColors.GrayText;
            return l;
        }

        static Button AddButton(Control parent, string text, int x, int y, int w, int h, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        static CheckBox AddCheck(Control parent, string text, int x, int y)
        {
            var c = new CheckBox { Text = text, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(c);
            return c;
        }

        static RadioButton AddRadio(Control parent, string text, int x, int y)
        {
            var r = new RadioButton { Text = text, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(r);
            return r;
        }

        static NumericUpDown AddNumber(Control parent, int x, int y, int w, int min, int max)
        {
            var n = new NumericUpDown { Location = new Point(x, y), Width = w, Minimum = min, Maximum = max, TextAlign = HorizontalAlignment.Right };
            parent.Controls.Add(n);
            return n;
        }

        static Icon LoadAppIcon()
        {
            try
            {
                using (Stream s = typeof(MainForm).Assembly.GetManifestResourceStream("AutoClicker.app.ico"))
                {
                    if (s != null) return new Icon(s);
                }
            }
            catch (Exception)
            {
                // Fall back to the default window icon.
            }
            return null;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Column widths aren't touched by auto-scaling, so scale them by how much the list itself grew.
            float scale = lvSteps.Width / (float)DesignListWidth;
            int[] widths = { 28, 128, 62, 96, 86, 76 };
            for (int i = 0; i < widths.Length; i++) lvSteps.Columns[i].Width = (int)(widths[i] * scale);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = lvSteps;
        }

        // ---------------------------------------------------------------- settings <-> controls

        void ApplySettingsToControls()
        {
            numInterval.Value = Clamp(numInterval, settings.IntervalMs);
            chkRandom.Checked = settings.RandomizeSpeed;
            numRandom.Value = Clamp(numRandom, settings.RandomPercent);
            chkJitter.Checked = settings.RandomizePosition;
            numJitter.Value = Clamp(numJitter, settings.JitterPx);
            chkHumanMouse.Checked = settings.HumanMouse;
            chkStopOnMouse.Checked = settings.StopOnUserMouse;
            chkBreaks.Checked = settings.Breaks;
            numBreakEvery.Value = Clamp(numBreakEvery, settings.BreakEveryMin);
            numBreakMin.Value = Clamp(numBreakMin, settings.BreakMinSec);
            numBreakMax.Value = Clamp(numBreakMax, settings.BreakMaxSec);
            rbForever.Checked = settings.RepeatForever;
            rbCycles.Checked = !settings.RepeatForever;
            numCycles.Value = Clamp(numCycles, settings.Cycles);
            chkRedo.Checked = settings.RedoPrevious;
            hkToggle.Hotkey = settings.ToggleHotkey;
            hkAdd.Hotkey = settings.AddPointHotkey;
            chkTopMost.Checked = settings.TopMost;
            FillWindowList();
            chkBackground.Checked = settings.Background && HasTarget;
            chkHid.Checked = settings.HidMouse;
            FillHidPorts();
            chkCount.Checked = settings.CountLumits;
            UpdateAlertLabel();
            UpdateCounterLabel();
            UpdateEnabledStates();
            UpdateTimingLabels();
            UpdateHotkeyTexts();
        }

        void ReadControlsIntoSettings()
        {
            settings.IntervalMs = (int)numInterval.Value;
            settings.RandomizeSpeed = chkRandom.Checked;
            settings.RandomPercent = (int)numRandom.Value;
            settings.RandomizePosition = chkJitter.Checked;
            settings.JitterPx = (int)numJitter.Value;
            settings.HumanMouse = chkHumanMouse.Checked;
            settings.StopOnUserMouse = chkStopOnMouse.Checked;
            settings.Breaks = chkBreaks.Checked;
            settings.BreakEveryMin = (int)numBreakEvery.Value;
            settings.BreakMinSec = (int)Math.Min(numBreakMin.Value, numBreakMax.Value);
            settings.BreakMaxSec = (int)Math.Max(numBreakMin.Value, numBreakMax.Value);
            settings.RepeatForever = rbForever.Checked;
            settings.Cycles = (int)numCycles.Value;
            settings.RedoPrevious = chkRedo.Checked;
            settings.ToggleHotkey = hkToggle.Hotkey;
            settings.AddPointHotkey = hkAdd.Hotkey;
            settings.TopMost = chkTopMost.Checked;
            settings.Background = chkBackground.Checked && HasTarget;
            settings.HidMouse = chkHid.Checked;
            settings.HidPort = (cbHidPort.SelectedItem as string) ?? "";
            settings.CountLumits = chkCount.Checked;
        }

        static decimal Clamp(NumericUpDown n, int value)
        {
            return Math.Max(n.Minimum, Math.Min(n.Maximum, value));
        }

        void SaveSettingsQuietly()
        {
            try { settings.Save(AppSettings.DefaultPath); }
            catch (Exception) { }
        }

        void UpdateEnabledStates()
        {
            numRandom.Enabled = chkRandom.Checked;
            lblRandomRange.Visible = chkRandom.Checked;
            numJitter.Enabled = chkJitter.Checked;
            numCycles.Enabled = rbCycles.Checked;
            numBreakEvery.Enabled = numBreakMin.Enabled = numBreakMax.Enabled = chkBreaks.Checked;
            chkBackground.Enabled = HasTarget;
            chkStopOnMouse.Enabled = !chkBackground.Checked && !chkHid.Checked;
            cbHidPort.Enabled = btnHidRefresh.Enabled = chkHid.Checked;
        }

        // ---------------------------------------------------------------- game window

        bool HasTarget { get { return !string.IsNullOrEmpty(settings.TargetProcess); } }

        GameWindow FindTarget()
        {
            return HasTarget ? GameWindow.Find(settings.TargetProcess, settings.TargetTitle) : null;
        }

        /// <summary>What step positions are measured from right now, or null (with a reason) if the chosen window can't be used.</summary>
        StepSpace CurrentSpace(out string problem)
        {
            problem = null;
            if (!HasTarget) return StepSpace.WholeScreen;
            GameWindow w = FindTarget();
            if (w == null)
                problem = "Can't find the " + settings.TargetTitle + " window. Open it, or choose another game window.";
            else if (w.IsMinimized)
                problem = settings.TargetTitle + " is minimized. Restore its window first.";
            return problem == null ? new StepSpace(w) : null;
        }

        void FillWindowList()
        {
            cbWindow.BeginUpdate();
            cbWindow.Items.Clear();
            cbWindow.Items.Add(NoWindowText);
            int select = 0;
            if (HasTarget)
            {
                GameWindow target = FindTarget();
                foreach (GameWindow w in GameWindow.List())
                {
                    int i = cbWindow.Items.Add(w);
                    if (target != null && w.Handle == target.Handle) select = i;
                }
                if (target == null)
                    select = cbWindow.Items.Add(settings.TargetTitle + "   (" + settings.TargetProcess + ") — not open right now");
            }
            else
            {
                foreach (GameWindow w in GameWindow.List()) cbWindow.Items.Add(w);
            }
            cbWindow.SelectedIndex = select;
            cbWindow.EndUpdate();
        }

        void FillHidPorts()
        {
            cbHidPort.BeginUpdate();
            cbHidPort.Items.Clear();
            foreach (string p in System.IO.Ports.SerialPort.GetPortNames().OrderBy(n => n)) cbHidPort.Items.Add(p);
            cbHidPort.SelectedItem = settings.HidPort;
            if (cbHidPort.SelectedIndex < 0 && cbHidPort.Items.Count > 0) cbHidPort.SelectedIndex = 0;
            cbHidPort.EndUpdate();
        }

        void OnWindowChosen()
        {
            object item = cbWindow.SelectedItem;
            var chosen = item as GameWindow;
            bool none = NoWindowText.Equals(item);
            if (chosen == null && !none) return; // the saved window that isn't open: nothing changes

            // Steps keep the same place on screen: shift them from the old reference point to the new one.
            Point? oldOrigin = null;
            if (!HasTarget) oldOrigin = Point.Empty;
            else
            {
                GameWindow old = FindTarget();
                if (old != null) oldOrigin = old.ClientOrigin;
            }
            Point newOrigin = chosen == null ? Point.Empty : chosen.ClientOrigin;

            settings.TargetProcess = chosen == null ? "" : chosen.ProcessName;
            settings.TargetTitle = chosen == null ? "" : chosen.Title;
            if (none) chkBackground.Checked = false;
            UpdateEnabledStates();

            if (points.Count > 0)
            {
                if (oldOrigin.HasValue) ShiftSteps(oldOrigin.Value.X - newOrigin.X, oldOrigin.Value.Y - newOrigin.Y);
                else SetStatus("Step positions weren't converted, because the previous window isn't open.", WarningColor);
            }
            RefreshList(SelectedIndex);
        }

        void ShiftSteps(int dx, int dy)
        {
            foreach (ClickPoint p in points)
            {
                p.X += dx;
                p.Y += dy;
                if (p.WatchArea.Width > 0) p.WatchArea.Offset(dx, dy);
            }
            if (settings.AlertArea.Width > 0) settings.AlertArea.Offset(dx, dy);
            foreach (string key in settings.CatchSpots.Keys.ToList())
            {
                Rectangle r = settings.CatchSpots[key];
                r.Offset(dx, dy);
                settings.CatchSpots[key] = r;
            }
        }

        // ---------------------------------------------------------------- radiant alert

        void UpdateAlertLabel()
        {
            string radiant = settings.AlertEnabled
                ? "Radiant check before step " + settings.AlertStep + (settings.CatchRadiants ? ", catches them" : ", stops")
                : "Radiant check off";
            string phone;
            if (settings.AlertPhone.Length == 0) phone = "no phone alerts";
            else
                phone = "phone alerts by " + (PhoneNotifier.IsDiscord(settings.AlertPhone) ? "Discord" : "ntfy") +
                        (settings.AlertOnStop ? " (radiants and stops)" : " (radiants)");
            lblAlert.Text = radiant + "   •   " + phone;
            bool anything = settings.AlertEnabled || settings.AlertPhone.Length > 0;
            lblAlert.ForeColor = anything ? RadiantColor : SystemColors.GrayText;
        }

        void OpenAlertSettings()
        {
            if (engine.IsRunning || modalOpen) return;
            string problem;
            StepSpace space = CurrentSpace(out problem);
            if (space == null)
            {
                SetStatus(problem, WarningColor);
                return;
            }
            modalOpen = true;
            try
            {
                using (var dlg = new AlertDialog(settings, points.Count, space, ScreenCopies(space)))
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }
            finally { modalOpen = false; }
            UpdateAlertLabel();
            UpdateCounterLabel();
            SaveSettingsQuietly();
        }

        void OnRadiantFound(RunResult result)
        {
            string what = result.RadiantName.Length > 0 ? "Radiant " + result.RadiantName : "Radiant";
            string after = runCounts && counter.LastRadiantAfter > 0 ? " after " + Plural(counter.LastRadiantAfter, "lumit") : "";
            string time = DateTime.Now.ToString("HH:mm");
            SetStatus(what.ToUpperInvariant() + " spotted" + after + "! Stopped without attacking. (" + time + ")", RadiantColor);
            Native.Flash(Handle);
            SendPhoneAlert(what + " spotted!",
                (after.Length > 0 ? "Found" + after + ". " : "") +
                "Auto Clicker stopped before attacking at " + time + ". Come catch it!", result.Screenshot);
        }

        // ---------------------------------------------------------------- lumits counter

        /// <summary>True when the attack step and the enemy's name card are set (in Alerts…).</summary>
        bool NameCardSet
        {
            get { return settings.AlertStep >= 1 && settings.AlertStep <= points.Count && settings.AlertArea.Width > 0; }
        }

        void UpdateCounterLabel()
        {
            if (lblCounter == null) return;
            lblCounter.Text = Plural(counter.Total, "lumit") + " met   •   " + counter.ThisRun.ToString("N0") + " this run";
            string more = counter.LastRadiantAfter > 0
                ? counter.SinceRadiant.ToString("N0") + " since the last radiant"
                : "no radiant yet";
            if (counter.Last.Length > 0) more += "   •   last: " + counter.Last;
            if (!chkCount.Checked) more = "Counting is off.";
            else if (!NameCardSet) more = "Needs the attack step and the enemy's name card: set them in Alerts…";
            lblCounterMore.Text = more;
            lblCounterMore.ForeColor = chkCount.Checked && !NameCardSet ? WarningColor : SystemColors.GrayText;

            List<LumitCounter.Entry> entries = counter.Entries();
            int ticked = entries.Count(e => e.Catch);
            string catching = !settings.CatchOn ? "Catching off"
                : "Catching " + ticked + " kind" + (ticked == 1 ? "" : "s") + (settings.CatchPractice ? " (practice)" : "");
            string healing = settings.HealBelowHp > 0 ? "heals below " + settings.HealBelowHp + " HP" : "no healing";
            lblCatch.Text = catching + "   •   " + healing + "   •   " + entries.Count(e => e.IsPerfect) + " perfect";
            lblCatch.ForeColor = settings.CatchAndHealOff ? SystemColors.GrayText : RadiantColor;
        }

        const string NewArea = "New area…", RenameArea = "Rename…";

        void FillAreas()
        {
            fillingAreas = true;
            cbArea.Items.Clear();
            foreach (string a in settings.Areas) cbArea.Items.Add(a);
            cbArea.Items.Add(NewArea);
            cbArea.Items.Add(RenameArea);
            cbArea.SelectedIndex = Math.Max(0, settings.Areas.IndexOf(settings.Area));
            fillingAreas = false;
        }

        /// <summary>Switches to another area's Lumits, or makes or renames an area.</summary>
        void AreaPicked()
        {
            if (fillingAreas || cbArea.SelectedItem == null) return;
            string picked = (string)cbArea.SelectedItem;
            if (picked == NewArea || picked == RenameArea)
            {
                bool rename = picked == RenameArea;
                string name = AskText(rename ? "New name for " + settings.Area + ":" : "Name of the new area:", rename ? settings.Area : "");
                name = (name ?? "").Replace('|', ' ').Trim();
                if (name.Length > 0 && !settings.Areas.Contains(name))
                {
                    if (rename)
                    {
                        string oldPath = LumitCounter.PathFor(settings.Area, settings.Areas);
                        int at = settings.Areas.IndexOf(settings.Area);
                        settings.Areas[at] = name;
                        string newPath = LumitCounter.PathFor(name, settings.Areas);
                        try { if (oldPath != newPath && File.Exists(oldPath)) File.Move(oldPath, newPath); }
                        catch (Exception ex) { SetStatus("Couldn't rename the area's list: " + ex.Message, WarningColor); }
                    }
                    else
                    {
                        settings.Areas.Add(name);
                    }
                    settings.Area = name;
                    SaveSettingsQuietly();
                }
                FillAreas();
                SwitchCounter();
                return;
            }
            if (picked == settings.Area) return;
            settings.Area = picked;
            SaveSettingsQuietly();
            SwitchCounter();
        }

        void SwitchCounter()
        {
            counter.Changed -= counterChanged;
            counter = LumitCounter.Load(LumitCounter.PathFor(settings.Area, settings.Areas));
            counter.Changed += counterChanged;
            UpdateCounterLabel();
        }

        string AskText(string question, string start)
        {
            using (var form = new Form { Text = "Area", FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(320, 110), Font = new Font("Segoe UI", 9F) })
            {
                form.Controls.Add(new Label { Text = question, AutoSize = true, Location = new Point(12, 12) });
                var box = new TextBox { Location = new Point(12, 36), Width = 296, Text = start };
                form.Controls.Add(box);
                var ok = new Button { Text = "OK", Location = new Point(146, 72), Size = new Size(76, 26), DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Location = new Point(230, 72), Size = new Size(76, 26), DialogResult = DialogResult.Cancel };
                form.Controls.Add(ok);
                form.Controls.Add(cancel);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                modalOpen = true;
                try { return form.ShowDialog(this) == DialogResult.OK ? box.Text : null; }
                finally { modalOpen = false; }
            }
        }

        void OpenCounterList()
        {
            if (modalOpen) return;
            modalOpen = true;
            try
            {
                using (var dlg = new CounterDialog(counter)) dlg.ShowDialog(this);
            }
            finally { modalOpen = false; }
        }

        void OpenCatching()
        {
            if (engine.IsRunning || modalOpen) return;
            string problem;
            StepSpace space = CurrentSpace(out problem);
            if (space == null)
            {
                SetStatus(problem, WarningColor);
                return;
            }
            modalOpen = true;
            try
            {
                using (var dlg = new CatchDialog(settings, counter, space, ScreenCopies(space)))
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;
            }
            finally { modalOpen = false; }
            UpdateCounterLabel();
            SaveSettingsQuietly();
        }

        /// <summary>Sends to the ntfy topic / Discord webhook in the background and notes the outcome in the status line.</summary>
        void SendPhoneAlert(string title, string message, byte[] picture)
        {
            string phone = settings.AlertPhone;
            if (phone.Length == 0) return;
            var t = new System.Threading.Thread(() =>
            {
                string error = PhoneNotifier.Send(phone, title, message, picture);
                PostToUi(delegate
                {
                    if (engine.IsRunning) return;
                    lblStatus.Text += error == null ? "  Phone alert sent." : "  Phone alert failed: " + error;
                });
            }) { IsBackground = true };
            t.Start();
        }

        /// <summary>Copies of the steps in screen coordinates, for drawing them in the picker.</summary>
        List<ClickPoint> ScreenCopies(StepSpace space)
        {
            Point o = space.Origin;
            return points.Select(p =>
            {
                ClickPoint c = p.Clone();
                c.X += o.X;
                c.Y += o.Y;
                return c;
            }).ToList();
        }

        void UpdateTimingLabels()
        {
            int interval = (int)numInterval.Value;
            double cps = 1000.0 / interval;
            lblCps.Text = "≈ " + cps.ToString(cps >= 10 ? "N0" : "0.##") + "/s";

            int pct = (int)numRandom.Value;
            int lo = (int)Math.Round(interval * (100 - pct) / 100.0);
            int hi = (int)Math.Round(interval * (100 + pct) / 100.0);
            lblRandomRange.Text = lo.ToString("N0") + "–" + hi.ToString("N0") + " ms";
        }

        void UpdateHotkeyTexts()
        {
            string toggle = settings.ToggleHotkey == Keys.None ? "" : "   " + HotkeyBox.Format(settings.ToggleHotkey);
            btnStart.Text = "▶  Start" + toggle;
            btnStop.Text = "■  Stop" + toggle;

            string add = settings.AddPointHotkey == Keys.None
                ? ""
                : " " + HotkeyBox.Format(settings.AddPointHotkey) + " adds the mouse position.";
            lblStepsHint.Text = "Runs top to bottom, then repeats. In Pick on screen, drag a box over a button: it waits for it " +
                                "to appear, then clicks a random spot inside." + add;
        }

        // ---------------------------------------------------------------- hotkeys

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            Native.UnregisterHotKey(Handle, HotkeyToggleId);
            Native.UnregisterHotKey(Handle, HotkeyAddId);
            base.OnHandleDestroyed(e);
        }

        void RegisterHotkeys()
        {
            if (!IsHandleCreated) return;
            Native.UnregisterHotKey(Handle, HotkeyToggleId);
            Native.UnregisterHotKey(Handle, HotkeyAddId);

            toggleHotkeyActive = TryRegisterHotkey(HotkeyToggleId, settings.ToggleHotkey);
            bool addActive = TryRegisterHotkey(HotkeyAddId, settings.AddPointHotkey);
            hkToggle.Invalid = settings.ToggleHotkey != Keys.None && !toggleHotkeyActive;
            hkAdd.Invalid = settings.AddPointHotkey != Keys.None && !addActive;
            ShowReadyStatus();
        }

        bool TryRegisterHotkey(int id, Keys keys)
        {
            if (keys == Keys.None) return false;
            uint mods = Native.MOD_NOREPEAT;
            if ((keys & Keys.Control) == Keys.Control) mods |= Native.MOD_CONTROL;
            if ((keys & Keys.Alt) == Keys.Alt) mods |= Native.MOD_ALT;
            if ((keys & Keys.Shift) == Keys.Shift) mods |= Native.MOD_SHIFT;
            return Native.RegisterHotKey(Handle, id, mods, (uint)(keys & Keys.KeyCode));
        }

        void OnHotkeyEdited(object sender, EventArgs e)
        {
            settings.ToggleHotkey = hkToggle.Hotkey;
            settings.AddPointHotkey = hkAdd.Hotkey;
            RegisterHotkeys();
            UpdateHotkeyTexts();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                // A registered hotkey is swallowed by Windows before the hotkey box sees it,
                // so while one of the boxes is being edited, hand the combination to it instead.
                HotkeyBox editing = hkToggle.Focused ? hkToggle : hkAdd.Focused ? hkAdd : null;
                if (editing != null)
                    editing.SetFromUser(id == HotkeyToggleId ? settings.ToggleHotkey : settings.AddPointHotkey);
                else if (id == HotkeyToggleId)
                    ToggleRun();
                else if (id == HotkeyAddId)
                    AddPointAtCursor();
                return;
            }
            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- running

        void ToggleRun()
        {
            if (engine.IsRunning) engine.Stop();
            else if (!modalOpen) StartRun(false);
        }

        void StartRun(bool fromButton)
        {
            if (engine.IsRunning || modalOpen) return;

            string problem;
            StepSpace space = CurrentSpace(out problem);
            if (space == null)
            {
                SetStatus(problem, WarningColor);
                return;
            }
            bool background = chkBackground.Checked && space.Window != null;
            if (background && points.Count == 0)
            {
                SetStatus("Background mode needs at least one step to click.", WarningColor);
                return;
            }
            bool alert = settings.AlertEnabled && points.Count > 0;
            if (alert && !NameCardSet)
            {
                SetStatus("The radiant check points at a step or area that doesn't exist. Check Alerts…", WarningColor);
                return;
            }
            CatchOptions catching = null;
            if (!settings.CatchAndHealOff && points.Count > 0)
            {
                // Catching insists on its setup; healing alone just stays off without it.
                if (settings.CatchOn && space.Window == null)
                {
                    SetStatus("Catching needs the game window chosen under Game window.", WarningColor);
                    return;
                }
                if (settings.CatchOn && !NameCardSet)
                {
                    SetStatus("Catching needs the attack step and the enemy's name card. Set them in Alerts…", WarningColor);
                    return;
                }
            }
            // A radiant is caught even with the rest off. Without a game window it stops the run instead.
            if ((!settings.CatchAndHealOff || (alert && settings.CatchRadiants)) && points.Count > 0 && space.Window != null && NameCardSet)
            {
                catching = new CatchOptions
                {
                    CatchOn = settings.CatchOn,
                    Practice = settings.CatchPractice,
                    CatchRadiants = settings.CatchRadiants,
                    PartnerWeaken = settings.PartnerWeaken,
                    PartnerKill = settings.PartnerKill,
                    HealBelowHp = settings.HealBelowHp,
                    LeadMaxHp = settings.LeadMaxHp,
                    DragRealPointer = settings.DragRealPointer,
                    Spots = CatchSpots.Resolve(space.Window.ClientSize, settings.CatchSpots),
                    Log = CatchLog.Default
                };
            }
            // Without a name card it simply doesn't count; catching needs the names, so it counts too.
            bool count = (chkCount.Checked || catching != null) && NameCardSet;

            bool hid = chkHid.Checked;
            if (hid)
            {
                string port = cbHidPort.SelectedItem as string;
                if (string.IsNullOrEmpty(port))
                {
                    SetStatus("Choose the Pico's COM port, or untick the HID mouse.", WarningColor);
                    return;
                }
                if (!HidMouse.Connect(port))
                {
                    SetStatus("Couldn't open " + port + ". Is the Pico plugged in, and is this its data port?", WarningColor);
                    return;
                }
            }
            Pointer.UseHid = hid;

            if (fromButton && !toggleHotkeyActive && points.Count > 0 && !background && !hid)
            {
                modalOpen = true;
                DialogResult answer;
                try
                {
                    answer = MessageBox.Show(this,
                        "There's no working Start/Stop hotkey, so it may be hard to reach the Stop button " +
                        "while the mouse is being moved around.\n\nStart anyway?",
                        "Auto Clicker", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                }
                finally { modalOpen = false; }
                if (answer != DialogResult.Yes) return;
            }

            ReadControlsIntoSettings();
            SaveSettingsQuietly();
            var options = new RunOptions
            {
                Hid = hid,
                Points = points.Select(p => p.Clone()).ToArray(),
                Space = space,
                Background = background,
                IntervalMs = settings.IntervalMs,
                RandomPercent = settings.RandomizeSpeed ? settings.RandomPercent : 0,
                JitterPx = settings.RandomizePosition ? settings.JitterPx : 0,
                Cycles = settings.RepeatForever ? 0 : settings.Cycles,
                HumanMouse = settings.HumanMouse,
                StopOnUserMouse = settings.StopOnUserMouse && !background && !hid,
                RedoPrevious = settings.RedoPrevious || catching != null, // your turn coming back is how a fight goes on
                BreakEveryMs = settings.Breaks ? settings.BreakEveryMin * 60000 : 0,
                BreakMinMs = settings.BreakMinSec * 1000,
                BreakMaxMs = settings.BreakMaxSec * 1000,
                NameStep = alert || count ? settings.AlertStep - 1 : -1,
                NameArea = settings.AlertArea,
                RadiantCheck = alert,
                AlertGoldPercent = settings.AlertGoldPercent,
                Counter = count ? counter : null,
                Catch = catching
            };
            counter.StartRun();
            if (!engine.Start(options)) return;
            runCounts = count;

            SetRunningUi(true);
            ShowRunningStatus(0, 1, "");
        }

        void SetRunningUi(bool running)
        {
            grpSteps.Enabled = grpWindow.Enabled = grpTiming.Enabled = grpHuman.Enabled = grpRepeat.Enabled = grpHotkeys.Enabled = !running;
            chkCount.Enabled = btnCatching.Enabled = cbArea.Enabled = !running;
            if (running)
            {
                btnStop.Enabled = true;
                btnStart.Enabled = false;
                btnStop.Focus();
            }
            else
            {
                btnStart.Enabled = true;
                btnStop.Enabled = false;
                btnStart.Focus();
            }
        }

        void OnEngineProgress(long clicks, int cycle, string activity)
        {
            PostToUi(delegate { if (engine.IsRunning) ShowRunningStatus(clicks, cycle, activity); });
        }

        void OnEngineFinished(RunResult result)
        {
            PostToUi(delegate
            {
                if (engine.IsRunning) return; // a new run already started
                HidMouse.Disconnect();
                Pointer.UseHid = false;
                SetRunningUi(false);

                if (result.RadiantFound)
                {
                    OnRadiantFound(result);
                    return;
                }
                string counts = Plural(result.Clicks, "click");
                if (points.Count > 0) counts += " over " + Plural(result.Cycles, "cycle");
                if (result.Problem != null)
                {
                    SetStatus("Stopped: " + result.Problem + " (" + counts + ").", WarningColor);
                    if (result.AlertTitle != null)
                    {
                        // You asked to always hear about these, whatever the "stops" setting says.
                        Native.Flash(Handle);
                        SendPhoneAlert(result.AlertTitle,
                            "Stopped at " + DateTime.Now.ToString("HH:mm") + ": " + result.Problem + ". (" + counts + ")", result.Screenshot);
                    }
                    else if (result.NotifyPhone && settings.AlertOnStop)
                    {
                        Native.Flash(Handle);
                        SendPhoneAlert("Auto Clicker stopped",
                            "Stopped at " + DateTime.Now.ToString("HH:mm") + ": " + result.Problem + ". (" + counts + ")", result.Screenshot);
                    }
                    return;
                }
                string text = (result.Completed ? "Done" : "Stopped") + " — " + counts + ".";
                if (toggleHotkeyActive) text += " Press " + HotkeyBox.Format(settings.ToggleHotkey) + " to start again.";
                SetStatus(text, ReadyColor);
            });
        }

        void PostToUi(MethodInvoker action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { } // window is closing
        }

        void ShowRunningStatus(long clicks, int cycle, string activity)
        {
            string text = "Running — " + Plural(clicks, "click");
            if (points.Count > 0)
                text += ", cycle " + cycle.ToString("N0") + (settings.RepeatForever ? "" : " of " + settings.Cycles.ToString("N0"));
            if (!string.IsNullOrEmpty(activity)) text += ", " + activity;
            if (toggleHotkeyActive) text += "   •   " + HotkeyBox.Format(settings.ToggleHotkey) + " stops";
            SetStatus(text, RunningColor);
        }

        void ShowReadyStatus()
        {
            if (engine.IsRunning || lblStatus == null) return;
            if (hkToggle.Invalid || hkAdd.Invalid)
            {
                HotkeyBox bad = hkToggle.Invalid ? hkToggle : hkAdd;
                SetStatus("Hotkey " + HotkeyBox.Format(bad.Hotkey) +
                          " is already in use (by another program, or the other hotkey). Pick a different one.", WarningColor);
            }
            else if (toggleHotkeyActive)
                SetStatus("Ready — press " + HotkeyBox.Format(settings.ToggleHotkey) + " or click Start.", ReadyColor);
            else
                SetStatus("Ready — click Start. (No Start/Stop hotkey set.)", ReadyColor);
        }

        void SetStatus(string text, Color color)
        {
            lblStatus.Text = text;
            lblStatus.ForeColor = color;
        }

        static string Plural(long n, string word)
        {
            return n.ToString("N0") + " " + word + (n == 1 ? "" : "s");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            engine.StopAndWait(1000);
            ReadControlsIntoSettings();
            SaveSettingsQuietly();
            HidMouse.Disconnect();
            base.OnFormClosing(e);
        }

        // ---------------------------------------------------------------- step list

        int SelectedIndex
        {
            get { return lvSteps.SelectedIndices.Count > 0 ? lvSteps.SelectedIndices[0] : -1; }
        }

        void RefreshList(int select)
        {
            lvSteps.BeginUpdate();
            lvSteps.Items.Clear();
            for (int i = 0; i < points.Count; i++)
            {
                ClickPoint p = points[i];
                var item = new ListViewItem((i + 1).ToString());
                item.SubItems.Add(p.X + ", " + p.Y + (p.IsArea ? "   " + p.Width + "×" + p.Height : ""));
                item.SubItems.Add(p.Button + (p.DoubleClick ? " ×2" : "") + (p.BorrowPointer ? ", real ptr" : ""));
                if (p.Waits)
                {
                    item.SubItems.Add("≥ " + p.MatchPercent + "%" + (p.IsArea && p.WatchArea == p.Area ? "" : " (other)"));
                    item.SubItems.Add(p.ReactMinMs.ToString("N0") + "–" + p.ReactMaxMs.ToString("N0") + " ms");
                }
                else
                {
                    item.SubItems.Add(p.WaitForScreen ? "needs snapshot" : "—");
                    item.SubItems.Add("—");
                }
                item.SubItems.Add(p.DelayMs >= 0 ? p.DelayMs.ToString("N0") + " ms" : "interval");
                lvSteps.Items.Add(item);
            }
            lvSteps.EndUpdate();

            if (select >= 0 && select < lvSteps.Items.Count)
            {
                ListViewItem item = lvSteps.Items[select];
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
            }
            grpSteps.Text = points.Count == 0 ? "Steps" : "Steps (" + points.Count + ")";
            lvSteps.Columns[1].Text = HasTarget ? "Click (in game window)" : "Click";
            UpdateStepButtons();
            UpdateCounterLabel(); // the attack step may have come or gone
        }

        void UpdateStepButtons()
        {
            int i = SelectedIndex;
            btnEdit.Enabled = btnRemove.Enabled = i >= 0;
            btnUp.Enabled = i > 0;
            btnDown.Enabled = i >= 0 && i < points.Count - 1;
            btnClear.Enabled = btnSave.Enabled = points.Count > 0;
        }

        void OnListKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { RemoveSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { EditSelected(); e.Handled = true; }
        }

        void PickOnScreen()
        {
            if (engine.IsRunning || modalOpen) return;
            string problem;
            StepSpace space = CurrentSpace(out problem);
            if (space == null)
            {
                SetStatus(problem, WarningColor);
                return;
            }

            var added = new List<ClickPoint>();
            modalOpen = true;
            try
            {
                ScreenGrab.WhileHidden(new Form[] { this }, delegate
                {
                    Rectangle vs = SystemInformation.VirtualScreen;
                    using (Bitmap frame = ScreenGrab.CaptureVirtualScreen())
                    using (var overlay = new PickOverlay(frame, vs, ScreenCopies(space), false))
                    {
                        overlay.ShowDialog();
                        foreach (Rectangle r in overlay.Picks) added.Add(StepFromPick(frame, vs, r, space));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't capture the screen:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                modalOpen = false;
                Activate();
            }

            if (added.Count == 0) return;
            points.AddRange(added);
            RefreshList(points.Count - 1);
            bool anyWaits = added.Any(p => p.Waits);
            SetStatus("Added " + Plural(added.Count, "step") + "." +
                      (anyWaits ? " Boxes wait until their area looks like it does now (Edit to change)." : ""), ReadyColor);
        }

        /// <summary>A dragged box becomes a step that waits for that area and clicks inside it; a plain click becomes an exact spot.</summary>
        /// <param name="r">Picked area in screen coordinates (0 × 0 for an exact spot).</param>
        static ClickPoint StepFromPick(Bitmap frame, Rectangle vs, Rectangle r, StepSpace space)
        {
            Rectangle s = space.FromScreen(r);
            var p = new ClickPoint { X = s.X, Y = s.Y, Width = s.Width, Height = s.Height };
            if (p.IsArea)
            {
                p.WatchArea = s;
                p.Snapshot = Snapshot.FromBitmap(frame, new Rectangle(r.X - vs.X, r.Y - vs.Y, r.Width, r.Height));
                p.WaitForScreen = p.Snapshot != null;
            }
            return p;
        }

        void AddPointAtCursor()
        {
            if (engine.IsRunning || modalOpen) return;
            string problem;
            StepSpace space = CurrentSpace(out problem);
            if (space == null)
            {
                SetStatus(problem, WarningColor);
                return;
            }
            Point pos = Cursor.Position, o = space.Origin;
            points.Add(new ClickPoint { X = pos.X - o.X, Y = pos.Y - o.Y });
            RefreshList(points.Count - 1);
            SetStatus("Added step " + points.Count + " at " + (pos.X - o.X) + ", " + (pos.Y - o.Y) + ".", ReadyColor);
        }

        void AddManually()
        {
            string problem;
            StepSpace space = CurrentSpace(out problem);
            if (space == null)
            {
                SetStatus(problem, WarningColor);
                return;
            }
            Point pos = Cursor.Position, o = space.Origin;
            ClickPoint p = ShowStepDialog(new ClickPoint { X = pos.X - o.X, Y = pos.Y - o.Y }, "Add step", space);
            if (p == null) return;
            points.Add(p);
            RefreshList(points.Count - 1);
        }

        void EditSelected()
        {
            int i = SelectedIndex;
            if (i < 0) return;
            string problem;
            StepSpace space = CurrentSpace(out problem);
            if (space == null)
            {
                SetStatus(problem, WarningColor);
                return;
            }
            ClickPoint p = ShowStepDialog(points[i], "Edit step " + (i + 1), space);
            if (p == null) return;
            points[i] = p;
            RefreshList(i);
        }

        ClickPoint ShowStepDialog(ClickPoint step, string title, StepSpace space)
        {
            modalOpen = true;
            try
            {
                using (var dlg = new StepDialog(step, title, (int)numInterval.Value, ScreenCopies(space), space))
                    return dlg.ShowDialog(this) == DialogResult.OK ? dlg.Result : null;
            }
            finally { modalOpen = false; }
        }

        void RemoveSelected()
        {
            int i = SelectedIndex;
            if (i < 0) return;
            points.RemoveAt(i);
            RefreshList(Math.Min(i, points.Count - 1));
        }

        void MoveSelected(int delta)
        {
            int i = SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= points.Count) return;
            ClickPoint tmp = points[i];
            points[i] = points[j];
            points[j] = tmp;
            RefreshList(j);
        }

        void ClearAll()
        {
            if (points.Count == 0) return;
            modalOpen = true;
            DialogResult answer;
            try
            {
                answer = MessageBox.Show(this, "Remove all " + Plural(points.Count, "step") + "?", "Auto Clicker",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            }
            finally { modalOpen = false; }
            if (answer != DialogResult.Yes) return;
            points.Clear();
            RefreshList(-1);
        }

        void SaveList()
        {
            ReadControlsIntoSettings();
            using (var dlg = new SaveFileDialog { Filter = ListFileFilter, DefaultExt = "clicklist", FileName = "my clicks.clicklist", Title = "Save click list" })
            {
                if (!ShowFileDialog(dlg)) return;
                try
                {
                    settings.SaveProfile(dlg.FileName);
                    SetStatus("Saved " + Plural(points.Count, "step") + " to " + Path.GetFileName(dlg.FileName) + ".", ReadyColor);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't save the file:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        void LoadList()
        {
            using (var dlg = new OpenFileDialog { Filter = ListFileFilter, Title = "Load click list" })
            {
                if (!ShowFileDialog(dlg)) return;
                try
                {
                    settings.LoadProfile(dlg.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Couldn't read the file:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                ApplySettingsToControls();
                RefreshList(points.Count > 0 ? 0 : -1);
                SetStatus("Loaded " + Plural(points.Count, "step") + " from " + Path.GetFileName(dlg.FileName) + ".", ReadyColor);
            }
        }

        bool ShowFileDialog(CommonDialog dlg)
        {
            modalOpen = true;
            try { return dlg.ShowDialog(this) == DialogResult.OK; }
            finally { modalOpen = false; }
        }
    }
}
