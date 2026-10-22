using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NetBunny
{
    // Only the explicit candidate overload is invoked. All candidates belong to
    // this fixture; all trim callbacks are synthetic. No forms or native trims.
    internal static class DesktopMemoryOutcomeTests
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const long MiB = 1024L * 1024;
        static int checks, failures, executedCases;
        static Type observationType;
        static MethodInfo reclaim;
        static readonly List<string> observations = new List<string>();
        static void Check(bool okay, string message) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++; }
        static void Set(object instance, string name, object value) { instance.GetType().GetField(name, F).SetValue(instance, value); }
        static T Get<T>(object instance, string name) { return (T)instance.GetType().GetField(name, F).GetValue(instance); }
        static object Call(object instance, string name) { return instance.GetType().GetMethod(name, F).Invoke(instance, null); }
        static bool Field(Type type, string name, Type expected)
        {
            FieldInfo field = type == null ? null : type.GetField(name, F);
            bool okay = field != null && field.FieldType == expected;
            Check(okay, "contract: " + (type == null ? "MemoryTrimObservation" : type.Name) + "." + name + " is " + expected.Name);
            return okay;
        }
        static bool Contract()
        {
            observationType = typeof(MemoryReclaimer).Assembly.GetType("NetBunny.MemoryTrimObservation", false);
            bool okay = true;
            okay &= Field(observationType, "Applied", typeof(bool));
            okay &= Field(observationType, "Observed", typeof(bool));
            okay &= Field(observationType, "Before", typeof(long));
            okay &= Field(observationType, "After", typeof(long));
            foreach (string name in new[] { "Cancelled", "TimedOut", "ProcessLimited" }) okay &= Field(typeof(MemoryReclaimResult), name, typeof(bool));
            okay &= Field(typeof(MemoryReclaimResult), "Unobserved", typeof(int));
            reclaim = typeof(MemoryReclaimer).GetMethods(S).FirstOrDefault(m => {
                ParameterInfo[] p = m.GetParameters();
                return m.Name == "Reclaim" && p.Length == 3 && p[0].ParameterType == typeof(CancellationToken)
                    && p[1].ParameterType == typeof(Process[]) && p[2].ParameterType.IsGenericType
                    && p[2].ParameterType.GetGenericTypeDefinition() == typeof(Func<,,>)
                    && p[2].ParameterType.GetGenericArguments()[0] == typeof(Process)
                    && p[2].ParameterType.GetGenericArguments()[1] == typeof(CancellationToken)
                    && p[2].ParameterType.GetGenericArguments()[2] == observationType;
            });
            Check(reclaim != null, "contract: explicit Process[] candidate Reclaim overload is available without using the global entry point");
            return okay && reclaim != null;
        }
        static object Observation(bool applied, bool observed, long before, long after)
        {
            object value = Activator.CreateInstance(observationType, true);
            Set(value, "Applied", applied); Set(value, "Observed", observed); Set(value, "Before", before); Set(value, "After", after);
            return value;
        }
        static MemoryReclaimResult Reclaim(CancellationToken token, Process[] candidates, Func<Process, CancellationToken, object> callback)
        {
            ParameterExpression p = Expression.Parameter(typeof(Process), "ownedProcess");
            ParameterExpression t = Expression.Parameter(typeof(CancellationToken), "token");
            Delegate bridge = Expression.Lambda(reclaim.GetParameters()[2].ParameterType,
                Expression.Convert(Expression.Invoke(Expression.Constant(callback), p, t), observationType), p, t).Compile();
            try { return (MemoryReclaimResult)reclaim.Invoke(null, new object[] { token, candidates, bridge }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
            finally { foreach (Process process in candidates) process.Dispose(); }
        }
        static void Observe(string name, MemoryReclaimResult result)
        {
            string text = name + ": " + result.Summary;
            observations.Add(text); Console.WriteLine("OBSERVED " + text);
        }
        static void Case(string name, Action body)
        {
            executedCases++;
            try { body(); }
            catch (Exception error) { Check(false, name + ": unexpected " + error); }
        }
        static void NoOtherStop(MemoryReclaimResult result, string name)
        { Check(!Get<bool>(result, "TimedOut") && !Get<bool>(result, "ProcessLimited"), name + ": cancellation is not a timeout or count limit"); }
        static void BaselineSummary()
        {
            MemoryReclaimResult result = new MemoryReclaimResult { Before = new MemorySnapshot { Available = true, UsedPercent = 80 }, After = new MemorySnapshot { Available = true, UsedPercent = 81 }, Trimmed = 2, Skipped = 3, WorkingSetReduction = MiB };
            Check(result.Summary.Contains("80.0%") && result.Summary.Contains("81.0%"), "summary preserves a real system-memory rise rather than inventing a drop");
            Check(result.Summary.Contains(AdventureBattle.FormatBytes(MiB)), "summary exposes the independently observed working-set change");
            Check(result.Summary.Contains("跳过"), "summary distinguishes skipped candidates from applied work");
            result.After.Available = false;
            Check(result.Summary.Contains("暂不可用") || result.Summary.Contains("采样不可用") || result.Summary.Contains("未能核实"), "unknown system sampling is explicitly unavailable");
            Observe("baseline summary", result);
        }

        sealed class OwnedChild : IDisposable
        {
            readonly Process child;
            readonly IntPtr retainedHandle;
            readonly long started;
            readonly string path;
            bool stopped;
            internal OwnedChild()
            {
                path = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
                child = Process.Start(new ProcessStartInfo(path, "--child") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true });
                retainedHandle = child.Handle; // Keep the actual created process identity/handle for cleanup.
                try
                {
                    started = child.StartTime.ToUniversalTime().Ticks;
                    Task<string> ready = Task.Factory.StartNew(() => child.StandardOutput.ReadLine());
                    if (!ready.Wait(6000) || ready.Result != "READY") throw new Exception("Owned child did not complete the timed READY protocol.");
                    Check(retainedHandle != IntPtr.Zero && !child.HasExited, "fixture owns a live hidden 96 MiB READY child and retains its process handle");
                }
                catch { Dispose(); throw; }
            }
            internal Process[] Candidates(int count)
            {
                List<Process> candidates = new List<Process>();
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        Process candidate = Process.GetProcessById(child.Id);
                        candidates.Add(candidate);
                        if (candidate.StartTime.ToUniversalTime().Ticks != started || !String.Equals(Path.GetFullPath(candidate.MainModule.FileName), path, StringComparison.OrdinalIgnoreCase))
                            throw new Exception("Owned candidate identity changed.");
                    }
                    return candidates.ToArray();
                }
                catch { foreach (Process candidate in candidates) candidate.Dispose(); throw; }
            }
            public void Dispose()
            {
                if (stopped) return; stopped = true;
                try
                {
                    if (!child.HasExited)
                    {
                        child.StandardInput.WriteLine("STOP"); child.StandardInput.Flush();
                        if (!child.WaitForExit(6000)) { child.Kill(); child.WaitForExit(3000); Check(false, "owned child required retained-handle cleanup after STOP timeout"); }
                    }
                    Check(child.HasExited && child.ExitCode == 0, "fixture STOP protocol ends only its own child with exit 0");
                }
                finally { child.Dispose(); }
            }
        }

        static AdventureController Controller(string root)
        {
            Directory.CreateDirectory(root);
            AdventureController game = (AdventureController)FormatterServices.GetUninitializedObject(typeof(AdventureController));
            Set(game, "isolatedProfileRoot", root); Set(game, "desktopEnvironment", new DesktopEnvironmentPolicy(false));
            Set(game, "battle", new AdventureBattle(new AdventureProgress())); Set(game, "cultivation", new CultivationProgress());
            Set(game, "desktopRecentEvents", new Queue<string>()); Set(game, "desktopMaintenance", new Queue<DesktopMaintenanceEntry>());
            Set(game, "demonstrationOnly", true); Set(game, "clock", Stopwatch.StartNew());
            return game; // No constructor, timer, Form, observers, business saves or worker startup.
        }
        static void DrainAndHistory(string root, MemoryReclaimResult result)
        {
            AdventureController game = Controller(root);
            game.Battle.Progress.FreedBytes = 777;
            int kills = game.Cultivation.MortalKills; long qi = game.Cultivation.LifetimeQi;
            result.Before = new MemorySnapshot { Available = true, UsedPercent = 80 };
            result.After = new MemorySnapshot { Available = true, UsedPercent = 81 };
            result.Error = "synthetic diagnostic retained";
            Check(result.Summary.Contains("已取消") && result.Summary.Contains("实际完成量") && result.Summary.Contains(result.Error), "partial cancellation and a diagnostic remain visible together");
            Set(game, "memoryReclaimTask", Task.FromResult(result)); Set(game, "memoryReclaimCancellation", new CancellationTokenSource());
            Call(game, "DrainMemoryReclaim");
            Queue<DesktopMaintenanceEntry> history = Get<Queue<DesktopMaintenanceEntry>>(game, "desktopMaintenance");
            Check(history.Count == 1, "actual controller drain records one maintenance result");
            DesktopMaintenanceEntry entry = history.Single();
            Check(entry.Kind == "Memory" && entry.TrimmedProcesses == result.Trimmed && entry.WorkingSetReduction == result.WorkingSetReduction, "history retains the exact applied count and measured reduction");
            Check(entry.Summary == result.Summary && game.Battle.LastReward == result.Summary && Get<string>(game, "cleanupNotice") == result.Summary, "history, feedback and reward notice share the accurate worker summary");
            Check(game.Battle.Progress.FreedBytes == 777 && entry.FreedBytes == 0 && entry.DeletedFiles == 0, "memory drain cannot create disk-cleanup bytes or files");
            Check(game.Cultivation.MortalKills == kills && game.Cultivation.LifetimeQi == qi, "drain cannot grant a second cultivation reward");
            Check(Get<Task<MemoryReclaimResult>>(game, "memoryReclaimTask") == null && Get<CancellationTokenSource>(game, "memoryReclaimCancellation") == null, "drain clears its completed task and token owner");
            Check(Get<bool>(game, "memoryAvailable") && Get<double>(game, "memoryPercent") == 81, "controller publishes the observed system-memory rise");
            string file = Path.Combine(root, "maintenance-results-v1.log"); string[] before = File.ReadAllLines(file, Encoding.UTF8);
            Call(game, "DrainMemoryReclaim");
            Check(history.Count == 1 && File.ReadAllLines(file, Encoding.UTF8).SequenceEqual(before), "repeated drain cannot duplicate history or rewards");
            Check(before.Length == 1 && before[0].Split('|').Length == 7, "existing seven-field history remains compatible");
            AdventureController reopened = Controller(root); Call(reopened, "LoadDesktopMaintenanceHistory");
            DesktopMaintenanceEntry restored = Get<Queue<DesktopMaintenanceEntry>>(reopened, "desktopMaintenance").Single();
            Check(restored.Summary == result.Summary && restored.Summary.Contains("已取消") && restored.TrimmedProcesses == result.Trimmed && restored.WorkingSetReduction == result.WorkingSetReduction, "private history reload preserves partial cancellation and actual measurements");
            Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 1, "the private controller fixture writes only its maintenance history");
            Check(Get<object>(game, "tempWatcher") == null && Get<object>(game, "timer") == null && Get<Task<CleanupResult>>(game, "cleanupTask") == null && Get<Task<ProcessCloseResult>>(game, "desktopProcessTask") == null, "controller test starts no forms, timers, observers or maintenance/process workers");
        }

        static void Outcomes(OwnedChild child, string root)
        {
            MemoryReclaimResult partial = null;
            Case("first-success cancellation", delegate {
                using (CancellationTokenSource stop = new CancellationTokenSource())
                {
                    int calls = 0;
                    partial = Reclaim(stop.Token, child.Candidates(3), (p, t) => { calls++; stop.Cancel(); return Observation(true, true, 12 * MiB, 9 * MiB); });
                    Check(calls == 1 && partial.Trimmed == 1 && partial.WorkingSetReduction == 3 * MiB, "first-success cancellation retains actual completed work and never starts later candidates");
                    Check(Get<bool>(partial, "Cancelled"), "partial completion carries an independent cancellation marker"); NoOtherStop(partial, "partial cancellation");
                    Check(partial.Summary.Contains("已取消") && partial.Summary.Contains("实际完成量"), "partial cancellation tells the player the numbers are actual completed work"); Observe("first-success cancellation", partial);
                }
            });
            Case("last-candidate cancellation", delegate {
                using (CancellationTokenSource stop = new CancellationTokenSource())
                {
                    int calls = 0;
                    MemoryReclaimResult result = Reclaim(stop.Token, child.Candidates(1), (p, t) => { calls++; stop.Cancel(); return Observation(true, true, 10 * MiB, 9 * MiB); });
                    Check(calls == 1 && result.Trimmed == 1 && Get<bool>(result, "Cancelled"), "cancellation in the last candidate cannot become an unmarked natural completion"); NoOtherStop(result, "last-candidate cancellation"); Observe("last-candidate cancellation", result);
                }
            });
            Case("zero cancellation", delegate {
                using (CancellationTokenSource stop = new CancellationTokenSource())
                {
                    stop.Cancel(); int calls = 0;
                    MemoryReclaimResult result = Reclaim(stop.Token, child.Candidates(2), (p, t) => { calls++; return Observation(true, true, MiB, 0); });
                    Check(calls == 0 && result.Trimmed == 0 && result.WorkingSetReduction == 0 && Get<bool>(result, "Cancelled"), "pre-cancelled explicit run performs no trim and preserves cancellation");
                    Check(result.Summary.Contains("已取消") && !result.Summary.Contains("当前没有可回收"), "zero cancellation is not described as a completed search finding nothing"); NoOtherStop(result, "zero cancellation");
                }
            });
            Case("last-candidate refused cancellation", delegate {
                using (CancellationTokenSource stop = new CancellationTokenSource())
                {
                    int calls = 0;
                    MemoryReclaimResult result = Reclaim(stop.Token, child.Candidates(1), (p, t) => { calls++; stop.Cancel(); return Observation(false, false, MiB, 0); });
                    Check(calls == 1 && result.Trimmed == 0 && Get<bool>(result, "Cancelled"), "last candidate interrupted before applying still reports cancellation");
                    Check(result.WorkingSetReduction == 0 && Get<int>(result, "Unobserved") == 0, "unapplied cancellation cannot fabricate applied or unobserved work");
                }
            });
            Case("partial unknown sampling", delegate {
                int calls = 0;
                MemoryReclaimResult result = Reclaim(CancellationToken.None, child.Candidates(3), (p, t) => { calls++; return calls == 1 ? Observation(true, true, 20 * MiB, 10 * MiB) : calls == 2 ? Observation(true, false, 16 * MiB, 0) : Observation(false, false, 8 * MiB, 0); });
                Check(calls == 3 && result.Trimmed == 2 && Get<int>(result, "Unobserved") == 1 && result.Skipped == 1, "applied-but-unobserved work remains distinct from skipped work");
                Check(result.WorkingSetReduction == 10 * MiB, "unknown samples cannot contribute invented reduction bytes");
                Check(result.Summary.Contains(AdventureBattle.FormatBytes(10 * MiB)) && (result.Summary.Contains("采样不可用") || result.Summary.Contains("未能核实")), "partial unknown summary preserves known bytes and identifies the unknown part");
                Check(!Get<bool>(result, "Cancelled") && !Get<bool>(result, "TimedOut") && !Get<bool>(result, "ProcessLimited"), "natural completed enumeration has no false stop marker"); Observe("partial unknown sampling", result);
            });
            Case("all unknown sampling", delegate {
                MemoryReclaimResult result = Reclaim(CancellationToken.None, child.Candidates(2), (p, t) => Observation(true, false, 16 * MiB, 0));
                Check(result.Trimmed == 2 && Get<int>(result, "Unobserved") == 2 && result.Skipped == 0 && result.WorkingSetReduction == 0, "all unobserved native successes retain their count without invented measured bytes");
                Check((result.Summary.Contains("采样不可用") || result.Summary.Contains("未能核实")) && !result.Summary.Contains(AdventureBattle.FormatBytes(0)), "all unknown work never presents 0 B as measured release"); Observe("all unknown sampling", result);
            });
            Case("working set growth", delegate {
                MemoryReclaimResult result = Reclaim(CancellationToken.None, child.Candidates(1), (p, t) => Observation(true, true, 8 * MiB, 10 * MiB));
                Check(result.Trimmed == 1 && Get<int>(result, "Unobserved") == 0 && result.WorkingSetReduction == 0, "observed working-set growth never becomes negative release or unknown work");
            });
            Case("24 process cap", delegate {
                int calls = 0;
                MemoryReclaimResult result = Reclaim(CancellationToken.None, child.Candidates(26), (p, t) => { calls++; return Observation(true, true, 2 * MiB, MiB); });
                Check(calls == 24 && result.Trimmed == 24 && result.WorkingSetReduction == 24 * MiB, "real candidate loop keeps the original 24 successful-process cap");
                Check(Get<bool>(result, "ProcessLimited") && !Get<bool>(result, "TimedOut") && !Get<bool>(result, "Cancelled"), "count-limited pass reports the correct independent stop reason");
                Check(result.Summary.Contains("24") && (result.Summary.Contains("上限") || result.Summary.Contains("限额")), "count-limit summary explicitly identifies the bounded pass"); Observe("24 process cap", result);
            });
            Case("five second bound", delegate {
                int calls = 0; Stopwatch wall = Stopwatch.StartNew();
                MemoryReclaimResult result = Reclaim(CancellationToken.None, child.Candidates(2), (p, t) => { calls++; Thread.Sleep(5100); return Observation(true, true, 2 * MiB, MiB); });
                wall.Stop();
                Check(calls == 1 && result.Trimmed == 1 && result.WorkingSetReduction == MiB, "real five-second guard stops before a second synthetic candidate");
                Check(Get<bool>(result, "TimedOut") && !Get<bool>(result, "Cancelled") && !Get<bool>(result, "ProcessLimited"), "five-second bounded pass reports a timeout without discarding completed work");
                Check(wall.Elapsed.TotalSeconds >= 5 && wall.Elapsed.TotalSeconds < 9, "five-second fixture remains bounded and uses no native trim");
                Check(result.Summary.Contains("上限") || result.Summary.Contains("超时") || result.Summary.Contains("时限"), "time-limit summary explains that the pass stopped early"); Observe("five second bound", result);
            });
            Case("self protection", delegate {
                int calls = 0; int ownId; using (Process own = Process.GetCurrentProcess()) ownId = own.Id;
                MemoryReclaimResult result = Reclaim(CancellationToken.None, new[] { Process.GetProcessById(ownId) }, (p, t) => { calls++; return Observation(true, true, MiB, 0); });
                Check(calls == 0 && result.Trimmed == 0 && result.Skipped == 1, "real original filter skips this fixture process before the synthetic trimmer");
                Check(!Get<bool>(result, "Cancelled") && !Get<bool>(result, "TimedOut") && !Get<bool>(result, "ProcessLimited"), "a naturally exhausted self-only pass has no false interruption flag");
            });
            if (partial != null) Case("drain and history", () => DrainAndHistory(Path.Combine(root, "partial-cancel"), partial));
            Case("task-level cancellation", delegate {
                AdventureController game = Controller(Path.Combine(root, "cancelled-task"));
                TaskCompletionSource<MemoryReclaimResult> completed = new TaskCompletionSource<MemoryReclaimResult>(); completed.SetCanceled();
                Set(game, "memoryReclaimTask", completed.Task); Set(game, "memoryReclaimCancellation", new CancellationTokenSource()); Call(game, "DrainMemoryReclaim");
                DesktopMaintenanceEntry entry = Get<Queue<DesktopMaintenanceEntry>>(game, "desktopMaintenance").Single();
                Check(entry.Summary.Contains("已取消") && entry.Summary.Contains("实际完成量") && entry.TrimmedProcesses == 0 && entry.WorkingSetReduction == 0, "controller task cancellation uses the same honest cancellation outcome");
                Check(game.Battle.Progress.FreedBytes == 0 && entry.FreedBytes == 0, "task cancellation cannot create disk bytes");
            });
        }

        static int Child()
        {
            byte[] block = new byte[96 * 1024 * 1024]; for (int i = 0; i < block.Length; i += 4096) block[i] = 1;
            Console.WriteLine("READY"); Console.Out.Flush();
            Task<string> command = Task.Factory.StartNew(() => Console.ReadLine());
            bool stopped = command.Wait(45000) && command.Result == "STOP"; GC.KeepAlive(block);
            return stopped ? 0 : 3;
        }
        static int Main(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            if (args.Length == 1 && args[0] == "--child") return Child();
            Stopwatch wall = Stopwatch.StartNew();
            string root = Path.GetFullPath(args.Length == 0 ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "private-fixtures") : args[0]);
            try
            {
                Directory.CreateDirectory(root); BaselineSummary();
                if (Contract()) using (OwnedChild child = new OwnedChild()) Outcomes(child, root);
                else Console.WriteLine("NOT EXECUTED: candidate-loop, owned child, cancellation, bounds, sampling and controller-history cases require the declared production seam. No default/global entry point was called.");
            }
            catch (Exception error) { Check(false, "fixture setup: " + error); }
            wall.Stop();
            var report = new { checks = checks, failures = failures, executedCases = executedCases, seconds = wall.Elapsed.TotalSeconds,
                nativeTrimCalled = false, globalCandidateEnumerationCalled = false, privateFixtureRoot = root, observations = observations.ToArray() };
            File.WriteAllText(Path.Combine(root, "memory-outcomes.json"), new JavaScriptSerializer().Serialize(report), new UTF8Encoding(true));
            Console.WriteLine("Desktop memory outcomes: " + checks + " checks, " + failures + " failures; " + executedCases + " cases, " + wall.Elapsed.TotalSeconds.ToString("0.000") + " seconds. Synthetic trims of explicit owned candidates only; no forms, global candidate enumeration, native trim, real archives or natural-time advancement.");
            return failures == 0 ? 0 : 1;
        }
    }
}
