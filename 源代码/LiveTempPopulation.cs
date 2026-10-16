using System;
using System.Collections.Generic;
using System.Linq;

namespace NetBunny
{
    // Observations and actor ownership only. Never grants deletion authorization.
    internal sealed class LiveTempPopulation
    {
        private readonly Dictionary<string, CleanupCandidate> known = new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CleanupCandidate> represented = new Dictionary<string, CleanupCandidate>(StringComparer.OrdinalIgnoreCase);
        internal long TotalBytes { get { return known.Values.Sum(f => f.Length); } }
        internal int TotalFiles { get { return known.Count; } }
        internal long EligibleBytes { get { return known.Values.Where(f => f.EligibleForCleanup).Sum(f => f.Length); } }
        internal int EligibleFiles { get { return known.Values.Count(f => f.EligibleForCleanup); } }
        internal bool Complete { get; private set; }
        internal bool Known { get; private set; }
        internal IEnumerable<CleanupCandidate> Files { get { return known.Values; } }
        internal void Observe(CleanupSnapshot snapshot, bool retry)
        {
            if (snapshot == null) return;
            IEnumerable<CleanupCandidate> input = snapshot.ObservedFilesAvailable ? snapshot.ObservedFiles : snapshot.Batches.SelectMany(b => b.Candidates);
            List<CleanupCandidate> files = input.Where(f => f != null && f.Length >= 0 && !String.IsNullOrEmpty(f.Path)).ToList();
            if (!snapshot.ObservedFilesAvailable) foreach (CleanupCandidate f in files) f.EligibleForCleanup = true;
            if (snapshot.InventoryComplete && snapshot.ObservedFilesAvailable)
            {
                HashSet<string> seen = new HashSet<string>(files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
                foreach (string path in known.Keys.ToArray()) if (!seen.Contains(path)) { known.Remove(path); represented.Remove(path); }
            }
            foreach (CleanupCandidate f in files) known[f.Path] = f;
            if (retry) represented.Clear();
            Complete = snapshot.InventoryComplete; Known = true;
        }
        internal CleanupBatch Refresh(CleanupBatch original)
        {
            if (original == null) return null;
            List<CleanupCandidate> files = original.Candidates.Where(f => known.ContainsKey(f.Path)).Select(f => known[f.Path]).ToList();
            foreach (CleanupCandidate file in files) represented[file.Path] = file;
            return files.Count == 0 ? null : new CleanupBatch(files, 1);
        }
        private static bool Increased(CleanupCandidate current, CleanupCandidate previous)
        {
            return current.Volume != previous.Volume || current.IndexHigh != previous.IndexHigh || current.IndexLow != previous.IndexLow || current.Created != previous.Created || current.Length > previous.Length;
        }
        internal List<CleanupBatch> TakePending(IEnumerable<AdventureEnemy> enemies, int slots)
        {
            HashSet<string> owned = new HashSet<string>(enemies.Where(e => e.Batch != null).SelectMany(e => e.Batch.Candidates).Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            List<CleanupCandidate> pending = known.Values.Where(f => !owned.Contains(f.Path) && (!represented.ContainsKey(f.Path) || Increased(f, represented[f.Path])))
                .OrderByDescending(f => f.Created).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Take(Math.Max(0, slots) * 128).ToList();
            int groups = Math.Min(Math.Max(0, slots), (pending.Count + 15) / 16);
            List<CleanupBatch> result = new List<CleanupBatch>();
            if (groups == 0) return result;
            List<CleanupCandidate>[] buckets = new List<CleanupCandidate>[groups];
            for (int i = 0; i < groups; i++) buckets[i] = new List<CleanupCandidate>();
            for (int i = 0; i < pending.Count; i++) { buckets[i % groups].Add(pending[i]); represented[pending[i].Path] = pending[i]; }
            for (int i = 0; i < groups; i++) result.Add(new CleanupBatch(buckets[i], i + 1));
            return result;
        }
        internal void Deleted(CleanupBatch batch, CleanupResult result)
        {
            if (batch == null) return;
            HashSet<string> remaining = new HashSet<string>(result.RemainingCandidates.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
            // Service supplies every skipped/remaining identity. Unknown results never remove data.
            if (result.DeletedFiles <= 0 || batch.FilesCount - remaining.Count != result.DeletedFiles) return;
            foreach (CleanupCandidate file in batch.Candidates)
                if (!remaining.Contains(file.Path)) { known.Remove(file.Path); represented.Remove(file.Path); }
        }
        internal static string SelfTest()
        {
            int n = 0; Action<bool,string> check = delegate(bool x,string s) { if (!x) throw new Exception("Live Temp: " + s); n++; };
            Func<string,long,bool,CleanupCandidate> file = delegate(string p,long len,bool safe) { return new CleanupCandidate { Path = p, Length = len, Created = 1, EligibleForCleanup = safe }; };
            Func<CleanupCandidate[],bool,CleanupSnapshot> snap = delegate(CleanupCandidate[] f,bool full) { return new CleanupSnapshot { ObservedFilesAvailable = true, ObservedFiles = f.ToList(), InventoryComplete = full }; };
            LiveTempPopulation pop = new LiveTempPopulation(); List<AdventureEnemy> actors = new List<AdventureEnemy>();
            CleanupCandidate a = file("a", 10, true), b = file("b", 0, false);
            pop.Observe(snap(new[] {a},true),false); List<CleanupBatch> first = pop.TakePending(actors,9); actors.Add(new AdventureEnemy {Batch=first[0]});
            check(first.Count == 1 && pop.EligibleBytes == 10, "initial live safe total");
            pop.Observe(snap(new[] {a,b},true),false); List<CleanupBatch> fresh = pop.TakePending(actors,8);
            check(fresh.Count == 1 && fresh[0].Candidates[0] == b && pop.TotalFiles == 2 && pop.EligibleFiles == 1, "zero-byte recent creation spawns without byte threshold");
            actors.Add(new AdventureEnemy {Batch=fresh[0]});
            check(pop.TakePending(actors,7).Count == 0, "duplicate scan cannot duplicate ownership");
            actors.RemoveAt(1); check(pop.TakePending(actors,8).Count == 0, "killed recent file is not farmed by unchanged scans");
            CleanupCandidate grown = file("b",1,false); pop.Observe(snap(new[]{a,grown},true),false);
            check(pop.TakePending(actors,8).Count == 1, "one-byte growth can reappear");
            pop.Observe(snap(new CleanupCandidate[0],false),false); check(pop.TotalFiles == 2 && !pop.Complete, "partial scan cannot erase real totals");
            pop.Observe(snap(new[]{grown},true),false); check(pop.TotalFiles == 1 && pop.EligibleBytes == 0 && pop.Refresh(first[0]) == null, "external deletion updates safe total and actor");
            CleanupCandidate[] many = Enumerable.Range(0,300).Select(i=>file("n"+i,1,true)).ToArray(); pop.Observe(snap(many,true),false);
            List<CleanupBatch> capped = pop.TakePending(new AdventureEnemy[0],9); check(capped.Count == 9 && capped.Sum(x=>x.FilesCount)==300, "bounded groups retain candidates");
            check(pop.TakePending(new AdventureEnemy[0],0).Count==0,"full field keeps pending");
            CleanupResult deleted = new CleanupResult {DeletedFiles=capped[0].FilesCount,FreedBytes=capped[0].Bytes}; pop.Deleted(capped[0],deleted);
            check(pop.EligibleFiles==300-deleted.DeletedFiles,"actual deletion updates census immediately");
            return "Live Temp population: " + n + " checks passed (synthetic, no deletion).";
        }
    }
}
