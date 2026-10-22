using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Explicit, read-only native desktop acceptance. This runner never creates
    // source fixtures, grants consent, attacks, cleans, trims, or exits programs.
    internal sealed class DesktopEcologyAcceptance : IDisposable
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
        readonly DesktopDemoSession session;
        readonly string output;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 150 };
        readonly List<object> assertions = new List<object>(), captures = new List<object>();
        readonly Dictionary<string, object> report;
        bool busy, finished;
        int phase;
        double next;
        string targetId;
        AdventureController Game { get { return session.Game; } }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length != 3) { Console.Error.WriteLine("Usage: DesktopEcologyAcceptance.exe assetsRoot artRoot outputDir"); return 2; }
            string output = null, formal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            bool outputReady = false;
            Dictionary<string, string> before = null;
            Dictionary<string, object> report = new Dictionary<string, object> {
                { "passed", false }, { "scope", "Observe entry: real local Temp inventory and memory sampling; real-file actor pixels; ecology panel visibility; private save and exit" },
                { "excluded", "No battle progression, maintenance execution, process exit, or virtual-only actor may satisfy the real-file pixel check" },
                { "processId", Process.GetCurrentProcess().Id }, { "startedUtc", DateTime.UtcNow.ToString("o") }
            };
            try
            {
                string assets = Path.GetFullPath(args[0]), art = Path.GetFullPath(args[1]);
                string demo = Path.Combine(Directory.GetParent(Directory.GetParent(assets).FullName).FullName, "美术Demo");
                if (!Directory.Exists(assets) || !Directory.Exists(art) || !Directory.Exists(Path.Combine(demo, "tools")))
                    throw new DirectoryNotFoundException("Require existing source Assets, art pack, and project 美术Demo/tools directories.");
                output = Path.GetFullPath(args[2]);
                string rawParent = Path.GetFullPath(Path.Combine(demo, "qa")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!output.StartsWith(rawParent + "desktop-accept-eco-observe-", StringComparison.OrdinalIgnoreCase)
                    || !String.Equals(Path.GetDirectoryName(output), rawParent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Raw desktop evidence must use 美术Demo/qa/desktop-accept-eco-observe-* (gitignored).");
                if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Evidence output must be empty; never overwrite history.");
                Directory.CreateDirectory(output);
                outputReady = true;
                string profile = Path.Combine(demo, "verification", "eco-observe-native", "profile-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                before = HashArchives(formal);
                report["formalArchiveHashesBefore"] = before;
                report["privateProfile"] = profile;
                report["entry"] = "new DesktopDemoSession(assetsRoot, privateProfile, false, null, true)";
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, art);
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (DesktopDemoSession session = new DesktopDemoSession(assets, profile, false, null, true))
                using (DesktopEcologyAcceptance runner = new DesktopEcologyAcceptance(session, output, report))
                {
                    runner.Start(); Application.Run(session);
                    if (!runner.finished) throw new InvalidOperationException("Message loop ended before acceptance completed.");
                }
            }
            catch (Exception error) { report["passed"] = false; report["error"] = error.ToString(); Console.Error.WriteLine(error.Message); }
            finally
            {
                try
                {
                    Dictionary<string, string> after = HashArchives(formal);
                    bool unchanged = before != null && before.Count == after.Count && before.All(p => after.ContainsKey(p.Key) && after[p.Key] == p.Value);
                    report["formalArchiveHashesAfter"] = after; report["formalArchivesUnchangedAfterDispose"] = unchanged;
                    if (!unchanged) { report["passed"] = false; report["archiveError"] = "Formal archive set or hashes changed, or no initial snapshot was taken."; }
                }
                catch (Exception error) { report["passed"] = false; report["archiveError"] = error.ToString(); }
                report["completedUtc"] = DateTime.UtcNow.ToString("o");
                if (outputReady)
                    File.WriteAllText(Path.Combine(output, "desktop-ecology-acceptance.json"), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(true));
            }
            Console.WriteLine("Observe native desktop acceptance passed=" + report["passed"]);
            return (bool)report["passed"] ? 0 : 1;
        }

        DesktopEcologyAcceptance(DesktopDemoSession session, string output, Dictionary<string, object> report)
        { this.session = session; this.output = output; this.report = report; report["assertions"] = assertions; report["captures"] = captures; }
        void Start() { timer.Tick += Tick; timer.Start(); }
        void Check(bool ok, string detail)
        {
            assertions.Add(new { passed = ok, detail = detail, elapsedSeconds = clock.Elapsed.TotalSeconds });
            if (!ok) throw new InvalidOperationException(detail);
        }
        object ReadField(string name)
        { return typeof(AdventureController).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(Game); }
        void CheckReadOnly()
        {
            Check(Game.IsolatedDesktop && Game.DesktopObservesSystem && Game.DesktopMode == DesktopEnvironmentMode.Observe, "普通入口处于Observe，非Virtual/演示/验收快进");
            Check(!Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance, "文件/内存/无人值守三项授权全关");
            Check(ReadField("cleanupTask") == null && ReadField("memoryReclaimTask") == null && !Game.DesktopProcessBusy, "无文件清理、内存回收或程序退出任务");
            Check(Game.ReadDesktopPlayerView().MaintenanceResults.Length == 0 && Game.Battle.Progress.FreedBytes == 0, "无真实维护记录或释放空间宣称");
            report["permissions"] = new { fileCleanup = false, memoryReclaim = false, automaticMaintenance = false, processExitRequested = false };
        }
        void Tick(object sender, EventArgs args)
        {
            if (busy || finished) return; busy = true;
            try
            {
                if (!String.IsNullOrEmpty(Game.ArtDemoFailure)) throw new InvalidOperationException(Game.ArtDemoFailure);
                if (clock.Elapsed.TotalSeconds < next) return;
                if (phase == 0)
                {
                    CheckReadOnly(); phase = 1;
                }
                if (phase == 1)
                {
                    DesktopPlayerView view = Game.ReadDesktopPlayerView();
                    AdventureEnemy target = Game.Battle.Enemies.FirstOrDefault(e => !e.IsVirtual && e.Batch != null && e.Batch.FilesCount > 0
                        && !e.Defeated && !e.VisualDefeated && view.Enemies.Any(v => v.Id == e.Id)
                        && Game.DesktopEnemyWindows.ContainsKey(e.Id) && Game.DesktopEnemyWindows[e.Id].Visible);
                    bool memory = (bool)ReadField("memoryAvailable");
                    if (target == null || !memory)
                    {
                        if (clock.Elapsed.TotalSeconds > 45) throw new TimeoutException("45秒内没有可验证的真实Temp文件目标或本机内存样本；未制造文件/伪造来源。" + view.EcologyStatus);
                        next = clock.Elapsed.TotalSeconds + .3; return;
                    }
                    targetId = target.Id;
                    string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    Check(target.Batch.Candidates.All(c => File.Exists(c.Path) && Path.GetFullPath(c.Path).StartsWith(temp, StringComparison.OrdinalIgnoreCase)), "指定Batch中的来源文件确实存在于本机Temp");
                    Check(view.Enemies.Any(v => v.Id == targetId && v.Source.IndexOf("临时文件", StringComparison.Ordinal) >= 0), "指定真实文件目标已进入玩家view");
                    report["realSource"] = new { enemyId = targetId, batchId = target.Batch.Id, files = target.Batch.FilesCount, bytes = target.Batch.Bytes,
                        tempRoot = temp, memoryPercent = ReadField("memoryPercent"), ecologyStatus = view.EcologyStatus,
                        candidates = target.Batch.Candidates.Select(c => new { path = c.Path, bytes = c.Length, eligibleForCleanup = c.EligibleForCleanup }).ToArray() };
                    session.ClosePanels(); phase = 2; next = clock.Elapsed.TotalSeconds + .5; return;
                }
                if (phase == 2)
                {
                    CaptureActors("01-observe-real-file-actors", targetId);
                    session.OpenPanel("ecology");
                    DesktopEcologyPanel ownPanel = Application.OpenForms.OfType<DesktopEcologyPanel>().Single(f => f.Visible);
                    // Explicit opt-in tester visibility only. It affects this
                    // process's own panel, never other applications or actors.
                    ownPanel.TopMost = true; ownPanel.BringToFront(); ownPanel.Activate();
                    report["panelPresentation"] = "session.OpenPanel(ecology), then tester-owned panel TopMost/BringToFront/Activate; no other application's windows changed";
                    phase = 3; next = clock.Elapsed.TotalSeconds + .6; return;
                }
                if (phase == 3)
                {
                    DesktopEcologyPanel panel = Application.OpenForms.OfType<DesktopEcologyPanel>().SingleOrDefault(f => f.Visible);
                    Check(panel != null && !panel.IsDisposed && panel.WindowState != FormWindowState.Minimized, "本进程生态面板已实际显示");
                    List<object> probes = new List<object>();
                    foreach (int x in new[] { 8, panel.ClientSize.Width / 2, panel.ClientSize.Width - 8 })
                        foreach (int y in new[] { 8, panel.ClientSize.Height / 2, panel.ClientSize.Height - 8 })
                        {
                            Point point = panel.PointToScreen(new Point(x, y)); IntPtr root = GetAncestor(WindowFromPoint(point), 2);
                            probes.Add(new { screen = point.ToString(), root = root.ToInt64(), expected = panel.Handle.ToInt64() });
                            Check(root == panel.Handle, "生态面板原生命中归属本进程面板 " + point);
                        }
                    using (Bitmap frame = DesktopFrameCapture.CaptureRegion(panel.Bounds)) frame.Save(Path.Combine(output, "02-visible-ecology-panel.png"), ImageFormat.Png);
                    captures.Add(new { image = "02-visible-ecology-panel.png", bounds = panel.Bounds.ToString(), nativeHitTests = probes, capture = "native BitBlt SRCCOPY|CAPTUREBLT" });
                    session.ClosePanels(); phase = 4; next = clock.Elapsed.TotalSeconds + .5; return;
                }
                if (phase == 4)
                {
                    Check(!Application.OpenForms.OfType<DesktopEcologyPanel>().Any(f => f.Visible) && Game.DesktopHeroWindow.Visible, "面板收起后角色窗口仍可见");
                    CaptureActors("03-panels-closed-hero", null); CheckReadOnly();
                    Check(Game.DesktopSaveNow(), "独立五档安全保存");
                    Game.DesktopAcceptanceVerifyFiles(Game.DesktopAcceptanceRestartMarker());
                    Check(true, "独立业务档重新Load核对通过");
                    Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
            finally { busy = false; }
        }
        void CaptureActors(string name, string specificEnemyId)
        {
            bool running = Game.PauseArtDemoCapture(); List<ArtDemoActorFrame> actors = null;
            Bitmap actual = null, background = null, expected = null;
            try
            {
                actors = Game.SnapshotArtDemoActors();
                ArtDemoActorFrame hero = actors.FirstOrDefault(a => a.Role == "hero"), enemy = null;
                if (hero == null) throw new InvalidOperationException("没有已呈现的主角帧。");
                if (specificEnemyId != null)
                {
                    AdventureEnemy model = Game.Battle.Enemies.Single(e => e.Id == specificEnemyId);
                    Check(!model.IsVirtual && model.Batch != null, "像素校验绑定指定真实文件Batch目标，未使用任意虚拟妖物");
                    KnightLayeredWindow window = Game.DesktopEnemyWindows[specificEnemyId];
                    enemy = actors.SingleOrDefault(a => a.Role == "enemy" && a.Window == window);
                    if (enemy == null) throw new InvalidOperationException("指定真实文件目标没有已呈现帧。");
                }
                Rectangle bounds = enemy == null ? hero.Bounds : Rectangle.Union(hero.Bounds, enemy.Bounds);
                bounds.Inflate(24, 24); bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
                actual = DesktopFrameCapture.CaptureRegion(bounds); actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                try { foreach (ArtDemoActorFrame actor in actors) actor.Window.Hide(); background = DesktopFrameCapture.CaptureRegion(bounds, false); }
                finally { foreach (ArtDemoActorFrame actor in actors.OrderByDescending(a => a.ZOrder)) if (!actor.Window.IsDisposed) actor.Window.Show(); }
                expected = ArtDemoPixelVerifier.ExpectedComposite(background, actors, bounds.Location);
                List<object> pixels = new List<object> { ArtDemoPixelVerifier.VerifyActor(hero, actors, actual, background, expected, bounds.Location) };
                if (enemy != null) pixels.Add(ArtDemoPixelVerifier.VerifyActor(enemy, actors, actual, background, expected, bounds.Location));
                captures.Add(new { image = name + ".png", bounds = bounds.ToString(), specificRealEnemyId = specificEnemyId, actors = pixels,
                    capture = "native BitBlt SRCCOPY|CAPTUREBLT; presented-frame alpha comparison; expected composite used only as verifier, never published as capture" });
            }
            finally
            {
                if (actual != null) actual.Dispose(); if (background != null) background.Dispose(); if (expected != null) expected.Dispose();
                if (actors != null) foreach (ArtDemoActorFrame actor in actors) actor.Dispose(); Game.ResumeArtDemoCapture(running);
            }
        }
        void Finish(Exception error)
        {
            if (finished) return; finished = true; timer.Stop(); report["passed"] = error == null;
            report["elapsedSeconds"] = clock.Elapsed.TotalSeconds;
            if (error != null) { report["error"] = error.ToString(); report["ecologyStatusAtFailure"] = Game.DesktopEcologyStatus; }
            // A nonzero failure code avoids a save-failure modal and still uses
            // the session's regular save/dispose lifetime, affecting only us.
            session.ExitAfterAcceptance(error == null ? 0 : 1);
        }
        static Dictionary<string, string> HashArchives(string root)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(root)) return values;
            foreach (string path in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
                using (SHA256 hash = SHA256.Create()) values[Path.GetFileName(path)] = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
            return values;
        }
        public void Dispose() { timer.Stop(); timer.Dispose(); }
    }
}
