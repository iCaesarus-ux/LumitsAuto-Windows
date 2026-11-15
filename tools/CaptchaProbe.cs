using System;
using System.Drawing;

static class CaptchaProbe
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length < 1) { Console.WriteLine("usage: probe <png> [topOffsetPx]"); return; }
        using (var full = new Bitmap(args[0]))
        {
            int top = args.Length > 1 ? int.Parse(args[1]) : 0;
            var client = new Rectangle(0, top, full.Width, full.Height - top);
            using (var shot = full.Clone(client, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                double k = shot.Width / 1920.0;
                Console.WriteLine("size " + shot.Width + "x" + shot.Height);
                Console.WriteLine("Showing: " + AutoClicker.Captcha.Showing(shot));
                foreach (Point q in new[] { new Point(700, 231), new Point(960, 231), new Point(1220, 231) })
                {
                    int qx = (int)Math.Round(q.X * k), qy = (int)Math.Round(q.Y * k);
                    if (qx >= shot.Width || qy >= shot.Height) continue;
                    Color c = shot.GetPixel(qx, qy);
                    Console.WriteLine("trigger " + q.X + "," + q.Y + " = " + c.R + "," + c.G + "," + c.B);
                }
                string trace;
                Rectangle box;
                Point? at = AutoClicker.Captcha.Solve(shot, out trace, out box);
                Console.WriteLine("Trace: " + trace);
                Console.WriteLine(at.HasValue ? "CHOSEN " + at.Value.X + "," + at.Value.Y : "NO MATCH");
                using (Graphics g = Graphics.FromImage(shot))
                {
                    if (!box.IsEmpty)
                        g.DrawRectangle(Pens.Cyan, (int)Math.Round(602 * k) + box.X, (int)Math.Round(331 * k) + box.Y, box.Width, box.Height);
                    if (at.HasValue)
                    {
                        g.DrawEllipse(Pens.Lime, at.Value.X - 18, at.Value.Y - 18, 36, 36);
                        g.DrawLine(Pens.Lime, at.Value.X - 26, at.Value.Y, at.Value.X + 26, at.Value.Y);
                        g.DrawLine(Pens.Lime, at.Value.X, at.Value.Y - 26, at.Value.X, at.Value.Y + 26);
                    }
                }
                shot.Save(args[0].Replace(".png", "-marked.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}