using System;
using System.Drawing;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetBunny
{
    // Acceptance infrastructure only. Normal gameplay never invokes desktop capture.
    internal static class DesktopFrameCapture
    {
        internal static bool HasVisibleDetail(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width < 2 || bitmap.Height < 2) return false;
            int step = Math.Max(1, (int)Math.Sqrt(bitmap.Width * (double)bitmap.Height / 50000));
            int visible = 0;
            HashSet<int> colors = new HashSet<int>();
            for (int y = 0; y < bitmap.Height; y += step)
                for (int x = 0; x < bitmap.Width; x += step)
                {
                    Color p = bitmap.GetPixel(x, y);
                    if (p.A <= 24) continue;
                    visible++;
                    colors.Add((p.R >> 3) << 10 | (p.G >> 3) << 5 | (p.B >> 3));
                    if (visible >= 64 && colors.Count >= 16) return true;
                }
            return false;
        }

        internal static Bitmap CaptureWindow(Form window)
        {
            if (window == null || window.IsDisposed || !window.Visible || window.WindowState == FormWindowState.Minimized)
                throw new InvalidOperationException("截图窗口必须已显示且未最小化。");
            return CaptureRegion(window.Bounds);
        }

        internal static Bitmap CaptureRegion(Rectangle bounds, bool requireVisibleDetail = true)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || !SystemInformation.VirtualScreen.Contains(bounds))
                throw new ArgumentOutOfRangeException("bounds", "截图区域必须完整位于可见桌面内。");
            DwmFlush();
            // Desktop pixels have no meaningful alpha; RGB avoids transparent screen grabs.
            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            try
            {
                IntPtr screen = GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                try
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        IntPtr target = g.GetHdc();
                        try
                        {
                            // .NET Framework rejects the combined enum value in CopyFromScreen.
                            if (!BitBlt(target, 0, 0, bounds.Width, bounds.Height, screen, bounds.X, bounds.Y, 0x40CC0020))
                                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                        }
                        finally { g.ReleaseHdc(target); }
                    }
                }
                finally { ReleaseDC(IntPtr.Zero, screen); }
                ValidateCapturedFrame(bitmap, requireVisibleDetail);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }

        internal static void ValidateCapturedFrame(Bitmap bitmap, bool requireVisibleDetail)
        {
            // Only an explicit actors-hidden background sample may be uniform.
            // Published frames still require detail, then the actor verifier
            // independently proves that real hero/enemy pixels are present.
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0 || (requireVisibleDetail && !HasVisibleDetail(bitmap)))
                throw new InvalidOperationException("截图缺少可辨内容，可能为空白、锁屏或捕获失败。");
        }

        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    }
}
