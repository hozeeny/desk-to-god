using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace NetBunny
{
    // This pet belongs to the desktop program, and never changes network settings.
    internal enum KnightPhase { Run, Pant, Dying, Grave, Revive }
    internal enum KnightConnectivity { Unknown, Online, Offline }

    internal sealed class KnightState
    {
        internal KnightPhase Phase = KnightPhase.Pant;
        internal double PhaseTime, AnimationTime, SmoothedBytes, OfflineTime, OnlineTime, LowTime;
        internal bool TelemetryAvailable;
        internal double PixelsPerSecond
        {
            get { return Phase == KnightPhase.Run ? SpeedFor(SmoothedBytes) : 0; }
        }
        internal static double SpeedFor(double bytes)
        {
            // A gentle start, then a smooth ramp. 5 MiB/s is already a sprint.
            if (double.IsNaN(bytes) || double.IsInfinity(bytes)) bytes = 0;
            return 24 + 476 * (1 - Math.Exp(-Math.Max(0, bytes) / (2.5 * 1024 * 1024)));
        }
        internal void SetPhase(KnightPhase phase)
        {
            if (Phase != phase) { Phase = phase; PhaseTime = 0; }
        }
        internal void Tick(double elapsed, double bytes, bool telemetryAvailable, KnightConnectivity connectivity)
        {
            double dt = Math.Max(0, Math.Min(0.25, elapsed));
            if (double.IsNaN(bytes) || double.IsInfinity(bytes)) bytes = 0;
            bytes = Math.Max(0, bytes);
            TelemetryAvailable = telemetryAvailable;
            SmoothedBytes += ((telemetryAvailable ? bytes : 0) - SmoothedBytes) * (1 - Math.Exp(-dt / 0.9));
            PhaseTime += dt;
            AnimationTime += dt * (3.2 + 12 * (1 - Math.Exp(-SmoothedBytes / (2 * 1024 * 1024))));
            OfflineTime = connectivity == KnightConnectivity.Offline ? OfflineTime + dt : 0;
            OnlineTime = connectivity == KnightConnectivity.Online ? OnlineTime + dt : 0;

            // Finish each story beat. A transient reconnect cannot skip the fall.
            if (Phase == KnightPhase.Dying)
            {
                if (PhaseTime >= 3.4) SetPhase(KnightPhase.Grave);
                return;
            }
            if (Phase == KnightPhase.Grave)
            {
                if (OnlineTime >= 1.0 && PhaseTime >= 0.65) SetPhase(KnightPhase.Revive);
                return;
            }
            if (Phase == KnightPhase.Revive)
            {
                if (PhaseTime >= 2.4) { LowTime = 0; SetPhase(telemetryAvailable ? KnightPhase.Run : KnightPhase.Pant); }
                return;
            }
            if (OfflineTime >= 2.5) { SetPhase(KnightPhase.Dying); return; }
            if (!telemetryAvailable || connectivity == KnightConnectivity.Unknown)
            {
                SetPhase(KnightPhase.Pant);
                return;
            }
            if (SmoothedBytes < 24 * 1024)
            {
                LowTime += dt;
                // At idle he walks for 3.2 seconds, then catches his breath for 4.8.
                SetPhase(LowTime % 8.0 < 3.2 ? KnightPhase.Run : KnightPhase.Pant);
            }
            else { LowTime = 0; SetPhase(KnightPhase.Run); }
        }
        internal int FrameIndex
        {
            get
            {
                if (Phase == KnightPhase.Run) return (int)AnimationTime % 6;
                if (Phase == KnightPhase.Pant) return 6 + (int)(PhaseTime * 1.8) % 2;
                if (Phase == KnightPhase.Grave) return 11;
                if (Phase == KnightPhase.Revive) return 12 + Math.Min(2, (int)(PhaseTime / 0.8));
                return PhaseTime < 0.38 ? 8 : PhaseTime < 0.8 ? 9 : PhaseTime < 1.8 ? 10 : 11;
            }
        }
        internal double GhostProgress
        {
            get { return Phase == KnightPhase.Dying && PhaseTime >= 0.8 ? Math.Min(1, (PhaseTime - 0.8) / 2.6) : -1; }
        }
        internal static KnightState Demo(double time)
        {
            double t = Math.Max(0, time) % 20;
            KnightState s = new KnightState(); s.TelemetryAvailable = true;
            if (t < 4) { s.Phase = KnightPhase.Run; s.PhaseTime = t; s.SmoothedBytes = 8 * 1024 * 1024; s.AnimationTime = t * 14; }
            else if (t < 6) { s.Phase = KnightPhase.Run; s.PhaseTime = t - 4; s.SmoothedBytes = 2048; s.AnimationTime = (t - 4) * 3.3; }
            else if (t < 8) { s.Phase = KnightPhase.Pant; s.PhaseTime = t - 6; }
            else if (t < 11.4) { s.Phase = KnightPhase.Dying; s.PhaseTime = t - 8; }
            else if (t < 13) { s.Phase = KnightPhase.Grave; s.PhaseTime = t - 11.4; }
            else if (t < 15.4) { s.Phase = KnightPhase.Revive; s.PhaseTime = t - 13; }
            else { s.Phase = KnightPhase.Run; s.PhaseTime = t - 15.4; s.SmoothedBytes = 12 * 1024 * 1024; s.AnimationTime = (t - 15.4) * 15; }
            return s;
        }
    }

    internal sealed class KnightConnectivityReader : IDisposable
    {
        private readonly object gate = new object();
        private readonly System.Threading.Timer timer;
        private volatile bool disposed;
        private int checking;
        private KnightConnectivity current = KnightConnectivity.Unknown;
        private string detail = "正在读取 Windows 联网状态";
        private DateTime trafficAt = DateTime.MinValue, lastProbe = DateTime.MinValue, lastProbeSuccess = DateTime.MinValue;
        private int failedRounds;
        internal KnightConnectivityReader()
        {
            timer = new System.Threading.Timer(Check, null, 0, 2500);
        }
        internal void TrafficEvidence(double bytes, bool available)
        {
            // Substantial observed traffic beats stale NCSI metadata. This is not
            // treated as proof of Internet reachability when all interfaces are down.
            if (available && bytes >= 1024) lock (gate) trafficAt = DateTime.UtcNow;
        }
        internal KnightConnectivity Read(out string description)
        {
            lock (gate) { description = detail; return current; }
        }
        private void Publish(KnightConnectivity status, string description)
        {
            lock (gate) { if (!disposed) { current = status; detail = description; } }
        }
        private void Check(object ignored)
        {
            if (disposed || Interlocked.Exchange(ref checking, 1) != 0) return;
            try
            {
                bool physical = false;
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        try
                        {
                            foreach (UnicastIPAddressInformation address in ni.GetIPProperties().UnicastAddresses)
                                if (!IPAddress.IsLoopback(address.Address) && !address.Address.Equals(IPAddress.Any) && !address.Address.Equals(IPAddress.IPv6Any)) { physical = true; break; }
                        }
                        catch (NetworkInformationException) { physical = true; }
                    }
                    if (physical) break;
                }
                if (!physical) { failedRounds = 0; Publish(KnightConnectivity.Offline, "没有可用的网络连接"); return; }

                bool? windowsOnline = ReadNlm();
                DateTime now = DateTime.UtcNow;
                bool hasTraffic; lock (gate) hasTraffic = (now - trafficAt).TotalSeconds < 8;
                bool recentProbe = (now - lastProbeSuccess).TotalSeconds < 20;
                if (windowsOnline == true || hasTraffic || recentProbe)
                {
                    failedRounds = 0;
                    Publish(KnightConnectivity.Online, windowsOnline == true ? "Windows 已确认联网" : hasTraffic ? "检测到实际网络流量" : "独立连通检测成功");
                    return;
                }
                if (!windowsOnline.HasValue) { failedRounds = 0; Publish(KnightConnectivity.Unknown, "暂时无法确认联网状态，骑士正在休息"); return; }
                // NLM=false can mean a blocked Windows connectivity test. Require
                // repeated independent failures before declaring Internet offline.
                if ((now - lastProbe).TotalSeconds >= 10)
                {
                    lastProbe = now;
                    bool reachable = Probe("https://www.microsoft.com/");
                    if (!reachable && !disposed) reachable = Probe("https://www.cloudflare.com/");
                    if (disposed) return;
                    if (reachable) { failedRounds = 0; lastProbeSuccess = DateTime.UtcNow; Publish(KnightConnectivity.Online, "独立连通检测成功"); return; }
                    failedRounds++;
                }
                if (failedRounds >= 2) Publish(KnightConnectivity.Offline, "Windows 报告无 Internet，且两轮连通检测失败");
                else Publish(KnightConnectivity.Unknown, "正在确认网络连接，骑士暂时休息");
            }
            catch { Publish(KnightConnectivity.Unknown, "联网状态暂不可用，骑士正在休息"); }
            finally { Interlocked.Exchange(ref checking, 0); }
        }
        private static bool? ReadNlm()
        {
            object instance = null;
            try
            {
                Type type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"), false);
                if (type == null) return null;
                instance = Activator.CreateInstance(type);
                dynamic manager = instance;
                return (bool)manager.IsConnectedToInternet;
            }
            catch { return null; }
            finally { if (instance != null && Marshal.IsComObject(instance)) try { Marshal.FinalReleaseComObject(instance); } catch { } }
        }
        private static bool Probe(string url)
        {
            // HEAD only: no user files, cookies, credentials, or response body.
            HttpWebRequest request = null;
            System.Threading.Timer abort = null;
            try
            {
                request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "HEAD"; request.Timeout = 2200; request.ReadWriteTimeout = 2200;
                request.AllowAutoRedirect = false; request.KeepAlive = false;
                request.UseDefaultCredentials = false; request.Credentials = null;
                request.UserAgent = "NetBunny-Knight/2.0";
                request.Proxy = WebRequest.DefaultWebProxy;
                HttpWebRequest captured = request;
                abort = new System.Threading.Timer(delegate { try { captured.Abort(); } catch { } }, null, 2500, Timeout.Infinite);
                using (WebResponse response = request.GetResponse()) return true;
            }
            catch (WebException e) { if (e.Response != null) { e.Response.Close(); return true; } return false; }
            catch { return false; }
            finally { if (abort != null) abort.Dispose(); }
        }
        public void Dispose() { disposed = true; timer.Dispose(); }
    }

    internal sealed class KnightAtlas : IDisposable
    {
        private readonly Bitmap bitmap;
        private readonly Rectangle[] sourceFrames = new Rectangle[16];
        private readonly Bitmap[] isolatedFrames = new Bitmap[16];
        // Exact pose bounds of the supplied 1254 x 1254 atlas, including a small
        // transparent gutter. Poses are arranged in four rows but are NOT equally
        // spaced: the fallen knight's spear extends beyond a mathematical cell.
        // Keeping these source rectangles avoids neighboring spear fragments.
        private static readonly Rectangle[] ReferenceFrames = {
            Rectangle.FromLTRB(24,82,318,321), Rectangle.FromLTRB(335,82,629,317),
            Rectangle.FromLTRB(650,83,945,320), Rectangle.FromLTRB(971,83,1239,293),
            Rectangle.FromLTRB(19,377,316,614), Rectangle.FromLTRB(341,376,652,614),
            Rectangle.FromLTRB(675,369,908,615), Rectangle.FromLTRB(995,369,1212,616),
            Rectangle.FromLTRB(58,688,347,901), Rectangle.FromLTRB(351,718,632,897),
            Rectangle.FromLTRB(641,763,977,901), Rectangle.FromLTRB(975,647,1235,909),
            Rectangle.FromLTRB(32,947,304,1218), Rectangle.FromLTRB(342,947,620,1221),
            Rectangle.FromLTRB(660,933,935,1218), Rectangle.FromLTRB(994,951,1191,1216)
        };
        internal KnightAtlas(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("缺少骑士动画素材，请完整解压程序文件夹。", path);
            using (Image source = Image.FromFile(path)) bitmap = new Bitmap(source);
            if (bitmap.Width < 64 || bitmap.Height < 64) throw new InvalidDataException("骑士动画素材尺寸无效。");
            for (int i = 0; i < sourceFrames.Length; i++)
            {
                Rectangle r = ReferenceFrames[i];
                sourceFrames[i] = Rectangle.FromLTRB((int)Math.Floor(r.Left * bitmap.Width / 1254.0), (int)Math.Floor(r.Top * bitmap.Height / 1254.0),
                    (int)Math.Ceiling(r.Right * bitmap.Width / 1254.0), (int)Math.Ceiling(r.Bottom * bitmap.Height / 1254.0));
                // Independent in-memory images also prevent bicubic filtering
                // from sampling a neighboring pose outside the source rectangle.
                isolatedFrames[i] = bitmap.Clone(sourceFrames[i], PixelFormat.Format32bppArgb);
            }
        }
        internal void Draw(Graphics g, int index, RectangleF destination, bool left, float alpha)
        {
            if (index < 0 || index >= sourceFrames.Length) throw new ArgumentOutOfRangeException("index");
            Rectangle source = sourceFrames[index];
            // One common scale for every pose. A lying knight remains short and
            // wide; an upright knight remains tall. Never stretch each crop to a
            // square. Center all poses on a shared horizontal ground baseline.
            float units = destination.Width / (336f * bitmap.Width / 1254f);
            float width = source.Width * units, height = source.Height * units;
            RectangleF posed = new RectangleF(destination.Left + (destination.Width - width) / 2,
                destination.Bottom - destination.Width * 0.038f - height, width, height);
            GraphicsState saved = g.Save();
            if (left) { g.TranslateTransform(destination.Left * 2 + destination.Width, 0); g.ScaleTransform(-1, 1); }
            using (ImageAttributes attributes = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix(); matrix.Matrix33 = Math.Max(0, Math.Min(1, alpha));
                attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
                attributes.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(isolatedFrames[index], Rectangle.Round(posed), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            g.Restore(saved);
        }
        internal Bitmap Render(KnightState state, float scale, bool left)
        {
            // Keep room above the body for the ascending soul. Only alpha pixels
            // receive mouse input; this is never a full-screen overlay.
            int width = Math.Max(100, (int)(220 * scale));
            int height = Math.Max(140, (int)(306 * scale));
            Bitmap result = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias;
                g.CompositingQuality = CompositingQuality.HighQuality; g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float size = 206 * scale, x = 7 * scale, y = 92 * scale;
                if (state.Phase == KnightPhase.Run) y -= (float)(Math.Abs(Math.Sin(state.AnimationTime * Math.PI / 3)) * 3.2 * scale);
                if (state.Phase == KnightPhase.Pant) y += (float)(Math.Sin(state.PhaseTime * 6) * 1.7 * scale);
                Draw(g, state.FrameIndex, new RectangleF(x, y, size, size), left, 1);
                double spirit = state.GhostProgress;
                if (spirit >= 0 && spirit < 1)
                {
                    float ghostSize = 108 * scale;
                    float gx = (width - ghostSize) / 2 + (float)(Math.Sin(spirit * 10) * 7 * scale);
                    float gy = (float)((154 - spirit * 152) * scale);
                    float opacity = (float)(Math.Min(1, spirit * 7) * Math.Min(1, (1 - spirit) * 2.7));
                    Draw(g, 15, new RectangleF(gx, gy, ghostSize, ghostSize), left, opacity);
                }
            }
            return result;
        }
        public void Dispose()
        {
            foreach (Bitmap frame in isolatedFrames) if (frame != null) frame.Dispose();
            bitmap.Dispose();
        }
    }

    internal sealed class KnightLayeredWindow : Form
    {
        internal static event EventHandler DesktopVisibilityChanged;
        private Bitmap current;
        private bool pointerPressed, dragging, suppressLeftClick;
        private Point mouseStart, formStart;
        internal Action<Point> DragMoved;
        internal Action<bool> DragChanged;
        internal Action ContextRequested;
        internal bool PassThrough;
        internal KnightLayeredWindow()
        {
            // Form.TopMost re-applies SetWindowPos during handle creation and
            // can activate a new actor before its first transparent frame.
            // Set the native extended style together with NOACTIVATE instead.
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; Text = "长枪骑士 · 网速宠物";
            Cursor = Cursors.Hand; AutoScaleMode = AutoScaleMode.None;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x00000008 | 0x00080000 | 0x08000000 | 0x00000080; if (PassThrough) p.ExStyle |= 0x20; return p; }
        }
        internal bool DesktopTopMost { get { return IsHandleCreated ? (GetWindowLongPtr(Handle, -20).ToInt64() & 8) != 0 : true; } }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            EventHandler changed = DesktopVisibilityChanged;
            if (changed != null) changed(this, e);
        }
        protected override void WndProc(ref Message message)
        {
            if (PassThrough && message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (message.Msg == 0x0084 && current != null && !pointerPressed)
            {
                long value = message.LParam.ToInt64();
                Point location = PointToClient(new Point(unchecked((short)(value & 0xffff)), unchecked((short)((value >> 16) & 0xffff))));
                if (location.X < 0 || location.Y < 0 || location.X >= current.Width || location.Y >= current.Height || current.GetPixel(location.X, location.Y).A < 20)
                { message.Result = new IntPtr(-1); return; }
                message.Result = new IntPtr(1); return;
            }
            base.WndProc(ref message);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // A press is still a click until it crosses Windows' drag box.
                // This also prevents small hand tremors from moving the actor.
                pointerPressed = true; dragging = false; suppressLeftClick = false;
                mouseStart = Cursor.Position; formStart = Location; Capture = true;
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (pointerPressed)
            {
                Point pointer = Cursor.Position;
                Size threshold = SystemInformation.DragSize;
                Rectangle clickBox = new Rectangle(mouseStart.X - threshold.Width / 2, mouseStart.Y - threshold.Height / 2,
                    Math.Max(1, threshold.Width), Math.Max(1, threshold.Height));
                if (!dragging && !clickBox.Contains(pointer))
                {
                    dragging = true; suppressLeftClick = true;
                    if (DragChanged != null) DragChanged(true);
                }
                if (!dragging) { base.OnMouseMove(e); return; }
                Point position = new Point(formStart.X + pointer.X - mouseStart.X, formStart.Y + pointer.Y - mouseStart.Y);
                if (DragMoved != null) DragMoved(position);
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && pointerPressed)
            {
                bool wasDragging = dragging; pointerPressed = dragging = false; Capture = false;
                if (wasDragging && DragChanged != null) DragChanged(false);
            }
            if (e.Button == MouseButtons.Right && ContextRequested != null) ContextRequested();
            base.OnMouseUp(e);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && suppressLeftClick) return;
            base.OnMouseClick(e);
        }
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && pointerPressed)
            {
                bool wasDragging = dragging; pointerPressed = dragging = false;
                suppressLeftClick = true;
                if (wasDragging && DragChanged != null) DragChanged(false);
            }
            base.OnMouseCaptureChanged(e);
        }
        internal void Present(Bitmap frame)
        {
            if (IsDisposed) { frame.Dispose(); return; }
            Size = frame.Size;
            IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, hbitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                memory = CreateCompatibleDC(screen);
                hbitmap = frame.GetHbitmap(Color.FromArgb(0)); previous = SelectObject(memory, hbitmap);
                NativePoint source = new NativePoint(0, 0), destination = new NativePoint(Left, Top);
                NativeSize size = new NativeSize(frame.Width, frame.Height);
                BlendFunction blend = new BlendFunction(); blend.SourceConstantAlpha = 255; blend.AlphaFormat = 1;
                if (!UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref source, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                Bitmap old = current; current = frame; frame = null; if (old != null) old.Dispose();
            }
            finally
            {
                if (previous != IntPtr.Zero && memory != IntPtr.Zero) SelectObject(memory, previous);
                if (hbitmap != IntPtr.Zero) DeleteObject(hbitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
                if (frame != null) frame.Dispose();
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && current != null) { current.Dispose(); current = null; }
            base.Dispose(disposing);
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; internal NativePoint(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { internal int Width, Height; internal NativeSize(int w, int h) { Width = w; Height = h; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { internal byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref NativePoint position, ref NativeSize size, IntPtr memory, ref NativePoint source, uint key, ref BlendFunction blend, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr item);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    }

    internal sealed class KnightPetController : IDisposable
    {
        private readonly Form owner;
        private readonly KnightAtlas atlas;
        private readonly KnightLayeredWindow window;
        private readonly KnightConnectivityReader connectivity;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private KnightState state = new KnightState();
        private bool enabled, roaming = true, paused, dragging, disposed, left;
        private bool monitoringAvailable;
        private double bytesPerSecond, lastTick, x, y, targetX, targetY, velocityX, velocityY, pauseMotionUntil;
        private double lastTelemetryAt = double.NegativeInfinity;
        private double demoStarted = -1;
        private float scale = 0.9f;
        private Rectangle area;
        private string connectivityDetail = "正在确认网络连接", failure;
        public KnightPetController(Form owner, string spritePath)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            this.owner = owner;
            atlas = new KnightAtlas(spritePath);
            window = new KnightLayeredWindow();
            area = Screen.FromControl(owner).WorkingArea;
            x = area.Right - 250; y = area.Bottom - 306;
            window.Size = new Size((int)(220 * scale), (int)(306 * scale));
            ClampPosition(); PickTarget();
            window.DragChanged = delegate(bool active) { dragging = active; velocityX = velocityY = 0; if (!active) { pauseMotionUntil = clock.Elapsed.TotalSeconds + 3; area = Screen.FromPoint(window.Location).WorkingArea; ClampPosition(); PickTarget(); } };
            window.DragMoved = delegate(Point p) { x = p.X; y = p.Y; window.Location = p; };
            window.ContextRequested = ShowSettings;
            connectivity = new KnightConnectivityReader();
            timer = new System.Windows.Forms.Timer(); timer.Interval = 33; timer.Tick += Tick;
            lastTick = clock.Elapsed.TotalSeconds; timer.Start();
        }
        public bool Enabled
        {
            get { return enabled; }
            set
            {
                if (disposed || enabled == value) return;
                enabled = value;
                if (value) { failure = null; lastTick = clock.Elapsed.TotalSeconds; Paint(); if (enabled) window.Show(); }
                else window.Hide();
            }
        }
        public bool Roaming { get { return roaming; } set { roaming = value; velocityX = velocityY = 0; if (value) PickTarget(); } }
        public string StateText
        {
            get
            {
                if (failure != null) return "骑士显示失败：" + failure;
                if (!enabled) return "骑士已隐藏";
                if (demoStarted >= 0) return "演示模式 · 不会改变真实网络";
                if (paused) return "骑士已暂停";
                if (state.Phase == KnightPhase.Dying) return "网络断开 · 倒地，灵魂升天";
                if (state.Phase == KnightPhase.Grave) return "等待重新联网 · 墓碑守候";
                if (state.Phase == KnightPhase.Revive) return "网络恢复 · 骑士正在爬出坟地";
                if (!state.TelemetryAvailable) return "流量读数暂不可用 · 骑士正在休息";
                if (state.Phase == KnightPhase.Pant) return "流量较低或等待确认联网 · 停下来喘口气";
                return state.SmoothedBytes >= 2 * 1024 * 1024 ? "网络繁忙 · 长枪骑士冲刺中" : "轻装慢跑 · 跑速跟随实时流量";
            }
        }
        public void UpdateTelemetry(double bytesPerSecond, bool monitoringAvailable)
        {
            if (disposed) return;
            if (owner.InvokeRequired)
            {
                try { owner.BeginInvoke(new Action<double, bool>(UpdateTelemetry), bytesPerSecond, monitoringAvailable); } catch (InvalidOperationException) { }
                return;
            }
            this.bytesPerSecond = double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) ? 0 : Math.Max(0, bytesPerSecond);
            this.monitoringAvailable = monitoringAvailable;
            lastTelemetryAt = clock.Elapsed.TotalSeconds;
            connectivity.TrafficEvidence(this.bytesPerSecond, monitoringAvailable);
        }
        public void PlayDemo()
        {
            if (disposed) return;
            Enabled = true; paused = false; demoStarted = clock.Elapsed.TotalSeconds; PickTarget();
        }
        public void ShowSettings()
        {
            if (disposed) return;
            menu.Items.Clear();
            ToolStripMenuItem title = new ToolStripMenuItem("长枪骑士 · " + StateText); title.Enabled = false; menu.Items.Add(title);
            ToolStripMenuItem roam = new ToolStripMenuItem("在屏幕上自由奔跑"); roam.Checked = Roaming; roam.Click += delegate { Roaming = !Roaming; }; menu.Items.Add(roam);
            ToolStripMenuItem pause = new ToolStripMenuItem(paused ? "继续动作" : "暂停动作"); pause.Click += delegate { paused = !paused; }; menu.Items.Add(pause);
            ToolStripMenuItem demo = new ToolStripMenuItem(demoStarted >= 0 ? "结束演示，跟随真实网络" : "观看完整动作演示（20 秒）");
            demo.Click += delegate { if (demoStarted >= 0) { demoStarted = -1; state = new KnightState(); } else PlayDemo(); }; menu.Items.Add(demo);
            ToolStripMenuItem sizes = new ToolStripMenuItem("骑士大小");
            AddScale(sizes, "小 · 75%", 0.75f); AddScale(sizes, "中 · 90%", 0.9f); AddScale(sizes, "大 · 115%", 1.15f); menu.Items.Add(sizes);
            ToolStripMenuItem screens = new ToolStripMenuItem("活动屏幕");
            int number = 0;
            foreach (Screen screen in Screen.AllScreens)
            {
                Screen captured = screen; number++;
                ToolStripMenuItem item = new ToolStripMenuItem("屏幕 " + number + (screen.Primary ? "（主屏）" : ""));
                item.Checked = screen.WorkingArea == area;
                item.Click += delegate { area = captured.WorkingArea; x = area.X + area.Width / 2 - window.Width / 2; y = area.Bottom - window.Height - 12; ClampPosition(); PickTarget(); Paint(); };
                screens.DropDownItems.Add(item);
            }
            menu.Items.Add(screens); menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem status = new ToolStripMenuItem(connectivityDetail); status.Enabled = false; menu.Items.Add(status);
            ToolStripMenuItem hide = new ToolStripMenuItem("隐藏骑士（可在主界面重新开启）"); hide.Click += delegate { Enabled = false; }; menu.Items.Add(hide);
            menu.Show(Cursor.Position);
        }
        private void AddScale(ToolStripMenuItem parent, string label, float value)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label); item.Checked = Math.Abs(scale - value) < 0.01;
            item.Click += delegate { double foot = y + window.Height; scale = value; window.Size = new Size((int)(220 * scale), (int)(306 * scale)); y = foot - window.Height; ClampPosition(); PickTarget(); Paint(); };
            parent.DropDownItems.Add(item);
        }
        private void Tick(object sender, EventArgs args)
        {
            if (disposed) return;
            double now = clock.Elapsed.TotalSeconds, dt = Math.Min(0.1, Math.Max(0, now - lastTick)); lastTick = now;
            if (!enabled || paused) return;
            KnightConnectivity online = connectivity.Read(out connectivityDetail);
            if (demoStarted >= 0)
            {
                double time = now - demoStarted;
                if (time >= 20) { demoStarted = -1; state = new KnightState(); }
                else state = KnightState.Demo(time);
            }
            bool freshTelemetry = monitoringAvailable && now - lastTelemetryAt <= 6;
            if (demoStarted < 0) state.Tick(dt, bytesPerSecond, freshTelemetry, online);
            if (!dragging && roaming && now >= pauseMotionUntil && state.Phase == KnightPhase.Run)
            {
                double dx = targetX - x, dy = targetY - y, distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 24) { PickTarget(); dx = targetX - x; dy = targetY - y; distance = Math.Sqrt(dx * dx + dy * dy); }
                if (distance > 0.1)
                {
                    double speed = state.PixelsPerSecond;
                    double easing = 1 - Math.Exp(-dt * 3.5);
                    velocityX += (dx / distance * speed - velocityX) * easing;
                    velocityY += (dy / distance * speed - velocityY) * easing;
                    x += velocityX * dt; y += velocityY * dt;
                    if (Math.Abs(velocityX) > 5) left = velocityX < 0;
                    ClampPosition();
                }
            }
            else { velocityX = velocityY = 0; }
            if (!dragging) window.Location = new Point((int)x, (int)y);
            Paint();
        }
        private void ClampPosition()
        {
            double previousX = x, previousY = y;
            PointF bounded = BoundPosition(x, y, window.Size, area);
            x = bounded.X; y = bounded.Y;
            if (x != previousX) velocityX = 0;
            if (y != previousY) velocityY = 0;
            window.Location = new Point((int)x, (int)y);
        }
        internal static PointF BoundPosition(double x, double y, Size petSize, Rectangle bounds)
        {
            return new PointF((float)Math.Max(bounds.Left + 4, Math.Min(bounds.Right - petSize.Width - 4, x)),
                (float)Math.Max(bounds.Top + 2, Math.Min(bounds.Bottom - petSize.Height - 4, y)));
        }
        private void PickTarget()
        {
            targetX = area.Left + 8 + random.NextDouble() * Math.Max(1, area.Width - window.Width - 16);
            targetY = area.Top + 4 + random.NextDouble() * Math.Max(1, area.Height - window.Height - 12);
        }
        private void Paint()
        {
            if (disposed) return;
            try { window.Present(atlas.Render(state, scale, left)); }
            catch (Exception e) { failure = e.Message; enabled = false; window.Hide(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; timer.Stop(); timer.Dispose(); connectivity.Dispose(); menu.Dispose(); window.Dispose(); atlas.Dispose();
        }

        public static string SelfTest()
        {
            int passed = 0;
            Action<bool, string> check = delegate(bool condition, string text) { if (!condition) throw new InvalidOperationException("Knight test failed: " + text); passed++; };
            KnightState idle = new KnightState(); bool ran = false, panted = false;
            for (int i = 0; i < 240; i++) { idle.Tick(0.1, 0, true, KnightConnectivity.Online); ran |= idle.Phase == KnightPhase.Run; panted |= idle.Phase == KnightPhase.Pant; }
            check(ran && panted && idle.Phase != KnightPhase.Grave, "zero traffic alternates slow movement and panting without death");
            KnightState fast = new KnightState(); for (int i = 0; i < 60; i++) fast.Tick(0.1, 8 * 1024 * 1024, true, KnightConnectivity.Online);
            check(fast.Phase == KnightPhase.Run && fast.PixelsPerSecond > 400 && fast.PixelsPerSecond <= 500, "high traffic creates bounded sprint");
            check(KnightState.SpeedFor(0) < KnightState.SpeedFor(64000) && KnightState.SpeedFor(64000) < KnightState.SpeedFor(1024 * 1024), "speed increases monotonically");
            check(KnightState.SpeedFor(double.NaN) == KnightState.SpeedFor(0) && KnightState.SpeedFor(-100) == KnightState.SpeedFor(0), "invalid speed input is harmless");
            PointF bounded = BoundPosition(-10000, 10000, new Size(220, 306), new Rectangle(-1920, -200, 1920, 1080));
            check(bounded.X == -1916 && bounded.Y == 570, "motion remains within selected monitor including negative coordinates");
            KnightState unknown = new KnightState(); for (int i = 0; i < 400; i++) unknown.Tick(0.1, 0, false, KnightConnectivity.Unknown);
            check(unknown.Phase == KnightPhase.Pant, "missing telemetry and unknown connectivity rest without false death");
            KnightState story = new KnightState(); for (int i = 0; i < 24; i++) story.Tick(0.1, 0, true, KnightConnectivity.Offline);
            check(story.Phase != KnightPhase.Dying, "short outage is debounced");
            for (int i = 0; i < 15; i++) story.Tick(0.1, 0, true, KnightConnectivity.Offline);
            check(story.Phase == KnightPhase.Dying && story.GhostProgress >= 0, "sustained outage plays fall and ghost");
            for (int i = 0; i < 60; i++) story.Tick(0.1, 0, true, KnightConnectivity.Offline);
            check(story.Phase == KnightPhase.Grave && story.FrameIndex == 11, "outage settles into tombstone");
            for (int i = 0; i < 30; i++) story.Tick(0.1, 0, false, KnightConnectivity.Unknown);
            check(story.Phase == KnightPhase.Grave, "unknown status cannot resurrect");
            for (int i = 0; i < 12; i++) story.Tick(0.1, 800000, true, KnightConnectivity.Online);
            check(story.Phase == KnightPhase.Revive && story.FrameIndex >= 12, "reconnect climbs from grave");
            for (int i = 0; i < 30; i++) story.Tick(0.1, 800000, true, KnightConnectivity.Online);
            check(story.Phase == KnightPhase.Run, "revival resumes running");
            KnightState fluctuation = new KnightState(); for (int i = 0; i < 27; i++) fluctuation.Tick(0.1, 0, true, KnightConnectivity.Offline);
            for (int i = 0; i < 10; i++) fluctuation.Tick(0.1, 800000, true, KnightConnectivity.Online);
            check(fluctuation.Phase == KnightPhase.Dying, "reconnection does not interrupt a fall mid-frame");
            for (int i = 0; i < 200; i++) { KnightState s = KnightState.Demo(i / 10.0); check(s.FrameIndex >= 0 && s.FrameIndex <= 14, "demo atlas index bounds"); }
            return "Knight pet: " + passed + " deterministic assertions passed; no network settings changed.";
        }

        public static void ExportPreview(string spritePath, string pngPath)
        {
            using (KnightAtlas atlas = new KnightAtlas(spritePath))
            using (Bitmap result = new Bitmap(1100, 660))
            using (Graphics g = Graphics.FromImage(result))
            using (Font heading = new Font("Microsoft YaHei UI", 26, FontStyle.Bold))
            using (Font title = new Font("Microsoft YaHei UI", 14, FontStyle.Bold))
            using (Font small = new Font("Microsoft YaHei UI", 11))
            using (Brush ink = new SolidBrush(Color.FromArgb(48, 43, 70)))
            using (Brush muted = new SolidBrush(Color.FromArgb(114, 106, 139)))
            {
                g.Clear(Color.FromArgb(247, 244, 255)); g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawString("长枪骑士 · 你的网速有了小伙伴", heading, ink, 34, 24);
                g.DrawString("透明桌面宠物  /  拖动换位置  /  右键设置  /  满屏自由奔跑", small, muted, 38, 82);
                double[] samples = { 1.1, 6.7, 10.0, 14.7 };
                string[] names = { "流量快，冲刺！", "流量慢，喘口气", "断网，灵魂升天", "重连，从坟地爬出" };
                string[] captions = { "速度平滑跟随真实上下行流量", "慢跑与原地大喘气交替", "倒地后留下墓碑，等待联网", "自动复活，继续举枪奔跑" };
                for (int i = 0; i < 4; i++)
                {
                    int panelX = 26 + i * 270;
                    using (Brush white = new SolidBrush(Color.White)) g.FillRectangle(white, panelX, 127, 246, 436);
                    using (Bitmap frame = atlas.Render(KnightState.Demo(samples[i]), 1.02f, false)) g.DrawImageUnscaled(frame, panelX + 11, 145);
                    g.DrawString(names[i], title, ink, panelX + 12, 475);
                    g.DrawString(captions[i], small, muted, new RectangleF(panelX + 12, 513, 225, 44));
                }
                g.DrawString("动作示意图 · 闲置零流量不会被当成断网；联网状态单独检测。", small, muted, 35, 605);
                result.Save(pngPath, ImageFormat.Png);
            }
        }

        // 80 frames, intended playback: 8 fps. The 20-second story is condensed
        // into a 10-second shareable animation. No real desktop window is created.
        public static void ExportAnimationFrames(string spritePath, string directory)
        {
            Directory.CreateDirectory(directory);
            using (KnightAtlas atlas = new KnightAtlas(spritePath))
            using (Font title = new Font("Microsoft YaHei UI", 22, FontStyle.Bold))
            using (Font label = new Font("Microsoft YaHei UI", 14, FontStyle.Bold))
            using (Font small = new Font("Microsoft YaHei UI", 10))
            using (Brush ink = new SolidBrush(Color.FromArgb(48, 43, 70)))
            using (Brush muted = new SolidBrush(Color.FromArgb(117, 106, 139)))
            {
                for (int i = 0; i < 80; i++)
                {
                    double t = i * 0.25; KnightState sample = KnightState.Demo(t);
                    using (Bitmap result = new Bitmap(760, 460))
                    using (Graphics g = Graphics.FromImage(result))
                    {
                        g.Clear(Color.FromArgb(247, 244, 255)); g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawString("长枪骑士 · 网速桌面宠物", title, ink, 26, 20);
                        g.DrawString("动作演示 · 不会断开或修改真实网络", small, muted, 29, 67);
                        using (Pen line = new Pen(Color.FromArgb(227, 218, 246), 2)) g.DrawLine(line, 30, 358, 730, 358);
                        double progress = t < 4 ? t / 4 : t >= 15.4 ? (t - 15.4) / 4.6 : 0.6;
                        int px = sample.Phase == KnightPhase.Run ? 50 + (int)(progress * 430) : 320;
                        bool facingLeft = t >= 15.4;
                        if (facingLeft) px = 490 - (int)(progress * 420);
                        using (Bitmap frame = atlas.Render(sample, 0.96f, facingLeft)) g.DrawImageUnscaled(frame, px, 70);
                        string caption = sample.Phase == KnightPhase.Run ? (sample.SmoothedBytes > 1024 * 1024 ? "流量快 → 提枪冲刺！" : "流量慢 → 小步慢跑") : sample.Phase == KnightPhase.Pant ? "流量慢 → 停下来大喘气" : sample.Phase == KnightPhase.Dying ? "网络断开 → 倒地，灵魂升天" : sample.Phase == KnightPhase.Grave ? "网络断开 → 墓碑静静等待" : "恢复联网 → 从坟地爬出来！";
                        g.DrawString(caption, label, ink, 30, 385);
                        g.DrawString("透明悬浮 · 右键设置 · 拖动移动 · 可随时隐藏", small, muted, 31, 427);
                        result.Save(Path.Combine(directory, "frame-" + i.ToString("000") + ".png"), ImageFormat.Png);
                    }
                }
            }
        }
    }
}
