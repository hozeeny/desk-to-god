using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Short diagnostic only. Each run owns a new private profile and exits its own session.
    internal sealed class DesktopLayeredStartupProbe
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr h, int index);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint process);
        [DllImport("user32.dll", SetLastError=true)] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        readonly DesktopDemoSession session;
        readonly string output;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        readonly List<object> captures = new List<object>();
        readonly Dictionary<string, object> report;
        bool busy, paused, wasRunning, finished;
        int phase;
        double next = 2;
        AdventureController Game { get { return session.Game; } }
        [STAThread] static int Main(string[] args)
        {
            if (args.Length < 3) return 2;
            string output = Path.GetFullPath(args[2]);
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) return 2;
            Directory.CreateDirectory(output);
            string profile = Path.Combine(output, "private-profile");
            Dictionary<string, object> report = new Dictionary<string, object> { { "startedUtc", DateTime.UtcNow.ToString("o") }, { "pid", Process.GetCurrentProcess().Id }, { "profile", profile }, { "label", args.Length > 3 ? args[3] : "unspecified" }, { "scope", "new private seeded virtual session; no production source change; no other-process action; native BitBlt actuals" } };
            try
            {
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[1]));
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (DesktopDemoSession session = new DesktopDemoSession(Path.GetFullPath(args[0]), profile, true, "qishi-full", false))
                {
                    DesktopLayeredStartupProbe probe = new DesktopLayeredStartupProbe(session, output, report);
                    probe.timer.Tick += probe.Tick; probe.timer.Start(); Application.Run(session);
                    report["finished"] = probe.finished;
                }
            }
            catch (Exception error) { report["error"] = error.ToString(); }
            report["completedUtc"] = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(output, "probe.json"), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(true));
            return report.ContainsKey("error") ? 1 : 0;
        }
        DesktopLayeredStartupProbe(DesktopDemoSession session, string output, Dictionary<string, object> report)
        { this.session = session; this.output = output; this.report = report; report["captures"] = captures; }
        void Tick(object sender, EventArgs e)
        {
            if (busy || finished || clock.Elapsed.TotalSeconds < next) return;
            busy = true;
            try
            {
                if (phase == 0)
                {
                    // Freeze normal render timer to compare exactly the submitted source frame.
                    // This does not show, hide, activate, reorder or redraw any window.
                    wasRunning = Game.PauseArtDemoCapture(); paused = true;
                    Capture("01-untouched");
                    bool raised = SetWindowPos(Game.DesktopHeroWindow.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x213);
                    report["raiseResult"] = raised; report["raiseError"] = Marshal.GetLastWin32Error();
                    phase = 1; next = clock.Elapsed.TotalSeconds + .35; return;
                }
                if (phase == 1)
                {
                    Capture("02-after-own-topmost-noactivate");
                    Game.DesktopHeroWindow.Hide(); DwmFlush(); Game.DesktopHeroWindow.Show();
                    phase = 2; next = clock.Elapsed.TotalSeconds + .35; return;
                }
                Capture("03-after-own-hide-show"); Finish();
            }
            catch (Exception error) { report["error"] = error.ToString(); Finish(); }
            finally { busy = false; }
        }
        void Capture(string name)
        {
            DwmFlush();
            List<ArtDemoActorFrame> actors = Game.SnapshotArtDemoActors();
            try
            {
                ArtDemoActorFrame hero = actors.Single(a => a.Role == "hero");
                RECT native; GetWindowRect(hero.Window.Handle, out native);
                int cloak; int cloakResult = DwmGetWindowAttribute(hero.Window.Handle, 14, out cloak, 4);
                Rectangle crop = hero.Bounds; crop.Inflate(24, 24); crop = Rectangle.Intersect(crop, SystemInformation.VirtualScreen);
                using (Bitmap actual = DesktopFrameCapture.CaptureRegion(crop, true))
                {
                    // These two files are source pixels and uncomposited native desktop capture respectively.
                    hero.Frame.Save(Path.Combine(output, name + "-current.png"), ImageFormat.Png);
                    actual.Save(Path.Combine(output, name + "-raw.png"), ImageFormat.Png);
                    List<object> points = new List<object>();
                    int samples = 0, matches = 0;
                    for (int y = 0; y < hero.Frame.Height; y += 2) for (int x = 0; x < hero.Frame.Width; x += 2)
                    {
                        Color source = hero.Frame.GetPixel(x, y); if (source.A < 245) continue;
                        Point screen = new Point(hero.Bounds.X + x, hero.Bounds.Y + y);
                        if (!crop.Contains(screen)) continue;
                        Color raw = actual.GetPixel(screen.X - crop.X, screen.Y - crop.Y);
                        samples++; if (Math.Abs(raw.R-source.R)+Math.Abs(raw.G-source.G)+Math.Abs(raw.B-source.B) <= 45) matches++;
                        if (samples % 250 == 1)
                        {
                            IntPtr hit = WindowFromPoint(screen), root = GetAncestor(hit, 2); uint process; GetWindowThreadProcessId(root, out process);
                            points.Add(new { screen = screen.ToString(), hit = hit.ToInt64(), root = root.ToInt64(), rootPid = process, expectedHero = hero.Window.Handle.ToInt64(), sourceArgb = source.ToArgb(), actualArgb = raw.ToArgb() });
                        }
                    }
                    captures.Add(new { name = name, utc = DateTime.UtcNow.ToString("o"), elapsedSeconds = clock.Elapsed.TotalSeconds, bounds = hero.Bounds.ToString(), nativeRect = new { native.L, native.T, native.R, native.B }, crop = crop.ToString(), hwnd = hero.Window.Handle.ToInt64(), managedVisible = hero.Window.Visible, nativeVisible = IsWindowVisible(hero.Window.Handle), iconic = IsIconic(hero.Window.Handle), style = GetWindowLongPtr(hero.Window.Handle, -16).ToInt64(), exStyle = GetWindowLongPtr(hero.Window.Handle, -20).ToInt64(), cloak = cloak, cloakResult = cloakResult, presentedFrames = Game.PresentedFrames, actorCount = actors.Count, opaqueSamples = samples, opaqueDirectMatches = matches, opaqueDirectRatio = samples == 0 ? 0 : matches/(double)samples, points = points, artFailure = Game.ArtDemoFailure, cultivation = new { Game.Cultivation.Mortal, Game.Cultivation.RealmIndex, Game.Cultivation.Layer }, auto = Game.AutoPlanning });
                }
            }
            finally { foreach (ArtDemoActorFrame actor in actors) actor.Dispose(); }
        }
        void Finish()
        {
            if (finished) return; finished = true; timer.Stop(); timer.Dispose();
            if (paused) Game.ResumeArtDemoCapture(wasRunning);
            report["exitRequested"] = "session.ExitAfterAcceptance(1), own private profile only";
            session.ExitAfterAcceptance(1);
        }
    }
}
