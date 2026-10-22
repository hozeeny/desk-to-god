using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
    // Standalone observer/first-ritual driver. No fixtures, clock stepping,
    // combat injection, system observation or maintenance are used here.
    internal sealed class DesktopNaturalSoak : IDisposable
    {
        const double MinimumSeconds = 8 * 3600;
        readonly DesktopDemoSession session;
        readonly DesktopDemoAcceptance input;
        readonly string output;
        readonly string assetsRoot, artRoot;
        readonly double requestedSeconds;
        readonly Stopwatch wall = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        readonly Dictionary<string, string> realBefore;
        readonly List<object> events = new List<object>(), captureErrors = new List<object>();
        readonly HashSet<string> verifiedCaptures = new HashSet<string>();
        readonly Dictionary<string, double> captureRetryAfter = new Dictionary<string, double>();
        readonly StreamWriter csv;
        readonly object initial;
        object lastCheckpointState;
        string lastCheckpointUtc;
        readonly DateTime startedUtc = DateTime.UtcNow;
        bool busy, finished, naturalZhuji, harvested, reloaded, saved, archivesUnchanged;
        int phase, plantedSlot = -1, samples, exitCode = 1;
        string plantedRaw, seedId, failure, waiting = "自然修炼至炼气圆满并获得本境契机";
        long readyUtcTicks;
        Dictionary<string, int> beforeHarvest;
        double nextSample, nextSave = 60, nextCapture, previousCpu, previousSample;
        double sampledSeconds, maximumSampleGap, nextBreakthroughAttempt;
        AdventureController Game { get { return session.Game; } }

        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process, int flag);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        internal static int Run(string[] args)
        {
            if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
            string destination = null;
            bool ownsOutput = false;
            try
            {
                if (args.Length < 4 || args.Length > 5) throw new ArgumentException("Usage: DesktopNaturalSoak assetsRoot artRoot privateProfile outputRoot [hours>=8]");
                double hours = args.Length == 5 ? Double.Parse(args[4], CultureInfo.InvariantCulture) : 8;
                if (Double.IsNaN(hours) || Double.IsInfinity(hours) || hours < 8 || hours > 168) throw new ArgumentException("hours 必须在8至168之间；短时运行不能伪称8小时长测。");
                string assets = Path.GetFullPath(args[0]), art = Path.GetFullPath(args[1]);
                string profile = DesktopDemoProfiles.ValidateRoot(Path.GetFullPath(args[2]));
                if (!Directory.Exists(assets) || !Directory.Exists(art)) throw new DirectoryNotFoundException("素材目录不存在。");
                if (!File.Exists(Path.Combine(profile, "cultivation-v2.txt"))) throw new ArgumentException("必须传入正常×1实测自然形成的既有炼气档；本工具不创建或播种测试档。");
                CultivationProgress existing = CultivationProgress.Load(Path.Combine(profile, "cultivation-v2.txt"));
                if (existing.Mortal || existing.RealmIndex != 0 || !existing.AutoBattle) throw new ArgumentException("长测起点必须是自动战斗已开启的炼气修士，沿用同一自然档。");
                destination = Path.GetFullPath(args[3]);
                if (destination.Equals(profile, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("证据目录不得位于测试档内。");
                if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("证据目录必须为空，不覆盖既有证据。");
                Directory.CreateDirectory(destination);
                ownsOutput = true;
                Dictionary<string, string> original = HashRealArchives();
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, art);
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (DesktopDemoSession host = new DesktopDemoSession(assets, profile, true, null, false))
                using (DesktopNaturalSoak soak = new DesktopNaturalSoak(host, destination, hours * 3600, original, assets, art))
                {
                    soak.Start(); Application.Run(host);
                    if (!soak.finished) soak.Finish("消息循环在长测完成前返回，拒绝通过。");
                    return soak.exitCode;
                }
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                if (ownsOutput && destination != null && Directory.Exists(destination) && !File.Exists(Path.Combine(destination, "natural-soak.json")))
                    File.WriteAllText(Path.Combine(destination, "natural-soak-start-error.json"), new JavaScriptSerializer().Serialize(new { passed = false, error = error.ToString() }), new UTF8Encoding(true));
                return 1;
            }
        }

        DesktopNaturalSoak(DesktopDemoSession host, string destination, double seconds, Dictionary<string, string> original, string assets, string art)
        {
            session = host; output = destination; requestedSeconds = seconds; realBefore = original;
            assetsRoot = assets; artRoot = art;
            // Deliberately never call Start(): it includes a separate, accelerated
            // acceptance state machine. Reuse only its audited input/capture methods.
            input = new DesktopDemoAcceptance(host, output, true);
            ((Stopwatch)Field("wall").GetValue(input)).Start();
            initial = State(Game.Cultivation);
            lastCheckpointState = initial;
            csv = new StreamWriter(Path.Combine(output, "natural-performance.csv"), false, new UTF8Encoding(true));
            csv.AutoFlush = true;
            csv.WriteLine("utc,elapsedSeconds,cpuOneCorePercent,cpuMachinePercent,privateMiB,workingSetMiB,handles,gdi,user,realmIndex,layer,qi,lifetimeQi,score,phase");
            previousCpu = Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
        }
        void Start()
        {
            if (!Game.IsolatedDesktop || Game.DesktopObservesSystem || !Game.DesktopAcceptanceMaintenanceOff()) throw new InvalidOperationException("纯虚拟隔离及维护关闭检查失败。");
            // Explicitly assert x1 through the existing setter; no speed-up is used.
            Game.DesktopSetAcceptanceSpeed(1); session.ClosePanels();
            Log("START 正常×1自然延续既有炼气档；10秒采样，60秒保存；仅推进至首次自然筑基、首次灵谷播种及收获；破境未成按正式资格等待60秒后重试。");
            timer.Interval = 1000; timer.Tick += Tick; timer.Start();
            Application.ApplicationExit += OnExit;
            Checkpoint(false);
        }
        void Tick(object sender, EventArgs args)
        {
            if (busy || finished) return; busy = true;
            try
            {
                double now = wall.Elapsed.TotalSeconds;
                if (!String.IsNullOrEmpty(Game.ArtDemoFailure)) throw new InvalidOperationException(Game.ArtDemoFailure);
                if (!Game.DesktopAcceptanceMaintenanceOff() || Game.DesktopObservesSystem) throw new InvalidOperationException("运行期间纯虚拟/维护关闭约束失效。");
                if (Game.DesktopHeroWindow.IsDisposed || !Game.DesktopHeroWindow.Visible) throw new InvalidOperationException("真实透明角色窗口不可见。");
                if (now >= nextSample) { Sample(now); nextSample = now + 10; }
                if (now >= nextSave) { if (!Game.DesktopSaveNow()) throw new IOException("周期保存失败：" + Game.DesktopNotice); nextSave = now + 60; Checkpoint(false); }
                if (now >= nextCapture && Game.Cultivation.RealmIndex == 0) { Capture("01-natural-qishi-start", true); nextCapture = now + 60; }
                if (now >= requestedSeconds) { Finish(null); return; }
                Advance();
            }
            catch (Exception error) { Finish(Unwrap(error).ToString()); }
            finally { busy = false; }
        }
        void Advance()
        {
            if (phase == 0)
            {
                if (!verifiedCaptures.Contains("01-natural-qishi-start")) return;
                if (!MayAttemptFirstBreakthrough(Game.Cultivation.RealmIndex, Game.Cultivation.CanAttemptBreakthrough, naturalZhuji, wall.Elapsed.TotalSeconds, nextBreakthroughAttempt)) return;
                Log("自然圆满且获得破境契机：" + json.Serialize(State(Game.Cultivation)));
                session.OpenPanel("breakthrough"); phase = 1; waiting = "等待自己破境面板的真实鼠标操作";
            }
            else if (phase == 1)
            {
                if (Game.Cultivation.RealmIndex != 0 || naturalZhuji) throw new InvalidOperationException("破境前已离开炼气，拒绝继续尝试更高境界。");
                if (Click("发起破境"))
                {
                    phase = 2;
                    Log("发起本档第" + (Game.Cultivation.BreakthroughAttempts + 1) + "次破境，真实鼠标点击已发送；当前感悟=" + Game.Cultivation.BreakthroughInsight + "; 无材料概率=" + Game.Cultivation.BreakthroughPercent(false, false) + "%；仅尝试炼气至筑基。");
                }
            }
            else if (phase == 2)
            {
                if (Game.Cultivation.RealmIndex != 0 || naturalZhuji) throw new InvalidOperationException("确认仪式前已离开炼气，拒绝继续尝试更高境界。");
                if (Confirm("破境仪式")) { phase = 3; waiting = "等待真实炼气至筑基破境结果"; }
            }
            else if (phase == 3)
            {
                if (Dialog("破境未成") != IntPtr.Zero)
                {
                    if (Confirm("破境未成"))
                    {
                        if (Game.Cultivation.RealmIndex != 0) throw new InvalidOperationException("破境失败提示与当前境界不符。");
                        nextBreakthroughAttempt = wall.Elapsed.TotalSeconds + 60;
                        Log("自然破境未成；本档已尝试=" + Game.Cultivation.BreakthroughAttempts + "; 当前感悟=" + Game.Cultivation.BreakthroughInsight + "; 下次无材料概率=" + Game.Cultivation.BreakthroughPercent(false, false) + "%；保留正式规则，60秒后重新检查破境资格。");
                        session.ClosePanels(); phase = 0; waiting = "正常修炼并等待60秒后，按正式资格再次尝试炼气至筑基";
                    }
                    return;
                }
                if (!Confirm("破境成功")) return;
                if (Game.Cultivation.RealmIndex != 1 || Game.ArtDemoHeroStage != "zhuji") throw new InvalidOperationException("确认成功后业务境界/真实外观未变为筑基。");
                naturalZhuji = true; Log("自然筑基成功；同一身份：" + json.Serialize(State(Game.Cultivation)));
                session.ClosePanels(); Capture("02-natural-zhuji", false);
                session.OpenPanel("dongtian"); phase = 4; waiting = "等待首次洞天灵谷播种";
            }
            else if (phase == 4)
            {
                if (!Game.DongtianUnlocked) throw new InvalidOperationException("自然筑基后洞天未解锁。");
                if (Game.Dongtian.Fields.Any(f => !String.IsNullOrEmpty(f)) || Game.Dongtian.Bag.Count != 0) throw new InvalidOperationException("首次播种前洞天已有作物或库存，无法证明首次自然收益。");
                if (Click("种灵谷")) phase = 5;
            }
            else if (phase == 5)
            {
                if (plantedSlot < 0)
                {
                    plantedSlot = Array.FindIndex(Game.Dongtian.Fields, f => !String.IsNullOrEmpty(f));
                    if (plantedSlot < 0) throw new InvalidOperationException("真实播种点击后未产生作物。");
                    plantedRaw = Game.Dongtian.Fields[plantedSlot];
                    if (!ParseSlot(plantedRaw, out seedId, out readyUtcTicks) || seedId != "linggu" || readyUtcTicks <= DateTime.UtcNow.Ticks) throw new InvalidOperationException("播种后的正式UTC成熟记录异常。");
                    Log("真实播种 slot=" + plantedSlot + "; raw=" + plantedRaw + "; readyUtc=" + new DateTime(readyUtcTicks, DateTimeKind.Utc).ToString("o"));
                }
                Capture("03-natural-plant", false);
                if (verifiedCaptures.Contains("03-natural-plant")) { session.ClosePanels(); phase = 6; waiting = "按真实UTC等待灵谷自然成熟"; }
            }
            else if (phase == 6)
            {
                if (Game.Dongtian.Fields[plantedSlot] != plantedRaw) throw new InvalidOperationException("成熟前作物记录被改动。");
                if (DateTime.UtcNow.Ticks < readyUtcTicks || !Game.Dongtian.FieldReady(plantedSlot, DateTime.UtcNow)) return;
                session.OpenPanel("dongtian"); phase = 7; waiting = "等待成熟灵谷的真实收获点击";
            }
            else if (phase == 7)
            {
                beforeHarvest = new Dictionary<string, int>(Game.Dongtian.Bag);
                if (Click("收获田" + (plantedSlot + 1))) { phase = 8; Log("自然UTC成熟后发送首次真实收获点击。"); }
            }
            else if (phase == 8)
            {
                int before; beforeHarvest.TryGetValue(seedId, out before);
                if (DateTime.UtcNow.Ticks < readyUtcTicks || Game.Dongtian.Fields[plantedSlot] != null || Game.Dongtian.CountOf(seedId) <= before) throw new InvalidOperationException("首次收获后未观察到自然成熟作物清空及真实Bag增长。");
                if (!harvested) { harvested = true; Log("首次洞天实际收益 " + seedId + ": " + before + " -> " + Game.Dongtian.CountOf(seedId)); }
                Capture("04-natural-first-harvest", false);
                if (verifiedCaptures.Contains("04-natural-first-harvest")) { session.ClosePanels(); phase = 9; waiting = "首次自然筑基和洞天收益已发生，继续正常×1采样至规定时长"; }
            }
            if (naturalZhuji) Capture("02-natural-zhuji", false);
            if (harvested) Capture("04-natural-first-harvest", false);
        }
        bool Click(string text)
        {
            Button button = Invoke("FindButton", text) as Button;
            return button != null && button.Enabled && SafeInput("ClickControl", button);
        }
        IntPtr Dialog(string title) { return (IntPtr)Invoke("OwnDialog", title); }
        bool Confirm(string title) { IntPtr dialog = Dialog(title); return dialog != IntPtr.Zero && SafeInput("ClickDialogOk", dialog); }
        bool SafeInput(string method, object target)
        {
            try { return (bool)Invoke(method, target); }
            catch (Exception error)
            {
                // The shared NativeClick refuses all foreign/occluded roots.
                // Its short acceptance timeout becomes a documented long-soak wait.
                if (!(Unwrap(error) is TimeoutException)) throw;
                Log("WAIT 安全点击被拒绝，等待自己窗口可见：" + Unwrap(error).Message);
                ((IDictionary)Field("blockedInputSince").GetValue(input)).Clear();
                ((IDictionary)Field("dialogWaitSince").GetValue(input)).Clear();
                return false;
            }
        }
        void Capture(string name, bool requireEnemy)
        {
            if (verifiedCaptures.Contains(name)) return;
            double after;
            if (captureRetryAfter.TryGetValue(name, out after) && wall.Elapsed.TotalSeconds < after) return;
            captureRetryAfter[name] = wall.Elapsed.TotalSeconds + 60;
            try { Invoke("Capture", name, requireEnemy, false, false); verifiedCaptures.Add(name); }
            catch (Exception error) { captureErrors.Add(new { utc = DateTime.UtcNow.ToString("o"), name = name, error = Unwrap(error).Message }); Log("CAPTURE 未通过像素验证 " + name + ": " + Unwrap(error).Message); }
        }
        void Sample(double now)
        {
            using (Process process = Process.GetCurrentProcess())
            {
                process.Refresh(); double cpu = process.TotalProcessorTime.TotalSeconds;
                double gap = now - previousSample;
                maximumSampleGap = Math.Max(maximumSampleGap, gap);
                if (gap <= 30) sampledSeconds += gap;
                double oneCore = 100 * (cpu - previousCpu) / Math.Max(.001, gap);
                object[] values = { DateTime.UtcNow.ToString("o"), now, oneCore, oneCore / Environment.ProcessorCount, process.PrivateMemorySize64 / 1048576.0, process.WorkingSet64 / 1048576.0, process.HandleCount, GetGuiResources(process.Handle, 0), GetGuiResources(process.Handle, 1), Game.Cultivation.RealmIndex, Game.Cultivation.Layer, Game.Cultivation.Qi, Game.Cultivation.LifetimeQi, Game.Battle.Progress.Score, phase };
                csv.WriteLine(String.Join(",", values.Select(v => Convert.ToString(v, CultureInfo.InvariantCulture)).ToArray()));
                previousCpu = cpu; previousSample = now; samples++;
            }
        }
        void Finish(string error)
        {
            if (finished) return; finished = true; timer.Stop(); failure = PreserveFailure(failure, error);
            // ApplicationContext.Dispose can clear session.Game before the
            // global ApplicationExit event. Persist the original reason first.
            TryFinalizationLog("FINISH " + (failure ?? "规定时长已到，开始核实最终状态"));
            try
            {
                if (Game == null) throw new InvalidOperationException("程序退出时游戏状态已释放，无法重新核实最终保存、Load或桌面画面。");
                Sample(wall.Elapsed.TotalSeconds);
                saved = Game.DesktopSaveNow();
                if (!saved) throw new IOException("最终保存失败：" + Game.DesktopNotice);
                object live = State(Game.Cultivation);
                object disk = State(CultivationProgress.Load(Game.CultivationWritePath));
                DongtianProgress loaded = DongtianProgress.Load(Game.DongtianWritePath);
                reloaded = json.Serialize(live) == json.Serialize(disk)
                    && SameArchiveState(Game.Cultivation, CultivationProgress.Load(Game.CultivationWritePath))
                    && SameArchiveState(Game.Battle.Progress, AdventureProgress.Load(Game.DesktopArchivePaths[0]))
                    && SameArchiveState(Game.Baiye, BaiyeProgress.Load(Game.BaiyeWritePath))
                    && SameArchiveState(Game.Lingyu, LingyuProgress.Load(Game.LingyuWritePath))
                    && SameArchiveState(Game.Dongtian, loaded)
                    && Game.Dongtian.Fields.SequenceEqual(loaded.Fields)
                    && Game.Dongtian.Bag.Count == loaded.Bag.Count && Game.Dongtian.Bag.All(p => loaded.CountOf(p.Key) == p.Value);
                if (!reloaded) throw new IOException("生产Load回读与内存修仙/洞天状态不一致。");
                Capture("05-natural-soak-end", true);
            }
            catch (Exception finalizeError) { failure = (failure ?? "") + "\n" + Unwrap(finalizeError); }
            try
            {
                if (Game == null) WriteTerminalFailure("程序提前退出；游戏状态已释放，长测未完成。");
                else Checkpoint(true);
            }
            catch (Exception reportError)
            {
                exitCode = 1; TryFinalizationLog("FINALIZE " + reportError);
                WriteTerminalFailure("最终验收记录未能完整生成：" + reportError);
            }
            finally
            {
                Application.ApplicationExit -= OnExit;
                if (session.Game != null) session.ExitAfterAcceptance(exitCode);
            }
        }
        void Checkpoint(bool final)
        {
            Dictionary<string, string> after = HashRealArchives();
            archivesUnchanged = realBefore.Count == after.Count && realBefore.All(p => after.ContainsKey(p.Key) && after[p.Key] == p.Value);
            bool evidence = new[] { "01-natural-qishi-start", "02-natural-zhuji", "03-natural-plant", "04-natural-first-harvest", "05-natural-soak-end" }.All(verifiedCaptures.Contains);
            bool samplingComplete = SamplingComplete(sampledSeconds, requestedSeconds, maximumSampleGap);
            bool passed = final && samplingComplete && Eligible(wall.Elapsed.TotalSeconds, requestedSeconds, naturalZhuji, harvested, saved, reloaded, archivesUnchanged, evidence, failure);
            var reasons = new List<string>();
            if (wall.Elapsed.TotalSeconds < Math.Max(MinimumSeconds, requestedSeconds)) reasons.Add("采样时长不足要求");
            if (final && !samplingComplete) reasons.Add("有效采样覆盖不足或存在超过30秒断档；有效秒数=" + sampledSeconds.ToString("F2", CultureInfo.InvariantCulture) + "; 最大间隔=" + maximumSampleGap.ToString("F2", CultureInfo.InvariantCulture));
            if (!naturalZhuji) reasons.Add("尚未自然筑基：" + waiting);
            if (!harvested) reasons.Add("尚无首次洞天实际收获：" + waiting);
            if (final && !saved) reasons.Add("最终保存未通过");
            if (final && !reloaded) reasons.Add("最终Load回读未通过");
            if (!archivesUnchanged) reasons.Add("真实档案hash发生变化");
            if (final && !evidence) reasons.Add("原生截图或像素验证元数据不足；空白或遮挡不算通过");
            if (!String.IsNullOrEmpty(failure)) reasons.Add(failure);
            object current = State(Game.Cultivation);
            string recordedUtc = DateTime.UtcNow.ToString("o");
            object report = new { passed = passed, running = !final, processId = Process.GetCurrentProcess().Id, startedUtc = startedUtc.ToString("o"), updatedUtc = recordedUtc, profile = session.ProfileRoot, output = output, assetsRoot = assetsRoot, artRoot = artRoot, executable = Application.ExecutablePath, requestedSeconds = requestedSeconds, elapsedSeconds = wall.Elapsed.TotalSeconds, effectiveSampledSeconds = sampledSeconds, maximumSampleGapSeconds = maximumSampleGap, maximumAllowedSampleGapSeconds = 30, speed = 1, sampleIntervalSeconds = 10, samples = samples, phase = phase, waiting = waiting, reasons = reasons.ToArray(), initial = initial, current = current, naturalZhuji = naturalZhuji, firstHarvest = harvested, plantedSlot = plantedSlot, plantedRecord = plantedRaw, readyUtcTicks = readyUtcTicks, bag = Game.Dongtian.Bag, beforeHarvest = beforeHarvest, finalSave = saved, loadVerified = reloaded, loadVerificationScope = "same-process production Load of all five archives", originalRealArchivesUnchanged = archivesUnchanged, realArchivesBefore = realBefore, realArchivesAfter = after, captures = Field("captures").GetValue(input), captureFailures = captureErrors.ToArray(), nativeMouseClicks = Field("clicks").GetValue(input), inputLog = Field("log").GetValue(input).ToString(), events = events.ToArray(), restrictions = "formal business only; x1; seed=null; observeSystem=false; no HP/kill/qi/time/archive injection; no other-process control" };
            WriteReport(report);
            lastCheckpointState = current; lastCheckpointUtc = recordedUtc;
            exitCode = passed ? 0 : 1;
        }
        void WriteTerminalFailure(string reason)
        {
            // No game/window access here: this path also runs after disposal.
            exitCode = 1; failure = PreserveFailure(failure, reason);
            Dictionary<string, string> after = null;
            try { after = HashRealArchives(); }
            catch (Exception error) { failure = PreserveFailure(failure, "真实档案哈希未能复核：" + error.Message); }
            bool unchanged = after != null && realBefore.Count == after.Count && realBefore.All(p => after.ContainsKey(p.Key) && after[p.Key] == p.Value);
            var report = new {
                passed = false, running = false, processId = Process.GetCurrentProcess().Id,
                startedUtc = startedUtc.ToString("o"), updatedUtc = DateTime.UtcNow.ToString("o"),
                profile = session.ProfileRoot, output = output, assetsRoot = assetsRoot, artRoot = artRoot,
                executable = Application.ExecutablePath, requestedSeconds = requestedSeconds,
                elapsedSeconds = wall.Elapsed.TotalSeconds, effectiveSampledSeconds = sampledSeconds,
                maximumSampleGapSeconds = maximumSampleGap, maximumAllowedSampleGapSeconds = 30,
                speed = 1, sampleIntervalSeconds = 10, samples = samples, phase = phase, waiting = waiting,
                error = failure, reasons = new[] { failure }, initial = initial, current = lastCheckpointState,
                currentIsLastCheckpoint = true, lastCurrentCheckpointUtc = lastCheckpointUtc,
                naturalZhuji = naturalZhuji, firstHarvest = harvested, finalSave = false, loadVerified = false,
                loadVerificationScope = "not verified after early exit; current is the last successful checkpoint only",
                originalRealArchivesUnchanged = unchanged, realArchivesBefore = realBefore, realArchivesAfter = after,
                captures = Field("captures").GetValue(input), captureFailures = captureErrors.ToArray(),
                events = events.ToArray(), restrictions = "x1; private Virtual profile; no injected growth or maintenance"
            };
            try { WriteReport(report); }
            catch (Exception reportError)
            {
                // A reader may hold the canonical checkpoint against replacement.
                // Keep it as historical evidence and write an independent terminal
                // record; readers must prefer this file over the old checkpoint.
                string terminalError = PreserveFailure(failure, "主报告未能替换：" + reportError);
                File.WriteAllText(Path.Combine(output, "natural-soak-terminal-failure.json"), json.Serialize(new {
                    passed = false, running = false, processId = Process.GetCurrentProcess().Id,
                    startedUtc = startedUtc.ToString("o"), updatedUtc = DateTime.UtcNow.ToString("o"),
                    profile = session.ProfileRoot, output = output, error = terminalError,
                    reasons = new[] { terminalError }, finalSave = false, loadVerified = false,
                    canonicalCheckpointIsHistorical = true, current = lastCheckpointState,
                    currentIsLastCheckpoint = true, lastCurrentCheckpointUtc = lastCheckpointUtc,
                    elapsedSeconds = wall.Elapsed.TotalSeconds, effectiveSampledSeconds = sampledSeconds,
                    samples = samples, terminalDetails = report
                }), new UTF8Encoding(true));
            }
        }
        void WriteReport(object report)
        {
            string temporary = Path.Combine(output, "natural-soak.json.tmp"), reportPath = Path.Combine(output, "natural-soak.json");
            File.WriteAllText(temporary, json.Serialize(report), new UTF8Encoding(true));
            if (File.Exists(reportPath)) File.Replace(temporary, reportPath, null); else File.Move(temporary, reportPath);
        }
        static bool Eligible(double elapsed, double requested, bool zhuji, bool harvest, bool save, bool load, bool unchanged, bool evidence, string error)
        { return elapsed >= MinimumSeconds && elapsed >= requested && zhuji && harvest && save && load && unchanged && evidence && String.IsNullOrEmpty(error); }
        static bool SamplingComplete(double effectiveSeconds, double requested, double maximumGap)
        { return effectiveSeconds >= Math.Max(MinimumSeconds, requested) && maximumGap <= 30; }
        static bool MayAttemptFirstBreakthrough(int realmIndex, bool canAttempt, bool alreadyReached, double now, double nextAttempt)
        { return realmIndex == 0 && canAttempt && !alreadyReached && now >= nextAttempt; }
        static string PreserveFailure(string existing, string incoming)
        { return String.IsNullOrEmpty(incoming) ? existing : String.IsNullOrEmpty(existing) ? incoming : existing + Environment.NewLine + incoming; }
        static int SelfTest()
        {
            bool ok = Eligible(28800, 28800, true, true, true, true, true, true, null)
                && !Eligible(28799, 28800, true, true, true, true, true, true, null)
                && !Eligible(28800, 32400, true, true, true, true, true, true, null);
            for (int i = 0; i < 6; i++) { bool[] flags = { true, true, true, true, true, true }; flags[i] = false; ok &= !Eligible(28800, 28800, flags[0], flags[1], flags[2], flags[3], flags[4], flags[5], null); }
            ok &= !Eligible(28800, 28800, true, true, true, true, true, true, "blocked");
            string seed; long ticks; ok &= ParseSlot("linggu,12345", out seed, out ticks) && seed == "linggu" && ticks == 12345;
            ok &= !ParseSlot("linggu,no-clock", out seed, out ticks);
            ok &= SamplingComplete(28800, 28800, 10) && !SamplingComplete(28799, 28800, 10) && !SamplingComplete(28800, 28800, 31);
            ok &= RetryRulesSelfTest();
            Console.WriteLine(ok ? "PASS: duration/continuous sampling coverage, natural milestones, archive/save/load/evidence gates, UTC slot parsing and natural retry rules; no GUI opened." : "FAIL: self-test"); return ok ? 0 : 1;
        }
        static bool RetryRulesSelfTest()
        {
            MethodInfo retry = typeof(DesktopNaturalSoak).GetMethod("MayAttemptFirstBreakthrough", BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo merge = typeof(DesktopNaturalSoak).GetMethod("PreserveFailure", BindingFlags.Static | BindingFlags.NonPublic);
            if (retry == null || merge == null) { Console.WriteLine("FAIL: failed natural breakthrough must resume through a 60-second, first-realm-only retry gate and preserve fatal errors."); return false; }
            var progress = new CultivationProgress { Mortal = false, RealmIndex = 0, Layer = 12 };
            progress.GrantOpportunity("realm");
            int failedSeed = Enumerable.Range(0, 128).First(s => new Random(s).Next(100) >= progress.BreakthroughPercent(false, false));
            bool failed = !progress.TryBreakthrough(new Random(failedSeed), false);
            Func<int, bool, bool, double, double, bool> can = (realm, allowed, reached, now, next) => (bool)retry.Invoke(null, new object[] { realm, allowed, reached, now, next });
            bool okay = failed && progress.CanAttemptBreakthrough && progress.BreakthroughAttempts == 1
                && progress.BreakthroughInsight == progress.RealmDef.FailureInsight
                && !can(0, true, false, 159.99, 160) && can(0, true, false, 160, 160)
                && !can(0, false, false, 160, 160) && !can(1, true, false, 160, 160)
                && !can(0, true, true, 160, 160);
            int successSeed = Enumerable.Range(0, 128).First(s => new Random(s).Next(100) < progress.BreakthroughPercent(false, false));
            okay &= progress.TryBreakthrough(new Random(successSeed), false) && progress.RealmIndex == 1
                && !can(progress.RealmIndex, true, true, 221, 220);
            okay &= (string)merge.Invoke(null, new object[] { "earlier fatal", null }) == "earlier fatal"
                && (string)merge.Invoke(null, new object[] { null, "later fatal" }) == "later fatal"
                && ((string)merge.Invoke(null, new object[] { "earlier fatal", "later fatal" })).Contains("earlier fatal");
            Console.WriteLine(okay ? "PASS: formal failed attempt keeps opportunity/insight; timed retry stops at first foundation establishment; fatal outcome cannot be cleared." : "FAIL: natural retry or fatal outcome rules");
            return okay;
        }
        static bool ParseSlot(string raw, out string seed, out long ticks)
        { string[] parts = (raw ?? "").Split(','); seed = parts[0]; ticks = 0; return parts.Length == 2 && seed.Length > 0 && Int64.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) && ticks > 0; }
        static object State(CultivationProgress p)
        { return new { mortal = p.Mortal, realmIndex = p.RealmIndex, layer = p.Layer, qi = p.Qi, lifetimeQi = p.LifetimeQi, fractionalQi = p.FractionalQi, root = p.SpiritRoot, daoTitle = p.DaoTitle, daoPath = p.DaoPath, technique = p.TechniqueId, autoBattle = p.AutoBattle, insight = p.BreakthroughInsight, attempts = p.BreakthroughAttempts, realmComplete = p.RealmComplete, canAttemptBreakthrough = p.CanAttemptBreakthrough }; }
        static Dictionary<string, string> HashRealArchives()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (string name in new[] { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt", "maintenance-consent.txt", "maintenance-consent.txt.revoked" })
                foreach (string suffix in new[] { "", ".bak", ".v54-backup" })
                {
                    string path = Path.Combine(root, name + suffix);
                    using (SHA256 sha = SHA256.Create()) result[name + suffix] = File.Exists(path) ? BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "") : "missing";
                }
            return result;
        }
        static FieldInfo Field(string name) { return typeof(DesktopDemoAcceptance).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); }
        bool SameArchiveState(object left, object right) { return json.Serialize(ArchiveState(left)) == json.Serialize(ArchiveState(right)); }
        static object ArchiveState(object value)
        {
            if (value == null || value is string || value.GetType().IsPrimitive || value is decimal) return value;
            IDictionary map = value as IDictionary;
            if (map != null) { var result = new SortedDictionary<string, object>(StringComparer.Ordinal); foreach (DictionaryEntry pair in map) result[Convert.ToString(pair.Key, CultureInfo.InvariantCulture)] = ArchiveState(pair.Value); return result; }
            IEnumerable list = value as IEnumerable;
            if (list != null) return list.Cast<object>().Select(ArchiveState).ToArray();
            var fields = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.Name != "LastNotice") fields[field.Name] = ArchiveState(field.GetValue(value));
            return fields;
        }
        object Invoke(string name, params object[] args) { return typeof(DesktopDemoAcceptance).GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic).Invoke(input, args); }
        static Exception Unwrap(Exception error) { return error is TargetInvocationException && error.InnerException != null ? error.InnerException : error; }
        void Log(string message) { string line = DateTime.UtcNow.ToString("o") + " " + message; events.Add(line); File.AppendAllText(Path.Combine(output, "natural-soak.log"), line + Environment.NewLine, new UTF8Encoding(true)); }
        void TryFinalizationLog(string message)
        {
            try { Log(message); }
            catch (Exception error) { failure = PreserveFailure(failure, "诊断日志未能持久化：" + error.Message); }
        }
        void OnExit(object sender, EventArgs args) { if (!finished) Finish("程序在长测完成前退出，拒绝通过。"); }
        public void Dispose() { if (!finished) Finish("长测记录器在验收完成前释放，拒绝通过。"); timer.Stop(); timer.Dispose(); Application.ApplicationExit -= OnExit; input.Dispose(); csv.Dispose(); }
    }
}
