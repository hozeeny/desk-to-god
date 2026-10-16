using System;
using System.IO;

namespace NetBunny
{
    // Read-only facts about the C: volume. This class has no file-cleanup authority.
    internal sealed class DiskSpaceSnapshot
    {
        public bool Available { get; private set; }
        public string RootPath { get; private set; }
        public long TotalBytes { get; private set; }
        public long AvailableBytes { get; private set; }
        public long UsedBytes { get { return Available ? TotalBytes - AvailableBytes : 0; } }
        public double FreeRatio { get { return Available ? AvailableBytes / (double)TotalBytes : 0; } }
        public double UsedRatio { get { return Available ? 1.0 - FreeRatio : 0; } }
        public double FreePercent { get { return FreeRatio * 100.0; } }
        public double UsedPercent { get { return UsedRatio * 100.0; } }
        public string Error { get; private set; }
        public DateTime CapturedUtc { get; private set; }

        private DiskSpaceSnapshot() { RootPath = @"C:\"; Error = ""; CapturedUtc = DateTime.UtcNow; }

        public static DiskSpaceSnapshot FromValues(long totalBytes, long availableBytes)
        {
            if (totalBytes <= 0 || availableBytes < 0 || availableBytes > totalBytes)
                return Unavailable("磁盘容量数据无效，等待下次采样。");
            return new DiskSpaceSnapshot { Available = true, TotalBytes = totalBytes, AvailableBytes = availableBytes };
        }

        public static DiskSpaceSnapshot Unavailable(string message)
        {
            return new DiskSpaceSnapshot { Available = false, Error = String.IsNullOrWhiteSpace(message) ? "暂时无法读取 C 盘剩余空间。" : message };
        }
    }

    internal sealed class DiskSpaceService
    {
        private readonly Func<DiskSpaceSnapshot> reader;
        public DiskSpaceService() : this(ReadCDrive) { }
        internal DiskSpaceService(Func<DiskSpaceSnapshot> snapshotReader)
        {
            if (snapshotReader == null) throw new ArgumentNullException("snapshotReader");
            reader = snapshotReader;
        }

        // The injectable provider lets tests use synthetic samples without inspecting any disk.
        public DiskSpaceSnapshot Read()
        {
            try { return reader() ?? DiskSpaceSnapshot.Unavailable("磁盘采样没有返回数据。"); }
            catch (Exception error) { return DiskSpaceSnapshot.Unavailable("C 盘空间读取失败：" + error.Message); }
        }

