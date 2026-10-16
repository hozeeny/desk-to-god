using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace NetBunny
{
    // Presentation only. The adventure controller owns all disk sampling.
    internal sealed class DiskSpaceBar : Control
    {
        private DiskSpaceSnapshot snapshot;
        internal DiskSpaceSnapshot Snapshot { get { return snapshot; } }
        internal bool IsLow { get { return snapshot != null && snapshot.Available && snapshot.AvailableBytes < LowDiskBossPolicy.LowThresholdBytes; } }
        internal string Summary
        {
            get
            {
                if (snapshot == null) return "C 盘剩余空间：等待采样。";
                if (!snapshot.Available) return "C 盘剩余空间暂不可用：" + snapshot.Error;
                return "C 盘剩余 " + FormatBytes(snapshot.AvailableBytes) + " / 总容量 " + FormatBytes(snapshot.TotalBytes) +
                    "；可用 " + snapshot.FreePercent.ToString("0.0", CultureInfo.InvariantCulture) + "%" +
                    (IsLow ? "。剩余不足 1 GiB，磁盘空间偏低。" : "。");
            }
        }

        internal DiskSpaceBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Art.Bg;
            Size = new Size(270, 95);
            MinimumSize = new Size(180, 90);
            AccessibleName = "C 盘剩余空间";
            AccessibleRole = AccessibleRole.ProgressBar;
            TabStop = false;
        }

        internal void SetSnapshot(DiskSpaceSnapshot value)
        {
            if (Object.ReferenceEquals(snapshot, value)) return;
            snapshot = value;
            AccessibleDescription = Summary;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Width < 40 || Height < 40) return;
            Art.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), 18, Color.White);
            bool known = snapshot != null && snapshot.Available;
            Color accent = IsLow ? Color.FromArgb(211, 66, 87) : known ? Color.FromArgb(31, 151, 126) : Art.Muted;
            string title = IsLow ? "C 盘剩余空间 · 不足 1 GiB" : "C 盘剩余空间";
            string value = known ? FormatBytes(snapshot.AvailableBytes) : "等待采样";
            string detail = known ? "共 " + FormatBytes(snapshot.TotalBytes) + " · " +
                snapshot.FreePercent.ToString("0.0", CultureInfo.InvariantCulture) + "% 可用" : "容量与可用比例尚未确认";
            TextFormatFlags text = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            using (Font caption = Art.Font(8.5f, FontStyle.Bold))
            using (Font number = Art.Font(19, FontStyle.Bold))
            using (Font note = Art.Font(8.2f))
            {
                TextRenderer.DrawText(g, title, caption, new Rectangle(17, 10, Width - 34, 19), accent, text);
                TextRenderer.DrawText(g, value, number, new Rectangle(15, 29, Width - 30, 31), IsLow ? accent : Art.Ink, text);
                TextRenderer.DrawText(g, detail, note, new Rectangle(17, 60, Width - 34, 18), Art.Muted, text);
            }
            Rectangle track = new Rectangle(18, Height - 13, Width - 36, 6);
            Art.Fill(g, track, 3, Color.FromArgb(231, 235, 241));
            if (known && snapshot.AvailableBytes > 0)
            {
                int fill = Math.Max(1, Math.Min(track.Width, (int)Math.Round(track.Width * snapshot.FreeRatio)));
                // A square dot keeps extremely small remaining ratios visible without
                // inflating the displayed percentage or claiming extra free capacity.
                if (fill <= 5) using (Brush dot = new SolidBrush(accent)) g.FillRectangle(dot, track.X, track.Y, fill, track.Height);
                else Art.Fill(g, new Rectangle(track.X, track.Y, fill, track.Height), 3, accent);
            }
            if (IsLow)
                using (Pen warning = new Pen(Color.FromArgb(150, accent), 1))
                using (GraphicsPath outline = Art.Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 17))
                    g.DrawPath(warning, outline);
        }

        internal static string FormatBytes(long bytes)
        {
            if (bytes >= 1099511627776L) return (bytes / 1099511627776.0).ToString("0.00", CultureInfo.InvariantCulture) + " TiB";
            if (bytes >= 1073741824L) return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GiB";
            if (bytes >= 1048576L) return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
            if (bytes >= 1024L) return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KiB";
            return Math.Max(0, bytes).ToString(CultureInfo.InvariantCulture) + " B";
        }
    }
}

