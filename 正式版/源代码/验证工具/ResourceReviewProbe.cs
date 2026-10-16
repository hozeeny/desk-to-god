using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NetBunny;

internal static class ResourceReviewProbe
{
    private static string Assets;
    private const long MiB = 1024L * 1024, GiB = 1024L * MiB;
    private static int checks;
    private static readonly Type Controller = typeof(AdventureController);
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Field<T>(AdventureController c, string name) { return (T)Controller.GetField(name, Private).GetValue(c); }
    private static void Set(AdventureController c, string name, object value) { Controller.GetField(name, Private).SetValue(c, value); }
    private static object Call(AdventureController c, string name, params object[] args) { return Controller.GetMethod(name, Private).Invoke(c, args); }
    private static void Check(bool okay, string label) { if (!okay) throw new InvalidOperationException("Independent resource review: " + label); checks++; }
    private static CleanupCandidate Candidate(string name, long bytes) { return new CleanupCandidate { Path = @"C:\KnightAssistantSynthetic\" + name + ".tmp", Length = bytes }; }
    private static CleanupBatch Batch(params CleanupCandidate[] files) { return new CleanupBatch(files.ToList(), 1); }
    private static CleanupSnapshot Inventory(long total, params CleanupBatch[] batches)
    { return new CleanupSnapshot { InventoryComplete = true, ObservedTempBytes = total, Batches = batches.ToList(), CandidateBytes = batches.Sum(b => b.Bytes), CandidateFiles = batches.Sum(b => b.FilesCount), Message = "isolated synthetic metadata" }; }
    private static void Observe(AdventureController c, long free, double time) { Call(c, "ObserveDisk", DiskSpaceSnapshot.FromValues(100 * GiB, free), time); }
    private sealed class Fixture : IDisposable
    {
        internal readonly Form Owner;
        internal readonly AdventureController C;
        internal TaskCompletionSource<CleanupResult> Work;
        internal Fixture()
        {
            Owner = new Form(); IntPtr handle = Owner.Handle;
            C = new AdventureController(Owner, Assets, true);
            Field<System.Windows.Forms.Timer>(C, "timer").Stop();
            Set(C, "enabled", true); Set(C, "area", new Rectangle(-24000, -24000, 1600, 1000));
            Set(C, "x", -23600d); Set(C, "y", -23500d); Call(C, "PositionLandmark");
            C.Battle.Progress.Inventory = AdventureWeapon.All.Select(w => w.Id).ToList();
        }
        internal TaskCompletionSource<CleanupResult> Start(AdventureEnemy enemy)
        {
            Work = new TaskCompletionSource<CleanupResult>();
            Set(C, "simulatedCleaner", new Func<CleanupBatch, CancellationToken, Task<CleanupResult>>(delegate(CleanupBatch batch, CancellationToken token) { return Work.Task; }));
            if (!enemy.Authorized) C.Battle.Authorize(enemy);
            enemy.Health = 1; C.Battle.Cooldown = 0; C.Battle.Strike(enemy);
            Call(C, "BeginCleanup", enemy); Call(C, "PumpCleanup");
            return Work;
        }
        public void Dispose()
        {
            if (Work != null) Work.TrySetResult(new CleanupResult { Cancelled = true, Message = "synthetic teardown" });
            C.Dispose(); Owner.Dispose();
        }
    }
    private static void PartialSuccess(bool cancelled, bool disk, bool empty)
    {
        using (Fixture f = new Fixture())
        {
            AdventureController c = f.C; AdventureBattle battle = c.Battle;
            CleanupCandidate deleted = Candidate("deleted", empty ? 0 : 5 * MiB), changed = Candidate("changed", 7 * MiB), busy = Candidate("busy", 9 * MiB);
            CleanupBatch original = Batch(deleted, changed, busy);
            Call(c, "ApplyInventory", Inventory(100 * MiB, original), false);
            if (disk) Observe(c, GiB / 2, 1);
            AdventureEnemy enemy = battle.Enemies.Single();
            f.Start(enemy); battle.Tick(.31);
            Check(enemy.IsVisuallyHidden && !enemy.Defeated && Field<long>(c, "candidateBytes") == original.Bytes, "delayed cleanup does not charge displayed bytes before results");
            if (cancelled) c.CancelPending();
            f.Work.SetResult(new CleanupResult { FreedBytes = deleted.Length, DeletedFiles = 1, SkippedFiles = 2, Cancelled = cancelled, RemainingCandidates = new List<CleanupCandidate> { changed, busy } });
            Call(c, "DrainCleanup");
            AdventureEnemy residue = battle.Enemies.Single(e => !e.Defeated);
            Check(enemy.Defeated && residue.Failed && !residue.Authorized && !residue.IsBoss && residue.Batch.FilesCount == 2 && !residue.Batch.Candidates.Contains(deleted), "partial result keeps only unresolved candidates in an unauthorized visible residue");
            Check(Field<long>(c, "candidateBytes") == 16 * MiB && battle.Progress.FreedBytes == deleted.Length && Field<ResourceWaveState>(c, "resourceWaves").BaselineTempBytes == 100 * MiB - deleted.Length, "partial accounting subtracts only actual deletion from castle and census");
            Check(Field<ResourceWaveState>(c, "resourceWaves").WaveActive && battle.LastReward.Contains("剩余 2 个文件"), "residue keeps its resource wave active and provides a rescan explanation");
            battle.SetAutomatic(true);
            Check(battle.PlanNext(0, 0) == null && !battle.Authorize(residue) && battle.ClearFieldSnapshot().Count == 0, "failed residue cannot be silently retried by manual, automatic or clear authorization");
            if (empty) Check(battle.Progress.Experience == 0 && battle.Progress.Score == 0, "zero-byte partial success grants no experience or combat score");
            int score = battle.Progress.Score; Call(c, "DrainCleanup");
            Check(battle.Progress.Score == score && battle.Progress.FreedBytes == deleted.Length && battle.Enemies.Count(e => !e.Defeated) == 1, "draining the same completion twice cannot duplicate rewards or residues");
            CleanupBatch remaining = Batch(changed, busy);
            Call(c, "ApplyInventory", Inventory(100 * MiB - deleted.Length, remaining), false);
            Check(battle.Enemies.Contains(residue) && residue.Failed, "automatic scan cannot replace a failed residue with new cleanup permission");
            Call(c, "ApplyInventory", Inventory(100 * MiB - deleted.Length, remaining), true);
            Check(battle.Enemies.Count(e => !e.Defeated) == 1 && !battle.Enemies.Single().Failed && battle.Enemies.Single().Batch.FilesCount == 2 && !battle.Enemies.Single().Authorized && Field<long>(c, "candidateBytes") == 16 * MiB, "explicit rescan revalidates only the remaining snapshot with no inherited authorization");
        }
    }
    private static void DiskRecovery()
    {
        using (Fixture f = new Fixture())
        {
            AdventureController c = f.C; AdventureBattle battle = c.Battle;
            CleanupCandidate[] files = Enumerable.Range(0, 257).Select(i => Candidate("wide-" + i, MiB)).ToArray();
            CleanupBatch[] batches = new[] { Batch(files.Take(128).ToArray()), Batch(files.Skip(128).Take(128).ToArray()), Batch(files.Skip(256).ToArray()) };
            Call(c, "ApplyInventory", Inventory(300 * MiB, batches), false); Observe(c, GiB / 2, 1);
            AdventureEnemy boss = battle.Enemies.Single();
            Check(boss.IsDiskBoss && boss.Batch.FilesCount == 257 && Field<long>(c, "candidateBytes") == 257 * MiB, "disk boss owns the complete candidate set once");
            Observe(c, LowDiskBossPolicy.RecoveryThresholdBytes - 1, 2);
            Check(battle.Enemies.Single() == boss, "hysteresis band does not prematurely split boss ownership");
            Observe(c, LowDiskBossPolicy.RecoveryThresholdBytes, 200);
            AdventureEnemy[] ordinary = battle.Enemies.ToArray();
            Check(ordinary.Length == 3 && ordinary.All(e => !e.IsBoss && !e.Authorized && !e.Cleaning && e.Batch.FilesCount <= 128), "measured recovery unbinds idle disk boss into bounded ordinary batches");
            Check(ordinary.SelectMany(e => e.Batch.Candidates).Distinct().Count() == 257 && ordinary.Sum(e => e.Bytes) == 257 * MiB && Field<long>(c, "candidateBytes") == 257 * MiB, "recovery preserves every candidate exactly once without inventing disk cleanup");
            Observe(c, 2 * GiB, 201);
            Check(battle.Enemies.Count == 3 && battle.Progress.FreedBytes == 0, "repeated recovery samples cannot duplicate files or rewards");
            Observe(c, GiB / 2, 202); boss = battle.Enemies.Single(); battle.Authorize(boss);
            Observe(c, 2 * GiB, 203);
            Check(battle.Enemies.Single() == boss && boss.Authorized && !boss.Cleaning && battle.QueueCount == 1, "recovery preserves an explicitly queued disk target");
            Call(c, "ApplyInventory", Inventory(1 * MiB), true);
            Check(battle.Enemies.Single() == boss && boss.Batch.FilesCount == 257 && Field<long>(c, "candidateBytes") == 257 * MiB, "manual refresh cannot replace already authorized disk files");
            f.Start(boss); Observe(c, 2 * GiB, 204);
            Check(battle.Enemies.Single() == boss && boss.CleanupStarted && boss.Authorized, "recovery preserves active worker ownership until the actual result arrives");
            c.CancelPending();
            Check(boss.Authorized && boss.Cleaning && Field<CancellationTokenSource>(c, "workCancellation").IsCancellationRequested, "cancel requests the active job to stop without losing its result authority");
            f.Work.SetResult(new CleanupResult { Cancelled = true, RemainingCandidates = files.ToList(), Message = "synthetic cancellation" }); Call(c, "DrainCleanup");
            Check(boss.Failed && !boss.Defeated && !boss.IsVisuallyHidden && battle.Progress.FreedBytes == 0, "cancelled zero-success disk work restores its visible failure state");
            Observe(c, 2 * GiB, 205);
            Check(battle.Enemies.Count == 3 && battle.Enemies.All(e => !e.IsBoss && e.Failed && !e.Authorized) && Field<long>(c, "candidateBytes") == 257 * MiB, "recovery after cancellation splits failed files without authorizing retries");
        }
    }
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            Assets = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            Console.WriteLine(Controller.GetMethod("ResourceSmoke", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { Assets }));
            PartialSuccess(false, false, false); PartialSuccess(true, true, false); PartialSuccess(false, true, true);
            DiskRecovery();
            Console.WriteLine("Independent resource boundaries: " + checks + " passed (metadata/task fakes only; no real Temp, network, disk sampling or progress writes).");
            return 0;
        }
        catch (Exception error) { Console.WriteLine(error.ToString()); return 1; }
    }
}
