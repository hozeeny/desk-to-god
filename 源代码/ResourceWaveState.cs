using System;
using System.Diagnostics;

namespace NetBunny
{
    // Pure admission state for finite waves. Observing resources never authorizes deletion.
    // The controller must keep current CleanupBatch ownership separate from this gate.
    internal sealed class ResourceWaveState
    {
        internal const long TempGrowthThresholdBytes = 1024L * 1024;
        internal const double MemoryGrowthThresholdPoints = 5.0;
        internal const double AutomaticWaveCooldownSeconds = 30.0;
        private bool hasBegunWave, inventoryKnown, memoryKnown, tempGrowth, memoryGrowth;
        private long inventoryBaseline, latestInventory;
        private double memoryBaseline, latestMemory;
        private readonly Func<double> now;
        private double automaticWaveReadyAt;

        internal ResourceWaveState(Func<double> now = null)
        {
            if (now != null) this.now = now;
            else
            {
                // Elapsed time is unaffected by changes to the Windows wall clock.
                Stopwatch elapsed = Stopwatch.StartNew();
                this.now = delegate { return elapsed.Elapsed.TotalSeconds; };
            }
        }

        internal bool WaveActive { get; private set; }
        internal int WaveNumber { get; private set; }
        internal bool HasBegunWave { get { return hasBegunWave; } }
        internal bool PendingGrowth { get { return tempGrowth || memoryGrowth; } }
        internal bool PendingTempGrowth { get { return tempGrowth; } }
        internal bool PendingMemoryGrowth { get { return memoryGrowth; } }
        internal bool InventoryKnown { get { return inventoryKnown; } }
        internal bool MemoryKnown { get { return memoryKnown; } }
        internal long LatestTrustedTempBytes { get { return latestInventory; } }
        internal long BaselineTempBytes { get { return inventoryBaseline; } }
        internal double LatestMemoryPercent { get { return latestMemory; } }
        internal double BaselineMemoryPercent { get { return memoryBaseline; } }
        internal long TempGrowthBytes { get { return inventoryKnown ? Math.Max(0L, latestInventory - inventoryBaseline) : 0L; } }
        internal double MemoryGrowthPoints { get { return memoryKnown ? Math.Max(0.0, latestMemory - memoryBaseline) : 0.0; } }
        internal double GrowthRatio
        {
            get
            {
                if (PendingGrowth) return 1.0;
                return Math.Min(1.0, Math.Max(TempGrowthBytes / (double)TempGrowthThresholdBytes,
                    MemoryGrowthPoints / MemoryGrowthThresholdPoints));
            }
        }
        internal double SecondsUntilNextWave
        {
            get { return !hasBegunWave || WaveActive ? 0.0 : Math.Max(0.0, automaticWaveReadyAt - now()); }
        }
        internal bool AutomaticWaveAllowed
        {
            get { return !WaveActive && (!hasBegunWave || PendingGrowth && SecondsUntilNextWave <= 0.0); }
        }
        internal string LastWaveReason { get; private set; }

        internal void ObserveInventory(long observedTempBytes, bool inventoryComplete)
        {
            // A bounded sample (including a 12-batch display window) is never a total.
            if (!inventoryComplete || observedTempBytes < 0) return;
            latestInventory = observedTempBytes;
            if (!inventoryKnown)
            {
                inventoryKnown = true; inventoryBaseline = observedTempBytes; return;
            }
            if (observedTempBytes < inventoryBaseline) inventoryBaseline = observedTempBytes;
            // Both values are non-negative and subtraction cannot overflow.
            if (observedTempBytes - inventoryBaseline >= TempGrowthThresholdBytes) tempGrowth = true;
        }

        internal void ObserveMemory(double usedPercent, bool available)
        {
            if (!available || Double.IsNaN(usedPercent) || Double.IsInfinity(usedPercent) || usedPercent < 0 || usedPercent > 100) return;
            latestMemory = usedPercent;
            if (!memoryKnown) { memoryKnown = true; memoryBaseline = usedPercent; return; }
            if (usedPercent < memoryBaseline) memoryBaseline = usedPercent;
            if (usedPercent - memoryBaseline >= MemoryGrowthThresholdPoints) memoryGrowth = true;
        }

        internal void RecordDeletion(long actualDeletedBytes)
        {
            if (!inventoryKnown || actualDeletedBytes <= 0) return;
            // Call after a real cleanup result and before accepting a post-cleanup census.
            // Lowering the expected baseline prevents the next old-file window from being
            // interpreted as new resource growth. Reward counters belong elsewhere.
            inventoryBaseline = actualDeletedBytes >= inventoryBaseline ? 0 : inventoryBaseline - actualDeletedBytes;
            latestInventory = actualDeletedBytes >= latestInventory ? 0 : latestInventory - actualDeletedBytes;
        }

