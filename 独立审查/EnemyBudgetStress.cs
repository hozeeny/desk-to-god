using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetBunny
{
    internal static class EnemyBudgetStress
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static object Read(object target, string name) { return target.GetType().GetField(name, Fields).GetValue(target); }
        private static void Set(object target, string name, object value) { target.GetType().GetField(name, Fields).SetValue(target, value); }
        private static object Call(object target, string name, params object[] args) { return target.GetType().GetMethod(name, Fields).Invoke(target, args); }
        private static CleanupSnapshot Inventory(int iteration, bool complete)
        {
            List<CleanupBatch> batches = new List<CleanupBatch>();
            for (int i = 0; i < 12; i++)
                batches.Add(new CleanupBatch(new List<CleanupCandidate> { new CleanupCandidate {
                    Path = (i % 3 == 0 ? @"D:\" : @"C:\") + @"KnightAssistantSynthetic\wave-" + iteration + "-" + i + ".tmp",
                    Length = (i + 1) * 1048576L, Written = iteration + 100, IndexLow = (uint)(iteration * 20 + i + 1) } }, i + 1));
            return new CleanupSnapshot { Batches = batches, CandidateBytes = batches.Sum(b => b.Bytes), ObservedTempBytes = (1000 + iteration * 2) * 1048576L, InventoryComplete = complete, Message = "synthetic stress" };
        }
        [STAThread]
        private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                const long GiB = 1024L * 1024 * 1024;
                Random random = new Random(52016);
                int invariants = 0, highWater = 0, successful = 0, failures = 0, cancellations = 0;
                using (Form owner = new Form())
                {
                    IntPtr handle = owner.Handle;
                    using (AdventureController c = new AdventureController(owner, @"D:\骑士助手-v5.2\源代码\Assets", true))
                    {
                        ((System.Windows.Forms.Timer)Read(c, "timer")).Stop(); Set(c, "enabled", true);
                        Set(c, "simulatedCleaner", new Func<CleanupBatch, CancellationToken, Task<CleanupResult>>(delegate(CleanupBatch batch, CancellationToken token)
                        {
                            if (token.IsCancellationRequested) throw new InvalidOperationException("unexpected pre-cancelled fake worker");
                            int outcome = random.Next(3);
                            if (outcome == 0) { successful++; return Task.FromResult(new CleanupResult { FreedBytes = batch.Bytes, DeletedFiles = batch.FilesCount }); }
                            if (outcome == 1) failures++; else cancellations++;
                            return Task.FromResult(new CleanupResult { Cancelled = outcome == 2, Message = "synthetic residue", RemainingCandidates = batch.Candidates.ToList() });
                        }));
                        AdventureBattle battle = c.Battle;
                        for (int step = 0; step < 1200; step++)
                        {
                            switch (random.Next(9))
                            {
                                case 0: Call(c, "ApplyInventory", Inventory(step, true), true); break;
                                case 1: Call(c, "ApplyInventory", Inventory(step, random.Next(4) != 0), false); break;
                                case 2: Call(c, "ObserveDisk", DiskSpaceSnapshot.FromValues(100 * GiB, GiB / 2), step * 300.0); break;
                                case 3: Call(c, "ObserveDisk", DiskSpaceSnapshot.FromValues(100 * GiB, 2 * GiB), step * 300.0); break;
                                case 4:
                                    if (EnemyFieldBudget.CanAddBoss(battle.Enemies, false)) battle.Enemies.Add(new AdventureEnemy { Id = "synthetic-memory-" + step, IsBoss = true, MonsterKind = "boss", MaxHealth = 600, Health = 600 });
                                    break;
                                case 5:
                                    AdventureEnemy victim = battle.Enemies.FirstOrDefault(e => !e.Defeated && !e.Cleaning && !e.Failed && !e.IsDiskWarning);
                                    if (victim != null) { battle.Authorize(victim); victim.Health = 1; battle.Cooldown = 0; foreach (AdventureEnemy killed in battle.Strike(victim)) Call(c, "BeginCleanup", killed); }
                                    break;
                                case 6:
                                    foreach (AdventureEnemy killed in battle.ClearFieldSnapshot()) Call(c, "BeginCleanup", killed);
                                    break;
                                case 7: Call(c, "CancelPending"); break;
                                case 8:
                                    battle.Progress.Dead = true; battle.StopFightingAfterDeath(); Call(c, "PumpCleanup"); battle.Progress.Dead = false;
                                    break;
                            }
                            Call(c, "DrainCleanup");
                            battle.Tick(.17);
                            battle.Enemies.RemoveAll(e => e.Defeated && e.IsVisuallyHidden);
                            highWater = Math.Max(highWater, battle.Enemies.Count);
                            if (!EnemyFieldBudget.IsWithinBudget(battle.Enemies)) throw new InvalidOperationException("field budget violated at step " + step);
                            invariants++;
                            List<CleanupCandidate> owned = battle.Enemies.Where(e => e.Batch != null).SelectMany(e => e.Batch.Candidates).ToList();
                            if (owned.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != owned.Count) throw new InvalidOperationException("file owned by more than one monster at step " + step);
                            invariants++;
                            if (battle.Enemies.Any(e => e.KillRewarded && (e.Health != 0 || !e.VisualDefeated))) throw new InvalidOperationException("dead actor revived at step " + step);
                            invariants++;
                        }
                        if (Read(c, "connectivity") != null || (bool)Read(c, "scanBusy") || (bool)Read(c, "diskBusy")) throw new InvalidOperationException("synthetic controller started an external service");
                    }
                }
                Console.WriteLine("Enemy budget stress: " + invariants + " invariants passed across 1200 seeded transitions; maximum retained actors " + highWater + ".");
                Console.WriteLine("Synthetic cleaner outcomes: " + successful + " success, " + failures + " failure, " + cancellations + " cancellation. No real files, network, disk samples, or progress writes.");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.ToString()); return 1; }
        }
    }
}
