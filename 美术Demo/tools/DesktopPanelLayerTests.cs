using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetBunny
{
    // Short native z-order fixture, not natural progression or game artwork.
    // Every form and archive belongs to this newly created private session.
    internal sealed class DesktopPanelLayerTests
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        readonly DesktopDemoSession session;
        readonly string output;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 80 };
        Form panel, modal, unrelated;
        KnightLayeredWindow first, newcomer;
        Point hitPoint;
        Point exposedOwnerPoint;
        int phase, checks, failures;
        double next;
        bool finished;
        internal int ExitCode { get { return failures == 0 && finished ? 0 : 1; } }
        void Check(bool okay, string description) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + description); }
        static bool NativeTopMost(Form form) { return (GetWindowLongPtr(form.Handle, -20).ToInt64() & 8) != 0; }
        static IntPtr RootAt(Point point) { return GetAncestor(WindowFromPoint(point), 2); }
        [STAThread] static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            try
            {
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                using (DesktopDemoSession session = new DesktopDemoSession(Path.GetFullPath(args[0]), Path.Combine(output, "private-profile"), true))
                {
                    DesktopPanelLayerTests test = new DesktopPanelLayerTests(session, output);
                    Application.Run(session);
                    return test.ExitCode;
                }
            }
            catch (Exception error) { Console.WriteLine("ERROR " + error); return 2; }
        }
        DesktopPanelLayerTests(DesktopDemoSession session, string output)
        {
            this.session = session; this.output = output;
            session.Game.PauseArtDemoCapture();
            timer.Tick += Tick; timer.Start();
        }
        KnightLayeredWindow SpawnFixture(Point point)
        {
            KnightLayeredWindow actor = new KnightLayeredWindow { Text = "Own z-order fixture (not game art)", Location = new Point(point.X - 20, point.Y - 20) };
            Bitmap bitmap = new Bitmap(60, 60, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap)) graphics.Clear(Color.DarkMagenta);
            actor.Present(bitmap); Console.WriteLine("SPAWN present hwnd=" + actor.Handle.ToInt64() + " fg=" + GetForegroundWindow().ToInt64());
            actor.Show(); Console.WriteLine("SPAWN show hwnd=" + actor.Handle.ToInt64() + " fg=" + GetForegroundWindow().ToInt64()); return actor;
        }
        void Tick(object sender, EventArgs e)
        {
            if (finished || clock.Elapsed.TotalSeconds < next) return;
            if (clock.Elapsed.TotalSeconds > 15) { Check(false, "native fixture finished within 15 seconds"); Finish(); return; }
            try
            {
                Console.WriteLine("STATE phase=" + phase + " foreground=" + GetForegroundWindow().ToInt64() + " panel=" + (panel == null ? "none" : panel.IsDisposed ? "disposed" : panel.Handle.ToInt64() + "/top=" + panel.TopMost + "/native=" + NativeTopMost(panel)) + " modal=" + (modal == null ? 0 : modal.Handle.ToInt64()));
                switch (phase++)
                {
                    case 0:
                        panel = Application.OpenForms.OfType<DesktopPracticePanel>().Single();
                        Console.WriteLine("WELCOME managed=" + panel.TopMost + " native=" + NativeTopMost(panel));
                        Check(panel.Visible && NativeTopMost(panel), "initial welcome remains visible and natively topmost");
                        Check(GetForegroundWindow() != panel.Handle, "initial welcome does not activate itself");
                        session.OpenPanel("practice");
                        break;
                    case 1:
                        Check(GetForegroundWindow() == panel.Handle, "manually opened own handbook is foreground");
                        hitPoint = panel.PointToScreen(new Point(40, 90));
                        first = SpawnFixture(hitPoint);
                        Check(RootAt(hitPoint) == panel.Handle, "new actor is already behind foreground panel when Show returns");
                        break;
                    case 2:
                        Check(RootAt(hitPoint) == panel.Handle, "foreground panel remains clickable under a newly spawned opaque actor");
                        Check(GetForegroundWindow() == panel.Handle, "actor reordering never activates an actor or another form");
                        Check(first.Visible && NativeTopMost(first), "actor remains visible and natively topmost");
                        Check(first.Bounds.Contains(hitPoint), "test actually overlaps the visible panel with an opaque actor");
                        newcomer = SpawnFixture(hitPoint);
                        Check(RootAt(hitPoint) == panel.Handle, "later actor creation needs no timer grace period to protect buttons");
                        break;
                    case 3:
                        Check(RootAt(hitPoint) == panel.Handle, "later actors cannot cover a panel that is already open");
                        using (Bitmap actual = DesktopFrameCapture.CaptureRegion(new Rectangle(panel.PointToScreen(Point.Empty), panel.ClientSize), true))
                            actual.Save(Path.Combine(output, "foreground-handbook-native.png"), ImageFormat.Png);
                        modal = new Form { Text = "Own modal ownership fixture", Owner = panel, StartPosition = FormStartPosition.Manual, Location = panel.PointToScreen(new Point(30, 50)), Size = new Size(260, 150), ShowInTaskbar = false };
                        modal.Show(); modal.Activate();
                        Console.WriteLine("MODAL after show foreground=" + GetForegroundWindow().ToInt64() + " rootOwner=" + GetAncestor(modal.Handle, 3).ToInt64() + " nativeOwner=" + GetWindow(modal.Handle, 4).ToInt64() + " managedOwner=" + (modal.Owner == null ? 0 : modal.Owner.Handle.ToInt64()));
                        hitPoint = modal.PointToScreen(new Point(40, 50));
                        newcomer.Location = new Point(hitPoint.X - 20, hitPoint.Y - 20);
                        SetWindowPos(newcomer.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213);
                        exposedOwnerPoint = panel.PointToScreen(new Point(panel.ClientSize.Width - 45, panel.ClientSize.Height - 55));
                        first.Location = new Point(exposedOwnerPoint.X - 20, exposedOwnerPoint.Y - 20);
                        SetWindowPos(first.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213);
                        break;
                    case 4:
                        Check(GetForegroundWindow() == modal.Handle, "owned dialog retains actual foreground");
                        Check(RootAt(hitPoint) == modal.Handle, "owned dialog stays above layered actors and its owner");
                        Check(!modal.Bounds.Contains(exposedOwnerPoint) && first.Bounds.Contains(exposedOwnerPoint), "modal fixture leaves an owner area exposed under another opaque actor");
                        Check(RootAt(exposedOwnerPoint) == panel.Handle, "owned dialog protection also keeps exposed owner buttons above actors");
                        modal.Close(); modal.Dispose(); modal = null;
                        unrelated = new Form { Text = "Own unregistered foreground fixture", StartPosition = FormStartPosition.Manual, Location = panel.Location, Size = new Size(300, 180), ShowInTaskbar = false };
                        unrelated.Show(); unrelated.Activate();
                        Console.WriteLine("UNRELATED hwnd=" + unrelated.Handle.ToInt64() + " after show foreground=" + GetForegroundWindow().ToInt64());
                        break;
                    case 5:
                        Check(GetForegroundWindow() == unrelated.Handle, "coordinator does not reclaim focus from unregistered windows");
                        Check(!NativeTopMost(panel), "inactive feature panel gives up native topmost status");
                        Check(first.Visible && NativeTopMost(first), "leaving a panel does not hide or demote desktop actors");
                        unrelated.Hide(); unrelated.Dispose(); unrelated = null;
                        session.OpenPanel("practice");
                        break;
                    case 6:
                        Check(NativeTopMost(panel), "reopening the panel restores active panel protection");
                        panel.WindowState = FormWindowState.Minimized;
                        break;
                    case 7:
                        Check(!NativeTopMost(panel), "minimized panels do not stay natively topmost");
                        session.OpenPanel("practice");
                        break;
                    case 8:
                        Check(panel.Visible && panel.WindowState == FormWindowState.Normal, "manual reopen restores a minimized feature panel");
                        session.ClosePanels();
                        break;
                    case 9:
                        Check(!panel.Visible && !NativeTopMost(panel), "closing all panels retires their native z-order protection");
                        Check(session.Game.DesktopHeroWindow.Visible && first.Visible && newcomer.Visible, "panel closure leaves hero and independent actors alive");
                        Check(!first.Capture && !newcomer.Capture, "coordinator never captures pointer input");
                        panel.Dispose();
                        break;
                    default:
                        Check(!Application.OpenForms.Cast<Form>().Any(f => f == panel), "disposed panels are not reopened by coordinator ticks");
                        Finish(); return;
                }
                next = clock.Elapsed.TotalSeconds + .32;
            }
            catch (Exception error) { Check(false, "native fixture exception: " + error); Finish(); }
        }
        void Finish()
        {
            if (finished) return; finished = true; timer.Stop(); timer.Dispose();
            if (modal != null) modal.Dispose(); if (unrelated != null) unrelated.Dispose();
            if (first != null) first.Dispose(); if (newcomer != null) newcomer.Dispose();
            Console.WriteLine("Panel layering: " + checks + " checks, " + failures + " failures; " + clock.Elapsed.TotalSeconds.ToString("F2") + " seconds; private fixture only.");
            session.ExitAfterAcceptance(failures == 0 ? 0 : 1);
        }
    }
}
