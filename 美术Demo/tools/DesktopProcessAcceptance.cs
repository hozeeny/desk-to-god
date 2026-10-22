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
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Opt-in native acceptance. The only real action target is the unique
    // DesktopProcessFixture child launched by this runner. No real user app,
    // Temp file, memory target, seed, time advance, HP or reward is altered.
    internal sealed class DesktopProcessAcceptance : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] struct Rect { internal int Left, Top, Right, Bottom; }
        delegate bool EnumChild(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [DllImport("user32.dll")] static extern IntPtr GetDlgItem(IntPtr parent, int id);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder value, int max);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumChild callback, IntPtr parameter);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr target, uint flags);
        [DllImport("user32.dll")] static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        [DllImport("dwmapi.dll")] static extern int DwmFlush();
        readonly DesktopDemoSession session;
        readonly DesktopDemoAcceptance native;
        readonly string output, fixturePath, marker;
        readonly bool visibleOwnerDiagnostic;
        readonly Dictionary<string, object> report;
        readonly List<object> checks = new List<object>(), captures = new List<object>();
        readonly List<object> dialogLifecycle = new List<object>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        Process fixture;
        Task<ProcessIdentity[]> observation;
        Thread dialogThread;
        volatile bool confirmed, finished, dialogInProgress;
        volatile Exception dialogError;
        object dialogEvidence;
        AdventureEnemy target;
        int phase, clicks, initialHealth, initialKills;
        bool busy, battleCaptured, confirmOwnerOpened;
        double next;
        AdventureController Game { get { return session.Game; } }
        static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        [STAThread] static int Main(string[] args)
        {
            if (args.Length != 4 && !(args.Length == 5 && args[4] == "--VisibleOwner")) { Console.Error.WriteLine("Usage: DesktopProcessAcceptance.exe assetsRoot artRoot fixtureExe outputDir [--VisibleOwner]"); return 2; }
            string output = null, profile = null, formal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            bool outputReady = false;
            Dictionary<string, string> before = null;
            Dictionary<string, object> report = new Dictionary<string, object> { { "passed", false }, { "startedUtc", DateTime.UtcNow.ToString("o") },
                { "runnerPid", Process.GetCurrentProcess().Id }, { "scope", "Native mortal combat against an explicitly marked, uniquely owned fixture; target-specific Yes confirmation; actual graceful exit and persisted truthful outcome" },
                { "excluded", "No user program action, real cleanup/memory action, automatic action, seed, accelerated clock, injected identity/HP/kill/reward" } };
            try
            {
                string assets = Path.GetFullPath(args[0]), art = Path.GetFullPath(args[1]), fixture = Path.GetFullPath(args[2]);
                string project = Directory.GetParent(Directory.GetParent(assets).FullName).FullName, demo = Path.Combine(project, "美术Demo");
                string fixtureRoot = Path.GetFullPath(Path.Combine(demo, "verification", "process-native")) + Path.DirectorySeparatorChar;
                if (!Directory.Exists(assets) || !Directory.Exists(art) || !File.Exists(fixture)
                    || !fixture.StartsWith(fixtureRoot, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(fixture) != "DesktopProcessFixture.exe")
                    throw new ArgumentException("Only the dedicated project verification/process-native/DesktopProcessFixture.exe is accepted.");
                output = Path.GetFullPath(args[3]); string qa = Path.GetFullPath(Path.Combine(demo, "qa"));
                if (!String.Equals(Path.GetDirectoryName(output), qa, StringComparison.OrdinalIgnoreCase)
                    || !Path.GetFileName(output).StartsWith("desktop-accept-process-", StringComparison.Ordinal))
                    throw new ArgumentException("Raw output must be 美术Demo/qa/desktop-accept-process-* (gitignored).");
                if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Evidence output must be empty.");
                Directory.CreateDirectory(output);
                outputReady = true;
                profile = Path.Combine(demo, "verification", "process-native", "profile-" + Guid.NewGuid().ToString("N"));
                report["profile"] = profile; report["fixtureExecutable"] = fixture; before = Hashes(formal); report["formalHashesBefore"] = before;
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, art);
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (DesktopDemoSession session = new DesktopDemoSession(assets, profile, false, null, true))
                using (DesktopProcessAcceptance runner = new DesktopProcessAcceptance(session, fixture, output, report, args.Length == 5))
                { runner.Start(); Application.Run(session); if (!runner.finished) throw new InvalidOperationException("UI exited before acceptance finished."); }
                // Load the same history into a second private controller after
                // disposing the session and its profile lock. No second GUI run.
                if ((bool)report["passed"])
                    using (Form owner = new Form())
                    using (AdventureController reload = new AdventureController(owner, assets, false, null, false, profile, true))
                    {
                        DesktopMaintenanceEntry entry = reload.ReadDesktopPlayerView().MaintenanceResults.Single(e => e.Kind == "Process");
                        bool valid = entry.Summary.Contains("程序已实际退出") && entry.FreedBytes == 0 && entry.DeletedFiles == 0 && entry.TrimmedProcesses == 0;
                        report["historyReloadVerified"] = valid;
                        if (!valid) throw new InvalidOperationException("Reloaded process result missing or invents file/memory work.");
                    }
            }
            catch (Exception error) { report["passed"] = false; report["error"] = error.ToString(); Console.Error.WriteLine(error.Message); }
            finally
            {
                try
                {
                    Dictionary<string, string> after = Hashes(formal);
                    bool unchanged = before != null && before.Count == after.Count && before.All(p => after.ContainsKey(p.Key) && after[p.Key] == p.Value);
                    report["formalHashesAfter"] = after; report["formalArchivesUnchanged"] = unchanged;
                    if (!unchanged) { report["passed"] = false; report["archiveError"] = "Real archive set/hashes changed or initial snapshot missing."; }
                }
                catch (Exception error) { report["passed"] = false; report["archiveError"] = error.ToString(); }
                report["completedUtc"] = DateTime.UtcNow.ToString("o");
                if (outputReady) File.WriteAllText(Path.Combine(output, "desktop-process-acceptance.json"), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(true));
            }
            Console.WriteLine("Native owned-process acceptance passed=" + report["passed"]); return (bool)report["passed"] ? 0 : 1;
        }
        DesktopProcessAcceptance(DesktopDemoSession session, string fixture, string output, Dictionary<string, object> report, bool visibleOwnerDiagnostic)
        {
            this.session = session; fixturePath = fixture; this.output = output; this.report = report;
            this.visibleOwnerDiagnostic = visibleOwnerDiagnostic;
            report["visibleOwnerDiagnostic"] = visibleOwnerDiagnostic;
            report["confirmationPath"] = visibleOwnerDiagnostic ? "EXPLICIT diagnostic: tester shows existing ecology panel before final hit" : "DEFAULT production path: all feature panels closed throughout combat; session chooses native confirmation host";
            marker = Path.Combine(output, "owned-fixture-" + Guid.NewGuid().ToString("N"));
            native = new DesktopDemoAcceptance(session, output, true);
            // Only initialize the helper's diagnostic stopwatch. Never call
            // Start(), which runs an unrelated gameplay acceptance driver.
            ((Stopwatch)typeof(DesktopDemoAcceptance).GetField("wall", Private).GetValue(native)).Start();
            report["assertions"] = checks; report["captures"] = captures; report["dialogLifecycle"] = dialogLifecycle;
        }
        object Invoke(string name, params object[] args)
        {
            try { return typeof(DesktopDemoAcceptance).GetMethod(name, Private).Invoke(name == "OwnDialog" ? null : native, args); }
            catch (TargetInvocationException error) { throw error.InnerException ?? error; }
        }
        void Check(bool value, string detail)
        { checks.Add(new { passed = value, detail = detail, seconds = clock.Elapsed.TotalSeconds }); if (!value) throw new InvalidOperationException(detail); }
        void Start()
        {
            initialKills = Game.Cultivation.MortalKills;
            fixture = Process.Start(new ProcessStartInfo(fixturePath, "\"" + marker + "\" close") { UseShellExecute = false });
            report["fixturePid"] = fixture.Id; report["fixtureMarker"] = marker;
            dialogThread = new Thread(WatchDialog) { IsBackground = true, Name = "Owned process confirmation evidence" }; dialogThread.Start();
            WaitHandle stop = lifetime.Token.WaitHandle;
            new Thread(delegate()
            {
                if (stop.WaitOne(115000)) return;
                // A hung UI cannot service its WinForms timeout. This final
                // fallback stops only our child through its marker and exits
                // this runner process; it never terminates the target forcibly.
                try
                {
                    File.WriteAllText(marker + ".stop", "owned fixture hard-timeout cleanup only");
                    Dictionary<string, object> failure = new Dictionary<string, object> {
                        { "passed", false }, { "error", "Runner UI did not finish within 115 seconds; own runner exiting nonzero." },
                        { "runnerPid", Process.GetCurrentProcess().Id }, { "fixturePid", fixture.Id }, { "fixtureMarker", marker },
                        { "completedUtc", DateTime.UtcNow.ToString("o") }, { "cleanup", "marker.stop only; no forced process termination" } };
                    File.WriteAllText(Path.Combine(output, "desktop-process-acceptance.json"), new JavaScriptSerializer().Serialize(failure), new UTF8Encoding(true));
                }
                finally { Environment.Exit(1); }
            }) { IsBackground = true, Name = "Owned acceptance hard timeout" }.Start();
            timer.Tick += Tick; timer.Start();
        }
        void Tick(object sender, EventArgs args)
        {
            if (finished || busy) return; busy = true;
            try
            {
                if (dialogError != null) throw dialogError;
                if (clock.Elapsed.TotalSeconds > 100) throw new TimeoutException("Owned-process native acceptance exceeded 100 seconds.");
                if (!String.IsNullOrEmpty(Game.ArtDemoFailure)) throw new InvalidOperationException(Game.ArtDemoFailure);
                if (clock.Elapsed.TotalSeconds < next) return;
                if (phase == 0)
                {
                    if (!File.Exists(marker + ".ready")) { if (clock.Elapsed.TotalSeconds > 10) throw new TimeoutException("Owned fixture never became ready."); return; }
                    Check(!fixture.HasExited && Game.Cultivation.Mortal && Game.DesktopObservesSystem, "新凡人Observe私有档，自有夹具真实运行");
                    Check(!Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance, "文件/内存/无人值守授权全关");
                    fixture.Refresh(); IntPtr window = fixture.MainWindowHandle; uint pid; GetWindowThreadProcessId(window, out pid);
                    Check(pid == fixture.Id && Text(window).StartsWith("桌面修仙专用进程夹具 close", StringComparison.Ordinal), "夹具窗口PID和专用标题吻合");
                    SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x13); // own fixture only, no activation
                    phase = 1; next = clock.Elapsed.TotalSeconds + .3; return;
                }
                if (phase == 1)
                {
                    CaptureWindow("01-owned-fixture-alive", fixture.MainWindowHandle, fixture.Id);
                    SetWindowPos(fixture.MainWindowHandle, new IntPtr(-2), 0, 0, 0, 0, 0x13);
                    session.ClosePanels(); observation = Task.Run(() => DesktopProcessTargets.Observe()); phase = 2; return;
                }
                if (phase == 2)
                {
                    if (!observation.IsCompleted) return;
                    ProcessIdentity identity = observation.Result.SingleOrDefault(p => p.Id == fixture.Id);
                    Check(identity != null && String.Equals(identity.ExecutablePath, fixturePath, StringComparison.OrdinalIgnoreCase), "Observe返回所启动夹具的真实PID/启动时间/路径，未构造身份");
                    report["observedIdentity"] = new { id = identity.Id, startedUtcTicks = identity.StartedUtcTicks, path = identity.ExecutablePath, name = identity.Name };
                    Check(Game.DesktopAddProcessTarget(identity), "明确标记唯一自有夹具为程灵");
                    target = Game.Battle.Enemies.Single(e => e.ProcessTarget != null && e.ProcessTarget.Id == fixture.Id);
                    initialHealth = target.Health; phase = 3; next = clock.Elapsed.TotalSeconds + .8; return;
                }
                if (phase == 3)
                {
                    CaptureActors("02-real-process-actor-before-combat");
                    Check(!fixture.HasExited && !Game.DesktopProcessBusy, "战斗前程序仍运行且无退出任务");
                    phase = 4;
                }
                if (phase == 4)
                {
                    if (confirmed) { phase = 5; next = clock.Elapsed.TotalSeconds + .1; return; }
                    if (dialogInProgress) return; // suspend all further combat input immediately
                    if (Game.DesktopAcceptanceDead) throw new InvalidOperationException("凡人自然力竭，程灵战斗未完成；不注入恢复。");
                    if (target.Health <= 0 || target.Defeated || target.VisualDefeated) return; // modal watcher confirms only our dialog
                    if (visibleOwnerDiagnostic && !confirmOwnerOpened && target.Health < target.MaxHealth * .35)
                    {
                        // Exercise the session's existing visible-owner path.
                        // A no-owner MessageBox discovered during creation is
                        // not a reliable visible desktop evidence anchor.
                        session.OpenPanel("ecology"); DesktopEcologyPanel host = Application.OpenForms.OfType<DesktopEcologyPanel>().Single(f => f.Visible);
                        Rectangle area = Screen.FromRectangle(Game.DesktopHeroWindow.Bounds).WorkingArea;
                        bool heroOnLeft = Game.DesktopHeroWindow.Left + Game.DesktopHeroWindow.Width / 2 < area.Left + area.Width / 2;
                        host.Location = new Point(heroOnLeft ? area.Right - host.Width - 12 : area.Left + 12, area.Top + 12);
                        host.TopMost = true; host.BringToFront(); host.Activate(); confirmOwnerOpened = true;
                        report["confirmationOwner"] = new { handle = host.Handle.ToInt64(), bounds = host.Bounds.ToString(), kind = "existing ecology panel, tester activated before final hit; native session chooses this visible owner" };
                        next = clock.Elapsed.TotalSeconds + .4; return;
                    }
                    if (Game.DesktopAcceptanceCanBlock) { if ((bool)Invoke("ClickActor", Game.DesktopHeroWindow)) clicks++; next = clock.Elapsed.TotalSeconds + .1; return; }
                    if ((bool)Invoke("ClickEnemy", target)) clicks++;
                    if (!battleCaptured && target.Health < initialHealth)
                    { CaptureActors("03-native-process-combat"); battleCaptured = true; Check(!fixture.HasExited, "真实出招已扣血，击杀前程序仍运行"); }
                    next = clock.Elapsed.TotalSeconds + .75; return;
                }
                if (phase == 5)
                {
                    DesktopMaintenanceEntry result = Game.ReadDesktopPlayerView().MaintenanceResults.FirstOrDefault(e => e.Kind == "Process");
                    if (Game.DesktopProcessBusy || result == null) return;
                    Check(confirmed && dialogEvidence != null && battleCaptured && clicks >= 2, "真实鼠标逐次出招，并单独确认指定PID退出");
                    Check(fixture.HasExited && !File.Exists(marker + ".stop"), "夹具由已确认的正常退出链路实际退出，尚未发送测试清理marker");
                    Check(result.Summary.Contains("程序已实际退出") && !result.Summary.Contains("未核实") && result.FreedBytes == 0 && result.DeletedFiles == 0 && result.TrimmedProcesses == 0,
                        "维护结果反映实际程序退出，未伪称磁盘释放或内存回收");
                    Check(Game.Cultivation.MortalKills == initialKills + 1, "真实战斗恰计一次凡人有效击杀");
                    report["outcome"] = new { summary = result.Summary, freedBytes = result.FreedBytes, deletedFiles = result.DeletedFiles, trimmedProcesses = result.TrimmedProcesses,
                        mortalKills = Game.Cultivation.MortalKills, nativeClicks = clicks, actualFixtureExited = fixture.HasExited, cleanupMarkerExisted = File.Exists(marker + ".stop") };
                    report["confirmation"] = dialogEvidence;
                    session.OpenPanel("ecology"); DesktopEcologyPanel panel = Application.OpenForms.OfType<DesktopEcologyPanel>().Single(f => f.Visible);
                    panel.TopMost = true; panel.BringToFront(); panel.Activate(); phase = 6; next = clock.Elapsed.TotalSeconds + .4; return;
                }
                if (phase == 6)
                {
                    DesktopEcologyPanel panel = Application.OpenForms.OfType<DesktopEcologyPanel>().Single(f => f.Visible);
                    CaptureWindow("05-actual-program-exit-history", panel.Handle, Process.GetCurrentProcess().Id);
                    Check(Game.DesktopSaveNow(), "五份私有业务档保存成功"); Game.DesktopAcceptanceVerifyFiles(Game.DesktopAcceptanceRestartMarker());
                    Check(true, "五份私有业务档重新Load核验通过"); Finish(null);
                }
            }
            catch (Exception error) { Finish(error); }
            finally { busy = false; }
        }
        void WatchDialog()
        {
            try
            {
                while (!lifetime.Token.WaitHandle.WaitOne(40) && !confirmed)
                {
                    if (clock.Elapsed.TotalSeconds > 105) throw new TimeoutException("Confirmation watcher exceeded deadline.");
                    IntPtr dialog = (IntPtr)Invoke("OwnDialog", "确认本次程序退出");
                    if (dialog == IntPtr.Zero) continue;
                    dialogInProgress = true; RecordDialogState(dialog, "first-discovery");
                    List<string> labels = new List<string>(); EnumChildWindows(dialog, delegate(IntPtr child, IntPtr ignored) { labels.Add(Text(child)); return true; }, IntPtr.Zero);
                    string body = String.Join("\n", labels.ToArray()).Replace("\r", ""); uint pid; GetWindowThreadProcessId(dialog, out pid);
                    if (pid != Process.GetCurrentProcess().Id || !body.Contains("PID " + fixture.Id + "\n") || !body.Contains(fixturePath))
                        throw new InvalidOperationException("Owned confirmation did not uniquely match fixture PID/path; no click sent.");
                    if (fixture.HasExited || Game.DesktopProcessBusy) throw new InvalidOperationException("Program exited or task started before confirmation.");
                    IntPtr yes = GetDlgItem(dialog, 6); Rect rect;
                    string caption = new String(Text(yes).Where(c => c != '&' && !Char.IsWhiteSpace(c)).ToArray());
                    if (yes == IntPtr.Zero || !IsWindowVisible(yes) || !IsWindowEnabled(yes) || !(caption.StartsWith("是", StringComparison.Ordinal) || caption.StartsWith("Yes", StringComparison.OrdinalIgnoreCase)) || !GetWindowRect(yes, out rect))
                        continue;
                    CaptureDialog("04-target-specific-native-confirmation", dialog);
                    // NativeClick verifies exact root hit immediately before
                    // sending actual mouse input, with no global Return/BM_CLICK.
                    bool clicked = (bool)Invoke("NativeClick", new Point((rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2), dialog, "owned-fixture-specific-yes");
                    if (clicked)
                    {
                        dialogEvidence = new { dialog = dialog.ToInt64(), ownerPid = pid, fixturePid = fixture.Id, expectedPath = fixturePath, body = body,
                            yesControlId = 6, caption = caption, nativeMouse = true, fixtureAliveBeforeYes = true, utc = DateTime.UtcNow.ToString("o") };
                        confirmed = true;
                    }
                }
            }
            catch (Exception error)
            {
                dialogError = error;
                IntPtr own = (IntPtr)Invoke("OwnDialog", "确认本次程序退出");
                if (own != IntPtr.Zero) PostMessage(own, 0x10, IntPtr.Zero, IntPtr.Zero); // cancel only our blocked modal
            }
        }
        void CaptureDialog(string name, IntPtr dialog)
        {
            // A just-created native dialog can own hit tests before DWM has
            // actually presented its pixels. Root visual review caught exactly
            // that race in r1; ownership alone is never visual proof.
            Stopwatch wait = Stopwatch.StartNew(); List<object> attempts = new List<object>();
            report["dialogVisualAttempts"] = attempts;
            while (wait.Elapsed.TotalSeconds < 6)
            {
                if (lifetime.Token.WaitHandle.WaitOne(350)) throw new OperationCanceledException();
                uint pid; Rect r; GetWindowThreadProcessId(dialog, out pid);
                RecordDialogState(dialog, "capture-attempt");
                if (!IsWindow(dialog) || pid != Process.GetCurrentProcess().Id)
                    throw new InvalidOperationException("Owned dialog was actually destroyed or its PID changed before visual proof; inspect dialogLifecycle.");
                if (!IsWindowVisible(dialog) || !GetWindowRect(dialog, out r))
                { attempts.Add(new { waitedMilliseconds = wait.ElapsedMilliseconds, dialogVisible = false }); continue; }
                Rectangle bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                bool hit = true;
                foreach (int x in new[] { bounds.Left + 12, bounds.Left + bounds.Width / 2, bounds.Right - 12 })
                    foreach (int y in new[] { bounds.Top + 12, bounds.Top + bounds.Height / 2, bounds.Bottom - 12 })
                        hit &= GetAncestor(WindowFromPoint(new Point(x, y)), 2) == dialog;
                if (!hit) { attempts.Add(new { waitedMilliseconds = wait.ElapsedMilliseconds, allNineNativeHits = false }); continue; }
                // Request only this owned native dialog and its children to
                // paint. PrintWindow below is a verifier reference, never a
                // published replacement for desktop BitBlt pixels.
                RedrawWindow(dialog, IntPtr.Zero, IntPtr.Zero, 0x185); DwmFlush();
                using (Bitmap reference = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb))
                {
                    bool printed;
                    using (Graphics graphics = Graphics.FromImage(reference))
                    {
                        graphics.Clear(Color.Magenta); IntPtr target = graphics.GetHdc();
                        try { printed = PrintWindow(dialog, target, 2); }
                        finally { graphics.ReleaseHdc(target); }
                    }
                    if (!printed || !DesktopFrameCapture.HasVisibleDetail(reference))
                    { attempts.Add(new { waitedMilliseconds = wait.ElapsedMilliseconds, verifierReferenceReady = false }); continue; }
                    DwmFlush();
                    using (Bitmap actual = DesktopFrameCapture.CaptureRegion(bounds))
                    {
                        Rectangle body = new Rectangle(12, Math.Min(38, bounds.Height / 4), bounds.Width - 24, Math.Max(1, bounds.Height - 54));
                        PixelComparison full = ComparePixels(actual, reference, body);
                        List<PixelComparison> buttons = new List<PixelComparison>(); bool controlsValid = true;
                        foreach (int id in new[] { 6, 7 })
                        {
                            IntPtr button = GetDlgItem(dialog, id); Rect b;
                            if (button == IntPtr.Zero || !IsWindowVisible(button) || !GetWindowRect(button, out b)) { controlsValid = false; break; }
                            Rectangle local = new Rectangle(b.Left - bounds.Left, b.Top - bounds.Top, b.Right - b.Left, b.Bottom - b.Top);
                            buttons.Add(ComparePixels(actual, reference, Rectangle.Intersect(local, new Rectangle(Point.Empty, actual.Size))));
                        }
                        bool valid = full.Samples >= 500 && full.MatchRatio >= .94 && full.DarkSamples >= 100 && full.DarkMatchRatio >= .88
                            && controlsValid && buttons.Count == 2 && buttons.All(b => b.Samples >= 50 && b.MatchRatio >= .92 && b.DarkSamples >= 5 && b.DarkMatchRatio >= .80);
                        attempts.Add(new { waitedMilliseconds = wait.ElapsedMilliseconds, allNineNativeHits = true, body = full, yesNoButtons = buttons, screenPixelsVerified = valid });
                        if (!valid) continue;
                        actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                        lock (captures) captures.Add(new { image = name + ".png", bounds = bounds.ToString(), ownDialogRoot = dialog.ToInt64(),
                            nativeNinePointHitsVerified = true, screenPixelsVerified = true, attempts = attempts,
                            capture = "native desktop BitBlt SRCCOPY|CAPTUREBLT after presentation wait; PrintWindow only a non-published verifier reference" });
                        return;
                    }
                }
            }
            report["dialogVisualAttempts"] = attempts;
            throw new InvalidOperationException("Owned native dialog screen pixels failed reference/text/button proof after six seconds; no Yes click sent.");
        }
        void RecordDialogState(IntPtr dialog, string phaseName)
        {
            uint pid; Rect r; GetWindowThreadProcessId(dialog, out pid); bool bounds = GetWindowRect(dialog, out r);
            IntPtr current = (IntPtr)Invoke("OwnDialog", "确认本次程序退出");
            object snapshot = new { phase = phaseName, seconds = clock.Elapsed.TotalSeconds, handle = dialog.ToInt64(),
                exists = IsWindow(dialog), visible = IsWindowVisible(dialog), ownerPid = pid, text = Text(dialog), currentOwnDialog = current.ToInt64(),
                nativeOwner = GetWindow(dialog, 4).ToInt64(), style = GetWindowLongPtr(dialog, -16).ToInt64(),
                bounds = bounds ? Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom).ToString() : null,
                targetHealth = target == null ? -1 : target.Health, targetDefeated = target != null && target.Defeated,
                nativeBattleClicks = clicks, combatInputSuspended = dialogInProgress };
            lock (dialogLifecycle) dialogLifecycle.Add(snapshot);
        }
        sealed class PixelComparison
        {
            public int Samples, Matches, DarkSamples, DarkMatches;
            public double MatchRatio { get { return Samples == 0 ? 0 : Matches / (double)Samples; } }
            public double DarkMatchRatio { get { return DarkSamples == 0 ? 0 : DarkMatches / (double)DarkSamples; } }
        }
        static PixelComparison ComparePixels(Bitmap actual, Bitmap reference, Rectangle area)
        {
            PixelComparison result = new PixelComparison();
            // Sample every other pixel, retaining enough native text strokes
            // to reject a blank page even when most dialog background is light.
            for (int y = area.Top; y < area.Bottom; y += 2)
                for (int x = area.Left; x < area.Right; x += 2)
                {
                    Color expected = reference.GetPixel(x, y), seen = actual.GetPixel(x, y);
                    int delta = Math.Abs(expected.R - seen.R) + Math.Abs(expected.G - seen.G) + Math.Abs(expected.B - seen.B);
                    bool match = delta <= 45; result.Samples++; if (match) result.Matches++;
                    if (expected.R + expected.G + expected.B < 420)
                    { result.DarkSamples++; if (match) result.DarkMatches++; }
                }
            return result;
        }
        void CaptureWindow(string name, IntPtr window, int expectedPid)
        {
            uint pid; Rect r; GetWindowThreadProcessId(window, out pid);
            Check(pid == expectedPid && IsWindowVisible(window) && GetWindowRect(window, out r), "截图窗口归属明确且可见：" + name);
            GetWindowRect(window, out r); Rectangle bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            foreach (int x in new[] { bounds.Left + 15, bounds.Left + bounds.Width / 2, bounds.Right - 15 })
                foreach (int y in new[] { bounds.Top + 40, bounds.Top + bounds.Height / 2, bounds.Bottom - 15 })
                    Check(GetAncestor(WindowFromPoint(new Point(x, y)), 2) == window, "原生窗口命中验证：" + name + " " + x + "," + y);
            using (Bitmap bitmap = DesktopFrameCapture.CaptureRegion(bounds)) bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            captures.Add(new { image = name + ".png", bounds = bounds.ToString(), ownerPid = pid, nativeHitVerified = true, capture = "native BitBlt SRCCOPY|CAPTUREBLT" });
        }
        void CaptureActors(string name)
        {
            bool running = Game.PauseArtDemoCapture(); List<ArtDemoActorFrame> actors = null;
            Bitmap actual = null, background = null, expected = null;
            try
            {
                actors = Game.SnapshotArtDemoActors(); ArtDemoActorFrame hero = actors.Single(a => a.Role == "hero");
                KnightLayeredWindow targetWindow = Game.DesktopEnemyWindows[target.Id];
                ArtDemoActorFrame enemy = actors.Single(a => a.Role == "enemy" && a.Window == targetWindow);
                Check(target.ProcessTarget != null && target.ProcessTarget.Id == fixture.Id && !target.IsVirtual, "像素校验绑定唯一真实夹具程灵");
                Rectangle bounds = Rectangle.Union(hero.Bounds, enemy.Bounds); bounds.Inflate(24, 24); bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
                actual = DesktopFrameCapture.CaptureRegion(bounds); actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                try { foreach (ArtDemoActorFrame actor in actors) actor.Window.Hide(); background = DesktopFrameCapture.CaptureRegion(bounds, false); }
                finally { foreach (ArtDemoActorFrame actor in actors.OrderByDescending(a => a.ZOrder)) if (!actor.Window.IsDisposed) actor.Window.Show(); }
                expected = ArtDemoPixelVerifier.ExpectedComposite(background, actors, bounds.Location);
                captures.Add(new { image = name + ".png", bounds = bounds.ToString(), processTarget = fixture.Id, actualHealth = target.Health, maxHealth = target.MaxHealth,
                    pixels = new[] { ArtDemoPixelVerifier.VerifyActor(hero, actors, actual, background, expected, bounds.Location), ArtDemoPixelVerifier.VerifyActor(enemy, actors, actual, background, expected, bounds.Location) },
                    capture = "native BitBlt; exact presented hero and specified process-monster alpha pixel validation; expected composite verifier only" });
            }
            finally
            {
                if (actual != null) actual.Dispose(); if (background != null) background.Dispose(); if (expected != null) expected.Dispose();
                if (actors != null) foreach (ArtDemoActorFrame actor in actors) actor.Dispose(); Game.ResumeArtDemoCapture(running);
            }
        }
        void Finish(Exception error)
        {
            if (finished) return; finished = true; timer.Stop(); lifetime.Cancel();
            report["passed"] = error == null; report["elapsedSeconds"] = clock.Elapsed.TotalSeconds;
            if (error != null) report["error"] = error.ToString();
            session.ExitAfterAcceptance(error == null ? 0 : 1);
        }
        static string Text(IntPtr window) { StringBuilder value = new StringBuilder(8192); GetWindowText(window, value, value.Capacity); return value.ToString(); }
        static Dictionary<string, string> Hashes(string root)
        {
            Dictionary<string, string> hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(root)) foreach (string file in Directory.GetFiles(root, "*", SearchOption.TopDirectoryOnly))
                using (SHA256 sha = SHA256.Create()) hashes[Path.GetFileName(file)] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "");
            return hashes;
        }
        public void Dispose()
        {
            timer.Stop(); timer.Dispose(); lifetime.Cancel(); if (dialogThread != null) dialogThread.Join(1500);
            // The cleanup channel is the fixture's stop marker, never Kill,
            // window-close automation on another app, or process-name matching.
            if (fixture != null)
            {
                if (!fixture.HasExited)
                {
                    File.WriteAllText(marker + ".stop", "owned fixture cleanup only"); bool stopped = fixture.WaitForExit(4000); report["fixtureCleanupStopped"] = stopped;
                    if (!stopped) { report["passed"] = false; report["fixtureCleanupError"] = "Dedicated fixture did not honor stop marker; no force close attempted, PID " + fixture.Id; }
                }
                report["fixtureExitedAfterCleanup"] = fixture.HasExited; fixture.Dispose();
            }
            native.Dispose(); lifetime.Dispose();
        }
    }
}
