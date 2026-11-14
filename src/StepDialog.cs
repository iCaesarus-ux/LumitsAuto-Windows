using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>Add/edit dialog for one step: where to click, and what to wait for first.</summary>
    public class StepDialog : Form
    {
        static readonly Color GoodColor = Color.FromArgb(0, 128, 0);
        static readonly Color BadColor = Color.FromArgb(190, 30, 30);

        readonly IList<ClickPoint> screenSteps;
        readonly StepSpace space;
        readonly NumericUpDown numX, numY, numW, numH, numDelay, numMatch, numReactMin, numReactMax, numTimeout;
        readonly ComboBox cbButton, cbClick, cbTimeout;
        readonly CheckBox chkDelay, chkWait, chkBorrow;
        readonly Label lblWatch, lblTest, lblShape;
        readonly Button btnBox;
        readonly PictureBox picSnapshot;
        readonly GroupBox grpWait;
        Rectangle watchArea;
        Point[] watchShape;
        Snapshot snapshot;
        /// <summary>The click area's lasso as last picked, with the box it was picked in; the numbers can move or stretch it since.</summary>
        Point[] shape;
        Rectangle shapeBox;
        ClickPoint result;

        public ClickPoint Result { get { return result; } }

        /// <param name="screenSteps">All steps in screen coordinates, shown as markers when picking an area.</param>
        /// <param name="space">What this step's positions are measured from.</param>
        public StepDialog(ClickPoint step, string title, int defaultDelayMs, IList<ClickPoint> screenSteps, StepSpace space)
        {
            this.screenSteps = screenSteps;
            this.space = space;
            watchArea = step.WatchArea;
            watchShape = step.WatchShape;
            snapshot = step.Snapshot;
            shape = step.IsLasso ? step.Shape : null;
            shapeBox = step.Area;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 558);
            var tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400 };

            // Where to click
            var grpClick = new GroupBox { Text = "Where to click", Location = new Point(12, 8), Size = new Size(416, 214) };
            Controls.Add(grpClick);
            AddLabel(grpClick, "X:", 14, 27);
            numX = AddNumber(grpClick, 34, 24, 68, -100000, 100000, step.X);
            AddLabel(grpClick, "Y:", 112, 27);
            numY = AddNumber(grpClick, 132, 24, 68, -100000, 100000, step.Y);
            AddLabel(grpClick, "Size:", 216, 27);
            numW = AddNumber(grpClick, 254, 24, 60, 0, 10000, step.Width);
            AddLabel(grpClick, "×", 318, 27);
            numH = AddNumber(grpClick, 332, 24, 60, 0, 10000, step.Height);
            var sizeHint = new Label
            {
                Text = "0 × 0 clicks the exact spot. A bigger box or a lasso clicks a random spot inside it, usually near the middle.",
                Location = new Point(14, 54),
                Size = new Size(388, 32),
                ForeColor = SystemColors.GrayText
            };
            grpClick.Controls.Add(sizeHint);

            var btnPickClick = AddButton(grpClick, "Pick on screen…", 14, 88, 116, 26, delegate { PickClickArea(); });
            lblShape = AddLabel(grpClick, "", 138, 94);
            btnBox = AddButton(grpClick, "Make it a box", 302, 88, 100, 26, delegate { MakeBox(); });
            tips.SetToolTip(btnPickClick, "Pick this step's click area again: drag a box, press L to draw a lasso around it instead,\n" +
                                          "or click once for an exact spot. If the step watches its click area, that's picked too,\n" +
                                          "so get the game to this step's screen first.");
            tips.SetToolTip(btnBox, "Click anywhere in the lasso's box instead of only inside the lasso.");

            AddLabel(grpClick, "Button:", 14, 126);
            cbButton = AddCombo(grpClick, 68, 123, 86, new object[] { "Left", "Right", "Middle" }, (int)step.Button);
            AddLabel(grpClick, "Click:", 172, 126);
            cbClick = AddCombo(grpClick, 214, 123, 86, new object[] { "Single", "Double" }, step.DoubleClick ? 1 : 0);

            chkDelay = new CheckBox { Text = "Custom delay after:", AutoSize = true, Location = new Point(14, 156), Checked = step.DelayMs >= 0 };
            grpClick.Controls.Add(chkDelay);
            numDelay = AddNumber(grpClick, 160, 154, 80, 0, 3600000, step.DelayMs >= 0 ? step.DelayMs : defaultDelayMs);
            numDelay.ThousandsSeparator = true;
            AddLabel(grpClick, "ms", 244, 157);
            tips.SetToolTip(chkDelay, "When unchecked, the main \"Delay between clicks\" is used.");

            chkBorrow = new CheckBox
            {
                Text = "Background mode: use the real pointer for this click",
                AutoSize = true,
                Location = new Point(14, 184),
                Checked = step.BorrowPointer
            };
            grpClick.Controls.Add(chkBorrow);
            tips.SetToolTip(chkBorrow, "Tick this for clicks on the game world, like the stone on the map. There the game\n" +
                                       "asks Windows where the real pointer is instead of reading the click, so the pointer\n" +
                                       "jumps to the spot for about a quarter of a second, then comes back.\n" +
                                       "Buttons such as Attack and Continue don't need it.");

            // Wait for the screen
            grpWait = new GroupBox { Text = "Wait for the screen", Location = new Point(12, 230), Size = new Size(416, 280) };
            Controls.Add(grpWait);
            chkWait = new CheckBox { Text = "Wait until the game shows this before clicking", AutoSize = true, Location = new Point(14, 24), Checked = step.WaitForScreen };
            grpWait.Controls.Add(chkWait);

            AddLabel(grpWait, "Watching:", 14, 52);
            lblWatch = AddLabel(grpWait, "", 78, 52);
            picSnapshot = new PictureBox
            {
                Location = new Point(14, 76),
                Size = new Size(170, 62),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            grpWait.Controls.Add(picSnapshot);
            tips.SetToolTip(picSnapshot, "What the screen should look like (shrunk) before the click happens.");

            var btnUseClick = AddButton(grpWait, "Use click area", 196, 76, 100, 26, delegate { UseClickArea(); });
            var btnPick = AddButton(grpWait, "Pick area…", 302, 76, 100, 26, delegate { PickWatchArea(); });
            var btnRetake = AddButton(grpWait, "Retake snapshot from screen", 196, 110, 206, 26, delegate { RetakeSnapshot(); });
            tips.SetToolTip(btnUseClick, "Watch the same box that gets clicked, and take its snapshot from the screen now.");
            tips.SetToolTip(btnPick, "Watch a different part of the screen, for example something steadier than the button.\n" +
                                     "Press L there to draw a lasso around it instead of a box.");
            var shotHint = new Label
            {
                Text = space.Window != null
                    ? "Snapshots and tests read the game window as it is right now (it can be covered, not minimized)."
                    : "Snapshots and tests use the screen as it is right now, so get the game to this screen first.",
                Location = new Point(14, 146),
                Size = new Size(388, 32),
                ForeColor = SystemColors.GrayText
            };
            grpWait.Controls.Add(shotHint);

            AddLabel(grpWait, "Match needed:", 14, 186);
            numMatch = AddNumber(grpWait, 110, 183, 52, 1, 100, step.MatchPercent);
            AddLabel(grpWait, "%", 165, 186);
            AddButton(grpWait, "Test now", 190, 181, 80, 26, delegate { TestNow(); });
            lblTest = AddLabel(grpWait, "", 278, 186);
            tips.SetToolTip(numMatch, "How closely the screen must match the snapshot. Lower it if the step never fires;\nraise it if it fires too early (e.g. while the button is still fading in).");

            AddLabel(grpWait, "Reaction delay:", 14, 217);
            numReactMin = AddNumber(grpWait, 110, 214, 60, 0, 60000, step.ReactMinMs);
            AddLabel(grpWait, "–", 174, 217);
            numReactMax = AddNumber(grpWait, 186, 214, 60, 0, 60000, step.ReactMaxMs);
            AddLabel(grpWait, "ms after it appears", 250, 217);
            tips.SetToolTip(numReactMin, "After the screen appears, wait a random time in this range before clicking.\nShort waits are more common than long ones, like a person's reactions.");

            AddLabel(grpWait, "If not seen within", 14, 248);
            numTimeout = AddNumber(grpWait, 124, 245, 50, 1, 3600, step.TimeoutSec);
            AddLabel(grpWait, "s:", 178, 248);
            cbTimeout = AddCombo(grpWait, 196, 245, 150, new object[] { "keep looking, then stop", "skip this step", "click anyway" }, (int)step.OnTimeout);
            tips.SetToolTip(cbTimeout, "Keep looking: if any step's screen shows up, the run carries on from there;\n" +
                                       "after 3 of these time-outs with nothing recognisable, it stops (and alerts your phone).");

            var ok = new Button { Text = "OK", Location = new Point(262, 520), Size = new Size(80, 28) };
            ok.Click += delegate { Commit(); };
            var cancel = new Button { Text = "Cancel", Location = new Point(348, 520), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            chkDelay.CheckedChanged += delegate { numDelay.Enabled = chkDelay.Checked; };
            chkWait.CheckedChanged += delegate { UpdateWaitEnabled(); };
            foreach (NumericUpDown n in new[] { numX, numY, numW, numH })
                n.ValueChanged += delegate
                {
                    UpdateShapeLabel();
                    UpdateWatchLabel();
                };
            numDelay.Enabled = chkDelay.Checked;
            UpdateWaitEnabled();
            UpdateShapeLabel();
            UpdateWatchLabel();
            UpdateSnapshotView();

            ResumeLayout(false);
            PerformLayout();
        }

        Rectangle ClickArea()
        {
            int x = (int)numX.Value, y = (int)numY.Value, w = (int)numW.Value, h = (int)numH.Value;
            if (w > 0 && h > 0) return new Rectangle(x, y, w, h);
            return new Rectangle(x - 18, y - 18, 36, 36); // an exact spot: watch a small square around it
        }

        /// <summary>The click area's lasso, fitted to the box the numbers give now; null for a box or an exact spot.</summary>
        Point[] CurrentShape()
        {
            if (shape == null || numW.Value <= 0 || numH.Value <= 0) return null;
            return Lasso.Fit(shape, shapeBox, ClickArea());
        }

        bool WatchesClickArea()
        {
            return watchArea == ClickArea() && Lasso.SameShape(watchShape, CurrentShape());
        }

        /// <summary>Which cells of the snapshot are compared: those inside the watch lasso, or all of them.</summary>
        bool[] Mask()
        {
            return snapshot == null ? null : Lasso.GridMask(watchShape, watchArea, snapshot.Width, snapshot.Height);
        }

        void UpdateShapeLabel()
        {
            Point[] now = CurrentShape();
            lblShape.Text = now != null ? "A lasso (" + now.Length + " points)"
                          : numW.Value > 0 && numH.Value > 0 ? "A box"
                          : "An exact spot";
            btnBox.Enabled = now != null;
        }

        void UpdateWaitEnabled()
        {
            foreach (Control c in grpWait.Controls)
                if (c != chkWait) c.Enabled = chkWait.Checked;
        }

        void UpdateWatchLabel()
        {
            if (watchArea.Width <= 0 || watchArea.Height <= 0)
            {
                lblWatch.Text = "nothing yet — use the buttons below";
                return;
            }
            lblWatch.Text = watchArea.X + ", " + watchArea.Y + "   " + watchArea.Width + " × " + watchArea.Height +
                            (watchShape != null ? " lasso" : "") +
                            (WatchesClickArea() ? "   (the click area)" : "   (a separate area)");
        }

        void UpdateSnapshotView()
        {
            Image old = picSnapshot.Image;
            bool[] mask = Mask();
            picSnapshot.Image = snapshot == null ? null : snapshot.ToPreview(mask);
            if (old != null) old.Dispose();
            if (snapshot != null && snapshot.IsPlainWithin(mask))
                ShowTest("Plain area: only its colour is compared", BadColor);
        }

        void ShowTest(string text, Color color)
        {
            lblTest.Text = text;
            lblTest.ForeColor = color;
        }

        IEnumerable<Form> WindowsToHide()
        {
            return new Form[] { Owner, this };
        }

        void UseClickArea()
        {
            watchArea = ClickArea();
            watchShape = CurrentShape();
            RetakeSnapshot();
        }

        /// <summary>
        /// Picks the click area again: a box, a lasso or an exact spot. A step that watched its click area watches the
        /// new one, with its snapshot taken from the same picture of the screen.
        /// </summary>
        void PickClickArea()
        {
            bool watchedClickArea = watchArea.Width > 0 && watchArea.Height > 0 && WatchesClickArea();
            Rectangle vs = SystemInformation.VirtualScreen;
            Rectangle picked = Rectangle.Empty;
            Point[] pickedShape = null;
            Snapshot shot = null;
            bool got = false;
            try
            {
                ScreenGrab.WhileHidden(WindowsToHide(), delegate
                {
                    using (Bitmap frame = ScreenGrab.CaptureVirtualScreen())
                    using (var overlay = new PickOverlay(frame, vs, screenSteps, true, true, true))
                    {
                        overlay.ShowDialog();
                        if (overlay.Picks.Count == 0) return;
                        picked = overlay.Picks[0];
                        pickedShape = overlay.Shapes[0];
                        got = true;
                        // An exact spot watches a small square around it, as ClickArea gives.
                        Rectangle watched = picked.Width > 0 ? picked : new Rectangle(picked.X - 18, picked.Y - 18, 36, 36);
                        shot = Snapshot.FromBitmap(frame, new Rectangle(watched.X - vs.X, watched.Y - vs.Y, watched.Width, watched.Height));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't capture the screen:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (!got) return;

            Rectangle s = space.FromScreen(picked);
            Point o = space.Origin;
            shape = Lasso.Offset(pickedShape, -o.X, -o.Y);
            shapeBox = s;
            SetNumber(numX, s.X);
            SetNumber(numY, s.Y);
            SetNumber(numW, s.Width);
            SetNumber(numH, s.Height);
            UpdateShapeLabel();

            if (watchedClickArea)
            {
                if (shot != null)
                {
                    watchArea = ClickArea();
                    watchShape = CurrentShape();
                    snapshot = shot;
                    ShowTest("Snapshot taken from the screen you picked on", GoodColor);
                    UpdateSnapshotView();
                }
                else
                {
                    ShowTest("Couldn't take a snapshot there: use Retake", BadColor);
                }
            }
            UpdateWatchLabel();
        }

        /// <summary>Back to clicking anywhere in the lasso's box; a watch lasso that was the click area goes too.</summary>
        void MakeBox()
        {
            bool watchedClickArea = watchArea.Width > 0 && watchArea.Height > 0 && WatchesClickArea();
            shape = null;
            if (watchedClickArea)
            {
                watchShape = null;
                UpdateSnapshotView();
            }
            UpdateShapeLabel();
            UpdateWatchLabel();
        }

        static void SetNumber(NumericUpDown n, int value)
        {
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, value));
        }

        /// <summary>Reads an area as it is now, hiding these windows first unless it comes straight from the game window.</summary>
        Snapshot ReadNow(Rectangle area)
        {
            if (space.Window != null) return space.Read(area);
            Snapshot shot = null;
            ScreenGrab.WhileHidden(WindowsToHide(), delegate { shot = space.Read(area); });
            return shot;
        }

        void RetakeSnapshot()
        {
            if (watchArea.Width <= 0 || watchArea.Height <= 0) return;
            Snapshot shot = ReadNow(watchArea);
            if (shot == null)
            {
                ShowTest("Couldn't read the screen", BadColor);
                return;
            }
            snapshot = shot;
            ShowTest("", GoodColor);
            UpdateWatchLabel();
            UpdateSnapshotView();
        }

        void PickWatchArea()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            Rectangle picked = Rectangle.Empty;
            Point[] pickedShape = null;
            Snapshot shot = null;
            try
            {
                ScreenGrab.WhileHidden(WindowsToHide(), delegate
                {
                    using (Bitmap frame = ScreenGrab.CaptureVirtualScreen())
                    using (var overlay = new PickOverlay(frame, vs, screenSteps, true, true, false))
                    {
                        overlay.ShowDialog();
                        if (overlay.Picks.Count == 0) return;
                        picked = overlay.Picks[0];
                        pickedShape = overlay.Shapes[0];
                        shot = Snapshot.FromBitmap(frame, new Rectangle(picked.X - vs.X, picked.Y - vs.Y, picked.Width, picked.Height));
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't capture the screen:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (shot == null) return;

            watchArea = space.FromScreen(picked);
            Point o = space.Origin;
            watchShape = Lasso.Offset(pickedShape, -o.X, -o.Y);
            snapshot = shot;
            chkWait.Checked = true;
            ShowTest("", GoodColor);
            UpdateWatchLabel();
            UpdateSnapshotView();
        }

        void TestNow()
        {
            if (snapshot == null)
            {
                ShowTest("Take a snapshot first", BadColor);
                return;
            }
            Snapshot live = ReadNow(watchArea);
            double score = snapshot.Similarity(live, Mask()) * 100;
            bool ok = score >= (double)numMatch.Value;
            ShowTest("Now " + Math.Round(score) + "% — " + (ok ? "matches" : "doesn't match"), ok ? GoodColor : BadColor);
        }

        void Commit()
        {
            if (chkWait.Checked && (snapshot == null || watchArea.Width <= 0 || watchArea.Height <= 0))
            {
                MessageBox.Show(this, "Choose what to watch first (Use click area or Pick area…), or untick waiting.",
                    "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int reactMin = (int)Math.Min(numReactMin.Value, numReactMax.Value);
            int reactMax = (int)Math.Max(numReactMin.Value, numReactMax.Value);
            result = new ClickPoint
            {
                X = (int)numX.Value,
                Y = (int)numY.Value,
                Width = (int)numW.Value,
                Height = (int)numH.Value,
                Button = (MouseButton)cbButton.SelectedIndex,
                DoubleClick = cbClick.SelectedIndex == 1,
                DelayMs = chkDelay.Checked ? (int)numDelay.Value : -1,
                WaitForScreen = chkWait.Checked,
                WatchArea = watchArea,
                Snapshot = snapshot,
                MatchPercent = (int)numMatch.Value,
                ReactMinMs = reactMin,
                ReactMaxMs = reactMax,
                TimeoutSec = (int)numTimeout.Value,
                OnTimeout = (TimeoutAction)cbTimeout.SelectedIndex,
                BorrowPointer = chkBorrow.Checked,
                Shape = CurrentShape(),
                WatchShape = watchArea.Width > 0 && watchArea.Height > 0 ? watchShape : null
            };
            if (result.Width == 0 || result.Height == 0) result.Width = result.Height = 0;
            DialogResult = DialogResult.OK;
        }

        static Label AddLabel(Control parent, string text, int x, int y)
        {
            var l = new Label { Text = text, AutoSize = true, Location = new Point(x, y) };
            parent.Controls.Add(l);
            return l;
        }

        static Button AddButton(Control parent, string text, int x, int y, int w, int h, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        static NumericUpDown AddNumber(Control parent, int x, int y, int width, int min, int max, int value)
        {
            var n = new NumericUpDown
            {
                Location = new Point(x, y),
                Width = width,
                Minimum = min,
                Maximum = max,
                Value = Math.Max(min, Math.Min(max, value)),
                TextAlign = HorizontalAlignment.Right
            };
            parent.Controls.Add(n);
            return n;
        }

        static ComboBox AddCombo(Control parent, int x, int y, int width, object[] items, int selected)
        {
            var c = new ComboBox { Location = new Point(x, y), Width = width, DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            c.SelectedIndex = selected >= 0 && selected < items.Length ? selected : 0;
            parent.Controls.Add(c);
            return c;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && picSnapshot.Image != null) picSnapshot.Image.Dispose();
            base.Dispose(disposing);
        }
    }
}
