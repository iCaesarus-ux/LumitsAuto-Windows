using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ClickRecorder
{
    /// <summary>The ticked recordings as graphs: the time between clicks, where they landed, and every click in a table.</summary>
    sealed class GraphsForm : Form
    {
        readonly IList<Recording> recordings;
        readonly CheckBox chkLeft;
        readonly NumericUpDown numBin, numUpTo;
        readonly TabControl tabs;
        readonly HistogramsView histograms;
        readonly MapView map;
        readonly ListView table;
        readonly List<string[]> rows = new List<string[]>();

        public GraphsForm(IList<Recording> recordings)
        {
            this.recordings = recordings;

            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9F);
            Text = "Click graphs";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 680);
            MinimumSize = new Size(560, 420);
            ShowIcon = false;

            var bar = new Panel { Dock = DockStyle.Top, Height = 40 };
            chkLeft = new CheckBox { Text = "Left button only", AutoSize = true, Location = new Point(12, 11), Checked = true };
            bar.Controls.Add(chkLeft);
            bar.Controls.Add(new Label { Text = "Column width:", AutoSize = true, Location = new Point(150, 12) });
            numBin = new NumericUpDown { Location = new Point(240, 9), Width = 64, Minimum = 0, Maximum = 10000, DecimalPlaces = 0, TextAlign = HorizontalAlignment.Right };
            bar.Controls.Add(numBin);
            bar.Controls.Add(new Label { Text = "ms", AutoSize = true, Location = new Point(308, 12) });
            bar.Controls.Add(new Label { Text = "Show up to:", AutoSize = true, Location = new Point(348, 12) });
            numUpTo = new NumericUpDown { Location = new Point(424, 9), Width = 80, Minimum = 0, Maximum = 3600000, ThousandsSeparator = true, TextAlign = HorizontalAlignment.Right };
            bar.Controls.Add(numUpTo);
            bar.Controls.Add(new Label { Text = "ms   (0 = picked for you)", AutoSize = true, Location = new Point(508, 12), ForeColor = SystemColors.GrayText });
            var btnSave = new Button { Text = "Save picture…", Location = new Point(700, 7), Size = new Size(110, 26), Anchor = AnchorStyles.Top | AnchorStyles.Right };
            btnSave.Left = ClientSize.Width - btnSave.Width - 12;
            btnSave.Click += delegate { SavePicture(); };
            bar.Controls.Add(btnSave);

            histograms = new HistogramsView { Dock = DockStyle.Fill };
            map = new MapView { Dock = DockStyle.Fill };
            table = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                VirtualMode = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            foreach (var col in new[] { Tuple.Create("Recording", 160), Tuple.Create("Click", 60), Tuple.Create("Time (s)", 80),
                                        Tuple.Create("Gap (ms)", 80), Tuple.Create("X", 60), Tuple.Create("Y", 60), Tuple.Create("Button", 70) })
                table.Columns.Add(col.Item1, col.Item2, col.Item1 == "Recording" || col.Item1 == "Button" ? HorizontalAlignment.Left : HorizontalAlignment.Right);
            table.RetrieveVirtualItem += (s, e) => e.Item = new ListViewItem(rows[e.ItemIndex]);

            tabs = new TabControl { Dock = DockStyle.Fill };
            tabs.TabPages.Add(Page("Time between clicks", histograms));
            tabs.TabPages.Add(Page("Where you clicked", map));
            tabs.TabPages.Add(Page("Table", table));

            Controls.Add(tabs);
            Controls.Add(bar);

            chkLeft.CheckedChanged += delegate { RefreshAll(); };
            numBin.ValueChanged += delegate { RefreshAll(); };
            numUpTo.ValueChanged += delegate { RefreshAll(); };
            ResumeLayout(false);
            PerformLayout();
            RefreshAll();
        }

        static TabPage Page(string title, Control content)
        {
            var page = new TabPage(title) { BackColor = Ink.Surface };
            page.Controls.Add(content);
            return page;
        }

        /// <summary>Draws everything again from the recordings as they are now (one may still be growing).</summary>
        public void RefreshAll()
        {
            List<Recording> shown = recordings.Where(r => r.Shown).ToList();
            bool leftOnly = chkLeft.Checked;
            histograms.ShowRecordings(shown, leftOnly, (double)numBin.Value, (double)numUpTo.Value);
            map.ShowRecordings(shown, leftOnly);

            rows.Clear();
            CultureInfo cc = CultureInfo.CurrentCulture;
            foreach (Recording r in shown)
            {
                Click[] clicks = r.Counted(leftOnly);
                for (int i = 0; i < clicks.Length; i++)
                {
                    Click c = clicks[i];
                    rows.Add(new[]
                    {
                        r.Name, (i + 1).ToString("N0", cc), (c.Ms / 1000).ToString("0.000", cc),
                        i == 0 ? "" : (c.Ms - clicks[i - 1].Ms).ToString("0.0", cc),
                        c.X.ToString(cc), c.Y.ToString(cc), c.Button.ToString()
                    });
                }
            }
            table.VirtualListSize = rows.Count;
            table.Invalidate();
        }

        void SavePicture()
        {
            Control view = tabs.SelectedTab.Controls[0];
            if (view == table)
            {
                MessageBox.Show(this, "The table is saved with Save CSV… in Click Recorder. Pick one of the graphs to save a picture.",
                    "Click Recorder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (var dlg = new SaveFileDialog { Filter = "PNG picture (*.png)|*.png", DefaultExt = "png", FileName = view == map ? "where I clicked.png" : "time between clicks.png" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Size size = view.ClientSize;
                int partHeight = 0;
                float dpi;
                using (Graphics g = CreateGraphics()) dpi = g.DpiX;
                if (view == histograms)
                {
                    int count = Math.Max(1, recordings.Count(r => r.Shown));
                    partHeight = Math.Max((int)(250 * dpi / 96f), size.Height / count);
                    size = new Size(size.Width, partHeight * count);
                }
                using (var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format24bppRgb))
                {
                    bmp.SetResolution(dpi, dpi); // the same text size as on screen
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        if (view == histograms) histograms.Render(g, size.Width, partHeight);
                        else map.Render(g, size);
                    }
                    bmp.Save(dlg.FileName, ImageFormat.Png);
                }
                view.Invalidate(); // lays the hover spots out for the screen again
            }
        }
    }
}
