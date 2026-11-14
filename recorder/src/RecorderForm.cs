using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ClickRecorder
{
    /// <summary>
    /// Records mouse presses between Start and Stop (F9 from anywhere): when, where and which button, from you or from
    /// a program such as Auto Clicker. Recordings stay in memory until the app closes; the graphs show them.
    /// </summary>
    sealed class RecorderForm : Form
    {
        const int HotkeyId = 1;
        const int WM_HOTKEY = 0x0312;
        const uint MOD_NOREPEAT = 0x4000;

        static readonly Color ReadyColor = Color.FromArgb(70, 70, 70);
        static readonly Color RecordingColor = Color.FromArgb(190, 30, 30);

        readonly List<Recording> recordings = new List<Recording>();
        readonly MouseHook hook;
        readonly Button btnRecord;
        readonly Label lblStatus;
        readonly TextBox txtName;
        readonly ListView lvRecordings;
        readonly Button btnGraphs, btnSave, btnRemove, btnClear;
        readonly CheckBox chkTop;
        readonly System.Windows.Forms.Timer tick;
        GraphsForm graphs;
        bool hotkeyOn;
        int made;
        int liveRefresh;
        bool filling;

        /// <summary>The recording presses are going into, or null between recordings. Set last, after its start time.</summary>
        volatile Recording active;
        long activeStart;

        public RecorderForm()
        {
            hook = new MouseHook(OnPress);

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Click Recorder";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 430);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            btnRecord = new Button { Location = new Point(12, 12), Size = new Size(190, 40), Font = new Font("Segoe UI", 10F, FontStyle.Bold) };
            btnRecord.Click += delegate { Toggle(); };
            Controls.Add(btnRecord);
            lblStatus = new Label { Location = new Point(214, 12), Size = new Size(334, 40), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9F, FontStyle.Bold) };
            Controls.Add(lblStatus);

            Controls.Add(new Label { Text = "Name of the next recording:", AutoSize = true, Location = new Point(12, 67) });
            txtName = new TextBox { Location = new Point(184, 64), Width = 220 };
            Controls.Add(txtName);

            Controls.Add(new Label { Text = "Recordings, kept until you close the app (tick to show in the graphs, F2 to rename):", AutoSize = true, Location = new Point(12, 98) });
            lvRecordings = new ListView
            {
                Location = new Point(12, 118),
                Size = new Size(536, 170),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                HideSelection = false,
                LabelEdit = true,
                MultiSelect = false
            };
            lvRecordings.Columns.Add("Name", 170);
            lvRecordings.Columns.Add("Clicks", 60, HorizontalAlignment.Right);
            lvRecordings.Columns.Add("Mean gap", 80, HorizontalAlignment.Right);
            lvRecordings.Columns.Add("Std dev", 70, HorizontalAlignment.Right);
            lvRecordings.Columns.Add("Length", 70, HorizontalAlignment.Right);
            lvRecordings.ItemChecked += OnItemChecked;
            lvRecordings.AfterLabelEdit += OnRenamed;
            lvRecordings.SelectedIndexChanged += delegate { UpdateButtons(); };
            lvRecordings.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F2 && lvRecordings.SelectedItems.Count > 0) lvRecordings.SelectedItems[0].BeginEdit();
                else if (e.KeyCode == Keys.Delete) RemoveSelected();
            };
            lvRecordings.DoubleClick += delegate { ShowGraphs(); };
            Controls.Add(lvRecordings);

            btnGraphs = AddButton("Show graphs", 12, 296, 110, delegate { ShowGraphs(); });
            btnSave = AddButton("Save CSV…", 128, 296, 100, delegate { SaveCsv(); });
            btnRemove = AddButton("Remove", 234, 296, 80, delegate { RemoveSelected(); });
            btnClear = AddButton("Clear all", 320, 296, 80, delegate { ClearAll(); });
            chkTop = new CheckBox { Text = "Keep on top", AutoSize = true, Location = new Point(456, 300) };
            chkTop.CheckedChanged += delegate { TopMost = chkTop.Checked; };
            Controls.Add(chkTop);

            Controls.Add(new Label
            {
                Text = "Records every mouse button press on the PC, yours and other programs' alike, with where it landed and " +
                       "when. Clicks on this window are left out. Auto Clicker's background mode sends its clicks straight to " +
                       "the game window as messages, so those don't pass through here and aren't recorded.\n" +
                       "Nothing is written to disk unless you save it.",
                Location = new Point(12, 334),
                Size = new Size(536, 86),
                ForeColor = SystemColors.GrayText
            });

            tick = new System.Windows.Forms.Timer { Interval = 250 };
            tick.Tick += delegate { OnTick(); };

            ResumeLayout(false);
            PerformLayout();
            txtName.Text = NextName();
            ShowState();
            UpdateButtons();
        }

        Button AddButton(string text, int x, int y, int width, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 28) };
            b.Click += onClick;
            Controls.Add(b);
            return b;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hotkeyOn = RegisterHotKey(Handle, HotkeyId, MOD_NOREPEAT, (uint)Keys.F9);
            ShowState();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId) Toggle();
            base.WndProc(ref m);
        }

        // ---------------------------------------------------------------- recording

        void Toggle()
        {
            if (active == null) StartRecording();
            else StopRecording();
        }

        void StartRecording()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            var r = new Recording
            {
                Name = txtName.Text.Trim().Length > 0 ? txtName.Text.Trim() : NextName(),
                Slot = made < 8 ? made : -1,
                Started = DateTime.Now,
                ScreenBounds = bounds,
                Screen = TakeScreen(bounds)
            };

            activeStart = Stopwatch.GetTimestamp();
            active = r;
            string problem;
            if (!hook.Start(out problem))
            {
                active = null;
                if (r.Screen != null) r.Screen.Dispose();
                MessageBox.Show(this, "Windows didn't let Click Recorder watch the mouse:\n" + problem, "Click Recorder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            made++;
            recordings.Add(r);
            AddRow(r);
            tick.Start();
            ShowState();
            UpdateButtons();
        }

        void StopRecording()
        {
            Recording r = active;
            if (r == null) return;
            hook.Stop();
            r.LengthMs = (Stopwatch.GetTimestamp() - activeStart) * 1000.0 / Stopwatch.Frequency;
            active = null;
            tick.Stop();
            UpdateRow(r);
            txtName.Text = NextName();
            ShowState();
            UpdateButtons();
            ShowGraphs();
        }

        /// <summary>Called on the mouse hook's thread for every press.</summary>
        void OnPress(MouseHook.Press p)
        {
            Recording r = active;
            if (r == null) return;
            r.Add(new Click
            {
                Ms = (p.Ticks - activeStart) * 1000.0 / Stopwatch.Frequency,
                X = p.X,
                Y = p.Y,
                Button = p.Button
            });
        }

        void OnTick()
        {
            Recording r = active;
            if (r == null) return;
            ShowState();
            UpdateRow(r);
            // The graphs follow along, though less often, since they redraw everything.
            if (graphs != null && !graphs.IsDisposed && ++liveRefresh % 4 == 0) graphs.RefreshAll();
        }

        string NextName()
        {
            int n = made + 1;
            while (recordings.Any(r => r.Name == "Recording " + n)) n++;
            return "Recording " + n;
        }

        /// <summary>
        /// A picture of every monitor, for the click map, shrunk to at most 1920 wide to save memory. This window is
        /// hidden for it. Null if the screen can't be read (a locked PC, say).
        /// </summary>
        Bitmap TakeScreen(Rectangle bounds)
        {
            bool hide = Visible && WindowState != FormWindowState.Minimized;
            var owned = OwnedForms.Where(f => f.Visible).ToList();
            try
            {
                if (hide)
                {
                    Opacity = 0;
                    foreach (Form f in owned) f.Opacity = 0;
                    Application.DoEvents();
                    Thread.Sleep(150); // time for the desktop to redraw without them
                }
                using (var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(full))
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
                    double scale = Math.Min(1.0, 1920.0 / bounds.Width);
                    var small = new Bitmap(Math.Max(1, (int)(bounds.Width * scale)), Math.Max(1, (int)(bounds.Height * scale)), PixelFormat.Format32bppRgb);
                    using (Graphics g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(full, new Rectangle(Point.Empty, small.Size));
                    }
                    return small;
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (hide)
                {
                    Opacity = 1;
                    foreach (Form f in owned) f.Opacity = 1;
                }
            }
        }

        // ---------------------------------------------------------------- the list

        void AddRow(Recording r)
        {
            filling = true;
            var item = new ListViewItem(r.Name) { Tag = r, Checked = r.Shown };
            for (int i = 0; i < 4; i++) item.SubItems.Add("");
            lvRecordings.Items.Add(item);
            filling = false;
            UpdateRow(r);
        }

        void UpdateRow(Recording r)
        {
            ListViewItem item = lvRecordings.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag == r);
            if (item == null) return;
            Summary sum = Summary.Of(r.Gaps(true));
            double length = r == active ? (Stopwatch.GetTimestamp() - activeStart) * 1000.0 / Stopwatch.Frequency : r.LengthMs;
            item.SubItems[1].Text = r.Count.ToString("N0");
            item.SubItems[2].Text = sum.Count > 0 ? Draw.Ms(sum.Mean) + " ms" : "–";
            item.SubItems[3].Text = sum.Count > 0 ? Draw.Ms(sum.StdDev) + " ms" : "–";
            item.SubItems[4].Text = Duration(length);
        }

        static string Duration(double ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalHours >= 1 ? ((int)t.TotalHours) + ":" + t.ToString("mm\\:ss") : t.ToString("m\\:ss");
        }

        void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (filling) return;
            var r = e.Item.Tag as Recording;
            if (r == null) return;
            r.Shown = e.Item.Checked;
            RefreshGraphs();
        }

        void OnRenamed(object sender, LabelEditEventArgs e)
        {
            if (e.Label == null) return;
            string name = e.Label.Trim();
            if (name.Length == 0)
            {
                e.CancelEdit = true;
                return;
            }
            ((Recording)lvRecordings.Items[e.Item].Tag).Name = name;
            BeginInvoke(new Action(RefreshGraphs));
        }

        Recording Selected
        {
            get { return lvRecordings.SelectedItems.Count > 0 ? lvRecordings.SelectedItems[0].Tag as Recording : null; }
        }

        void RemoveSelected()
        {
            Recording r = Selected;
            if (r == null || r == active) return;
            recordings.Remove(r);
            lvRecordings.Items.Remove(lvRecordings.SelectedItems[0]);
            if (r.Screen != null) r.Screen.Dispose();
            RefreshGraphs();
            UpdateButtons();
        }

        void ClearAll()
        {
            if (recordings.Count == 0) return;
            if (MessageBox.Show(this, "Forget every recording? Ones you haven't saved are gone for good.", "Click Recorder",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            if (active != null) StopRecording();
            foreach (Recording r in recordings)
                if (r.Screen != null) r.Screen.Dispose();
            recordings.Clear();
            lvRecordings.Items.Clear();
            made = 0;
            txtName.Text = NextName();
            RefreshGraphs();
            UpdateButtons();
        }

        void UpdateButtons()
        {
            btnGraphs.Enabled = btnSave.Enabled = btnClear.Enabled = recordings.Count > 0;
            btnRemove.Enabled = Selected != null && Selected != active;
        }

        void ShowState()
        {
            Recording r = active;
            btnRecord.Text = r == null ? "Start recording" + (hotkeyOn ? " (F9)" : "") : "Stop" + (hotkeyOn ? " (F9)" : "");
            if (r != null)
            {
                double ms = (Stopwatch.GetTimestamp() - activeStart) * 1000.0 / Stopwatch.Frequency;
                lblStatus.Text = "Recording “" + r.Name + "”: " + r.Count.ToString("N0") + " clicks, " + Duration(ms);
                lblStatus.ForeColor = RecordingColor;
            }
            else
            {
                lblStatus.Text = hotkeyOn ? "Not recording. F9 starts and stops from any window."
                                          : "Not recording. F9 is taken by another program, so use the button.";
                lblStatus.ForeColor = ReadyColor;
            }
        }

        // ---------------------------------------------------------------- graphs and saving

        void ShowGraphs()
        {
            if (recordings.Count == 0) return;
            if (graphs == null || graphs.IsDisposed)
            {
                graphs = new GraphsForm(recordings);
                graphs.Show(this); // owned, so it's hidden with this window when a recording takes its screen picture
            }
            else
            {
                graphs.RefreshAll();
                if (graphs.WindowState == FormWindowState.Minimized) graphs.WindowState = FormWindowState.Normal;
                graphs.Activate();
            }
        }

        void RefreshGraphs()
        {
            if (graphs != null && !graphs.IsDisposed) graphs.RefreshAll();
        }

        void SaveCsv()
        {
            List<Recording> chosen = recordings.Where(r => r.Shown).ToList();
            if (chosen.Count == 0) chosen = recordings.ToList();
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                DefaultExt = "csv",
                FileName = "clicks " + DateTime.Now.ToString("yyyy-MM-dd HH.mm", CultureInfo.InvariantCulture) + ".csv"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, Csv.Of(chosen));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (recordings.Count > 0 &&
                MessageBox.Show(this, "Close Click Recorder? The recordings are only in memory, so ones you haven't saved are lost.",
                    "Click Recorder", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            tick.Stop();
            hook.Dispose();
            if (hotkeyOn) UnregisterHotKey(Handle, HotkeyId);
            base.OnFormClosed(e);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
