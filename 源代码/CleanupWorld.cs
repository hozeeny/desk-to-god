using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace NetBunny
{
    internal sealed class CleanupBatch
    {
        internal readonly List<CleanupCandidate> Candidates;
        public string Id { get; private set; }
        public string Label { get; private set; }
        public long Bytes { get; private set; }
        public int FilesCount { get { return Candidates.Count; } }
        internal CleanupBatch(List<CleanupCandidate> files, int number)
        {
            Candidates = new List<CleanupCandidate>(files);
            Candidates.Sort(delegate(CleanupCandidate a, CleanupCandidate b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path); });
            StringBuilder identity = new StringBuilder();
            foreach (CleanupCandidate item in Candidates) identity.Append(item.Path.ToUpperInvariant()).Append('|').Append(item.Volume).Append('|').Append(item.IndexHigh).Append('|').Append(item.IndexLow).Append('|').Append(item.Length).Append('|').Append(item.Written).Append(';');
            using (SHA256 hash = SHA256.Create()) Id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity.ToString()))).Replace("-", "");
            Label = "临时文件群 " + number;
            foreach (CleanupCandidate item in Candidates) Bytes += item.Length;
        }
    }
    internal sealed class CleanupSnapshot
    {
        public List<CleanupBatch> Batches = new List<CleanupBatch>();
        public long CandidateBytes;
        public int CandidateFiles;
        public int ExaminedEntries;
        public bool Truncated;
        public long ObservedTempBytes;
        public int ObservedTempFiles;
        public HashSet<string> ObservedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool ObservedPathsAvailable;
        public bool InventoryComplete;
        public List<CleanupCandidate> ObservedFiles = new List<CleanupCandidate>();
        public bool ObservedFilesAvailable;
        public long EligibleBytes;
        public int EligibleFiles;
        public string Message = "";
        public DateTime CapturedUtc = DateTime.UtcNow;
    }
    internal sealed class CleanupResult
    {
        // Logical size of files actually deleted, not a claim about RAM or allocated disk clusters.
        public long FreedBytes;
        public int DeletedFiles;
        public int SkippedFiles;
        public bool Cancelled;
        public string Message = "";
        // Snapshot entries not successfully deleted, including skipped and not-yet-started files.
        // Keeping them separate from the original batch avoids losing partial-cleanup ownership.
        public List<CleanupCandidate> RemainingCandidates = new List<CleanupCandidate>();
    }
    internal sealed class MemorySnapshot
    {
        public double UsedPercent;
        public ulong TotalBytes;
        public ulong UsedBytes;
        public ulong AvailableBytes;
        public bool Available;
    }
    internal sealed class CleanupCandidate
    {
        internal string Path;
        internal long Length, Written, Created;
        internal uint Volume, IndexHigh, IndexLow;
        internal bool EligibleForCleanup;
    }

    // No timer and no implicit deletion. The game must explicitly call Clean after a user attack.
    internal sealed class CleanupService
    {
        const uint ReadAttributes = 0x80, DeleteAccess = 0x10000, OpenExisting = 3;
        const uint ShareReadWrite = 3, ShareAll = 7, OpenReparse = 0x200000, BackupSemantics = 0x2000000;
        const uint Reparse = 0x400, DirectoryFlag = 0x10, ReadOnly = 1, SystemFlag = 4;
        const long BatchTarget = 32L * 1024 * 1024;
        const int MaxBatches = 12, MaxFilesPerBatch = 128, MaxEntries = 25000, MaxDirectoryDepth = 15;
        readonly string root;
        readonly bool fixture;
        readonly Func<string, FileAttributes> fixtureScanAttributes;
        readonly object gate = new object();
        public string RootPath { get { return root; } }
        public CleanupService()
        {
            root = Canonical(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
        }
        private CleanupService(string isolatedFixture, Func<string, FileAttributes> attributeReader = null) { root = Canonical(isolatedFixture); fixture = true; fixtureScanAttributes = attributeReader; }

        public CleanupSnapshot Scan(CancellationToken token)
        {
            // Read-only census may overlap cleanup; handle checks tolerate disappearance.
            // The controller rejects a census that overlaps a completed mutation epoch.
            {
                CleanupSnapshot result = new CleanupSnapshot();
                if (token.IsCancellationRequested) { result.Message = "扫描已取消"; return result; }
                string reason;
                if (!CheckRoot(out reason)) { result.Message = reason; return result; }
                result.ObservedPathsAvailable = true;
                result.ObservedFilesAvailable = true;
                result.InventoryComplete = true;
                Stopwatch elapsed = Stopwatch.StartNew();
                Stack<string> directories = new Stack<string>(); directories.Push(root);
                List<CleanupCandidate> batch = new List<CleanupCandidate>(); long batchBytes = 0;
                while (directories.Count > 0)
                {
                    if (token.IsCancellationRequested || elapsed.ElapsedMilliseconds > 5000 || result.ExaminedEntries >= MaxEntries)
                    { result.Truncated = true; result.InventoryComplete = false; break; }
                    string directory = directories.Pop();
                    try
                    {
                        using (DirectoryLease lease = LockAncestors(directory))
                        {
                            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                            {
                                if (token.IsCancellationRequested || elapsed.ElapsedMilliseconds > 5000 || ++result.ExaminedEntries > MaxEntries)
                                { result.Truncated = true; result.InventoryComplete = false; break; }
                                string path = Canonical(entry);
                                if (!WithinRoot(path) || IsExcluded(path)) continue;
                                FileAttributes attributes;
                                try { attributes = fixtureScanAttributes == null ? File.GetAttributes(path) : fixtureScanAttributes(path); }
                                catch { result.InventoryComplete = false; continue; }
                                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                                if ((attributes & FileAttributes.Directory) != 0)
                                {
                                    // System directories remain outside the deletion walk. Their
                                    // unknown contents cannot be described as a complete census.
                                    if ((attributes & FileAttributes.System) != 0) { result.InventoryComplete = false; continue; }
                                    int depth = path.Substring(root.Length).TrimStart('\\').Split('\\').Length;
                                    if (depth <= MaxDirectoryDepth) directories.Push(path);
                                    else { result.Truncated = true; result.InventoryComplete = false; }
                                    continue;
                                }
                                long observedBytes;
                                CleanupCandidate observed;
                                CleanupCandidate candidate = ReadCandidate(path, out observedBytes, out observed);
                                if (observedBytes >= 0) { result.ObservedTempBytes += observedBytes; result.ObservedTempFiles++; result.ObservedPaths.Add(path); }
                                else result.InventoryComplete = false;
                                if (observed != null) result.ObservedFiles.Add(observed);
                                if (candidate == null) continue;
                                result.EligibleBytes += candidate.Length; result.EligibleFiles++;
                                if (result.Batches.Count >= MaxBatches) { result.Truncated = true; continue; }
                                batch.Add(candidate); batchBytes += candidate.Length;
                                if (batchBytes >= BatchTarget || batch.Count >= MaxFilesPerBatch)
                                {
                                    AddBatch(result, batch); batch = new List<CleanupCandidate>(); batchBytes = 0;
                                }
                            }
                        }
                    }
                    catch (IOException) { result.InventoryComplete = false; }
                    catch (UnauthorizedAccessException) { result.InventoryComplete = false; }
                    catch (System.Security.SecurityException) { result.InventoryComplete = false; }
                    catch (ArgumentException) { result.InventoryComplete = false; }
                }
                if (batch.Count > 0 && result.Batches.Count < MaxBatches) AddBatch(result, batch);
                if (token.IsCancellationRequested) { result.Truncated = true; result.InventoryComplete = false; }
                result.Message = token.IsCancellationRequested ? "扫描已取消；保留本次已确认的候选" :
                    result.CandidateFiles == 0 ? "没有找到可安全清理的旧临时文件（只检查当前用户 Temp 中超过 24 小时的文件）" :
                    "只读扫描完成：本轮 " + result.CandidateFiles + " 个旧临时文件" + (result.Truncated ? "；本轮候选有上限，资源增长或手动刷新后再开启下一轮" : "");
                if (!result.InventoryComplete && !token.IsCancellationRequested) result.Message += "；部分内容未能完整读取，临时文件总量仅为已确认部分";
                return result;
            }
        }
        static void AddBatch(CleanupSnapshot result, List<CleanupCandidate> files)
        {
            CleanupBatch batch = new CleanupBatch(files, result.Batches.Count + 1);
            result.Batches.Add(batch); result.CandidateBytes += batch.Bytes; result.CandidateFiles += batch.FilesCount;
        }
        public CleanupResult Clean(CleanupBatch batch, CancellationToken token)
        {
            CleanupResult result = new CleanupResult();
            if (batch == null) { result.Message = "没有待处理文件"; return result; }
            result.RemainingCandidates.AddRange(batch.Candidates);
            lock (gate)
            {
                string reason;
                if (!CheckRoot(out reason)) { result.SkippedFiles = batch.FilesCount; result.Message = reason; return result; }
                foreach (CleanupCandidate expected in batch.Candidates)
                {
                    if (token.IsCancellationRequested) { result.Cancelled = true; break; }
                    bool deleted = false;
                    try
                    {
                        if (!WithinRoot(expected.Path) || IsExcluded(expected.Path)) throw new IOException("不在允许范围");
                        // Holding non-delete-shared handles to every ancestor prevents a junction/rename swap.
                        using (DirectoryLease lease = LockAncestors(Path.GetDirectoryName(expected.Path)))
                        using (SafeFileHandle handle = CreateFile(expected.Path, DeleteAccess | ReadAttributes, 0, IntPtr.Zero, OpenExisting, OpenReparse, IntPtr.Zero))
                        {
                            if (handle.IsInvalid) throw new IOException("文件占用或权限不足");
                            FileInfoNative actual;
                            if (!GetFileInformationByHandle(handle, out actual) || !AcceptFile(actual)) throw new IOException("文件状态不再允许清理");
                            if (!SameIdentity(expected, actual) || !String.Equals(FinalPath(handle), expected.Path, StringComparison.OrdinalIgnoreCase)) throw new IOException("文件已变化");
                            // Cancellation is checked immediately before the sole mutation in this service.
                            if (token.IsCancellationRequested) { result.Cancelled = true; break; }
                            Disposition disposition = new Disposition { Delete = 1 };
                            if (!SetFileInformationByHandle(handle, 4, ref disposition, 1)) throw new IOException("系统拒绝删除");
                            deleted = true;
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (System.Security.SecurityException) { }
                    catch (ArgumentException) { }
                    if (deleted) { result.DeletedFiles++; result.FreedBytes += expected.Length; result.RemainingCandidates.Remove(expected); }
                    else result.SkippedFiles++;
                }
                if (result.Cancelled) result.SkippedFiles = batch.FilesCount - result.DeletedFiles;
            }
            result.Message = (result.Cancelled ? "清理已停止。" : "") + "已删除 " + result.DeletedFiles + " 个临时文件，跳过 " + result.SkippedFiles + " 个（占用、变化或不符合规则）。释放的是磁盘文件空间，不是运行内存。";
            return result;
        }
        CleanupCandidate ReadCandidate(string path)
        { long ignored; return ReadCandidate(path, out ignored); }
        CleanupCandidate ReadCandidate(string path, out long observedBytes)
        { CleanupCandidate ignored; return ReadCandidate(path, out observedBytes, out ignored); }
        CleanupCandidate ReadCandidate(string path, out long observedBytes, out CleanupCandidate observed)
        {
            observedBytes = -1; observed = null;
            try
            {
                using (SafeFileHandle handle = CreateFile(path, ReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, OpenReparse, IntPtr.Zero))
                {
                    FileInfoNative info;
                    if (handle.IsInvalid || !GetFileInformationByHandle(handle, out info)) return null;
                    string final = FinalPath(handle);
                    if (!WithinRoot(final) || !String.Equals(final, path, StringComparison.OrdinalIgnoreCase)) return null;
                    if ((info.Attributes & (Reparse | DirectoryFlag)) == 0 && info.Links == 1) observedBytes = Length(info);
                    if (observedBytes < 0) return null;
                    observed = new CleanupCandidate { Path = path, Length = Length(info), Written = Stamp(info.Write), Created = Stamp(info.Creation), Volume = info.Volume, IndexHigh = info.IndexHigh, IndexLow = info.IndexLow, EligibleForCleanup = AcceptFile(info) };
                    return observed.EligibleForCleanup ? observed : null;
                }
            }
            catch { return null; }
        }
        static bool AcceptFile(FileInfoNative file)
        {
            return (file.Attributes & (Reparse | DirectoryFlag | ReadOnly | SystemFlag)) == 0 && file.Links == 1 && Length(file) >= 0 &&
                Stamp(file.Write) > 0 && Stamp(file.Creation) > 0 &&
                Stamp(file.Write) <= DateTime.UtcNow.AddHours(-24).ToFileTimeUtc() && Stamp(file.Creation) <= DateTime.UtcNow.AddHours(-24).ToFileTimeUtc();
        }
        static bool SameIdentity(CleanupCandidate expected, FileInfoNative actual)
        {
            return expected.Volume == actual.Volume && expected.IndexHigh == actual.IndexHigh && expected.IndexLow == actual.IndexLow &&
                expected.Length == Length(actual) && expected.Written == Stamp(actual.Write) && expected.Created == Stamp(actual.Creation);
        }
        bool CheckRoot(out string reason)
        {
            reason = "";
            try
            {
                if (!fixture && !String.Equals(Path.GetPathRoot(root), "C:\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("只允许 C 盘当前用户 Temp");
                if (!fixture)
                {
                    string expected = Canonical(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
                    if (!String.Equals(root, expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("临时目录不在白名单");
                    string profile = Canonical(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                    if (root.Length <= profile.Length || !root.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("临时目录不在当前用户下");
                }
                using (DirectoryLease lease = LockAncestors(root)) { }
                return true;
            }
            catch (Exception ex) { reason = "安全扫描不可用：" + ex.Message; return false; }
        }
        bool WithinRoot(string path)
        {
            if (String.IsNullOrEmpty(path)) return false;
            try { return Canonical(path).StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }
        bool IsExcluded(string path)
        {
            if (!WithinRoot(path)) return true;
            string relative = path.Substring(root.Length + 1);
            foreach (string part in relative.Split('\\'))
            {
                string lower = part.ToLowerInvariant();
                if (lower.IndexOf("netbunny", StringComparison.Ordinal) >= 0 || lower.IndexOf("knightassistant", StringComparison.Ordinal) >= 0 ||
                    lower.IndexOf("test", StringComparison.Ordinal) >= 0 || lower == "骑士助手") return true;
            }
            return relative.IndexOf(':') >= 0;
        }
        static string Canonical(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.StartsWith("\\\\", StringComparison.Ordinal)) throw new IOException("不接受网络或设备路径");
            string full = Path.GetFullPath(path);
            return full.Length <= 3 ? full : full.TrimEnd('\\');
        }
        static string FinalPath(SafeFileHandle handle)
        {
            string result = ReadFinalPath(delegate(StringBuilder buffer) { return GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0); });
            if (result.StartsWith("\\\\?\\", StringComparison.Ordinal)) result = result.Substring(4);
            return Canonical(result);
        }
        internal static string ReadFinalPath(Func<StringBuilder, uint> read)
        {
            if (read == null) throw new ArgumentNullException("read");
            int capacity = 260;
            StringBuilder buffer = new StringBuilder(capacity);
            uint length = read(buffer);
            if (length == 0 || length > 32768) throw new IOException("不能确认文件实际位置");
            if (length >= capacity)
            {
                // W API: insufficient-buffer length includes the terminating null;
                // success length excludes it. Keep the original capacity ceiling.
                capacity = (int)length; buffer = new StringBuilder(capacity);
                length = read(buffer);
            }
            if (length == 0 || length >= capacity || length != buffer.Length) throw new IOException("不能确认文件实际位置");
            return buffer.ToString();
        }
        DirectoryLease LockAncestors(string path)
        {
            string full = Canonical(path), volume = Path.GetPathRoot(full);
            if (!fixture && !String.Equals(volume, "C:\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("不在 C 盘");
            DirectoryLease lease = new DirectoryLease();
            try
            {
                string current = volume;
                lease.Add(current);
                foreach (string piece in full.Substring(volume.Length).Split(new char[] { '\\' }, StringSplitOptions.RemoveEmptyEntries))
                { current = Path.Combine(current, piece); lease.Add(current); }
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }
        sealed class DirectoryLease : IDisposable
        {
            readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
            internal void Add(string path)
            {
                // FILE_LIST_DIRECTORY is required: attribute-only opens do not enforce delete sharing.
                SafeFileHandle handle = CreateFile(path, ReadAttributes | 1, ShareReadWrite, IntPtr.Zero, OpenExisting, OpenReparse | BackupSemantics, IntPtr.Zero);
                if (handle.IsInvalid) { handle.Dispose(); throw new IOException("不能锁定目录，已跳过"); }
                handles.Add(handle);
                FileInfoNative info;
                if (!GetFileInformationByHandle(handle, out info) || (info.Attributes & Reparse) != 0 || (info.Attributes & DirectoryFlag) == 0 ||
                    !String.Equals(FinalPath(handle), Canonical(path), StringComparison.OrdinalIgnoreCase)) throw new IOException("目录含重解析点或位置已变化：" + path + " -> " + FinalPath(handle) + " [" + info.Attributes + "]");
            }
            public void Dispose() { for (int i = handles.Count - 1; i >= 0; --i) handles[i].Dispose(); handles.Clear(); }
        }
        public static MemorySnapshot ReadMemory()
        {
            MemoryStatus status = new MemoryStatus(); status.Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            if (!GlobalMemoryStatusEx(ref status)) return new MemorySnapshot();
            return new MemorySnapshot { Available = true, TotalBytes = status.TotalPhysical, AvailableBytes = status.AvailablePhysical,
                UsedBytes = status.TotalPhysical - status.AvailablePhysical,
                UsedPercent = status.TotalPhysical == 0 ? 0 : 100.0 * (status.TotalPhysical - status.AvailablePhysical) / status.TotalPhysical };
        }
        static long Stamp(NativeTime time) { return ((long)time.High << 32) | time.Low; }
        static long Length(FileInfoNative file) { return ((long)file.SizeHigh << 32) | file.SizeLow; }
        [StructLayout(LayoutKind.Sequential)] struct NativeTime { public uint Low, High; }
        [StructLayout(LayoutKind.Sequential)] struct FileInfoNative
        {
            public uint Attributes; public NativeTime Creation, Access, Write;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [StructLayout(LayoutKind.Sequential)] struct Disposition { public byte Delete; }
        [StructLayout(LayoutKind.Sequential)] struct MemoryStatus
        {
            public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfoNative info);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint count, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool SetFileInformationByHandle(SafeFileHandle file, int kind, ref Disposition info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateHardLink(string link, string existing, IntPtr security);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateSymbolicLink(string link, string target, uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle handle, uint control, byte[] input, int inputLength, IntPtr output, int outputLength, out uint returned, IntPtr overlapped);

        static bool MakeFixtureJunction(string link, string destination)
        {
            // Invoked only by SelfTest, against directories created by that test.
            string substitute = "\\??\\" + destination;
            byte[] sub = Encoding.Unicode.GetBytes(substitute), print = Encoding.Unicode.GetBytes(destination);
            byte[] data = new byte[16 + sub.Length + 2 + print.Length + 2];
            Array.Copy(BitConverter.GetBytes(0xA0000003u), 0, data, 0, 4);
            Array.Copy(BitConverter.GetBytes((ushort)(data.Length - 8)), 0, data, 4, 2);
            Array.Copy(BitConverter.GetBytes((ushort)sub.Length), 0, data, 10, 2);
            Array.Copy(BitConverter.GetBytes((ushort)(sub.Length + 2)), 0, data, 12, 2);
            Array.Copy(BitConverter.GetBytes((ushort)print.Length), 0, data, 14, 2);
            Array.Copy(sub, 0, data, 16, sub.Length); Array.Copy(print, 0, data, 18 + sub.Length, print.Length);
            using (SafeFileHandle handle = CreateFile(link, 0x40000000, 0, IntPtr.Zero, OpenExisting, OpenReparse | BackupSemantics, IntPtr.Zero))
            { uint returned; return !handle.IsInvalid && DeviceIoControl(handle, 0x900A4, data, data.Length, IntPtr.Zero, 0, out returned, IntPtr.Zero); }
        }

        public static string SelfTest()
        {
            int checks = 0; Action<bool, string> assert = delegate(bool ok, string name) { if (!ok) throw new InvalidOperationException("Cleanup: " + name); checks++; };
            string sandbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistantProject", "v4", "test-fixtures", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(sandbox);
            // Packaged Windows apps may redirect newly created LocalAppData paths to another volume.
            // Resolve only this newly created fixture; the production whitelist remains physical C:\\.
            using (SafeFileHandle fixtureHandle = CreateFile(sandbox, ReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, OpenReparse | BackupSemantics, IntPtr.Zero))
            { if (fixtureHandle.IsInvalid) throw new IOException("不能打开隔离测试目录"); sandbox = FinalPath(fixtureHandle); }
            string area = Path.Combine(sandbox, "allowed"), outside = Path.Combine(sandbox, "outside");
            List<string> files = new List<string>(), directories = new List<string>();
            Action<string> mkdir = delegate(string p) { Directory.CreateDirectory(p); directories.Add(p); };
            Func<string, int, string> old = delegate(string name, int count) { string p = Path.Combine(area, name); File.WriteAllBytes(p, new byte[count]); File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-2)); File.SetCreationTimeUtc(p, DateTime.UtcNow.AddDays(-2)); files.Add(p); return p; };
            Directory.CreateDirectory(sandbox); directories.Add(sandbox); mkdir(area); mkdir(outside);
            try
            {
                CleanupService service = new CleanupService(area); string reason;
                bool fixtureValid = service.CheckRoot(out reason); assert(fixtureValid, "fixture whitelist: " + reason);
                assert(!service.WithinRoot(outside), "outside rejected");
                assert(!service.WithinRoot(area + "-suffix\\file"), "prefix collision rejected");
                string eligible = old("eligible.bin", 1234), changed = old("changed.bin", 23), locked = old("locked.bin", 31), recent = old("recent.bin", 7), readOnly = old("readonly.bin", 8);
                File.SetLastWriteTimeUtc(recent, DateTime.UtcNow); File.SetAttributes(readOnly, FileAttributes.ReadOnly);
                string newlyExtracted = old("newly-extracted.bin", 9); File.SetCreationTimeUtc(newlyExtracted, DateTime.UtcNow);
                string hardA = old("hard-a.bin", 17), hardB = Path.Combine(area, "hard-b.bin");
                assert(CreateHardLink(hardB, hardA, IntPtr.Zero), "create isolated hard link"); files.Add(hardB);
                string outsideFile = Path.Combine(outside, "protected.bin"); File.WriteAllBytes(outsideFile, new byte[991]); File.SetLastWriteTimeUtc(outsideFile, DateTime.UtcNow.AddDays(-2)); File.SetCreationTimeUtc(outsideFile, DateTime.UtcNow.AddDays(-2)); files.Add(outsideFile);
                string linkFile = Path.Combine(area, "linked.bin"), linkDir = Path.Combine(area, "linked-dir");
                bool madeFileLink = CreateSymbolicLink(linkFile, outsideFile, 2) || CreateSymbolicLink(linkFile, outsideFile, 0);
                assert(madeFileLink, "create isolated file symbolic link"); files.Add(linkFile);
                bool madeDirLink = CreateSymbolicLink(linkDir, outside, 3) || CreateSymbolicLink(linkDir, outside, 1);
                assert(madeDirLink, "create isolated directory symbolic link"); directories.Add(linkDir);
                string junction = Path.Combine(area, "junction-dir"); mkdir(junction);
                assert(MakeFixtureJunction(junction, outside), "create isolated directory junction");
                mkdir(Path.Combine(area, "NetBunny-download")); string excluded = old("NetBunny-download\\cache.bin", 5);
                CleanupSnapshot scan = service.Scan(CancellationToken.None);
                assert(scan.CandidateFiles == 3, "age/read-only/hardlink/reparse/excluded filters");
                assert(scan.CandidateBytes == 1288, "snapshot sizes"); assert(scan.Batches.Count == 1, "bounded batch");
                assert(service.Scan(CancellationToken.None).Batches[0].Id == scan.Batches[0].Id, "stable batch identity");
                assert(File.Exists(eligible), "scan never deletes");
                CancellationTokenSource cancellation = new CancellationTokenSource(); cancellation.Cancel();
                CleanupResult cancelled = service.Clean(scan.Batches[0], cancellation.Token);
                assert(cancelled.DeletedFiles == 0 && cancelled.Cancelled, "pre-cancel deletes nothing");
                assert(cancelled.RemainingCandidates.Count == 3 && scan.Batches[0].Candidates.TrueForAll(delegate(CleanupCandidate c) { return cancelled.RemainingCandidates.Contains(c); }), "cancellation retains every unprocessed snapshot candidate");
                assert(!Object.ReferenceEquals(cancelled.RemainingCandidates, scan.Batches[0].Candidates), "cleanup residuals have their own list and cannot mutate the authorized snapshot");
                CleanupSnapshot cancelledScan = service.Scan(cancellation.Token);
                assert(cancelledScan.CandidateFiles == 0, "cancel scan");
                assert(!cancelledScan.ObservedPathsAvailable && !cancelledScan.InventoryComplete && cancelledScan.ObservedPaths.Count == 0, "pre-cancelled scan has no trusted path census and cannot prove a prior file disappeared"); cancellation.Dispose();
                File.AppendAllText(changed, "modified");
                using (FileStream busy = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    CleanupResult cleaned = service.Clean(scan.Batches[0], CancellationToken.None);
                    assert(cleaned.DeletedFiles == 1 && cleaned.FreedBytes == 1234 && cleaned.SkippedFiles == 2, "actual handle deletion; changes and locks skipped");
                    assert(cleaned.RemainingCandidates.Count == 2 && cleaned.RemainingCandidates.Exists(delegate(CleanupCandidate c) { return c.Path == changed; }) && cleaned.RemainingCandidates.Exists(delegate(CleanupCandidate c) { return c.Path == locked; }) && !cleaned.RemainingCandidates.Exists(delegate(CleanupCandidate c) { return c.Path == eligible; }), "partial success retains changed and occupied entries but excludes the actually deleted file");
                    assert(scan.Batches[0].Candidates.Count == 3 && scan.Batches[0].Bytes == 1288, "partial cleanup preserves the original authorized batch snapshot");
                }
                assert(!File.Exists(eligible), "eligible physically absent");
                assert(File.Exists(changed) && File.Exists(locked), "changed and occupied files remain");
                assert(File.Exists(outsideFile) && new FileInfo(outsideFile).Length == 991, "symlink target untouched");
                assert(File.Exists(hardA) && File.Exists(hardB), "both hard links remain");
                assert(File.Exists(recent) && File.Exists(readOnly) && File.Exists(excluded), "protected categories remain");
                assert(File.Exists(newlyExtracted) && service.ReadCandidate(newlyExtracted) == null, "newly extracted files with historical mtime are protected");
                CleanupCandidate escaped = new CleanupService(outside).ReadCandidate(outsideFile);
                CleanupResult escapeResult = service.Clean(new CleanupBatch(new List<CleanupCandidate> { escaped }, 1), CancellationToken.None);
                assert(escapeResult.DeletedFiles == 0 && File.Exists(outsideFile), "forged batch root escape rejected");
                assert(escapeResult.RemainingCandidates.Count == 1 && escapeResult.RemainingCandidates[0] == escaped, "rejected out-of-root entry remains unprocessed without claiming deletion");
                CleanupResult replay = service.Clean(scan.Batches[0], CancellationToken.None);
                assert(replay.DeletedFiles == 1 && replay.FreedBytes == 31, "replay only deletes still unchanged formerly locked file");
                assert(replay.RemainingCandidates.Count == 2 && !replay.RemainingCandidates.Exists(delegate(CleanupCandidate c) { return c.Path == locked; }), "replay residuals exclude only that invocation's successful deletion");
                CleanupResult again = service.Clean(scan.Batches[0], CancellationToken.None);
                assert(again.DeletedFiles == 0 && again.FreedBytes == 0, "no duplicate deletion rewards");
                assert(!new CleanupService(linkDir).CheckRoot(out reason), "reparse root rejected");
                CleanupSnapshot invalidRootScan = new CleanupService(linkDir).Scan(CancellationToken.None);
                assert(!invalidRootScan.ObservedPathsAvailable && !invalidRootScan.InventoryComplete && invalidRootScan.ObservedPaths.Count == 0, "invalid root cannot masquerade as a complete empty path census");
                assert(!new CleanupService(junction).CheckRoot(out reason), "junction root rejected");
                using (DirectoryLease held = service.LockAncestors(outside))
                {
                    bool renameBlocked = false; try { Directory.Move(outside, outside + "-moved"); } catch (IOException) { renameBlocked = true; }
                    if (!renameBlocked) Directory.Move(outside + "-moved", outside);
                    assert(renameBlocked, "ancestor lock prevents directory rename race");
                }
                string replacement = old("replacement.bin", 47); CleanupCandidate replacementBefore = service.ReadCandidate(replacement);
                File.Delete(replacement); File.WriteAllBytes(replacement, new byte[47]); File.SetLastWriteTimeUtc(replacement, DateTime.FromFileTimeUtc(replacementBefore.Written));
                CleanupResult replaced = service.Clean(new CleanupBatch(new List<CleanupCandidate> { replacementBefore }, 1), CancellationToken.None);
                assert(replaced.DeletedFiles == 0 && File.Exists(replacement), "same-size timestamp-preserving replacement rejected by identity");
                string lateLink = old("late-link-source.bin", 29); CleanupCandidate beforeLink = service.ReadCandidate(lateLink);
                string secondLink = Path.Combine(area, "late-link-alias.bin"); assert(CreateHardLink(secondLink, lateLink, IntPtr.Zero), "create post-scan hard link"); files.Add(secondLink);
                assert(service.Clean(new CleanupBatch(new List<CleanupCandidate> { beforeLink }, 1), CancellationToken.None).DeletedFiles == 0, "new hard link after scan blocks deletion");
                string becameReadOnly = old("became-readonly.bin", 19); CleanupCandidate beforeReadOnly = service.ReadCandidate(becameReadOnly); File.SetAttributes(becameReadOnly, FileAttributes.ReadOnly);
                assert(service.Clean(new CleanupBatch(new List<CleanupCandidate> { beforeReadOnly }, 1), CancellationToken.None).DeletedFiles == 0, "read-only change after scan blocks deletion");
                string emptyFile = old("empty.bin", 0); CleanupCandidate emptyCandidate = service.ReadCandidate(emptyFile);
                CleanupResult emptyResult = service.Clean(new CleanupBatch(new List<CleanupCandidate> { emptyCandidate }, 1), CancellationToken.None);
                assert(emptyResult.DeletedFiles == 1 && emptyResult.FreedBytes == 0 && emptyResult.RemainingCandidates.Count == 0 && !File.Exists(emptyFile), "successful zero-byte deletion removes its residual without inventing freed bytes");
                MemorySnapshot memory = ReadMemory();
                assert(memory.Available && memory.TotalBytes > 0 && memory.UsedPercent >= 0 && memory.UsedPercent <= 100, "physical memory separate from cleanup bytes");
                string inventoryArea = Path.Combine(sandbox, "inventory"); mkdir(inventoryArea);
                DateTime inventoryOldTime = DateTime.UtcNow.AddDays(-2);
                int inventoryOldCount = 13 * MaxFilesPerBatch + 1;
                for (int i = 0; i < inventoryOldCount; i++)
                {
                    string inventoryFile = Path.Combine(inventoryArea, "old-" + i.ToString("D4") + ".bin");
                    File.WriteAllBytes(inventoryFile, new byte[3]); files.Add(inventoryFile);
                    File.SetCreationTimeUtc(inventoryFile, inventoryOldTime); File.SetLastWriteTimeUtc(inventoryFile, inventoryOldTime);
                }
                string inventoryRecent = Path.Combine(inventoryArea, "recent.bin"); File.WriteAllBytes(inventoryRecent, new byte[11]); files.Add(inventoryRecent);
                CleanupService inventoryService = new CleanupService(inventoryArea); CleanupSnapshot inventoryScan = inventoryService.Scan(CancellationToken.None);
                assert(inventoryScan.Batches.Count == MaxBatches && inventoryScan.CandidateFiles == MaxBatches * MaxFilesPerBatch && inventoryScan.CandidateBytes == 3L * MaxBatches * MaxFilesPerBatch, "candidate batches remain capped at twelve while census continues");
                assert(inventoryScan.InventoryComplete && inventoryScan.Truncated && inventoryScan.ObservedTempFiles == inventoryOldCount + 1 && inventoryScan.ObservedTempBytes == 3L * inventoryOldCount + 11, "complete census counts old files beyond all twelve batches and marks only candidate selection truncated");
                assert(inventoryScan.ObservedFilesAvailable && inventoryScan.ObservedFiles.Count == inventoryOldCount + 1, "live population includes recent files beyond displayed batch budget");
                assert(inventoryScan.EligibleFiles == inventoryOldCount && inventoryScan.EligibleBytes == 3L * inventoryOldCount, "live processable totals do not stop at twelve batches");
                assert(inventoryScan.ObservedFiles.FindAll(f => !f.EligibleForCleanup).Count == 1, "recent file is visible but never mislabeled safe");
                bool everyOldPathObserved = true; for (int i = 0; i < inventoryOldCount; i++) everyOldPathObserved &= inventoryScan.ObservedPaths.Contains(Path.Combine(inventoryArea, "old-" + i.ToString("D4") + ".bin"));
                assert(inventoryScan.ObservedPathsAvailable && inventoryScan.ObservedPaths.Count == inventoryOldCount + 1 && everyOldPathObserved && inventoryScan.ObservedPaths.Contains(inventoryRecent), "path census includes all 1666 old and recent files, including old entries beyond the twelve-batch candidate cap");
                assert(inventoryScan.ObservedPaths.Contains(inventoryRecent.ToUpperInvariant()), "path census membership follows Windows case-insensitive path semantics");
                bool recentSelected = false; foreach (CleanupBatch inventoryBatch in inventoryScan.Batches) foreach (CleanupCandidate candidate in inventoryBatch.Candidates) if (String.Equals(candidate.Path, inventoryRecent, StringComparison.OrdinalIgnoreCase)) recentSelected = true;
                assert(!recentSelected && inventoryService.ReadCandidate(inventoryRecent) == null && File.Exists(inventoryRecent), "recent temporary files contribute to total usage without becoming cleanup candidates");
                assert(File.Exists(Path.Combine(inventoryArea, "old-0000.bin")) && File.Exists(Path.Combine(inventoryArea, "old-" + (inventoryOldCount - 1).ToString("D4") + ".bin")), "census is read-only even for old files after the candidate cap");
                using (CancellationTokenSource midScanCancel = new CancellationTokenSource())
                {
                    int attributeReads = 0;
                    CleanupService interruptedService = new CleanupService(inventoryArea, delegate(string observedPath) { if (++attributeReads == 2) midScanCancel.Cancel(); return File.GetAttributes(observedPath); });
                    CleanupSnapshot interruptedScan = interruptedService.Scan(midScanCancel.Token);
                    assert(interruptedScan.ObservedPathsAvailable && !interruptedScan.InventoryComplete && interruptedScan.ObservedPaths.Count > 0 && interruptedScan.ObservedPaths.Count < inventoryScan.ObservedPaths.Count, "mid-scan cancellation preserves observed paths while explicitly withholding proof about unvisited files");
                }
                string attributesArea = Path.Combine(sandbox, "attribute-gap"); mkdir(attributesArea);
                string unreadAttributes = Path.Combine(attributesArea, "unread.bin"); File.WriteAllBytes(unreadAttributes, new byte[5]); files.Add(unreadAttributes);
                CleanupService attributesUnavailable = new CleanupService(attributesArea, delegate(string unused) { throw new IOException("isolated synthetic attribute-read failure"); });
                CleanupSnapshot attributeGap = attributesUnavailable.Scan(CancellationToken.None);
                assert(!attributeGap.InventoryComplete && attributeGap.ObservedTempFiles == 0 && attributeGap.CandidateFiles == 0 && File.Exists(unreadAttributes), "an attribute read failure cannot masquerade as a complete empty inventory");
                assert(attributeGap.ObservedPathsAvailable && !attributeGap.InventoryComplete && !attributeGap.ObservedPaths.Contains(unreadAttributes), "unreadable path remains unknown rather than confirmed absent");
                string depthArea = Path.Combine(sandbox, "depth"); mkdir(depthArea); string deepestAllowed = depthArea;
                for (int i = 0; i < MaxDirectoryDepth; i++) { deepestAllowed = Path.Combine(deepestAllowed, "d"); mkdir(deepestAllowed); }
                string boundaryFile = Path.Combine(deepestAllowed, "boundary.bin"); File.WriteAllBytes(boundaryFile, new byte[3]); files.Add(boundaryFile); File.SetCreationTimeUtc(boundaryFile, inventoryOldTime); File.SetLastWriteTimeUtc(boundaryFile, inventoryOldTime);
                CleanupService depthService = new CleanupService(depthArea); CleanupSnapshot allowedDepth = depthService.Scan(CancellationToken.None);
                assert(allowedDepth.InventoryComplete && allowedDepth.ObservedTempBytes == 3 && allowedDepth.CandidateFiles == 1, "existing permitted directory depth can still be fully inventoried");
                string tooDeep = Path.Combine(deepestAllowed, "d"); mkdir(tooDeep); string hiddenDeepFile = Path.Combine(tooDeep, "deeper.bin"); File.WriteAllBytes(hiddenDeepFile, new byte[7]); files.Add(hiddenDeepFile); File.SetCreationTimeUtc(hiddenDeepFile, inventoryOldTime); File.SetLastWriteTimeUtc(hiddenDeepFile, inventoryOldTime);
                CleanupSnapshot depthGap = depthService.Scan(CancellationToken.None);
                assert(!depthGap.InventoryComplete && depthGap.Truncated && depthGap.ObservedTempBytes == 3 && depthGap.CandidateFiles == 1 && File.Exists(hiddenDeepFile), "depth-limit skips are explicitly partial without expanding the deletion walk");
                string systemArea = Path.Combine(sandbox, "system-scope"); mkdir(systemArea);
                string systemFile = Path.Combine(systemArea, "system.bin"); File.WriteAllBytes(systemFile, new byte[9]); files.Add(systemFile); File.SetCreationTimeUtc(systemFile, inventoryOldTime); File.SetLastWriteTimeUtc(systemFile, inventoryOldTime); File.SetAttributes(systemFile, FileAttributes.System);
                CleanupSnapshot systemFiles = new CleanupService(systemArea).Scan(CancellationToken.None);
                assert(systemFiles.InventoryComplete && systemFiles.ObservedTempBytes == 9 && systemFiles.CandidateFiles == 0, "readable system-file sizes count toward usage while deletion protections stay intact");
                string systemDirectory = Path.Combine(systemArea, "restricted-folder"); mkdir(systemDirectory); File.SetAttributes(systemDirectory, FileAttributes.System);
                try { assert(!new CleanupService(systemArea).Scan(CancellationToken.None).InventoryComplete, "skipped system directories cannot imply a complete inventory of unknown contents"); }
                finally { File.SetAttributes(systemDirectory, FileAttributes.Normal); }
                return "Cleanup self-test: " + checks + " passed (isolated fixtures only; user Temp untouched).";
            }
            finally
            {
                // Only paths explicitly created above are removed. Never recurse or enumerate user Temp.
                for (int i = files.Count - 1; i >= 0; --i)
                {
                    string p = files[i];
                    if (!p.StartsWith(sandbox + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                    try { if (File.Exists(p)) { if ((File.GetAttributes(p) & FileAttributes.ReparsePoint) == 0) File.SetAttributes(p, FileAttributes.Normal); File.Delete(p); } } catch { }
                }
                for (int i = directories.Count - 1; i >= 0; --i)
                    try { if ((String.Equals(directories[i], sandbox, StringComparison.OrdinalIgnoreCase) || directories[i].StartsWith(sandbox + "\\", StringComparison.OrdinalIgnoreCase)) && Directory.Exists(directories[i])) Directory.Delete(directories[i], false); } catch { }
            }
        }
    }
}