        public static DiskSpaceSnapshot ReadCDrive()
        {
            try
            {
                DriveInfo drive = new DriveInfo(@"C:\");
                if (!drive.IsReady) return DiskSpaceSnapshot.Unavailable("C 盘尚未就绪。");
                // AvailableFreeSpace is the amount available to this process, including quota limits.
                long total = drive.TotalSize, available = drive.AvailableFreeSpace;
                return DiskSpaceSnapshot.FromValues(total, available);
            }
            catch (Exception error) { return DiskSpaceSnapshot.Unavailable("C 盘空间读取失败：" + error.Message); }
        }

        public static string SelfTest()
        {
            int checks = 0;
            Action<bool, string> assert = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Disk space: " + label);
                checks++;
            };
            const long GiB = 1024L * 1024 * 1024;
            DiskSpaceSnapshot sample = DiskSpaceSnapshot.FromValues(100 * GiB, 25 * GiB);
            assert(sample.Available && sample.RootPath == @"C:\", "valid C volume sample");
            assert(sample.TotalBytes == 100 * GiB && sample.AvailableBytes == 25 * GiB && sample.UsedBytes == 75 * GiB, "byte counts remain exact");
            assert(sample.FreeRatio == .25 && sample.UsedRatio == .75 && sample.FreePercent == 25 && sample.UsedPercent == 75, "progress bar ratios");
            assert(DiskSpaceSnapshot.FromValues(GiB, 0).Available, "zero free bytes is a valid full disk");
            assert(DiskSpaceSnapshot.FromValues(GiB, GiB).UsedBytes == 0, "empty disk has zero used bytes");
            assert(!DiskSpaceSnapshot.FromValues(0, 0).Available, "unknown capacity is not shown as a full disk");
            assert(!DiskSpaceSnapshot.FromValues(-1, 0).Available, "negative capacity rejected");
            assert(!DiskSpaceSnapshot.FromValues(GiB, -1).Available, "negative free space rejected");
            assert(!DiskSpaceSnapshot.FromValues(GiB, GiB + 1).Available, "impossible free space rejected");
            assert(DiskSpaceSnapshot.FromValues(Int64.MaxValue, Int64.MaxValue - 1).UsedBytes == 1, "large drive count does not overflow");
            int reads = 0;
            DiskSpaceService injected = new DiskSpaceService(delegate { reads++; return sample; });
            assert(Object.ReferenceEquals(injected.Read(), sample) && reads == 1, "injected read performs no native disk access");
            assert(!new DiskSpaceService(delegate { return null; }).Read().Available, "null provider result is unavailable");
            DiskSpaceSnapshot denied = new DiskSpaceService(delegate { throw new IOException("synthetic denied"); }).Read();
            assert(!denied.Available && denied.Error.Contains("synthetic denied"), "read error preserved without inventing space");
            bool nullProviderRejected = false;
            try { new DiskSpaceService(null); } catch (ArgumentNullException) { nullProviderRejected = true; }
            assert(nullProviderRejected, "missing provider rejected");
            assert(DiskSpaceSnapshot.Unavailable(null).FreeRatio == 0 && DiskSpaceSnapshot.Unavailable(null).Error.Length > 0, "unknown snapshot carries explanation");

            LowDiskBossPolicy policy = new LowDiskBossPolicy();
            DiskSpaceSnapshot low = DiskSpaceSnapshot.FromValues(100 * GiB, GiB - 1);
            DiskSpaceSnapshot exact = DiskSpaceSnapshot.FromValues(100 * GiB, GiB);
            DiskSpaceSnapshot recovery = DiskSpaceSnapshot.FromValues(100 * GiB, LowDiskBossPolicy.RecoveryThresholdBytes);
            assert(!policy.Observe(exact, 0, false), "exactly one GiB does not trigger less-than threshold");
            assert(policy.Observe(low, 0, false), "first low-space sample triggers");
            assert(!policy.Observe(low, 1, false), "duplicate low-space samples do not respawn");
            assert(!policy.Observe(low, 200, false), "sustained pressure alone cannot respawn after cooldown");
            assert(!policy.Observe(DiskSpaceSnapshot.Unavailable("synthetic"), 201, false), "unknown measurement cannot rearm");
            assert(!policy.Observe(exact, 202, false) && !policy.Armed, "partial recovery inside hysteresis band cannot rearm");
            assert(!policy.Observe(recovery, 203, true) && policy.Armed, "recovery rearms while a boss exists");
            assert(!policy.Observe(low, 204, true), "existing boss prevents duplicate spawn");
            assert(policy.Observe(low, 205, false), "new pressure after recovery and cooldown triggers");
            assert(!policy.Observe(recovery, 206, false) && policy.Armed, "second recovery rearms");
            assert(!policy.Observe(low, 384.999, false), "cooldown blocks early respawn");
            assert(policy.Observe(low, 385, false), "cooldown boundary permits eligible new pressure");
            LowDiskBossPolicy invalidClock = new LowDiskBossPolicy();
            assert(!invalidClock.Observe(low, Double.NaN, false), "NaN clock cannot spawn");
            assert(!invalidClock.Observe(low, Double.PositiveInfinity, false), "infinite clock cannot spawn");
            assert(!invalidClock.Observe(low, -1, false), "negative clock cannot spawn");
            assert(!invalidClock.Observe(null, 0, false), "null snapshot cannot spawn");
            assert(invalidClock.Observe(low, 0, false), "invalid observations do not consume eligibility");
            LowDiskBossPolicy zeroSpace = new LowDiskBossPolicy();
            assert(zeroSpace.Observe(DiskSpaceSnapshot.FromValues(GiB, 0), 0, false), "completely full disk triggers");
            assert(!zeroSpace.Observe(recovery, 10, false) && zeroSpace.Armed, "recovery sample does not itself spawn");
            assert(!zeroSpace.Observe(low, 9, false), "backward clock does not bypass cooldown");
            return "Disk space: " + checks + " assertions passed (synthetic snapshots only; no disk read or write).";
        }
    }

    // Pure hysteresis/cooldown decision; it never creates a monster or authorizes cleanup.
    // A spawn signal must be paired with known, non-duplicated CleanupBatch instances by the caller.
    internal sealed class LowDiskBossPolicy
    {
        internal const long LowThresholdBytes = 1024L * 1024 * 1024;
        internal const long RecoveryThresholdBytes = 5L * 1024 * 1024 * 1024 / 4;
        internal const double CooldownSeconds = 180;
        private bool armed = true;
        private double lastSpawnSeconds = Double.NegativeInfinity;
        internal bool Armed { get { return armed; } }

        internal bool Observe(DiskSpaceSnapshot snapshot, double nowSeconds, bool bossPresent)
        {
            if (snapshot == null || !snapshot.Available || Double.IsNaN(nowSeconds) || Double.IsInfinity(nowSeconds) || nowSeconds < 0) return false;
            if (snapshot.AvailableBytes >= RecoveryThresholdBytes) { armed = true; return false; }
            if (snapshot.AvailableBytes >= LowThresholdBytes || !armed || bossPresent || nowSeconds - lastSpawnSeconds < CooldownSeconds) return false;
            armed = false; lastSpawnSeconds = nowSeconds; return true;
        }
    }
}

