using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    internal static class DesktopCensusCadenceTests
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool okay, string text) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + text); if (!okay) failures++; }
        static T Get<T>(object value, string name) { return (T)value.GetType().GetField(name, F).GetValue(value); }
        static void Set(object value, string name, object data) { value.GetType().GetField(name, F).SetValue(value, data); }
        static void Refresh(AdventureController game, bool manual)
        { typeof(AdventureController).GetMethod("RefreshWorld", F, null, new[] { typeof(bool) }, null).Invoke(game, new object[] { manual }); }
        static double Rest(double seconds)
        { MethodInfo method = typeof(AdventureController).GetMethod("AutomaticScanRestSeconds", BindingFlags.Static | BindingFlags.NonPublic); return method == null ? Double.NaN : (double)method.Invoke(null, new object[] { seconds }); }
        sealed class Fixture : IDisposable
        {
            internal readonly Form Owner = new Form { ShowInTaskbar = false };
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim(), Release = new ManualResetEventSlim();
            internal readonly AdventureController Game;
            internal readonly CleanupService Service;
            internal int Reads;
            internal double Now { get { return Get<Stopwatch>(Game, "clock").Elapsed.TotalSeconds; } }
            internal Fixture(string root, string assets)
            {
                string owned = Path.Combine(root, "owned"); Directory.CreateDirectory(owned);
                string file = Path.Combine(owned, "old.bin"); File.WriteAllBytes(file, new byte[4096]); File.SetCreationTimeUtc(file, DateTime.UtcNow.AddDays(-2)); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
                Func<string, FileAttributes> attributes = delegate(string path) { Interlocked.Increment(ref Reads); Entered.Set(); if (!Release.Wait(2500)) throw new TimeoutException("Owned scan test gate timed out."); return File.GetAttributes(path); };
                Service = (CleanupService)Activator.CreateInstance(typeof(CleanupService), F, null, new object[] { owned, attributes }, null);
                IntPtr handle = Owner.Handle;
                Game = new AdventureController(Owner, assets, false, null, false, Path.Combine(root, "verify-profile"), true);
                Get<System.Windows.Forms.Timer>(Game, "timer").Stop(); Set(Game, "cleanup", Service);
                Set(Game, "simulatedCleaner", new Func<CleanupBatch, CancellationToken, Task<CleanupResult>>(delegate { throw new Exception("Maintenance forbidden."); }));
                Set(Game, "simulatedMemoryReclaimer", new Func<CancellationToken, Task<MemoryReclaimResult>>(delegate { throw new Exception("Maintenance forbidden."); }));
                Game.ConfigureDesktopDemo(false); AwaitEntry();
            }
            internal void AwaitEntry() { if (!Entered.Wait(2000)) throw new TimeoutException("Real production scan did not reach the owned attribute reader."); }
            internal void Complete()
            {
                Release.Set();
                Stopwatch timeout = Stopwatch.StartNew();
                while (Get<bool>(Game, "scanBusy") && timeout.ElapsedMilliseconds < 2000) { Application.DoEvents(); Thread.Sleep(2); }
                if (Get<bool>(Game, "scanBusy")) throw new TimeoutException("Queued production completion did not drain.");
            }
            internal void Prepare() { Entered.Reset(); Release.Reset(); }
            internal void DrainUnexpected() { if (Get<bool>(Game, "scanBusy")) { AwaitEntry(); Complete(); } }
            internal void ExpireBudget()
            { FieldInfo field = typeof(AdventureController).GetField("nextAutoScanAt", F); if (field != null) { field.SetValue(Game, Now - .01); Set(Game, "scanFinishedAt", Now - 2.1); } }
            internal void AdvanceCompletedTimes(double seconds)
            {
                foreach (string name in new[] { "lastScan", "scanFinishedAt", "nextAutoScanAt" })
                { FieldInfo field = typeof(AdventureController).GetField(name, F); if (field != null) field.SetValue(Game, (double)field.GetValue(Game) - seconds); }
            }
            internal double Budget()
            { FieldInfo field = typeof(AdventureController).GetField("nextAutoScanAt", F); return field == null ? Double.NaN : Math.Max((double)field.GetValue(Game), Get<double>(Game, "scanFinishedAt") + 2) - Now; }
            public void Dispose()
            {
                Release.Set(); if (!Get<bool>(Game, "disposed")) { DrainUnexpected(); Game.Dispose(); }
                Owner.Dispose(); Entered.Dispose(); Release.Dispose();
            }
        }
        [STAThread] static int Main(string[] args)
        {
            string root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
            try
            {
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                using (Fixture f = new Fixture(root, args[0]))
                {
                    AdventureController g = f.Game;
                    Thread.Sleep(450); f.Complete();
                    Check(Get<LiveTempPopulation>(g, "liveTemp").Known, "real owned Scan and queued production ApplyInventory have completed");
                    f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy"), "automatic RefreshWorld cannot restart immediately after a slow completed scan"); f.DrainUnexpected();
                    Check(Get<double>(g, "scanDurationSeconds") >= .4 && Get<double>(g, "scanDurationSeconds") < 2, "real gated scan completion measures its actual bounded elapsed duration");
                    Check(Rest(3) == 9, "three-second scan receives nine seconds of shared CPU rest budget");
                    f.Prepare(); Refresh(g, true); f.AwaitEntry();
                    Check(Get<bool>(g, "scanBusy"), "manual RefreshWorld bypasses the pending automatic budget");
                    var token = Get<CancellationTokenSource>(g, "scanCancellation"); Refresh(g, true);
                    Check(Object.ReferenceEquals(token, Get<CancellationTokenSource>(g, "scanCancellation")), "manual refresh cannot create a concurrent scan"); f.Complete();
                    Check(f.Budget() >= 1.8 && f.Budget() <= 2.2, "fast completed scan retains the original two-second minimum idle interval");

                    Set(g, "tempChangeVersion", Get<int>(g, "scannedChangeVersion") + 1);
                    f.AdvanceCompletedTimes(.1); f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy"), "fast dirty scan must wait 350ms after completion, not only after its start"); f.DrainUnexpected();
                    f.AdvanceCompletedTimes(.3); f.Prepare(); Refresh(g, false);
                    Check(Get<bool>(g, "scanBusy"), "fast dirty scan may start after 350ms without waiting the quiet two-second interval");
                    if (!Get<bool>(g, "scanBusy")) Refresh(g, true);
                    f.AwaitEntry(); f.Complete();

                    f.Prepare(); Refresh(g, true); f.AwaitEntry(); Set(g, "lastScan", -100.0); f.Complete();
                    Check(Get<double>(g, "scanDurationSeconds") < 1 && f.Budget() < 2.3, "in-flight lastScan invalidation cannot turn a fast real scan into fifteen seconds of rest");

                    int captured = Get<int>(g, "scannedChangeVersion"); Set(g, "tempChangeVersion", captured + 1);
                    f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy") && Get<int>(g, "scannedChangeVersion") == captured, "dirty automatic request cannot spend budget or consume its change version"); f.DrainUnexpected();
                    Set(g, "tempChangeVersion", captured + 9); f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy") && Get<int>(g, "scannedChangeVersion") == captured, "continuously dirty requests coalesce while budget is pending"); f.DrainUnexpected();
                    f.ExpireBudget(); Set(g, "lastScan", f.Now - 1); f.Prepare(); Refresh(g, false); f.AwaitEntry();
                    Check(Get<int>(g, "scannedChangeVersion") == captured + 9, "first permitted dirty scan captures the newest version exactly once");
                    Set(g, "tempChangeVersion", captured + 10); Refresh(g, false);
                    Check(Get<int>(g, "scannedChangeVersion") == captured + 9, "a change during an active scan is not falsely claimed by that snapshot"); f.Complete();
                    f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy") && Get<int>(g, "tempChangeVersion") != Get<int>(g, "scannedChangeVersion"), "change during scan remains pending through the next completion budget"); f.DrainUnexpected();
                    f.ExpireBudget(); Set(g, "lastScan", f.Now - .1); f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy"), "dirty input still respects the original 350ms debounce"); f.DrainUnexpected();

                    f.Prepare(); Refresh(g, true); f.AwaitEntry(); double previous = Get<double>(g, "lastInventoryObservation");
                    Set(g, "cleanupEpoch", Get<int>(g, "cleanupEpoch") + 1); f.Complete();
                    Check(Get<double>(g, "lastInventoryObservation") == previous, "epoch-invalid real snapshot cannot replace the known inventory");
                    f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy"), "epoch invalidation schedules a rested retry instead of a tight rescan loop"); f.DrainUnexpected();
                    f.ExpireBudget(); f.Prepare(); Refresh(g, false); f.AwaitEntry();
                    Check(Get<bool>(g, "scanBusy"), "epoch-invalid snapshot is retried after the budget without requiring another watcher event"); f.Complete();
                    Check(Rest(100) == 15, "very slow scan idle budget is capped at fifteen seconds without waiting a hundred seconds");

                    Set(g, "cleanup", null); Refresh(g, true); f.Complete();
                    Check(Get<string>(g, "scanStatus").StartsWith("扫描失败"), "real worker exception is reported through the normal completion callback");
                    Set(g, "cleanup", f.Service); f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy"), "worker exception still leaves an automatic retry budget"); f.DrainUnexpected();
                    f.Prepare(); Refresh(g, true); f.AwaitEntry(); Get<CancellationTokenSource>(g, "scanCancellation").Cancel(); f.Complete();
                    f.Prepare(); Refresh(g, false);
                    Check(!Get<bool>(g, "scanBusy"), "cancelled owned scan cannot immediately spin into another scan"); f.DrainUnexpected();
                    Check(!f.Owner.Visible && !g.DesktopHeroWindow.Visible && !g.DesktopActorsVisible && g.PresentedFrames == 0, "all timing fixtures remain hidden with zero presented actor frames");
                    Check(!g.MaintenanceConsentCleanup && !g.MaintenanceConsentMemory && !g.MaintenanceConsentAutoMaintenance && Get<object>(g, "cleanupTask") == null && Get<object>(g, "memoryReclaimTask") == null, "cadence fixture invokes no real maintenance");
                    int reads = f.Reads; g.Dispose(); Refresh(g, true); Refresh(g, false);
                    Check(f.Reads == reads, "disposed controller never restarts an automatic or manual census");
                }
            }
            catch (Exception error) { Check(false, "unexpected fixture error: " + error); }
            File.WriteAllText(Path.Combine(root, "census-cadence.json"), new JavaScriptSerializer().Serialize(new { checks, failures, scope = "Actual private Observe controller, stopped production timer, real Scan restricted to one owned file, controlled Post pumping, bounded real scan gate, pure rest-budget boundaries and completion-age fields. No changed game clock, visible actors, user Temp scan, maintenance, real archives or natural-time progression." }), new UTF8Encoding(true));
            Console.WriteLine("Census cadence: " + checks + " checks, " + failures + " failures."); return failures == 0 ? 0 : 1;
        }
    }
}
