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
                Console.WriteLine("size " + shot.Width + "x" + shot.Height);
                Console.WriteLine("Showing: " + AutoClicker.Captcha.Showing(shot));
                Point? at = AutoClicker.Captcha.Solve(shot);
                Console.WriteLine(at.HasValue ? "CHOSEN " + at.Value.X + "," + at.Value.Y : "NO MATCH");
                using (Graphics g = Graphics.FromImage(shot))
                {
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