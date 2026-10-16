// NetBunny network backend. C# 5 / .NET Framework 4.8 / x64.
// Primary API references:
// https://reqrypt.org/windivert-doc.html (WinDivert 2.2)
// https://learn.microsoft.com/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable
// https://learn.microsoft.com/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedudptable
// No packet content is written to disk. Buffers live only in bounded memory queues.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NetBunny
{
    public sealed class AppInfo
    {
        public string Name, Path;
        public List<int> Pids = new List<int>();
        public int TcpConnections, UdpEndpoints;
        public double DownloadBps, UploadBps;
        public long DownloadLimit, UploadLimit;
        // Exact captured IP byte counts within one engine session; used by the
        // daily ledger, never by rate limiting. Snapshot() remains the live list.
        public long DownloadBytes, UploadBytes;
        public string TrafficSessionId, TrafficKey;
    }

    /// <summary>
    /// Read-only sniffing unless at least one speed limit has been applied. Existing
    /// connections are discovered with IP Helper; no firewall or persistent QoS rules
    /// are created. Limits aggregate all processes using the same executable path.
    /// </summary>
    public sealed class NetworkEngine : IDisposable
    {
        private readonly object gate = new object();
        private readonly object operationGate = new object();
        private readonly ManualResetEvent finish = new ManualResetEvent(false);
        private readonly Dictionary<string, RateLimit> limits = new Dictionary<string, RateLimit>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Counters> counters = new Dictionary<string, Counters>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TrafficTotal> trafficTotals = new Dictionary<string, TrafficTotal>(StringComparer.OrdinalIgnoreCase);
        private readonly string trafficSessionId = Guid.NewGuid().ToString("N");
        private volatile Ownership ownership = new Ownership();
        private readonly object recentGate = new object();
        private readonly Dictionary<FlowKey, Identity> recentOwners = new Dictionary<FlowKey, Identity>();
        private Dictionary<string, AppInfo> apps = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
        private volatile bool started, disposed, capturing;
        private volatile string status = "等待启动";
        private IntPtr sniffHandle = Native.InvalidHandle;
        private Thread tableThread, sniffThread;
        private ThrottleSession throttle;
        private long lastSample = Stopwatch.GetTimestamp();

        public string Status { get { return status; } }
        public bool IsCapturing { get { return capturing; } }

        public void Start()
        {
            lock (operationGate)
            {
                if (disposed) throw new ObjectDisposedException("NetworkEngine");
                if (started) return;
                started = true;
                try { RefreshOwnership(); } catch (Exception ex) { status = "进程列表读取失败：" + ex.Message; }
                tableThread = BackgroundThread(TableLoop, "NetBunny endpoints");
                tableThread.Start();
                try
                {
                    // Monitoring cannot delay/drop traffic: originals are never diverted.
                    sniffHandle = Native.WinDivertOpen("(tcp or udp) and !loopback and !impostor", 0, -1000, 1UL | 4UL);
                    if (sniffHandle == Native.InvalidHandle) throw new Win32Exception(Marshal.GetLastWin32Error());
                    Native.WinDivertSetParam(sniffHandle, 0, 8192);
                    Native.WinDivertSetParam(sniffHandle, 2, 16777216);
                    capturing = true;
                    status = "实时监控中 · 上传/下载为 IP 流量 · 不含本机回环";
                    sniffThread = BackgroundThread(SniffLoop, "NetBunny traffic monitor");
                    sniffThread.Start();
                }
                catch (Exception ex)
                {
                    capturing = false;
                    if (sniffHandle != Native.InvalidHandle) Native.WinDivertClose(sniffHandle);
                    sniffHandle = Native.InvalidHandle;
                    status = "仅显示连接列表，监控驱动未就绪：" + FriendlyError(ex);
                }
            }
        }

        public List<AppInfo> Snapshot()
        {
            var result = new List<AppInfo>();
            lock (gate)
            {
                foreach (KeyValuePair<string, AppInfo> item in apps)
                {
                    AppInfo a = item.Value;
                    Counters c;
                    RateLimit r;
                    counters.TryGetValue(item.Key, out c);
                    limits.TryGetValue(item.Key, out r);
                    result.Add(new AppInfo { Name = a.Name, Path = a.Path, Pids = new List<int>(a.Pids),
                        TcpConnections = a.TcpConnections, UdpEndpoints = a.UdpEndpoints,
                        DownloadBps = c == null ? 0 : c.DownRate, UploadBps = c == null ? 0 : c.UpRate,
                        DownloadLimit = r == null ? 0 : r.Down, UploadLimit = r == null ? 0 : r.Up });
                }
                foreach (KeyValuePair<string, RateLimit> item in limits)
                    if (!apps.ContainsKey(item.Key))
                        result.Add(new AppInfo { Name = System.IO.Path.GetFileNameWithoutExtension(item.Key), Path = item.Key,
                            DownloadLimit = item.Value.Down, UploadLimit = item.Value.Up });
            }
            result.Sort(delegate(AppInfo a, AppInfo b) {
                int traffic = (b.DownloadBps + b.UploadBps).CompareTo(a.DownloadBps + a.UploadBps);
                return traffic == 0 ? StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name) : traffic;
            });
            return result;
        }

        // Includes stopped processes so a short-lived downloader is not lost
        // between UI polls. Identity retention is bounded; overflow is explicit.
        public List<AppInfo> TrafficSnapshot()
        {
            var result = new List<AppInfo>();
            lock (gate)
            {
                foreach (KeyValuePair<string, TrafficTotal> item in trafficTotals)
                {
                    Counters current;
                    counters.TryGetValue(item.Key, out current);
                    TrafficTotal total = item.Value;
                    result.Add(new AppInfo { Name = total.Name, Path = total.Path,
                        TrafficKey = item.Key, TrafficSessionId = trafficSessionId,
                        DownloadBytes = total.Down, UploadBytes = total.Up,
                        DownloadBps = current == null ? 0 : current.DownRate,
                        UploadBps = current == null ? 0 : current.UpRate });
                }
            }
            return result;
        }

        public void SetLimit(string path, long downloadBytesPerSecond, long uploadBytesPerSecond)
        {
            if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathRooted(path))
                throw new ArgumentException("无法获取程序的完整路径，不能对它设置限速。");
            path = System.IO.Path.GetFullPath(path);
            if (downloadBytesPerSecond < 0 || uploadBytesPerSecond < 0)
                throw new ArgumentOutOfRangeException("限速值不能为负数；0 表示不限速。");
            lock (operationGate)
            {
                if (disposed) throw new ObjectDisposedException("NetworkEngine");
                if (downloadBytesPerSecond == 0 && uploadBytesPerSecond == 0)
                {
                    bool noLimits;
                    lock (gate) { limits.Remove(path); noLimits = limits.Count == 0; }
                    if (noLimits) StopThrottle();
                    else if (throttle != null) throttle.Wake();
                    return;
                }
                if (!capturing) throw new InvalidOperationException("监控驱动未启动，不能应用限速。请以管理员身份运行并检查驱动文件。");
                if (throttle == null)
                {
                    // Session construction starts its receiver immediately. Until the rule
                    // is committed below, every packet is reinjected without queuing.
                    throttle = new ThrottleSession(this);
                }
                lock (gate) limits[path] = new RateLimit { Down = downloadBytesPerSecond, Up = uploadBytesPerSecond };
                throttle.Wake();
            }
        }

        public void ClearLimits()
        {
            lock (operationGate)
            {
                lock (gate) limits.Clear();
                StopThrottle();
            }
        }

        private void StopThrottle()
        {
            ThrottleSession old = throttle;
            throttle = null;
            if (old != null) old.Dispose();
        }

        private long GetRate(string path, bool outbound)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            lock (gate)
            {
                RateLimit r;
                return limits.TryGetValue(path, out r) ? (outbound ? r.Up : r.Down) : 0;
            }
        }

        private Identity Identify(Packet p)
        {
            Ownership table = ownership; // immutable snapshot, atomically published
            int pid = table.Find(p);
            Identity id;
            if (pid < 0) return null;
            if (pid > 0 && table.Processes.TryGetValue(pid, out id)) return id;
            lock (recentGate) return recentOwners.TryGetValue(p.Key, out id) ? id : null;
        }

        private Identity ResolveFirstPacket(Packet p)
        {
            Identity found = Identify(p);
            if (found != null) return found;
            // New connections can finish between timer ticks. Resolve the very first
            // unmatched packet synchronously before choosing whether it must be paced.
            // This queries kernel connection metadata, never packet payload or DNS.
            var table = new Ownership();
            List<Endpoint> rows = Native.ReadPacketEndpoints(p);
            int listenerPid = 0;
            foreach (Endpoint row in rows)
            {
                if (row.Pid <= 0) continue;
                if (p.Key.Protocol == 6)
                {
                    if (row.State != 1 && row.State != 2) Ownership.Add(table.Tcp, row.Key, row.Pid);
                    else if (row.State == 2 && p.InitialSyn && row.Key.LocalPort == p.Key.LocalPort &&
                        (row.Key.Local.Equals(p.Key.Local) || (row.Key.Local.Family == p.Key.Local.Family && row.Key.Local.High == 0 && row.Key.Local.Low == 0)))
                    {
                        if (listenerPid == 0) listenerPid = row.Pid;
                        else if (listenerPid != row.Pid) listenerPid = -1;
                    }
                }
                else Ownership.Add(table.Udp, row.Key, row.Pid);
            }
            int pid = table.Find(p);
            if (pid == 0 && p.InitialSyn && listenerPid > 0) pid = listenerPid;
            if (pid <= 0) return null; // shared/unknown ports pass immediately
            found = Native.ReadIdentity(pid);
            // This cache is cleared on every ownership refresh (500 ms), bounding the
            // unavoidable OS tuple/PID reuse race. No permanent PID identity cache.
            lock (recentGate)
            {
                if (recentOwners.Count < 8192) recentOwners[p.Key] = found;
            }
            return found;
        }

        private void SniffLoop()
        {
            var buffer = new byte[65575];
            try
            {
                while (!disposed)
                {
                    Native.Address address;
                    uint length;
                    if (!Native.WinDivertRecv(sniffHandle, buffer, (uint)buffer.Length, out length, out address))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (disposed) break;
                        throw new Win32Exception(error);
                    }
                    Packet packet;
                    if (!Packet.TryParse(buffer, (int)length, address.Outbound, out packet)) continue;
                    Identity id = Identify(packet);
                    if (id == null) continue;
                    lock (gate)
                    {
                        Counters c;
                        if (!counters.TryGetValue(id.Key, out c)) counters[id.Key] = c = new Counters();
                        if (packet.Outbound) c.Up += length; else c.Down += length;
                        string totalKey = id.Key;
                        TrafficTotal total;
                        if (!trafficTotals.TryGetValue(totalKey, out total))
                        {
                            if (trafficTotals.Count >= 8191) totalKey = "@traffic-overflow";
                            if (!trafficTotals.TryGetValue(totalKey, out total))
                                trafficTotals[totalKey] = total = new TrafficTotal {
                                    Name = totalKey == "@traffic-overflow" ? "其他程序（统计上限合并）" : id.Name,
                                    Path = totalKey == "@traffic-overflow" ? null : id.Path };
                        }
                        if (packet.Outbound) total.Up += length; else total.Down += length;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!disposed)
                {
                    status = "实时监控已停止：" + FriendlyError(ex);
                    // A monitoring failure must not leave invisible throttles running.
                    ThreadPool.QueueUserWorkItem(delegate { try { ClearLimits(); } catch { } });
                }
            }
            finally { capturing = false; }
        }

        private void TableLoop()
        {
            while (!finish.WaitOne(500))
            {
                try { RefreshOwnership(); }
                catch (Exception ex)
                {
                    // Do not continue using stale tuples after table failure or PID reuse.
                    ownership = new Ownership();
                    lock (recentGate) recentOwners.Clear();
                    status = "连接列表暂时不可读，未知连接放行：" + FriendlyError(ex);
                }
            }
        }

        private void RefreshOwnership()
        {
            List<Endpoint> rows = Native.ReadEndpoints();
            var next = new Ownership();
            var nextApps = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (Endpoint row in rows)
            {
                if (row.Pid <= 0) continue;
                Identity id;
                if (!next.Processes.TryGetValue(row.Pid, out id))
                {
                    // Re-resolve each polling pass; retaining PID-to-path indefinitely can
                    // throttle an unrelated process after Windows reuses a terminated PID.
                    id = Native.ReadIdentity(row.Pid);
                    next.Processes[row.Pid] = id;
                }
                AppInfo app;
                if (!nextApps.TryGetValue(id.Key, out app))
                    nextApps[id.Key] = app = new AppInfo { Name = id.Name, Path = id.Path };
                if (!app.Pids.Contains(row.Pid)) app.Pids.Add(row.Pid);
                if (row.Key.Protocol == 6)
                {
                    // Listeners are visible, but not used as an attribution fallback.
                    // Only a complete TCP tuple can unambiguously own a packet.
                    if (row.State != 2 && row.State != 1) app.TcpConnections++;
                    if (row.State != 2 && row.State != 1) Ownership.Add(next.Tcp, row.Key, row.Pid);
                }
                else { app.UdpEndpoints++; Ownership.Add(next.Udp, row.Key, row.Pid); }
            }
            ownership = next;
            lock (recentGate) recentOwners.Clear();
            long now = Stopwatch.GetTimestamp();
            double seconds = Math.Max(0.001, (now - lastSample) / (double)Stopwatch.Frequency);
            lastSample = now;
            lock (gate)
            {
                var remove = new List<string>();
                foreach (KeyValuePair<string, Counters> item in counters)
                {
                    Counters c = item.Value;
                    c.DownRate = (c.Down - c.LastDown) / seconds;
                    c.UpRate = (c.Up - c.LastUp) / seconds;
                    c.LastDown = c.Down; c.LastUp = c.Up;
                    if (!nextApps.ContainsKey(item.Key) && !limits.ContainsKey(item.Key)) remove.Add(item.Key);
                }
                foreach (string key in remove) counters.Remove(key);
                apps = nextApps;
            }
        }

        private void ThrottleFailed(ThrottleSession failed, Exception error)
        {
            status = "限速已自动解除，网络恢复放行：" + FriendlyError(error);
            ThreadPool.QueueUserWorkItem(delegate {
                lock (operationGate)
                {
                    if (throttle == failed)
                    {
                        lock (gate) limits.Clear();
                        StopThrottle();
                    }
                }
            });
        }

        public void Dispose()
        {
            lock (operationGate)
            {
                if (disposed) return;
                disposed = true;
                finish.Set();
                lock (gate) limits.Clear();
                StopThrottle();
                if (sniffHandle != Native.InvalidHandle)
                {
                    Native.WinDivertShutdown(sniffHandle, Native.ShutdownReceive);
                    if (sniffThread != null) sniffThread.Join(1500);
                    Native.WinDivertClose(sniffHandle);
                    sniffHandle = Native.InvalidHandle;
                }
                if (tableThread != null) tableThread.Join(1500);
                capturing = false;
                status = "已停止";
            }
        }

        private static Thread BackgroundThread(ThreadStart action, string name)
        { return new Thread(action) { IsBackground = true, Name = name }; }

        private static string FriendlyError(Exception ex)
        {
            if (ex is DllNotFoundException) return "找不到 WinDivert.dll，请完整解压后运行。";
            if (ex is BadImageFormatException) return "驱动架构不匹配，需要 Windows x64。";
            var win32 = ex as Win32Exception;
            return win32 == null ? ex.Message : ex.Message + "（错误 " + win32.NativeErrorCode + "）";
        }

        private sealed class RateLimit { public long Down, Up; }
        private sealed class Counters { public long Down, Up, LastDown, LastUp; public double DownRate, UpRate; }
        private sealed class TrafficTotal { public string Name, Path; public long Down, Up; }
        private sealed class Identity { public string Name, Path, Key; }
        private sealed class Endpoint { public FlowKey Key; public int Pid, State; }

        private struct NetAddress : IEquatable<NetAddress>
        {
            public ulong High, Low;
            public byte Family;
            public static NetAddress Read(byte[] data, int start, bool v6)
            {
                if (!v6) return new NetAddress { Family = 4, Low = ReadBig(data, start, 4) };
                ulong high = ReadBig(data, start, 8), low = ReadBig(data, start + 8, 8);
                if (high == 0 && (low >> 32) == 0xFFFF)
                    return new NetAddress { Family = 4, Low = low & 0xFFFFFFFFUL };
                return new NetAddress { Family = 6, High = high, Low = low };
            }
            private static ulong ReadBig(byte[] data, int offset, int count)
            { ulong n = 0; for (int i = 0; i < count; i++) n = (n << 8) | data[offset + i]; return n; }
            public bool Equals(NetAddress other) { return Family == other.Family && High == other.High && Low == other.Low; }
            public override bool Equals(object other) { return other is NetAddress && Equals((NetAddress)other); }
            public override int GetHashCode() { unchecked { return ((High.GetHashCode() * 397) ^ Low.GetHashCode()) * 31 + Family; } }
        }

        private struct FlowKey : IEquatable<FlowKey>
        {
            public byte Protocol;
            public NetAddress Local, Remote;
            public ushort LocalPort, RemotePort;
            public bool Equals(FlowKey other)
            { return Protocol == other.Protocol && LocalPort == other.LocalPort && RemotePort == other.RemotePort && Local.Equals(other.Local) && Remote.Equals(other.Remote); }
            public override bool Equals(object obj) { return obj is FlowKey && Equals((FlowKey)obj); }
            public override int GetHashCode()
            { unchecked { return ((((Local.GetHashCode() * 397) ^ Remote.GetHashCode()) * 397 ^ LocalPort) * 397 ^ RemotePort) * 31 + Protocol; } }
        }

        private struct Packet
        {
            public FlowKey Key;
            public bool Outbound, IpV6, InitialSyn;
            public static bool TryParse(byte[] bytes, int length, bool outbound, out Packet packet)
            {
                packet = new Packet();
                if (length < 20 || bytes == null || length > bytes.Length) return false;
                bool v6 = (bytes[0] >> 4) == 6;
                int cursor, protocol, effectiveLength;
                NetAddress source, destination;
                if ((bytes[0] >> 4) == 4)
                {
                    cursor = (bytes[0] & 15) * 4;
                    effectiveLength = Read16(bytes, 2);
                    if (cursor < 20 || cursor > length || effectiveLength < cursor || effectiveLength > length) return false;
                    // Non-reassembled fragments cannot reliably be attributed to a PID.
                    if ((Read16(bytes, 6) & 0x3FFF) != 0) return false;
                    protocol = bytes[9];
                    source = NetAddress.Read(bytes, 12, false); destination = NetAddress.Read(bytes, 16, false);
                }
                else if (v6)
                {
                    if (length < 40) return false;
                    effectiveLength = 40 + Read16(bytes, 4);
                    if (effectiveLength > length || effectiveLength == 40) return false; // no jumbograms
                    cursor = 40; protocol = bytes[6];
                    source = NetAddress.Read(bytes, 8, true); destination = NetAddress.Read(bytes, 24, true);
                    int extensions = 0;
                    while (protocol != 6 && protocol != 17)
                    {
                        if (++extensions > 8 || cursor + 2 > effectiveLength) return false;
                        int extensionLength;
                        if (protocol == 0 || protocol == 43 || protocol == 60) extensionLength = (bytes[cursor + 1] + 1) * 8;
                        else if (protocol == 51) extensionLength = (bytes[cursor + 1] + 2) * 4;
                        else if (protocol == 44)
                        {
                            if (cursor + 8 > effectiveLength || (Read16(bytes, cursor + 2) & 0xFFF9) != 0) return false;
                            extensionLength = 8;
                        }
                        else return false;
                        if (cursor + extensionLength > effectiveLength) return false;
                        protocol = bytes[cursor]; cursor += extensionLength;
                    }
                }
                else return false;
                if (protocol != 6 && protocol != 17) return false;
                int minimum = protocol == 6 ? 20 : 8;
                if (cursor + minimum > effectiveLength) return false;
                if (protocol == 6 && ((bytes[cursor + 12] >> 4) * 4 < 20 || cursor + (bytes[cursor + 12] >> 4) * 4 > effectiveLength)) return false;
                ushort sourcePort = (ushort)Read16(bytes, cursor), destinationPort = (ushort)Read16(bytes, cursor + 2);
                packet.Outbound = outbound;
                packet.IpV6 = v6;
                packet.InitialSyn = protocol == 6 && (bytes[cursor + 13] & 0x12) == 0x02;
                packet.Key = new FlowKey { Protocol = (byte)protocol,
                    Local = outbound ? source : destination, Remote = outbound ? destination : source,
                    LocalPort = outbound ? sourcePort : destinationPort, RemotePort = outbound ? destinationPort : sourcePort };
                return true;
            }
            internal static int Read16(byte[] bytes, int index) { return (bytes[index] << 8) | bytes[index + 1]; }
        }

        private sealed class Ownership
        {
            internal readonly Dictionary<FlowKey, int> Tcp = new Dictionary<FlowKey, int>();
            internal readonly Dictionary<FlowKey, int> Udp = new Dictionary<FlowKey, int>();
            internal readonly Dictionary<int, Identity> Processes = new Dictionary<int, Identity>();
            internal static void Add(Dictionary<FlowKey, int> map, FlowKey key, int pid)
            { int old; if (!map.TryGetValue(key, out old)) map.Add(key, pid); else if (old != pid) map[key] = -1; }
            internal int Find(Packet packet)
            {
                int result;
                if (packet.Key.Protocol == 6) return Tcp.TryGetValue(packet.Key, out result) ? result : 0;
                FlowKey key = packet.Key;
                key.Remote = new NetAddress(); key.RemotePort = 0;
                int exact, wildcard;
                Udp.TryGetValue(key, out exact);
                key.Local = new NetAddress { Family = key.Local.Family };
                Udp.TryGetValue(key, out wildcard);
                // Never guess when SO_REUSEADDR/shared UDP ports have multiple owners.
                if (exact < 0 || wildcard < 0 || (exact > 0 && wildcard > 0 && exact != wildcard)) return -1;
                int owner = exact > 0 ? exact : wildcard;
                if (packet.Key.Local.Family == 4)
                {
                    // A dual-stack :: UDP socket can receive/send IPv4 (e.g. QUIC).
                    // IP Helper does not expose IPV6_V6ONLY. Include its possible
                    // owner, and reject attribution if the families disagree.
                    key.Local = new NetAddress { Family = 6 };
                    int dualStack; Udp.TryGetValue(key, out dualStack);
                    if (dualStack < 0 || (owner > 0 && dualStack > 0 && owner != dualStack)) return -1;
                    if (owner == 0) owner = dualStack;
                }
                return owner;
            }
        }

        private sealed class ThrottleSession : IDisposable
        {
            private const long MaximumBufferedBytes = 32 * 1024 * 1024;
            private readonly NetworkEngine owner;
            private readonly object queueGate = new object();
            private readonly Dictionary<string, Bucket> buckets = new Dictionary<string, Bucket>(StringComparer.OrdinalIgnoreCase);
            private readonly AutoResetEvent changed = new AutoResetEvent(false);
            private readonly IntPtr handle;
            private readonly Thread receiver, scheduler;
            private volatile bool stopping;
            private int faultReported, closed;
            private long queuedBytes;

            internal ThrottleSession(NetworkEngine engine)
            {
                owner = engine;
                // NETWORK has no PID filter. Unknown/unlimited packets are immediately
                // reinjected by the receiver; only identified target packets enter queues.
                // At least one explicit user limit is required to create this handle.
                handle = Native.WinDivertOpen("(tcp or udp) and !loopback and !impostor", 0, 100, 0);
                if (handle == Native.InvalidHandle) throw new Win32Exception(Marshal.GetLastWin32Error());
                Native.WinDivertSetParam(handle, 0, 8192);
                Native.WinDivertSetParam(handle, 1, 1000);
                Native.WinDivertSetParam(handle, 2, 16777216);
                receiver = BackgroundThread(Receive, "NetBunny limiter receive");
                scheduler = BackgroundThread(Schedule, "NetBunny limiter scheduler");
                receiver.Start(); scheduler.Start();
            }

            internal void Wake() { changed.Set(); }

            private void Receive()
            {
                byte[] buffer = new byte[65575];
                try
                {
                    for (;;)
                    {
                        uint length; Native.Address address;
                        if (!Native.WinDivertRecv(handle, buffer, (uint)buffer.Length, out length, out address))
                        {
                            int error = Marshal.GetLastWin32Error();
                            if (stopping && (error == 232 || error == 6 || error == 995)) break;
                            throw new Win32Exception(error);
                        }
                        Packet packet;
                        Identity id = null;
                        if (!stopping && Packet.TryParse(buffer, (int)length, address.Outbound, out packet))
                        {
                            try { id = owner.ResolveFirstPacket(packet); }
                            catch { id = null; } // metadata failure must not interrupt unrelated traffic
                        }
                        long rate = id == null ? 0 : owner.GetRate(id.Path, address.Outbound);
                        bool sendNow = true;
                        if (rate > 0)
                        {
                            lock (queueGate)
                            {
                                if (!stopping)
                                {
                                    sendNow = false;
                                    string key = id.Path + (address.Outbound ? "|upload" : "|download");
                                    Bucket bucket;
                                    if (!buckets.TryGetValue(key, out bucket))
                                        buckets[key] = bucket = new Bucket(id.Path, address.Outbound);
                                    long maxForBucket = Math.Min(4 * 1024 * 1024L, Math.Max(65536, rate > long.MaxValue / 2 ? long.MaxValue : rate * 2));
                                    if (queuedBytes + length <= MaximumBufferedBytes && bucket.Bytes + length <= maxForBucket && bucket.Items.Count < 4096)
                                    {
                                        byte[] copy = new byte[length];
                                        Buffer.BlockCopy(buffer, 0, copy, 0, (int)length);
                                        bucket.Items.Enqueue(new QueuedPacket { Data = copy, Address = address });
                                        bucket.Bytes += length; queuedBytes += length;
                                    }
                                    // Overflow drops only this explicitly limited target.
                                    // TCP backs off; UDP can lose packets. Fail-open overflow
                                    // would make a configured cap silently ineffective.
                                }
                            }
                            changed.Set();
                        }
                        if (sendNow) Send(buffer, (int)length, address);
                    }
                }
                catch (Exception ex) { Fail(ex); }
            }

            private void Schedule()
            {
                try
                {
                    for (;;)
                    {
                        var ready = new List<QueuedPacket>();
                        int wait = 50;
                        bool done;
                        lock (queueGate)
                        {
                            long now = Stopwatch.GetTimestamp();
                            var empty = new List<string>();
                            foreach (KeyValuePair<string, Bucket> item in buckets)
                            {
                                Bucket bucket = item.Value;
                                long rate = stopping ? 0 : owner.GetRate(bucket.Path, bucket.Outbound);
                                while (bucket.Items.Count > 0)
                                {
                                    QueuedPacket front = bucket.Items.Peek();
                                    if (rate > 0)
                                    {
                                        bucket.Pacer.Refill(now, rate, front.Data.Length);
                                        if (!bucket.Pacer.TryTake(front.Data.Length))
                                        {
                                            wait = Math.Min(wait, bucket.Pacer.WaitMilliseconds(front.Data.Length, rate));
                                            break;
                                        }
                                    }
                                    ready.Add(bucket.Items.Dequeue());
                                    bucket.Bytes -= front.Data.Length; queuedBytes -= front.Data.Length;
                                    if (ready.Count >= 512 && !stopping) { wait = 1; break; }
                                }
                                if (bucket.Items.Count == 0)
                                {
                                    // Preserve credit/time for two seconds between bursts.
                                    if (now - bucket.LastUsed > Stopwatch.Frequency * 2L) empty.Add(item.Key);
                                }
                                else bucket.LastUsed = now;
                            }
                            foreach (string key in empty) buckets.Remove(key);
                            done = stopping && queuedBytes == 0;
                        }
                        foreach (QueuedPacket packet in ready) Send(packet.Data, packet.Data.Length, packet.Address);
                        if (done) break;
                        changed.WaitOne(Math.Max(1, wait));
                    }
                }
                catch (Exception ex) { Fail(ex); }
            }

            private void Send(byte[] bytes, int length, Native.Address address)
            {
                uint sent;
                if (!Native.WinDivertSend(handle, bytes, (uint)length, out sent, ref address))
                {
                    int error = Marshal.GetLastWin32Error();
                    // An inbound interface may vanish during VPN/Wi-Fi changes. Stop
                    // diversion on any send failure instead of risking a stuck network.
                    throw new Win32Exception(error);
                }
            }

            private void Fail(Exception error)
            {
                if (Interlocked.Exchange(ref faultReported, 1) != 0 || stopping) return;
                lock (queueGate) stopping = true;
                Native.WinDivertShutdown(handle, Native.ShutdownReceive); // stop capture, keep Send alive
                changed.Set();
                owner.ThrottleFailed(this, error);
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref closed, 1) != 0) return;
                lock (queueGate) stopping = true;
                // Shutdown(RECV) stops capturing new packets. Receiver drains the
                // kernel queue; scheduler releases its memory queue without pacing.
                Native.WinDivertShutdown(handle, Native.ShutdownReceive);
                changed.Set();
                if (receiver != Thread.CurrentThread) receiver.Join(2500);
                if (scheduler != Thread.CurrentThread) scheduler.Join(2500);
                if (!receiver.IsAlive)
                {
                    // A receiver that failed may have left captured packets in the
                    // kernel queue. Shutdown(RECV) makes this finite; release them.
                    byte[] remaining = new byte[65575]; uint count; Native.Address address;
                    while (Native.WinDivertRecv(handle, remaining, (uint)remaining.Length, out count, out address))
                    { uint sent; Native.WinDivertSend(handle, remaining, count, out sent, ref address); }
                }
                // If scheduler failed, make a final best effort to release held packets.
                lock (queueGate)
                {
                    foreach (Bucket bucket in buckets.Values)
                        while (bucket.Items.Count > 0)
                        {
                            QueuedPacket packet = bucket.Items.Dequeue();
                            uint sent; Native.Address address = packet.Address;
                            Native.WinDivertSend(handle, packet.Data, (uint)packet.Data.Length, out sent, ref address);
                        }
                    buckets.Clear(); queuedBytes = 0;
                }
                Native.WinDivertClose(handle);
                // Never persist a limiting handle or a limit across app restarts.
            }

            private sealed class QueuedPacket { internal byte[] Data; internal Native.Address Address; }
            private sealed class Bucket
            {
                internal readonly string Path;
                internal readonly bool Outbound;
                internal readonly Queue<QueuedPacket> Items = new Queue<QueuedPacket>();
                internal readonly Pacer Pacer = new Pacer(Stopwatch.GetTimestamp());
                internal long Bytes, LastUsed = Stopwatch.GetTimestamp();
                internal Bucket(string path, bool outbound) { Path = path; Outbound = outbound; }
            }
        }

        // Each executable/direction has independent credit; a low limit for app A
        // cannot make app B wait. Limits include IP headers and have a packet-sized
        // burst tolerance plus up to 25 ms of credit, not an exact wire-speed SLA.
        // Inbound pacing limits delivery to the application. It cannot undo bytes
        // already received by the NIC: TCP usually adapts; UDP sources may not.
        // First packets use a synchronous IP Helper lookup. OS process termination
        // and tuple/PID reuse still race metadata snapshots; unknown/ambiguous tuples
        // are intentionally passed instead of guessed.
        private sealed class Pacer
        {
            internal double Credit;
            private long last;
            internal Pacer(long start) { last = start; }
            internal void Refill(long now, long bytesPerSecond, int nextPacketBytes)
            {
                double elapsed = Math.Max(0, (now - last) / (double)Stopwatch.Frequency);
                Credit = Math.Min(Math.Max(nextPacketBytes, bytesPerSecond * 0.025), Credit + elapsed * bytesPerSecond);
                last = now;
            }
            internal bool TryTake(int bytes)
            { if (Credit + 0.000001 < bytes) return false; Credit = Math.Max(0, Credit - bytes); return true; }
            internal int WaitMilliseconds(int bytes, long rate)
            { return (int)Math.Max(1, Math.Min(50, Math.Ceiling((bytes - Credit) * 1000 / Math.Max(1, rate)))); }
        }

        private static class Native
        {
            internal static readonly IntPtr InvalidHandle = new IntPtr(-1);
            internal const int ShutdownReceive = 1; // windivert.h v2.2.2, not a zero-based enum
            [StructLayout(LayoutKind.Explicit, Size = 80)]
            internal struct Address
            {
                [FieldOffset(0)] internal long Timestamp;
                [FieldOffset(8)] internal ulong Flags;
                [FieldOffset(16)] internal uint IfIdx;
                [FieldOffset(20)] internal uint SubIfIdx;
                // The remaining union bytes must survive unmodified reinjection.
                [FieldOffset(24)] private ulong data1;
                [FieldOffset(32)] private ulong data2;
                [FieldOffset(40)] private ulong data3;
                [FieldOffset(48)] private ulong data4;
                [FieldOffset(56)] private ulong data5;
                [FieldOffset(64)] private ulong data6;
                [FieldOffset(72)] private ulong data7;
                internal bool Outbound { get { return (Flags & (1UL << 17)) != 0; } }
            }
            [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
            internal static extern IntPtr WinDivertOpen(string filter, int layer, short priority, ulong flags);
            [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinDivertRecv(IntPtr handle, [Out] byte[] packet, uint packetLength, out uint received, out Address address);
            [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinDivertSend(IntPtr handle, byte[] packet, uint packetLength, out uint sent, ref Address address);
            [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinDivertSetParam(IntPtr handle, int parameter, ulong value);
            [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinDivertShutdown(IntPtr handle, int how);
            [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool WinDivertClose(IntPtr handle);
            [DllImport("iphlpapi.dll", SetLastError = true)]
            private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
            [DllImport("iphlpapi.dll", SetLastError = true)]
            private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int family, int tableClass, uint reserved);
            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
            [DllImport("kernel32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);

            internal static Identity ReadIdentity(int pid)
            {
                string path = null, name = null;
                IntPtr process = OpenProcess(0x1000, false, pid);
                if (process != IntPtr.Zero)
                {
                    try
                    {
                        var text = new StringBuilder(32768); int length = text.Capacity;
                        if (QueryFullProcessImageName(process, 0, text, ref length)) path = text.ToString();
                    }
                    finally { CloseHandle(process); }
                }
                if (!string.IsNullOrEmpty(path)) name = System.IO.Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(name))
                {
                    try { using (Process p = Process.GetProcessById(pid)) name = p.ProcessName; }
                    catch { name = "进程 " + pid; }
                }
                return new Identity { Path = path ?? "", Name = name, Key = string.IsNullOrEmpty(path) ? "#pid:" + pid : path };
            }

            internal static List<Endpoint> ReadEndpoints()
            {
                var rows = new List<Endpoint>();
                ReadTable(rows, false, true); ReadTable(rows, true, true);
                ReadTable(rows, false, false); ReadTable(rows, true, false);
                return rows;
            }

            internal static List<Endpoint> ReadPacketEndpoints(Packet packet)
            {
                var rows = new List<Endpoint>();
                bool tcp = packet.Key.Protocol == 6;
                ReadTable(rows, packet.IpV6, tcp);
                if (!tcp && !packet.IpV6) ReadTable(rows, true, false);
                return rows;
            }

            private static void ReadTable(List<Endpoint> rows, bool v6, bool tcp)
            {
                int size = 0, family = v6 ? 23 : 2;
                uint error = tcp ? GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0)
                    : GetExtendedUdpTable(IntPtr.Zero, ref size, false, family, 1, 0);
                if (error == 50 || error == 232) return;
                if (error != 0 && error != 122) throw new Win32Exception((int)error);
                if (size < 4) return;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    int allocated = size;
                    IntPtr memory = Marshal.AllocHGlobal(allocated);
                    try
                    {
                        error = tcp ? GetExtendedTcpTable(memory, ref size, false, family, 5, 0)
                            : GetExtendedUdpTable(memory, ref size, false, family, 1, 0);
                        if (error == 122) continue;
                        if (error != 0) throw new Win32Exception((int)error);
                        byte[] bytes = new byte[allocated]; Marshal.Copy(memory, bytes, 0, allocated);
                        int count = BitConverter.ToInt32(bytes, 0), rowSize = tcp ? (v6 ? 56 : 24) : (v6 ? 28 : 12);
                        if (count < 0 || count > (allocated - 4) / rowSize) throw new InvalidDataException("网络连接表长度无效。");
                        for (int i = 0, offset = 4; i < count; i++, offset += rowSize)
                        {
                            FlowKey key = new FlowKey { Protocol = (byte)(tcp ? 6 : 17) };
                            int pid, state = 0;
                            if (tcp && !v6)
                            {
                                state = BitConverter.ToInt32(bytes, offset);
                                key.Local = NetAddress.Read(bytes, offset + 4, false); key.LocalPort = (ushort)Packet.Read16(bytes, offset + 8);
                                key.Remote = NetAddress.Read(bytes, offset + 12, false); key.RemotePort = (ushort)Packet.Read16(bytes, offset + 16);
                                pid = BitConverter.ToInt32(bytes, offset + 20);
                            }
                            else if (tcp)
                            {
                                key.Local = NetAddress.Read(bytes, offset, true); key.LocalPort = (ushort)Packet.Read16(bytes, offset + 20);
                                key.Remote = NetAddress.Read(bytes, offset + 24, true); key.RemotePort = (ushort)Packet.Read16(bytes, offset + 44);
                                state = BitConverter.ToInt32(bytes, offset + 48); pid = BitConverter.ToInt32(bytes, offset + 52);
                            }
                            else
                            {
                                key.Local = NetAddress.Read(bytes, offset, v6); key.LocalPort = (ushort)Packet.Read16(bytes, offset + (v6 ? 20 : 4));
                                pid = BitConverter.ToInt32(bytes, offset + (v6 ? 24 : 8));
                            }
                            rows.Add(new Endpoint { Key = key, Pid = pid, State = state });
                        }
                        return;
                    }
                    finally { Marshal.FreeHGlobal(memory); }
                }
                throw new InvalidOperationException("连接变化过快，稍后重试。");
            }
        }

        /// <summary>Deterministic tests; never opens WinDivert or changes network state.</summary>
        public static string SelfTest()
        {
            int assertions = 0;
            Action<bool, string> check = delegate(bool condition, string name) {
                assertions++; if (!condition) throw new InvalidOperationException("Network self-test failed: " + name);
            };
            check(Marshal.SizeOf(typeof(Native.Address)) == 80, "WinDivert address ABI");
            check(Native.ShutdownReceive == 1, "WinDivert receive shutdown constant");
            Native.Address address = new Native.Address { Flags = 1UL << 17 };
            check(address.Outbound, "outbound flag ABI");
            var tcp4 = new byte[40]; tcp4[0] = 0x45; tcp4[3] = 40; tcp4[9] = 6;
            tcp4[12] = 10; tcp4[15] = 1; tcp4[16] = 1; tcp4[19] = 2;
            tcp4[20] = 0xC3; tcp4[21] = 0x50; tcp4[22] = 0x01; tcp4[23] = 0xBB; tcp4[32] = 0x50;
            Packet a, b;
            check(Packet.TryParse(tcp4, tcp4.Length, true, out a), "IPv4 TCP parser");
            check(a.Key.LocalPort == 50000 && a.Key.RemotePort == 443, "network port order");
            check(Packet.TryParse(tcp4, tcp4.Length, false, out b) && b.Key.LocalPort == 443, "inbound tuple reversal");
            check(!Packet.TryParse(tcp4, 20, true, out b), "truncated packet rejected");
            tcp4[6] = 0x20; check(!Packet.TryParse(tcp4, tcp4.Length, true, out b), "fragment rejected"); tcp4[6] = 0;
            var udp6 = new byte[56]; udp6[0] = 0x60; udp6[5] = 16; udp6[6] = 0;
            udp6[8] = 0x20; udp6[9] = 1; udp6[23] = 1; udp6[24] = 0x20; udp6[25] = 1; udp6[39] = 2;
            udp6[40] = 17; udp6[41] = 0; udp6[48] = 0x13; udp6[49] = 0x88; udp6[51] = 53; udp6[53] = 8;
            check(Packet.TryParse(udp6, udp6.Length, true, out b) && b.Key.Protocol == 17 && b.Key.LocalPort == 5000 && b.Key.Local.Family == 6, "IPv6 extension UDP parser");
            var ownershipTest = new Ownership(); Ownership.Add(ownershipTest.Tcp, a.Key, 100);
            check(ownershipTest.Find(a) == 100, "exact TCP ownership");
            Ownership.Add(ownershipTest.Tcp, a.Key, 200);
            check(ownershipTest.Find(a) <= 0, "ambiguous TCP not guessed");
            FlowKey udpKey = b.Key; udpKey.Remote = new NetAddress(); udpKey.RemotePort = 0;
            Ownership.Add(ownershipTest.Udp, udpKey, 300);
            check(ownershipTest.Find(b) == 300, "UDP endpoint ownership");
            udpKey.Local = new NetAddress { Family = 6 }; Ownership.Add(ownershipTest.Udp, udpKey, 400);
            check(ownershipTest.Find(b) <= 0, "shared UDP wildcard ambiguity");
            var dualOwnership = new Ownership();
            Packet ipv4Udp = a; ipv4Udp.Key.Protocol = 17;
            FlowKey dualKey = new FlowKey { Protocol = 17, Local = new NetAddress { Family = 6 }, LocalPort = ipv4Udp.Key.LocalPort };
            Ownership.Add(dualOwnership.Udp, dualKey, 500);
            check(dualOwnership.Find(ipv4Udp) == 500, "IPv4 traffic through dual-stack UDP wildcard");
            dualKey.Local = new NetAddress { Family = 4 }; Ownership.Add(dualOwnership.Udp, dualKey, 600);
            check(dualOwnership.Find(ipv4Udp) <= 0, "dual-stack shared UDP ambiguity");
            var pacer = new Pacer(0); pacer.Refill(Stopwatch.Frequency / 2, 1000, 1000);
            check(!pacer.TryTake(1000), "no early packet");
            pacer.Refill(Stopwatch.Frequency, 1000, 1000); check(pacer.TryTake(1000), "exact one-second pacing");
            check(!pacer.TryTake(1), "credit consumed");
            var otherPacer = new Pacer(0); otherPacer.Refill(Stopwatch.Frequency, 2000, 1000);
            check(otherPacer.TryTake(1000), "independent app-direction pacing");
            pacer.Refill(Stopwatch.Frequency * 100L, 1000, 1000);
            check(pacer.Credit <= 1000, "idle burst bounded to a packet");
            using (var ledgerEngine = new NetworkEngine())
            {
                ledgerEngine.trafficTotals["closed.exe"] = new TrafficTotal { Name = "closed", Path = "C:\\closed.exe", Down = 1500, Up = 320 };
                List<AppInfo> saved = ledgerEngine.TrafficSnapshot();
                check(saved.Count == 1 && saved[0].DownloadBytes == 1500 && saved[0].UploadBytes == 320, "exited process final byte counts survive");
                check(ledgerEngine.Snapshot().Count == 0, "history does not pollute live process list");
                check(saved[0].TrafficSessionId == ledgerEngine.TrafficSnapshot()[0].TrafficSessionId, "stable cumulative session identity");
                saved[0].DownloadBytes = 0;
                check(ledgerEngine.TrafficSnapshot()[0].DownloadBytes == 1500, "traffic snapshots are detached copies");
            }
            return "NetworkEngine: " + assertions + " deterministic checks passed (driver not opened).";
        }
    }
}