        internal bool TryBeginWave(bool manualRequest)
        {
            if (WaveActive || (!manualRequest && !AutomaticWaveAllowed)) return false;
            LastWaveReason = manualRequest ? "用户重新扫描" : !hasBegunWave ? "首次扫描" :
                tempGrowth && memoryGrowth ? "临时文件与内存占用增长" : tempGrowth ? "临时文件增长" : "内存占用增长";
            hasBegunWave = true; WaveActive = true;
            if (WaveNumber < Int32.MaxValue) WaveNumber++;
            // Consume only the observations accepted before this wave. Later observations
            // can set new pending growth while this wave is still active.
            tempGrowth = memoryGrowth = false;
            if (inventoryKnown) inventoryBaseline = latestInventory;
            if (memoryKnown) memoryBaseline = latestMemory;
            return true;
        }

        internal void CompleteWave()
        {
            // The controller may report an empty field repeatedly. Only the first
            // active -> complete transition starts the rest period.
            if (!WaveActive) return;
            WaveActive = false;
            automaticWaveReadyAt = now() + AutomaticWaveCooldownSeconds;
        }

        internal static string SelfTest()
        {
            int checks = 0;
            Action<bool, string> assert = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Resource wave: " + label);
                checks++;
            };
            const long MiB = 1024L * 1024;
            double tick = 100.0;
            ResourceWaveState state = new ResourceWaveState(delegate { return tick; });
            assert(state.AutomaticWaveAllowed && !state.PendingGrowth, "first wave is allowed without inventing growth");
            assert(!state.HasBegunWave && state.SecondsUntilNextWave == 0 && state.GrowthRatio == 0, "fresh gate has no cooldown or invented accumulation");
            state.CompleteWave();
            assert(!state.HasBegunWave && state.AutomaticWaveAllowed && state.SecondsUntilNextWave == 0, "completion before any wave cannot delay first wave");
            state.ObserveInventory(100 * MiB, true); state.ObserveMemory(40, true);
            assert(state.InventoryKnown && state.MemoryKnown && !state.PendingGrowth, "initial trusted samples establish baselines");
            assert(state.TryBeginWave(false) && state.WaveActive && state.WaveNumber == 1, "first wave starts once");
            assert(state.HasBegunWave && state.TempGrowthBytes == 0 && state.MemoryGrowthPoints == 0, "starting wave exposes consumed baseline");
            assert(!state.TryBeginWave(false) && !state.TryBeginWave(true), "active wave cannot be duplicated even by manual rescan");
            state.RecordDeletion(30 * MiB);
            assert(state.BaselineTempBytes == 70 * MiB && state.LatestTrustedTempBytes == 70 * MiB, "real deletion reduces expected inventory");
            state.ObserveInventory(70 * MiB, true);
            assert(!state.PendingGrowth, "stable remaining old resources do not count as growth");
            state.ObserveInventory(12 * MiB, false);
            assert(state.LatestTrustedTempBytes == 70 * MiB && state.BaselineTempBytes == 70 * MiB && !state.PendingGrowth, "bounded old-file window is ignored as a census");
            state.CompleteWave();
            assert(!state.TryBeginWave(false) && !state.AutomaticWaveAllowed, "finishing a wave does not auto-refill old files");
            assert(state.SecondsUntilNextWave == AutomaticWaveCooldownSeconds, "completed first wave starts thirty second rest");
            tick += 10.0;
            state.CompleteWave();
            assert(state.SecondsUntilNextWave == 20.0, "repeated completion cannot restart cooldown");
            state.ObserveInventory(70 * MiB, true);
            assert(!state.TryBeginWave(false), "later full census of same old files still cannot refill");
            state.ObserveInventory(60 * MiB, true);
            assert(state.BaselineTempBytes == 60 * MiB && !state.PendingGrowth, "external reduction lowers baseline without new wave");
            state.ObserveInventory(60 * MiB + MiB / 2, true);
            assert(!state.PendingGrowth, "subthreshold growth waits");
            assert(state.TempGrowthBytes == MiB / 2 && state.GrowthRatio == 0.5, "partial temp growth is visible during cooldown");
            state.ObserveInventory(61 * MiB, true);
            assert(state.PendingTempGrowth && state.GrowthRatio == 1.0 && !state.AutomaticWaveAllowed, "threshold growth remains pending until cooldown expires");
            assert(!state.TryBeginWave(false) && state.PendingTempGrowth, "rejected early wave does not consume growth");
            tick += 19.999;
            assert(state.SecondsUntilNextWave > 0 && !state.AutomaticWaveAllowed, "wave is blocked immediately before cooldown boundary");
            tick = 130.0;
            assert(state.SecondsUntilNextWave == 0 && state.AutomaticWaveAllowed, "wave is admitted at exact cooldown boundary");
            assert(state.TryBeginWave(false) && !state.PendingGrowth && state.WaveNumber == 2 && state.BaselineTempBytes == 61 * MiB, "growth is consumed exactly once");
            state.ObserveInventory(61 * MiB, true); state.CompleteWave();
            assert(!state.TryBeginWave(false), "unchanged new baseline cannot duplicate wave");
            state.ObserveMemory(44.999, true);
            assert(!state.PendingMemoryGrowth, "memory threshold is five percentage points");
            assert(state.MemoryGrowthPoints > 4.99 && state.GrowthRatio > 0.99 && state.GrowthRatio < 1.0, "partial memory growth contributes visible progress");
            state.ObserveMemory(45, true);
            tick += AutomaticWaveCooldownSeconds;
            assert(state.PendingMemoryGrowth && state.TryBeginWave(false), "memory increase allows next wave");
            state.ObserveMemory(50, true);
            assert(state.PendingMemoryGrowth && !state.TryBeginWave(false), "growth during a wave remains pending");
            state.CompleteWave();
            assert(!state.TryBeginWave(false) && state.PendingMemoryGrowth, "active-wave growth waits through subsequent rest");
            tick += AutomaticWaveCooldownSeconds;
            assert(state.TryBeginWave(false) && !state.PendingGrowth && state.BaselineMemoryPercent == 50, "pending growth consumed after prior wave completes");
            state.CompleteWave();
            assert(!state.TryBeginWave(false) && state.TryBeginWave(true), "manual explicit rescan can inspect old resource backlog");
            assert(state.SecondsUntilNextWave == 0 && state.WaveActive && state.GrowthRatio == 0, "manual wave bypasses rest and resets accumulation");
            assert(state.LastWaveReason == "用户重新扫描", "manual wave reason is explicit");
            state.ObserveInventory(1000 * MiB, false);
            assert(!state.PendingTempGrowth && state.LatestTrustedTempBytes == 61 * MiB, "partial huge sample cannot misreport growth");
            state.ObserveInventory(-1, true);
            assert(state.LatestTrustedTempBytes == 61 * MiB, "invalid inventory ignored");
            state.ObserveMemory(Double.NaN, true); state.ObserveMemory(Double.PositiveInfinity, true); state.ObserveMemory(101, true); state.ObserveMemory(-1, true); state.ObserveMemory(90, false);
            assert(state.LatestMemoryPercent == 50 && !state.PendingMemoryGrowth, "invalid or unavailable memory cannot trigger");
            state.RecordDeletion(-1); state.RecordDeletion(0);
            assert(state.BaselineTempBytes == 61 * MiB, "nonpositive deletion ignored");
            state.RecordDeletion(Int64.MaxValue);
            assert(state.BaselineTempBytes == 0 && state.LatestTrustedTempBytes == 0, "deletion clamp never underflows");
            state.ObserveInventory(0, true); state.CompleteWave();
            assert(!state.PendingGrowth && !state.TryBeginWave(false), "zero remaining inventory cannot auto-refill");

