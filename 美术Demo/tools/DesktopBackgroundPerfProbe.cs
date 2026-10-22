using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Short background-only comparison. The production controller still owns
    // its normal timer and Observe policy; actors never enter a visible state.
    internal static class DesktopBackgroundPerfProbe
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);
        static T Get<T>(object value, string name) { return (T)value.GetType().GetField(name, F).GetValue(value); }
        static void Set(object value, string name, object data) { value.GetType().GetField(name, F).SetValue(value, data); }
        static double? OptionalTime(object value, string name) { FieldInfo field = value.GetType().GetField(name, F); return field == null ? (double?)null : (double)field.GetValue(value); }
        static void Require(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
        static double UiCpu()
        {
            long created, exited, kernel, user;
            if (!GetThreadTimes(GetCurrentThread(), out created, out exited, out kernel, out user)) throw new InvalidOperationException("Own UI thread CPU unavailable.");
            return (kernel + user) / 10000000.0;
        }
        static object Sections()
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            string[] names = (string[])typeof(PerfProbe).GetField("names", flags).GetValue(null);
            double[] millis = (double[])typeof(PerfProbe).GetField("millis", flags).GetValue(null);
            long[] calls = (long[])typeof(PerfProbe).GetField("calls", flags).GetValue(null);
            return names.Select((name, index) => new { name, uiWallMilliseconds = millis[index], calls = calls[index] }).Where(x => x.name != null).ToArray();
        }
        [STAThread] static int Main(string[] args)
        {
            Stopwatch lifetime = Stopwatch.StartNew();
            int seconds = args.Length == 5 ? Int32.Parse(args[4]) : 8;
            if (seconds < 8 || seconds > 24) return 2;
            using (var expiry = new System.Threading.Timer(delegate { Console.Error.WriteLine("Own background probe exceeded its short process budget."); Environment.Exit(124); }, null, (seconds + 1) * 1000, Timeout.Infinite))
            {
                try { return Run(args, lifetime, seconds); }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
                finally { PerfProbe.Enabled = false; }
            }
        }
        static int Run(string[] args, Stopwatch lifetime, int seconds)
        {
            Require((args.Length == 4 || args.Length == 5) && (args[0] == "observe" || args[0] == "virtual"), "Expected observe|virtual, Assets, ArtDemoAssets, fresh private output and optional short seconds.");
            bool observe = args[0] == "observe";
            string output = Path.GetFullPath(args[3]);
            Require(output.StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(output) && !File.Exists(output), "Output must be fresh and inside this probe build.");
            Directory.CreateDirectory(output);
            string profile = Path.Combine(output, "verify-background-" + args[0]);
            Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var samples = new List<object>(); int scanStarts = 0, scanCompletions = 0, forbiddenWorkers = 0;
            double priorScan = Double.NaN, priorObservation = Double.NaN;
            object sections = null; double measuredSeconds = 0, processCpu = 0, uiCpu = 0; string failure = null;
            using (var process = Process.GetCurrentProcess())
            using (var owner = new Form { ShowInTaskbar = false })
            {
                IntPtr ownHiddenHandle = owner.Handle;
                using (var game = new AdventureController(owner, Path.GetFullPath(args[1]), false, null, false, profile, observe))
                using (var context = new ApplicationContext())
                using (var sampler = new System.Windows.Forms.Timer { Interval = 250 })
                {
                    Set(game, "simulatedCleaner", new Func<CleanupBatch, CancellationToken, Task<CleanupResult>>(delegate { forbiddenWorkers++; throw new InvalidOperationException("No cleanup permitted."); }));
                    Set(game, "simulatedMemoryReclaimer", new Func<CancellationToken, Task<MemoryReclaimResult>>(delegate { forbiddenWorkers++; throw new InvalidOperationException("No memory maintenance permitted."); }));
                    game.ConfigureDesktopDemo(false); // Before any message pump or RenderWorld can show an actor.
                    Action guard = delegate {
                        Require(!owner.Visible && !game.DesktopActorsVisible && !game.DesktopHeroWindow.Visible && !Get<Form>(game, "landmarkWindow").Visible && game.DesktopEnemyWindows.Values.All(w => !w.Visible), "Visible UI is forbidden.");
                        Require(!game.MaintenanceConsentCleanup && !game.MaintenanceConsentMemory && !game.MaintenanceConsentAutoMaintenance && forbiddenWorkers == 0
                            && Get<object>(game, "cleanupTask") == null && Get<object>(game, "memoryReclaimTask") == null && Get<object>(game, "desktopProcessTask") == null, "All maintenance must remain off.");
                        Require(game.PresentedFrames == 0, "Background-only probe must not rasterize actors.");
                    };
                    Action sample = delegate {
                        guard(); process.Refresh();
                        double scan = Get<double>(game, "lastScan"), observation = Get<double>(game, "lastInventoryObservation");
                        if (scan >= 0 && scan != priorScan) scanStarts++; priorScan = scan;
                        if (observation >= 0 && observation != priorObservation) scanCompletions++; priorObservation = observation;
                        LiveTempPopulation population = Get<LiveTempPopulation>(game, "liveTemp");
                        samples.Add(new { seconds = lifetime.Elapsed.TotalSeconds, cpuSeconds = process.TotalProcessorTime.TotalSeconds, uiCpuSeconds = UiCpu(),
                            privateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64, threads = process.Threads.Count, handles = process.HandleCount,
                            scanBusy = Get<bool>(game, "scanBusy"), lastScan = scan, lastInventoryObservation = observation,
                            controllerSeconds = Get<Stopwatch>(game, "clock").Elapsed.TotalSeconds,
                            scanFinishedAt = OptionalTime(game, "scanFinishedAt"), scanDurationSeconds = OptionalTime(game, "scanDurationSeconds"), nextAutoScanAt = OptionalTime(game, "nextAutoScanAt"),
                            scannedChangeVersion = Get<int>(game, "scannedChangeVersion"), tempChangeVersion = Get<int>(game, "tempChangeVersion"),
                            observedFiles = population.TotalFiles, population.Known, population.Complete, observedEligibleFiles = population.EligibleFiles,
                            enemyCount = game.Battle.Enemies.Count, aliveEnemies = game.Battle.Enemies.Count(e => !e.Defeated), game.Battle.Automatic, game.PresentedFrames,
                            memoryBusy = Get<bool>(game, "memoryBusy"), diskBusy = Get<bool>(game, "diskBusy"), timerMilliseconds = game.CurrentTickInterval });
                    };
                    PerfProbe.Reset(); PerfProbe.Enabled = true;
                    process.Refresh(); double cpuStart = process.TotalProcessorTime.TotalSeconds, uiStart = UiCpu(), start = lifetime.Elapsed.TotalSeconds;
                    sample();
                    sampler.Tick += delegate {
                        try { sample(); if (lifetime.Elapsed.TotalSeconds >= seconds) context.ExitThread(); }
                        catch (Exception error) { failure = error.ToString(); context.ExitThread(); }
                    };
                    ThreadExceptionEventHandler threadError = delegate(object sender, ThreadExceptionEventArgs error) { failure = error.Exception.ToString(); context.ExitThread(); };
                    Application.ThreadException += threadError;
                    try { sampler.Start(); Application.Run(context); }
                    finally { sampler.Stop(); Get<System.Windows.Forms.Timer>(game, "timer").Stop(); Application.ThreadException -= threadError; }
                    sample(); process.Refresh(); measuredSeconds = lifetime.Elapsed.TotalSeconds - start;
                    processCpu = process.TotalProcessorTime.TotalSeconds - cpuStart; uiCpu = UiCpu() - uiStart; sections = Sections(); PerfProbe.Enabled = false;
                    guard(); Require(failure == null, failure); Require(measuredSeconds >= 5, "Startup left too little measurement time.");
                }
                Require(!owner.Visible, "Owner stayed hidden through disposal.");
            }
            var report = new { passed = true, mode = args[0], profile, processSeconds = lifetime.Elapsed.TotalSeconds, measuredSeconds,
                logicalProcessors = Environment.ProcessorCount, processCpuSeconds = processCpu, cpuCoreEquivalents = processCpu / measuredSeconds,
                cpuMachinePercent = processCpu / measuredSeconds / Environment.ProcessorCount * 100, uiThreadId = GetCurrentThreadId(), uiThreadCpuSeconds = uiCpu,
                backgroundCpuSecondsEstimate = Math.Max(0, processCpu - uiCpu), scanStarts, scanCompletions, forbiddenWorkers, samples, sections,
                scope = "Fresh private mortal background-only x1; normal production controller/timer, actors invisible and zero presented frames, all maintenance off. Observe may read real Temp. Virtual has two initial ambient opponents; Observe scene depends on census. No input/capture/native maintenance. CPU is own-process CPU; PerfProbe sections are UI wall time, not thread CPU. Samples stay in memory; one final JSON write." };
            File.WriteAllText(Path.Combine(output, "background-perf.json"), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(true));
            Console.WriteLine(args[0] + ": " + measuredSeconds.ToString("0.000") + "s, " + (processCpu / measuredSeconds).ToString("0.000") + " cores, UI " + uiCpu.ToString("0.000") + " CPU-s, scans " + scanStarts + "/" + scanCompletions + ", maintenance 0, frames 0.");
            return 0;
        }
    }
}
