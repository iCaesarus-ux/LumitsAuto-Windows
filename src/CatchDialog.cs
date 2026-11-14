using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace AutoClicker
{
    /// <summary>Catching on or off, practice mode, healing with Bark Tea, and where the game's buttons and numbers are.</summary>
    public class CatchDialog : Form
    {
        static readonly Color GoodColor = Color.FromArgb(0, 128, 0);
        static readonly Color BadColor = Color.FromArgb(190, 30, 30);

        readonly AppSettings settings;
        readonly StepSpace space;
        readonly IList<ClickPoint> screenSteps;
        readonly Dictionary<string, Rectangle> picked;
        readonly CheckBox chkCatch, chkPractice, chkRadiants, chkRadiantsOnly, chkDragReal;
        readonly ComboBox cbPartnerWeaken, cbPartnerKill;
        readonly TrackBar trkHeal;
        readonly NumericUpDown numMaxHp;
        readonly Label lblHeal, lblTest;
        readonly ListView lvSpots;
        readonly PictureBox picTest;

        public CatchDialog(AppSettings settings, LumitCounter counter, StepSpace space, IList<ClickPoint> screenSteps)
        {
            this.settings = settings;
            this.space = space;
            this.screenSteps = screenSteps;
            picked = new Dictionary<string, Rectangle>(settings.CatchSpots);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Catching";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(580, 722);

            int ticked = counter.Entries().Count(e => e.Catch);
            chkCatch = new CheckBox
            {
                Text = "Catch the Lumits ticked in the Lumits list (" + ticked + " ticked)",
                AutoSize = true,
                Location = new Point(14, 12),
                Checked = settings.CatchOn
            };
            chkPractice = new CheckBox
            {
                Text = "Practice mode: always press Keep, and only note what it would have released",
                AutoSize = true,
                Location = new Point(14, 38),
                Checked = settings.CatchPractice
            };
            chkRadiants = new CheckBox
            {
                Text = "Catch radiants by itself: only Strike, down to a safe HP, then Capture",
                AutoSize = true,
                Location = new Point(14, 64),
                Checked = settings.CatchRadiants
            };
            chkRadiantsOnly = new CheckBox
            {
                Text = "Radiants only: no fighting, it runs from every Lumit that isn't a radiant",
                AutoSize = true,
                Location = new Point(14, 90),
                Checked = settings.RadiantsOnly
            };
            Controls.Add(chkCatch);
            Controls.Add(chkPractice);
            Controls.Add(chkRadiants);
            Controls.Add(chkRadiantsOnly);
            // Ticked Lumits are run from too, so their catching has nothing to do meanwhile.
            chkCatch.Enabled = !chkRadiantsOnly.Checked;
            chkRadiantsOnly.CheckedChanged += delegate { chkCatch.Enabled = !chkRadiantsOnly.Checked; };
            Controls.Add(new Label { Text = "In a 2v2, your second Lumit weakens with its", AutoSize = true, Location = new Point(16, 121) });
            cbPartnerWeaken = SlotBox(268, 117, settings.PartnerWeaken);
            Controls.Add(new Label { Text = "move, finishes with its", AutoSize = true, Location = new Point(378, 121) });
            cbPartnerKill = SlotBox(500, 117, settings.PartnerKill);

            var grpHeal = new GroupBox { Text = "Healing with Bark Tea", Location = new Point(12, 148), Size = new Size(556, 112) };
            Controls.Add(grpHeal);
            grpHeal.Controls.Add(new Label { Text = "After a fight, feed 3 Bark Tea if your Lumit has fewer than:", AutoSize = true, Location = new Point(14, 22) });
            numMaxHp = new NumericUpDown { Location = new Point(490, 44), Width = 54, Minimum = 1, Maximum = 99999, TextAlign = HorizontalAlignment.Right };
            numMaxHp.Value = Math.Max(1, Math.Min(99999, settings.LeadMaxHp));
            trkHeal = new TrackBar { Location = new Point(10, 42), Size = new Size(330, 40), Minimum = 0, Maximum = (int)numMaxHp.Value, TickFrequency = 10, SmallChange = 1, LargeChange = 10 };
            trkHeal.Value = Math.Max(0, Math.Min(trkHeal.Maximum, settings.HealBelowHp));
            lblHeal = new Label { AutoSize = true, Location = new Point(344, 47), Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            grpHeal.Controls.Add(trkHeal);
            grpHeal.Controls.Add(lblHeal);
            grpHeal.Controls.Add(new Label { Text = "Max HP:", AutoSize = true, Location = new Point(436, 47) });
            grpHeal.Controls.Add(numMaxHp);
            chkDragReal = new CheckBox
            {
                Text = "Background mode: drag with the real pointer (only if feeding doesn't work without it)",
                AutoSize = true,
                Location = new Point(14, 84),
                Checked = settings.DragRealPointer
            };
            grpHeal.Controls.Add(chkDragReal);
            trkHeal.ValueChanged += delegate { UpdateHealLabel(); };
            numMaxHp.ValueChanged += delegate
            {
                trkHeal.Maximum = (int)numMaxHp.Value;
                UpdateHealLabel();
            };

            var grpSpots = new GroupBox { Text = "Where things are in the game window", Location = new Point(12, 268), Size = new Size(556, 404) };
            Controls.Add(grpSpots);
            lvSpots = new ListView
            {
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HideSelection = false,
                MultiSelect = false,
                Location = new Point(12, 22),
                Size = new Size(532, 250)
            };
            lvSpots.Columns.Add("Spot", 210);
            lvSpots.Columns.Add("On screen", 150);
            lvSpots.Columns.Add("Where", 150);
            lvSpots.DoubleClick += delegate { TestSpot(); };
            grpSpots.Controls.Add(lvSpots);

            AddButton(grpSpots, "Pick…", 12, 280, 80, 26, delegate { PickSpot(); });
            AddButton(grpSpots, "Use default", 98, 280, 90, 26, delegate { ResetSpot(); });
            AddButton(grpSpots, "Test", 194, 280, 80, 26, delegate { TestSpot(); });
            grpSpots.Controls.Add(new Label
            {
                Text = "Test a spot while its screen is showing. If the green box is off, Pick it again.",
                Location = new Point(282, 278),
                Size = new Size(262, 34),
                ForeColor = SystemColors.GrayText
            });
            picTest = new PictureBox
            {
                Location = new Point(12, 316),
                Size = new Size(240, 76),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            grpSpots.Controls.Add(picTest);
            lblTest = new Label { Location = new Point(262, 316), Size = new Size(282, 76) };
            grpSpots.Controls.Add(lblTest);

            AddButton(this, "Catch log", 12, 684, 90, 28, delegate { OpenLog(); });
            var ok = new Button { Text = "OK", Location = new Point(402, 684), Size = new Size(80, 28) };
            ok.Click += delegate { Commit(); };
            var cancel = new Button { Text = "Cancel", Location = new Point(488, 684), Size = new Size(80, 28), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            var tips = new ToolTip { AutoPopDelay = 20000 };
            tips.SetToolTip(chkCatch, "Ticked Lumits get Heat Siphon until another one would usually kill them, then Capture.\n" +
                                      "The rest are attacked with your attack step as before.");
            tips.SetToolTip(chkPractice, "Nothing is released while this is on. Check the catch log against what you see in the game,\nthen turn it off.");
            tips.SetToolTip(chkRadiants, "Strike's damage is measured in each fight. It attacks only while the radiant's HP is above 1.8 times that:\n" +
                                         "1.5 for a crit, plus 20%. Then it uses Capture, and always keeps the catch.\n" +
                                         "Needs the radiant check in Alerts… and a chosen game window. Off: a radiant stops the run before it attacks.");
            tips.SetToolTip(chkRadiantsOnly, "For hunting radiants. Make the step checked for radiants (in Alerts…) the Run button, bottom left in a fight.\n" +
                                             "Every Lumit that isn't a radiant gets that step: ticked ones, and 2v2s too. A radiant gets Strike and\n" +
                                             "Capture as above, or stops the run with that off. Keep a Continue step for after a radiant is caught.\n" +
                                             "Needs the radiant check in Alerts… and a chosen game window.");
            tips.SetToolTip(trkHeal, "Your Lumit's HP is measured on your last turn of each fight. 0 = never heal.");
            tips.SetToolTip(numMaxHp, "Your fighting Lumit's full HP (Infibex: 182), used to turn its HP bar into a number.");

            UpdateHealLabel();
            FillSpots();
            ResumeLayout(false);
            PerformLayout();
        }

        static Button AddButton(Control parent, string text, int x, int y, int w, int h, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            b.Click += onClick;
            parent.Controls.Add(b);
            return b;
        }

        void UpdateHealLabel()
        {
            lblHeal.Text = trkHeal.Value == 0 ? "never" : trkHeal.Value + " HP";
        }

        Size ClientSizeNow
        {
            get { return space.Window != null ? space.Window.ClientSize : new Size(CatchSpots.LayoutWidth, CatchSpots.LayoutHeight); }
        }

        void FillSpots()
        {
            string selected = SelectedKey;
            Dictionary<string, Rectangle> where = CatchSpots.Resolve(ClientSizeNow, picked);
            lvSpots.BeginUpdate();
            lvSpots.Items.Clear();
            foreach (Spot s in CatchSpots.All)
            {
                Rectangle r = where[s.Key];
                var item = new ListViewItem(s.Label) { Tag = s.Key };
                item.SubItems.Add(s.Screen);
                item.SubItems.Add(r.X + ", " + r.Y + "  " + r.Width + "×" + r.Height + (picked.ContainsKey(s.Key) ? "  (yours)" : ""));
                if (picked.ContainsKey(s.Key)) item.Font = new Font(lvSpots.Font, FontStyle.Bold);
                lvSpots.Items.Add(item);
                if (s.Key == selected) item.Selected = true;
            }
            lvSpots.EndUpdate();
        }

        string SelectedKey
        {
            get { return lvSpots.SelectedItems.Count > 0 ? lvSpots.SelectedItems[0].Tag as string : null; }
        }

        void PickSpot()
        {
            string key = SelectedKey;
            if (key == null) return;
            Rectangle vs = SystemInformation.VirtualScreen;
            Rectangle chosen = Rectangle.Empty;
            try
            {
                ScreenGrab.WhileHidden(new Form[] { Owner, this }, delegate
                {
                    using (Bitmap frame = ScreenGrab.CaptureVirtualScreen())
                    using (var overlay = new PickOverlay(frame, vs, screenSteps, true))
                    {
                        overlay.ShowDialog();
                        if (overlay.Picks.Count > 0) chosen = overlay.Picks[0];
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't capture the screen:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (chosen.Width <= 0) return;
            picked[key] = space.FromScreen(chosen);
            FillSpots();
            TestSpot();
        }

        void ResetSpot()
        {
            string key = SelectedKey;
            if (key == null || !picked.Remove(key)) return;
            FillSpots();
        }

        /// <summary>Shows the spot as the game window looks now, with what's read there.</summary>
        void TestSpot()
        {
            string key = SelectedKey;
            if (key == null) return;
            if (space.Window == null)
            {
                ShowTest(null, "Choose the game window first.", BadColor);
                return;
            }
            Spot spot = CatchSpots.Get(key);
            Rectangle r = CatchSpots.Resolve(ClientSizeNow, picked)[key];
            using (Bitmap shot = space.Window.CaptureClient())
            {
                if (shot == null || !new Rectangle(Point.Empty, shot.Size).Contains(r))
                {
                    ShowTest(null, "Couldn't read that part of the game window.", BadColor);
                    return;
                }
                Rectangle around = Rectangle.Intersect(Rectangle.Inflate(r, Math.Max(8, r.Width / 4), Math.Max(8, r.Height)), new Rectangle(Point.Empty, shot.Size));
                var picture = shot.Clone(around, shot.PixelFormat);
                using (Graphics g = Graphics.FromImage(picture))
                using (var pen = new Pen(Color.Lime, 1))
                    g.DrawRectangle(pen, r.X - around.X, r.Y - around.Y, r.Width - 1, r.Height - 1);

                string text;
                switch (spot.Kind)
                {
                    case SpotKind.Bar:
                        {
                            double fill = ScreenText.BarFill(shot, r, key == CatchSpots.PanelBar ? (Func<int, int, int, bool>)ScreenText.IsGreen : ScreenText.IsRed);
                            text = fill < 0 ? "Couldn't measure the bar." : "Bar is " + Math.Round(fill * 100) + "% full" +
                                   (key == CatchSpots.LeadBar || key == CatchSpots.PanelBar ? " (≈ " + Math.Round(fill * (double)numMaxHp.Value) + " HP)" : "") + ".";
                            break;
                        }
                    case SpotKind.Text:
                        {
                            string read = key.StartsWith("iv")
                                ? ReadOnWorker(() =>
                                {
                                    Dictionary<int, int> votes = ScreenText.ReadVotes(shot, r, 1, 25);
                                    int n = ScreenText.Confident(votes);
                                    string tally = string.Join(", ", votes.OrderByDescending(v => v.Value).Select(v => v.Key + " ×" + v.Value));
                                    return votes.Count == 0 ? "" : (n < 0 ? "not sure" : "IV " + n) + "  (readings: " + tally + ")";
                                })
                                : ReadOnWorker(() => ScreenText.Read(shot, r, Ink.Light, 0, false));
                            text = read == null ? "Can't read text on this PC (no Windows text recognition)."
                                 : read.Trim().Length == 0 ? "Nothing read. Is the \"" + spot.Screen + "\" screen showing?"
                                 : "Reads: " + read.Trim();
                            break;
                        }
                    default:
                        text = "Clicks inside the green box. Check it sits on the button while the \"" + spot.Screen + "\" screen shows.";
                        break;
                }
                ShowTest(picture, text, spot.Kind == SpotKind.Click ? SystemColors.ControlText : GoodColor);
            }
        }

        /// <summary>Text recognition waits on Windows; doing it on a worker keeps it off this window's own thread.</summary>
        static string ReadOnWorker(Func<string> read)
        {
            string result = "";
            var t = new Thread(() => result = read()) { IsBackground = true };
            t.Start();
            return t.Join(8000) ? result : "";
        }

        void ShowTest(Bitmap picture, string text, Color color)
        {
            Image old = picTest.Image;
            picTest.Image = picture;
            if (old != null) old.Dispose();
            lblTest.Text = text;
            lblTest.ForeColor = color;
        }

        void OpenLog()
        {
            string file = CatchLog.Default.FilePath;
            if (!File.Exists(file))
            {
                MessageBox.Show(this, "Nothing has been caught yet.", "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Process.Start(file); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't open the log:\n" + ex.Message, "Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        /// <summary>A choice of the six move places; your second Lumit's moves have their own names, so only the place is shown.</summary>
        ComboBox SlotBox(int x, int y, int selected)
        {
            var box = new ComboBox { Location = new Point(x, y), Width = 104, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (string s in LumitCounter.Slots) box.Items.Add(s);
            box.SelectedIndex = Math.Max(0, Math.Min(5, selected));
            Controls.Add(box);
            return box;
        }

        void Commit()
        {
            settings.CatchOn = chkCatch.Checked;
            settings.CatchPractice = chkPractice.Checked;
            settings.CatchRadiants = chkRadiants.Checked;
            settings.RadiantsOnly = chkRadiantsOnly.Checked;
            settings.PartnerWeaken = cbPartnerWeaken.SelectedIndex;
            settings.PartnerKill = cbPartnerKill.SelectedIndex;
            settings.HealBelowHp = trkHeal.Value;
            settings.LeadMaxHp = (int)numMaxHp.Value;
            settings.DragRealPointer = chkDragReal.Checked;
            settings.CatchSpots.Clear();
            foreach (KeyValuePair<string, Rectangle> kv in picked) settings.CatchSpots[kv.Key] = kv.Value;
            DialogResult = DialogResult.OK;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && picTest.Image != null) picTest.Image.Dispose();
            base.Dispose(disposing);
        }
    }
}