            double concurrentTick = 0;
            ResourceWaveState concurrent = new ResourceWaveState(delegate { return concurrentTick; });
            concurrent.ObserveInventory(100 * MiB, true); concurrent.TryBeginWave(false);
            concurrent.ObserveInventory(105 * MiB, true); concurrent.RecordDeletion(10 * MiB);
            assert(concurrent.PendingTempGrowth && concurrent.BaselineTempBytes == 90 * MiB && concurrent.LatestTrustedTempBytes == 95 * MiB, "actual growth remains pending through deletion accounting");
            concurrent.ObserveInventory(95 * MiB, true); concurrent.CompleteWave();
            concurrentTick += AutomaticWaveCooldownSeconds;
            assert(concurrent.TryBeginWave(false) && concurrent.BaselineTempBytes == 95 * MiB && !concurrent.PendingGrowth, "post-deletion full census consumes true growth once");
            concurrent.CompleteWave(); concurrent.ObserveInventory(95 * MiB, true);
            assert(!concurrent.TryBeginWave(false), "remaining old-file backlog cannot create another wave");

            double unknownTick = 0;
            ResourceWaveState initiallyUnknown = new ResourceWaveState(delegate { return unknownTick; });
            initiallyUnknown.ObserveInventory(999 * MiB, false); initiallyUnknown.RecordDeletion(10 * MiB);
            assert(!initiallyUnknown.InventoryKnown && initiallyUnknown.TryBeginWave(false), "unknown census still permits only initial wave");
            initiallyUnknown.CompleteWave(); initiallyUnknown.ObserveInventory(100 * MiB, true);
            assert(!initiallyUnknown.PendingGrowth && !initiallyUnknown.TryBeginWave(false), "first complete census after a partial one sets baseline without false increase");
            initiallyUnknown.ObserveMemory(80, true); initiallyUnknown.ObserveMemory(70, true);
            assert(initiallyUnknown.BaselineMemoryPercent == 70 && !initiallyUnknown.PendingGrowth, "memory reduction alone does not spawn");
            initiallyUnknown.ObserveMemory(75, true);
            assert(initiallyUnknown.PendingMemoryGrowth, "later real memory rise after recovery can spawn");

