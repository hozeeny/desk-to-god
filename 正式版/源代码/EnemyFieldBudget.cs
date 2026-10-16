using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NetBunny
{
    // A pure memory budget and batch-packaging helper. No scan, deletion, authorization,
    // actor removal, or window creation occurs here; those remain controller decisions.
    internal static class EnemyFieldBudget
    {
        internal const int MaxEnemies = 11;
        internal const int MaxOrdinaryEnemies = 9;
        internal const int ReservedBossSlots = 2;

        private struct Usage
        {
            internal int Total, Ordinary, MemoryBosses, DiskBosses;
            internal int Bosses { get { return MemoryBosses + DiskBosses; } }
        }

        private static Usage Count(IEnumerable<AdventureEnemy> enemies)
        {
            if (enemies == null) throw new ArgumentNullException("enemies");
            Usage usage = new Usage();
            foreach (AdventureEnemy enemy in enemies)
            {
                if (enemy == null) continue;
                // Retained dying, failed, hidden and cleanup-in-flight objects still own a slot.
                usage.Total++;
                if (enemy.IsDiskBoss) usage.DiskBosses++;
                else if (enemy.IsBoss) usage.MemoryBosses++;
                else usage.Ordinary++;
            }
            return usage;
        }

        internal static int CountEnemies(IEnumerable<AdventureEnemy> enemies) { return Count(enemies).Total; }
        internal static int CountOrdinary(IEnumerable<AdventureEnemy> enemies) { return Count(enemies).Ordinary; }
        internal static int CountBosses(IEnumerable<AdventureEnemy> enemies) { return Count(enemies).Bosses; }

        internal static bool IsWithinBudget(IEnumerable<AdventureEnemy> enemies)
        {
            Usage usage = Count(enemies);
            return usage.Total <= MaxEnemies && usage.Ordinary <= MaxOrdinaryEnemies &&
                usage.MemoryBosses <= 1 && usage.DiskBosses <= 1;
        }

        internal static int AvailableOrdinarySlots(IEnumerable<AdventureEnemy> enemies)
        {
            Usage usage = Count(enemies);
            if (usage.MemoryBosses > 1 || usage.DiskBosses > 1) return 0;
            return Math.Max(0, Math.Min(MaxOrdinaryEnemies - usage.Ordinary, MaxEnemies - usage.Total));
        }

        internal static bool CanAddOrdinary(IEnumerable<AdventureEnemy> enemies)
        {
            return AvailableOrdinarySlots(enemies) > 0;
        }

        internal static bool CanAddBoss(IEnumerable<AdventureEnemy> enemies, bool isDiskBoss)
        {
            Usage usage = Count(enemies);
            return usage.Total < MaxEnemies && usage.Ordinary <= MaxOrdinaryEnemies &&
                usage.Bosses < ReservedBossSlots &&
                (isDiskBoss ? usage.DiskBosses == 0 : usage.MemoryBosses == 0);
        }

        // Whole input batches are assigned to balanced groups. A source batch is not split
        // merely to create more monsters. Original CleanupCandidate object identities and all
        // file metadata remain intact. Exact duplicate snapshots are included only once.
        internal static List<CleanupBatch> CoalesceBatches(IEnumerable<CleanupBatch> source, int maxGroups)
        {
            if (source == null) throw new ArgumentNullException("source");
            if (maxGroups < 0) throw new ArgumentOutOfRangeException("maxGroups");
            List<CleanupBatch> inputs = source.ToList();
            if (inputs.Any(batch => batch == null)) throw new ArgumentException("批次列表不能包含空项。", "source");
            CleanupBatch[] originalOrder = inputs.ToArray();
            inputs.Sort(delegate(CleanupBatch a, CleanupBatch b) { return StringComparer.Ordinal.Compare(a.Id, b.Id); });
            Dictionary<string, CleanupCandidate> byPath = new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);
            List<GroupUnit> units = new List<GroupUnit>();
            long allBytes = 0;
            bool removedDuplicates = false;
            foreach (CleanupBatch batch in inputs)
            {
                GroupUnit unit = new GroupUnit { Key = batch.Id };
                foreach (CleanupCandidate candidate in batch.Candidates)
                {
                    if (candidate == null || String.IsNullOrWhiteSpace(candidate.Path) || candidate.Length < 0)
                        throw new InvalidDataException("临时文件快照无效，请重新扫描。");
                    CleanupCandidate prior;
                    if (byPath.TryGetValue(candidate.Path, out prior))
                    {
                        if (!SameIdentity(prior, candidate))
                            throw new InvalidDataException("同一路径存在不同文件快照，不能合并授权；请重新扫描。");
                        removedDuplicates = true;
                        continue;
                    }
                    byPath.Add(candidate.Path, candidate);
                    unit.Candidates.Add(candidate);
                    unit.Bytes = checked(unit.Bytes + candidate.Length);
                    allBytes = checked(allBytes + candidate.Length);
                }
                if (unit.Candidates.Count > 0) units.Add(unit);
            }
            if (units.Count == 0) return new List<CleanupBatch>();
            int count = Math.Min(MaxOrdinaryEnemies, Math.Min(maxGroups, units.Count));
            if (count <= 0)
                throw new InvalidOperationException("桌面怪物名额已满；必须保留或延后这些批次，不能截断候选文件。");
            // With enough slots retain the source batch objects, order and IDs exactly;
            // existing click authorization can then keep referring to its original batch.
            if (!removedDuplicates && units.Count <= count)
                return originalOrder.Where(batch => batch.FilesCount > 0).ToList();
            units.Sort(delegate(GroupUnit a, GroupUnit b)
            {
                int comparison = b.Bytes.CompareTo(a.Bytes);
                return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(a.Key, b.Key);
            });
            List<CleanupCandidate>[] groups = new List<CleanupCandidate>[count];
            long[] bytes = new long[count];
            for (int i = 0; i < count; i++) groups[i] = new List<CleanupCandidate>();
            for (int unitIndex = 0; unitIndex < units.Count; unitIndex++)
            {
                int chosen = unitIndex < count ? unitIndex : 0;
                if (unitIndex >= count)
                    for (int i = 1; i < count; i++)
                        if (bytes[i] < bytes[chosen] || (bytes[i] == bytes[chosen] && groups[i].Count < groups[chosen].Count)) chosen = i;
                groups[chosen].AddRange(units[unitIndex].Candidates);
                bytes[chosen] = checked(bytes[chosen] + units[unitIndex].Bytes);
            }
            List<CleanupBatch> output = new List<CleanupBatch>();
            for (int i = 0; i < groups.Length; i++) output.Add(new CleanupBatch(groups[i], i + 1));
            return output;
        }

        private sealed class GroupUnit
        {
            internal string Key;
            internal long Bytes;
            internal readonly List<CleanupCandidate> Candidates = new List<CleanupCandidate>();
        }

        private static bool SameIdentity(CleanupCandidate a, CleanupCandidate b)
        {
            return String.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase) &&
                a.Volume == b.Volume && a.IndexHigh == b.IndexHigh && a.IndexLow == b.IndexLow &&
                a.Length == b.Length && a.Written == b.Written && a.Created == b.Created;
        }

        internal static string SelfTest()
        {
            int checks = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Enemy field budget: " + label);
                checks++;
            };
            Func<int, long, CleanupCandidate> file = delegate(int id, long size)
            {
                return new CleanupCandidate { Path = @"C:\synthetic-only\file-" + id.ToString("D3") + ".tmp",
                    Length = size, Written = 100 + id, Created = 50 + id, Volume = 7, IndexHigh = 0, IndexLow = (uint)id };
            };
            List<CleanupBatch> originals = new List<CleanupBatch>();
            List<CleanupCandidate> originalFiles = new List<CleanupCandidate>();
            for (int i = 0; i < 12; i++)
            {
                List<CleanupCandidate> files = new List<CleanupCandidate> { file(i * 2, 1000 + i), file(i * 2 + 1, 2000 + i) };
                originals.Add(new CleanupBatch(files, i + 1)); originalFiles.AddRange(files);
            }
            List<CleanupBatch> packed = CoalesceBatches(originals, 6);
            List<CleanupCandidate> packedFiles = packed.SelectMany(batch => batch.Candidates).ToList();
            check(packed.Count == 6, "twelve scanned batches coalesce into six ordinary monsters");
            check(packedFiles.Count == originalFiles.Count && packedFiles.Distinct().Count() == originalFiles.Count, "no original file missing or duplicated");
            check(originalFiles.All(f => packedFiles.Any(candidate => Object.ReferenceEquals(f, candidate))), "original file objects and identity metadata retained");
            check(packed.Sum(batch => batch.Bytes) == originals.Sum(batch => batch.Bytes), "total candidate bytes unchanged");
            check(originals.Count == 12 && originals.All(batch => batch.FilesCount == 2), "source batches remain untouched");
            check(CoalesceBatches(originals.AsEnumerable().Reverse(), 6).Select(batch => batch.Id).SequenceEqual(packed.Select(batch => batch.Id)), "grouping stable when input order changes");
            List<CleanupBatch> narrow = CoalesceBatches(originals, 2);
            check(narrow.Count == 2 && narrow.Sum(batch => batch.FilesCount) == originalFiles.Count, "recovery with two slots retains all files");
            check(CoalesceBatches(originals, 99).Count == 9, "caller cannot exceed fixed ordinary budget");
            check(CoalesceBatches(originals.Take(1), 6).Count == 1, "one source batch is not split into extra monsters");
            check(CoalesceBatches(originals.Take(2), 6).SequenceEqual(originals.Take(2)), "sufficient slots preserve original batch objects, IDs and order");
            check(CoalesceBatches(new CleanupBatch[0], 0).Count == 0, "empty input needs no slots");
            check(CoalesceBatches(new[] { new CleanupBatch(new List<CleanupCandidate>(), 1) }, 0).Count == 0, "empty source batch creates no monster");
            bool rejected = false;
            try { CoalesceBatches(originals, 0); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && originals.Sum(batch => batch.FilesCount) == 24, "no-space result is explicit and cannot silently discard data");
            rejected = false; try { CoalesceBatches(originals, -1); } catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "negative group budget rejected");
            check(CoalesceBatches(originals.Concat(new[] { originals[0] }), 6).Sum(batch => batch.FilesCount) == 24, "duplicate batch does not duplicate file ownership");
            CleanupCandidate duplicate = file(0, 1000);
            check(CoalesceBatches(originals.Concat(new[] { new CleanupBatch(new List<CleanupCandidate> { duplicate }, 44) }), 6).Sum(batch => batch.FilesCount) == 24, "same metadata from another snapshot counts once");
            duplicate.Path = duplicate.Path.ToUpperInvariant();
            check(CoalesceBatches(originals.Concat(new[] { new CleanupBatch(new List<CleanupCandidate> { duplicate }, 44) }), 6).Sum(batch => batch.FilesCount) == 24, "Windows path casing cannot duplicate cleanup ownership");
            CleanupCandidate changed = file(0, 1000); changed.Created++;
            rejected = false; try { CoalesceBatches(new[] { originals[0], new CleanupBatch(new List<CleanupCandidate> { changed }, 2) }, 2); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "same path with changed creation identity is not merged");
            changed = file(0, 1000); changed.IndexLow++;
            rejected = false; try { CoalesceBatches(new[] { originals[0], new CleanupBatch(new List<CleanupCandidate> { changed }, 2) }, 2); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "same path with replaced file identity rejected");
            List<CleanupBatch> zeros = new List<CleanupBatch>();
            for (int i = 0; i < 12; i++) zeros.Add(new CleanupBatch(new List<CleanupCandidate> { file(100 + i, 0) }, i));
            check(CoalesceBatches(zeros, 6).Count == 6 && CoalesceBatches(zeros, 6).All(batch => batch.FilesCount == 2), "zero-byte batches balanced without loss");

            List<AdventureEnemy> enemies = new List<AdventureEnemy>();
            List<CleanupBatch> budgetPacked = CoalesceBatches(originals, MaxOrdinaryEnemies);
            check(IsWithinBudget(enemies) && AvailableOrdinarySlots(enemies) == 9, "empty field reserves two boss slots");
            for (int i = 0; i < 9; i++) enemies.Add(new AdventureEnemy { Id = "ordinary-" + i, Batch = budgetPacked[i] });
            check(IsWithinBudget(enemies) && !CanAddOrdinary(enemies), "ordinary monsters stop at nine even when total below eleven");
            check(CanAddBoss(enemies, false) && CanAddBoss(enemies, true), "two distinct boss roles remain available");
            AdventureEnemy memory = new AdventureEnemy { Id = "memory", IsBoss = true };
            enemies.Add(memory);
            check(IsWithinBudget(enemies) && !CanAddBoss(enemies, false) && CanAddBoss(enemies, true), "existing memory boss only leaves disk boss slot");
            AdventureEnemy disk = new AdventureEnemy { Id = "disk", IsBoss = true, IsDiskBoss = true };
            enemies.Add(disk);
            check(IsWithinBudget(enemies) && CountEnemies(enemies) == 11 && CountBosses(enemies) == 2, "nine ordinary plus both bosses fills exactly eleven slots");
            check(!CanAddBoss(enemies, false) && !CanAddBoss(enemies, true) && !CanAddOrdinary(enemies), "full field admits no further enemy");
            enemies[0].StartShatter(); enemies[0].DeathElapsed = 1; enemies[0].Cleaning = enemies[0].Authorized = true;
            check(CountOrdinary(enemies) == 9 && AvailableOrdinarySlots(enemies) == 0, "hidden in-flight monster still occupies a logical slot");
            enemies[0].Defeated = true; enemies[0].Cleaning = false;
            check(!CanAddOrdinary(enemies), "settled but retained corpse still occupies a slot");
            enemies.RemoveAt(0);
            check(AvailableOrdinarySlots(enemies) == 1, "slot opens only after object retirement");
            List<CleanupBatch> recovered = CoalesceBatches(originals, AvailableOrdinarySlots(enemies));
            check(recovered.Count == 1 && recovered.Sum(batch => batch.FilesCount) == 24, "single recovery slot packages all residue without truncation");
            enemies.Add(new AdventureEnemy { Id = "residue", Batch = recovered[0] });
            check(IsWithinBudget(enemies) && CountEnemies(enemies) == 11, "replacement rather than append preserves total limit");
            memory.Defeated = true; memory.StartShatter();
            check(!CanAddBoss(enemies, false), "fading boss blocks same-role replacement until retirement");
            enemies.Remove(memory);
            check(CanAddBoss(enemies, false) && !CanAddBoss(enemies, true), "retired memory boss releases only its own role");
            enemies.Add(new AdventureEnemy { Id = "invalid-extra-ordinary" });
            check(!IsWithinBudget(enemies) && !CanAddBoss(enemies, false), "tenth ordinary is invalid even with no memory boss");
            enemies.RemoveAt(enemies.Count - 1);
            enemies.Add(new AdventureEnemy { Id = "duplicate-disk", IsBoss = true, IsDiskBoss = true });
            check(!IsWithinBudget(enemies) && AvailableOrdinarySlots(enemies) == 0, "duplicate boss role cannot disguise an invalid field");
            enemies.Clear();
            enemies.Add(new AdventureEnemy { Id = "disk-flag-only", IsDiskBoss = true });
            check(CountBosses(enemies) == 1 && CountOrdinary(enemies) == 0 && !CanAddBoss(enemies, true), "disk role is counted defensively even without generic boss flag");
            return "Enemy field budget: " + checks + " assertions passed (synthetic data only; no filesystem, cleanup, network, or progress operations).";
        }
    }
}

