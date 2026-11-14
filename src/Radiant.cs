using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace AutoClicker
{
    /// <summary>Spots gold lettering (a radiant's name) among the usual white text on dark cards.</summary>
    public static class GoldText
    {
        /// <summary>Where a 1920 × 1025 Lumits window shows the enemy's name, star included, without the portrait.</summary>
        public static readonly Rectangle LumitsNameArea = new Rectangle(1482, 119, 140, 44);
        const int LumitsWidth = 1920, LumitsHeight = 1025;

        /// <summary>The Lumits name area scaled to a window of the given content size.</summary>
        public static Rectangle LumitsNameAreaFor(Size client)
        {
            double sx = client.Width / (double)LumitsWidth, sy = client.Height / (double)LumitsHeight;
            Rectangle a = LumitsNameArea;
            return new Rectangle((int)Math.Round(a.X * sx), (int)Math.Round(a.Y * sy),
                                 (int)Math.Round(a.Width * sx), (int)Math.Round(a.Height * sy));
        }

        /// <summary>Warm yellow-orange: well above grey/white in red over blue, but not the red of the HP bar.</summary>
        public static bool IsGold(int r, int g, int b)
        {
            return r >= 150 && g >= 100 && b <= r * 0.7 && r - b >= 55 && g >= r * 0.55 && g <= r * 0.97;
        }

        /// <summary>Percentage of gold pixels in <paramref name="area"/> of <paramref name="bmp"/>, or -1 if the area isn't inside it.</summary>
        public static double Percent(Bitmap bmp, Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0 || !new Rectangle(Point.Empty, bmp.Size).Contains(area)) return -1;
            BitmapData data = bmp.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            int gold = 0;
            try
            {
                var row = new byte[area.Width * 4];
                for (int y = 0; y < area.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    for (int x = 0; x < area.Width; x++)
                        if (IsGold(row[x * 4 + 2], row[x * 4 + 1], row[x * 4])) gold++;
                }
            }
            finally
            {
                bmp.UnlockBits(data);
            }
            return gold * 100.0 / (area.Width * area.Height);
        }

        /// <summary>Picture of an area in step coordinates as it is now (from the game window if there is one), or null.</summary>
        public static Bitmap Capture(StepSpace space, Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return null;
            if (space.Window != null)
            {
                using (Bitmap whole = space.Window.CaptureClient())
                {
                    if (whole == null || !new Rectangle(Point.Empty, whole.Size).Contains(area)) return null;
                    return whole.Clone(area, PixelFormat.Format32bppArgb);
                }
            }
            try
            {
                var bmp = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(area.Location, Point.Empty, area.Size, CopyPixelOperation.SourceCopy);
                return bmp;
            }
            catch (ExternalException)
            {
                return null;
            }
        }

        public static byte[] ToJpeg(Bitmap bmp)
        {
            ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using (var parameters = new EncoderParameters(1))
            using (var ms = new MemoryStream())
            {
                parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
                bmp.Save(ms, jpeg, parameters);
                return ms.ToArray();
            }
        }
    }

    /// <summary>
    /// Sends an alert to a phone, through either an ntfy topic (the free ntfy app) or a Discord
    /// webhook (a message in one of your Discord channels).
    /// </summary>
    public static class PhoneNotifier
    {
        /// <summary>True for a Discord webhook address; anything else is treated as an ntfy topic or ntfy URL.</summary>
        public static bool IsDiscord(string target)
        {
            return target.IndexOf("/api/webhooks/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Sends the alert. Returns null when it was delivered, otherwise what went wrong.</summary>
        public static string Send(string target, string title, string message, byte[] jpeg)
        {
            target = (target ?? "").Trim();
            if (target.Length == 0) return "no ntfy topic or Discord webhook set";
            try
            {
                // Older .NET Framework defaults can lack TLS 1.2, which both services require.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                if (IsDiscord(target)) SendDiscord(target, title, message, jpeg);
                else SendNtfy(target, title, message, jpeg);
                return null;
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                return response != null ? "the service answered " + (int)response.StatusCode + " " + response.StatusDescription : ex.Message;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        static void SendNtfy(string target, string title, string message, byte[] jpeg)
        {
            string url = target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? target
                : "https://ntfy.sh/" + Uri.EscapeDataString(target);
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 20000;
            req.Headers["Title"] = Ascii(title);
            req.Headers["Priority"] = "urgent";
            req.Headers["Tags"] = "star";
            byte[] body;
            if (jpeg != null)
            {
                // With a picture attached, the text goes in a header and the picture is the body.
                req.Method = "PUT";
                req.Headers["Message"] = Ascii(message);
                req.Headers["Filename"] = "radiant.jpg";
                req.ContentType = "image/jpeg";
                body = jpeg;
            }
            else
            {
                req.Method = "POST";
                req.ContentType = "text/plain; charset=utf-8";
                body = Encoding.UTF8.GetBytes(message);
            }
            Upload(req, body);
        }

        static void SendDiscord(string url, string title, string message, byte[] jpeg)
        {
            string json = "{\"content\":\"" + JsonEscape("**" + title + "**\n" + message) + "\"}";
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "POST";
            req.Timeout = 20000;
            if (jpeg == null)
            {
                req.ContentType = "application/json";
                Upload(req, Encoding.UTF8.GetBytes(json));
                return;
            }

            string boundary = "----AutoClicker" + Guid.NewGuid().ToString("N");
            req.ContentType = "multipart/form-data; boundary=" + boundary;
            using (var ms = new MemoryStream())
            {
                Action<string> write = s => { byte[] b = Encoding.UTF8.GetBytes(s); ms.Write(b, 0, b.Length); };
                write("--" + boundary + "\r\nContent-Disposition: form-data; name=\"payload_json\"\r\nContent-Type: application/json\r\n\r\n" + json + "\r\n");
                write("--" + boundary + "\r\nContent-Disposition: form-data; name=\"files[0]\"; filename=\"radiant.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n");
                ms.Write(jpeg, 0, jpeg.Length);
                write("\r\n--" + boundary + "--\r\n");
                Upload(req, ms.ToArray());
            }
        }

        static void Upload(HttpWebRequest req, byte[] body)
        {
            req.ContentLength = body.Length;
            using (Stream s = req.GetRequestStream()) s.Write(body, 0, body.Length);
            using (req.GetResponse()) { }
        }

        static string Ascii(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(c >= 32 && c < 127 ? c : ' ');
            return sb.ToString();
        }

        static string JsonEscape(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    default:
                        if (c < 32) sb.Append("\\u" + ((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
