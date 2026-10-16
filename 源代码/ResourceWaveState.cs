using System;

namespace NetBunny
{
    // Pure admission state for finite waves. Observing resources never authorizes deletion.
    // The controller must keep current CleanupBatch ownership separate from this gate.
    internal sealed class ResourceWaveState
    {
        internal const long TempGrowthThresholdBytes = 1024L * 1024;
        internal const double MemoryGrowthThresholdPoints = 5.0;
        private bool hasBegunWave, inventoryKnown, memoryKnown, tempGrowth, memoryGrowth;
        private long inventoryBaseline, latestInventory;
        private double memoryBaseline, latestMemory;

        internal bool WaveActive { get; private set; }
        internal int WaveNumber { get; private set; }
        internal bool PendingGrowth { get { return tempGrowth || memoryGrowth; } }
        internal bool PendingTempGrowth { get { return tempGrowth; } }
        internal bool PendingMemoryGrowth { get { return memoryGrowth; } }
        internal bool InventoryKnown { get { return inventoryKnown; } }
        internal bool MemoryKnown { get { return memoryKnown; } }
        internal long LatestTrustedTempBytes { get { return latestInventory; } }
        internal long BaselineTempBytes { get { return inventoryBaseline; } }
        internal double LatestMemoryPercent { get { return latestMemory; } }
        internal double BaselineMemoryPercent { get { return memoryBaseline; } }
        internal bool AutomaticWaveAllowed { get { return !WaveActive && (!hasBegunWave || PendingGrowth); } }
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
            if (WaveActive || (!manualRequest && hasBegunWave && !PendingGrowth)) return false;
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

        internal void CompleteWave() { WaveActive = false; }

        internal static string SelfTest()
        {
            int checks = 0;
            Action<bool, string> assert = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Resource wave: " + label);
                checks++;
            };
            const long MiB = 1024L * 1024;
            ResourceWaveState state = new ResourceWaveState();
            assert(state.AutomaticWaveAllowed && !state.PendingGrowth, "first wave is allowed without inventing growth");
            state.ObserveInventory(100 * MiB, true); state.ObserveMemory(40, true);
            assert(state.InventoryKnown && state.MemoryKnown && !state.PendingGrowth, "initial trusted samples establish baselines");
            assert(state.TryBeginWave(false) && state.WaveActive && state.WaveNumber == 1, "first wave starts once");
            assert(!state.TryBeginWave(false) && !state.TryBeginWave(true), "active wave cannot be duplicated even by manual rescan");
            state.RecordDeletion(30 * MiB);
            assert(state.BaselineTempBytes == 70 * MiB && state.LatestTrustedTempBytes == 70 * MiB, "real deletion reduces expected inventory");
            state.ObserveInventory(70 * MiB, true);
            assert(!state.PendingGrowth, "stable remaining old resources do not count as growth");
            state.ObserveInventory(12 * MiB, false);
            assert(state.LatestTrustedTempBytes == 70 * MiB && state.BaselineTempBytes == 70 * MiB && !state.PendingGrowth, "bounded old-file window is ignored as a census");
            state.CompleteWave();
            assert(!state.TryBeginWave(false) && !state.AutomaticWaveAllowed, "finishing a wave does not auto-refill old files");
            state.ObserveInventory(70 * MiB, true);
            assert(!state.TryBeginWave(false), "later full census of same old files still cannot refill");
            state.ObserveInventory(60 * MiB, true);
            assert(state.BaselineTempBytes == 60 * MiB && !state.PendingGrowth, "external reduction lowers baseline without new wave");
            state.ObserveInventory(60 * MiB + MiB / 2, true);
            assert(!state.PendingGrowth, "subthreshold growth waits");
            state.ObserveInventory(61 * MiB, true);
            assert(state.PendingTempGrowth && state.AutomaticWaveAllowed, "small increases accumulate to one MiB");
            assert(state.TryBeginWave(false) && !state.PendingGrowth && state.WaveNumber == 2 && state.BaselineTempBytes == 61 * MiB, "growth is consumed exactly once");
            state.ObserveInventory(61 * MiB, true); state.CompleteWave();
            assert(!state.TryBeginWave(false), "unchanged new baseline cannot duplicate wave");
            state.ObserveMemory(44.999, true);
            assert(!state.PendingMemoryGrowth, "memory threshold is five percentage points");
            state.ObserveMemory(45, true);
            assert(state.PendingMemoryGrowth && state.TryBeginWave(false), "memory increase allows next wave");
            state.ObserveMemory(50, true);
            assert(state.PendingMemoryGrowth && !state.TryBeginWave(false), "growth during a wave remains pending");
            state.CompleteWave();
            assert(state.TryBeginWave(false) && !state.PendingGrowth && state.BaselineMemoryPercent == 50, "pending growth consumed after prior wave completes");
            state.CompleteWave();
            assert(!state.TryBeginWave(false) && state.TryBeginWave(true), "manual explicit rescan can inspect old resource backlog");
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

            ResourceWaveState concurrent = new ResourceWaveState();
            concurrent.ObserveInventory(100 * MiB, true); concurrent.TryBeginWave(false);
            concurrent.ObserveInventory(105 * MiB, true); concurrent.RecordDeletion(10 * MiB);
            assert(concurrent.PendingTempGrowth && concurrent.BaselineTempBytes == 90 * MiB && concurrent.LatestTrustedTempBytes == 95 * MiB, "actual growth remains pending through deletion accounting");
            concurrent.ObserveInventory(95 * MiB, true); concurrent.CompleteWave();
            assert(concurrent.TryBeginWave(false) && concurrent.BaselineTempBytes == 95 * MiB && !concurrent.PendingGrowth, "post-deletion full census consumes true growth once");
            concurrent.CompleteWave(); concurrent.ObserveInventory(95 * MiB, true);
            assert(!concurrent.TryBeginWave(false), "remaining old-file backlog cannot create another wave");

            ResourceWaveState initiallyUnknown = new ResourceWaveState();
            initiallyUnknown.ObserveInventory(999 * MiB, false); initiallyUnknown.RecordDeletion(10 * MiB);
            assert(!initiallyUnknown.InventoryKnown && initiallyUnknown.TryBeginWave(false), "unknown census still permits only initial wave");
            initiallyUnknown.CompleteWave(); initiallyUnknown.ObserveInventory(100 * MiB, true);
            assert(!initiallyUnknown.PendingGrowth && !initiallyUnknown.TryBeginWave(false), "first complete census after a partial one sets baseline without false increase");
            initiallyUnknown.ObserveMemory(80, true); initiallyUnknown.ObserveMemory(70, true);
            assert(initiallyUnknown.BaselineMemoryPercent == 70 && !initiallyUnknown.PendingGrowth, "memory reduction alone does not spawn");
            initiallyUnknown.ObserveMemory(75, true);
            assert(initiallyUnknown.PendingMemoryGrowth, "later real memory rise after recovery can spawn");
            return "Resource waves: " + checks + " assertions passed (pure state; no filesystem or network operations).";
        }
    }
}