            double stableTick = 0;
            ResourceWaveState stable = new ResourceWaveState(delegate { return stableTick; });
            stable.ObserveInventory(10 * MiB, true); stable.ObserveMemory(30, true); stable.TryBeginWave(false); stable.CompleteWave();
            stableTick = 86400;
            stable.ObserveInventory(10 * MiB, true); stable.ObserveMemory(30, true);
            assert(stable.SecondsUntilNextWave == 0 && !stable.PendingGrowth && stable.GrowthRatio == 0 && !stable.TryBeginWave(false), "time alone and unchanged samples never fabricate a wave");
            stable.ObserveInventory(10 * MiB + MiB / 4, true); stable.ObserveMemory(32.5, true);
            assert(stable.TempGrowthBytes == MiB / 4 && stable.MemoryGrowthPoints == 2.5 && stable.GrowthRatio == 0.5, "independent partial growth uses highest resource ratio without adding unlike units");
            stable.ObserveInventory(100 * MiB, false); stable.ObserveInventory(-1, true); stable.ObserveMemory(99, false);
            stable.ObserveMemory(Double.NaN, true); stable.ObserveMemory(Double.NegativeInfinity, true);
            assert(stable.TempGrowthBytes == MiB / 4 && stable.MemoryGrowthPoints == 2.5 && stable.GrowthRatio == 0.5 && !stable.PendingGrowth, "partial census and invalid observations do not change displayed accumulation");
            stable.ObserveInventory(11 * MiB, true);
            assert(stable.PendingTempGrowth && stable.GrowthRatio == 1, "complete census at exact byte threshold latches readiness");
            stable.ObserveInventory(9 * MiB, true); stable.ObserveMemory(20, true);
            assert(stable.TempGrowthBytes == 0 && stable.MemoryGrowthPoints == 0 && stable.GrowthRatio == 1, "latched accepted growth remains ready after a later resource drop");
            assert(stable.TryBeginWave(false) && stable.GrowthRatio == 0 && stable.BaselineTempBytes == 9 * MiB, "latched growth is consumed once with latest real baseline");
            stable.ObserveMemory(25, true); stableTick += 600;
            assert(!stable.AutomaticWaveAllowed && stable.PendingMemoryGrowth, "elapsed active-wave time cannot bypass active field");
            stable.CompleteWave();
            assert(stable.SecondsUntilNextWave == 30 && !stable.AutomaticWaveAllowed, "long active wave still gets rest from completion time");
            stableTick += 29.0;
            assert(stable.TryBeginWave(true) && stable.WaveActive && !stable.PendingGrowth && stable.LastWaveReason == "用户重新扫描", "manual scan bypasses cooldown even when growth is waiting");
            stableTick += 4.0; stable.CompleteWave();
            assert(stable.SecondsUntilNextWave == 30, "manual wave completion starts its own rest");
            stable.ObserveInventory(Int64.MaxValue, true);
            assert(stable.TempGrowthBytes == Int64.MaxValue - 9 * MiB && stable.GrowthRatio == 1, "maximum inventory size has finite clamped accumulation without overflow");
            stableTick += 30.0;
            assert(stable.AutomaticWaveAllowed && stable.TryBeginWave(false), "pending maximum-size growth waits then admits normally");
            return "Resource waves: " + checks + " assertions passed (pure state; no filesystem or network operations).";
        }
    }
}

