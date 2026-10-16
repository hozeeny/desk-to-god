using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace NetBunny
{
    // This is an optional visual theme hint, not a claim that a VPN or a person's location was verified.
    internal sealed class RegionThemeService : IDisposable
    {
        readonly object gate = new object();
        Timer timer;
        string code = "OTHER", mode = "AUTO", status = "无法确认出口地区，可手动切换；自动模式仅在疑似 VPN / 系统代理时查询。";
        bool possibleVpn, disposed, busy;
        DateTime nextCheckUtc = DateTime.MinValue, networkStableUtc = DateTime.MinValue, cooldownUntilUtc = DateTime.MinValue;
        readonly RegionLookupCache lookupCache = new RegionLookupCache();
        int generation;
        HttpWebRequest activeRequest;
        public event EventHandler Changed;
        public string CurrentCode { get { lock (gate) return code; } }
        public string Mode { get { lock (gate) return mode; } }
        public string Status { get { lock (gate) { int seconds = (int)Math.Ceiling((cooldownUntilUtc - DateTime.UtcNow).TotalSeconds); return status + (cooldownUntilUtc == DateTime.MinValue ? "" : seconds > 0 ? "（剩余 " + seconds + " 秒）" : "（冷却结束，可刷新）"); } } }
        public bool PossibleVpn { get { lock (gate) return possibleVpn; } }
        public const string PrivacyNote = "地区查询仅向 ipapi.co 暴露本次请求的出口 IP；不上传程序或文件数据。不能完全确认 VPN，分流时其他程序的出口可能不同。可关闭查询或手动选地区。";
        public RegionThemeService()
        {
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            timer = new Timer(delegate(object ignored) { Tick(); }, null, 5000, 5000);
        }
        public void SelectMode(string selected)
        {
            selected = (selected ?? "OFF").Trim().ToUpperInvariant();
            if (selected != "AUTO" && selected != "OFF" && selected != "CN" && selected != "JP" && selected != "US" && selected != "TW" && selected != "HK" && selected != "OTHER") selected = "OFF";
            HttpWebRequest abort;
            lock (gate)
            {
                if (disposed) return;
                mode = selected; generation++; nextCheckUtc = DateTime.MinValue; cooldownUntilUtc = DateTime.MinValue; abort = activeRequest;
                if (mode == "OFF") status = "地区查询已关闭；保留当前造型。";
                else if (mode == "AUTO") status = "自动地区模式：仅发现疑似 VPN / 系统代理时查询；无法确认时保留造型。";
                else { code = mode; status = "手动地区造型（不发送地区查询）"; }
            }
            if (abort != null) try { abort.Abort(); } catch { }
            Notify();
            if (selected == "AUTO") RefreshAsync();
        }
        void OnNetworkChanged(object sender, EventArgs e)
        {
            HttpWebRequest abort = null;
            lock (gate) if (!disposed) { generation++; networkStableUtc = DateTime.UtcNow.AddSeconds(8); nextCheckUtc = networkStableUtc; abort = activeRequest; }
            if (abort != null) try { abort.Abort(); } catch { }
        }
        void Tick()
        {
            lock (gate) if (disposed || mode != "AUTO" || busy || DateTime.UtcNow < nextCheckUtc) return;
            RefreshAsync();
        }
        public Task RefreshAsync() { return StartRefresh(false); }
        public Task RequestRefresh() { return StartRefresh(true); }
        Task StartRefresh(bool explicitRequest)
        {
            int version = -1;
            lock (gate)
            {
                if (disposed) return Task.FromResult(0);
                if (mode != "AUTO") { if (explicitRequest) status = "当前是关闭查询或手动造型模式；切换到“自动”后可刷新出口地区。"; }
                else if (busy) { if (explicitRequest) status = "已有地区查询正在进行，请等待本次结果。"; }
                else if (explicitRequest || DateTime.UtcNow >= networkStableUtc)
                { busy = true; version = generation; nextCheckUtc = DateTime.UtcNow.AddSeconds(60); }
            }
            if (version < 0) { if (explicitRequest) Notify(); return Task.FromResult(0); }
            return Task.Run(delegate { Refresh(version, explicitRequest); });
        }
        void Refresh(int version, bool explicitRequest)
        {
            string fingerprint = null;
            bool attempted = false;
            try
            {
                string hint;
                bool suspected = DetectPossibleVpn(out hint);
                fingerprint = NetworkFingerprint();
                lock (gate)
                {
                    if (disposed || generation != version || mode != "AUTO") return;
                    possibleVpn = suspected;
                    cooldownUntilUtc = DateTime.MinValue;
                    if (!suspected && !explicitRequest) { status = "未发现明显 VPN / 系统代理；无法确认出口地区，可手动刷新或切换造型。"; return; }
                    string cached; int remaining;
                    RegionLookupDecision decision = lookupCache.Decide(fingerprint, DateTime.UtcNow, explicitRequest, out cached, out remaining);
                    if (decision == RegionLookupDecision.Cached)
                    { code = cached; status = "使用本地网络指纹对应的 10 分钟地区缓存：" + DisplayName(code) + "。同一 VPN 接口内换节点可点“刷新出口地区”。"; return; }
                    if (decision == RegionLookupDecision.Cooldown)
                    { cooldownUntilUtc = DateTime.UtcNow.AddSeconds(remaining); nextCheckUtc = cooldownUntilUtc; status = "地区查询冷却中，保留当前造型"; return; }
                    lookupCache.BeginAttempt(DateTime.UtcNow); attempted = true;
                    status = (explicitRequest ? "手动刷新" : hint) + "；正在查询本程序的出口地区……";
                }
                Notify();
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://ipapi.co/country/");
                request.Method = "GET"; request.Timeout = 4500; request.ReadWriteTimeout = 4500;
                request.AllowAutoRedirect = false; request.UserAgent = "KnightAssistant/4";
                request.UseDefaultCredentials = false; request.Credentials = null; request.CookieContainer = null;
                request.AutomaticDecompression = DecompressionMethods.None;
                lock (gate)
                {
                    if (disposed || generation != version || mode != "AUTO") return;
                    activeRequest = request;
                }
                string mapped = null;
                // Timeout also bounds a stalled body / proxy exchange, not just the initial response.
                using (Timer timeout = new Timer(delegate(object ignored) { try { request.Abort(); } catch { } }, null, 6500, Timeout.Infinite))
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > 64) throw new IOException("地区服务未返回有效结果");
                    using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    {
                        char[] buffer = new char[65]; int count = 0, read;
                        while (count < buffer.Length && (read = reader.Read(buffer, count, buffer.Length - count)) > 0) count += read;
                        if (count > 64) throw new IOException("地区响应超出上限");
                        mapped = MapCountry(new string(buffer, 0, count));
                    }
                }
                lock (gate)
                {
                    if (disposed || generation != version || mode != "AUTO") return;
                    lookupCache.Record(fingerprint, mapped, DateTime.UtcNow);
                    if (mapped == null) { cooldownUntilUtc = DateTime.UtcNow.AddSeconds(60); nextCheckUtc = cooldownUntilUtc; status = "无法确认出口地区，保留当前造型；可手动切换"; }
                    else { code = mapped; status = "本程序出口地区对应造型：" + DisplayName(mapped) + "。这不能证明 VPN，分流程序可能不同。"; }
                }
            }
            catch (Exception)
            {
                lock (gate) if (!disposed && generation == version && mode == "AUTO")
                {
                    if (attempted) lookupCache.Record(fingerprint, null, DateTime.UtcNow);
                    cooldownUntilUtc = DateTime.UtcNow.AddSeconds(60); nextCheckUtc = cooldownUntilUtc;
                    status = "地区查询失败或超时；保留当前造型，可手动切换";
                }
            }
            finally
            {
                lock (gate) { activeRequest = null; busy = false; }
                Notify();
            }
        }
        static string NetworkFingerprint()
        {
            List<string> parts = new List<string>();
            try
            {
                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (item.OperationalStatus != OperationalStatus.Up || item.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    parts.Add("adapter:" + item.Id + ":" + item.NetworkInterfaceType);
                    try
                    {
                        IPInterfaceProperties properties = item.GetIPProperties();
                        foreach (GatewayIPAddressInformation gateway in properties.GatewayAddresses) parts.Add(item.Id + ":gateway:" + gateway.Address);
                        foreach (UnicastIPAddressInformation address in properties.UnicastAddresses) parts.Add(item.Id + ":address:" + address.Address);
                    }
                    catch { }
                }
            }
            catch { parts.Add("adapters-unavailable"); }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                    if (key != null) foreach (string name in new[] { "ProxyEnable", "ProxyServer", "ProxyOverride", "AutoConfigURL" }) parts.Add(name + ":" + Convert.ToString(key.GetValue(name, "")));
            }
            catch { parts.Add("proxy-unavailable"); }
            return HashFingerprint(parts);
        }
        internal static string HashFingerprint(IEnumerable<string> values)
        {
            List<string> sorted = new List<string>(values); sorted.Sort(StringComparer.Ordinal);
            using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n", sorted.ToArray())))).Replace("-", "");
        }
        static bool DetectPossibleVpn(out string hint)
        {
            hint = "发现疑似 VPN 网络适配器";
            try
            {
                foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (item.OperationalStatus != OperationalStatus.Up) continue;
                    if (item.NetworkInterfaceType == NetworkInterfaceType.Ppp || item.NetworkInterfaceType == NetworkInterfaceType.Tunnel) return true;
                    string name = (item.Name + " " + item.Description).ToLowerInvariant();
                    foreach (string word in new string[] { "wireguard", "wintun", "openvpn", "tap-windows", "tailscale", "zerotier", "clash", "v2ray", "protonvpn", "nordvpn", "surfshark", "tun adapter", "vpn" })
                        if (name.IndexOf(word, StringComparison.Ordinal) >= 0) return true;
                }
            }
            catch { }
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings"))
                {
                    if (key != null && ((Convert.ToInt32(key.GetValue("ProxyEnable", 0)) != 0 && !String.IsNullOrWhiteSpace(Convert.ToString(key.GetValue("ProxyServer", "")))) ||
                        !String.IsNullOrWhiteSpace(Convert.ToString(key.GetValue("AutoConfigURL", "")))))
                    { hint = "发现系统代理设置（不一定是 VPN）"; return true; }
                }
            }
            catch { }
            hint = "未发现明显 VPN / 系统代理"; return false;
        }
        internal static string MapCountry(string value)
        {
            if (value == null) return null;
            value = value.Trim().ToUpperInvariant();
            if (value.Length != 2 || value[0] < 'A' || value[0] > 'Z' || value[1] < 'A' || value[1] > 'Z' || value == "XX" || value == "ZZ") return null;
            return value == "CN" || value == "JP" || value == "US" || value == "TW" || value == "HK" ? value : "OTHER";
        }
        public static string DisplayName(string value)
        {
            switch (value) { case "CN": return "中国 · 妖塔与剑修"; case "JP": return "日本 · 鸟居与武士"; case "US": return "美国 · 自由女神像与牛仔";
                case "TW": return "台湾 · 旧屋与青蛙"; case "HK": return "香港 · 九龙城寨与街头小弟"; default: return "其他地区 · 古堡与骑士"; }
        }
        void Notify()
        {
            EventHandler handler;
            lock (gate) { if (disposed) return; handler = Changed; }
            if (handler != null) try { handler(this, EventArgs.Empty); } catch { }
        }
        public void Dispose()
        {
            HttpWebRequest request; Timer oldTimer;
            lock (gate) { if (disposed) return; disposed = true; generation++; request = activeRequest; oldTimer = timer; timer = null; Changed = null; }
            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            if (oldTimer != null) oldTimer.Dispose();
            if (request != null) try { request.Abort(); } catch { }
        }
        public static string SelfTest()
        {
            int passed = 0;
            foreach (string country in new string[] { "CN", "JP", "US", "TW", "HK" }) { if (MapCountry(country) != country) throw new InvalidOperationException("Region mapping"); passed++; }
            if (MapCountry("de") != "OTHER" || MapCountry("XX") != null || MapCountry("bad response") != null || MapCountry(null) != null) throw new InvalidOperationException("Region unknown handling"); passed += 4;
            using (RegionThemeService service = new RegionThemeService())
            {
                service.SelectMode("OFF");
                if (service.CurrentCode != "OTHER") throw new InvalidOperationException("Region default"); passed++;
                service.SelectMode("JP"); if (service.CurrentCode != "JP" || service.Mode != "JP") throw new InvalidOperationException("Region manual"); passed++;
                service.SelectMode("OFF"); if (service.CurrentCode != "JP" || service.Mode != "OFF") throw new InvalidOperationException("Region disable preserves theme"); passed++;
                string unknown = MapCountry("unknown"); if (unknown != null) throw new InvalidOperationException("Region failed lookup changed skin"); passed++;
                service.SelectMode("invalid"); if (service.Mode != "OFF") throw new InvalidOperationException("Region invalid mode"); passed++;
                service.RequestRefresh().Wait(); if (service.Mode != "OFF" || service.CurrentCode != "JP") throw new InvalidOperationException("Region explicit refresh respects OFF"); passed++;
                int oldGeneration = service.generation; DateTime beforeChange = DateTime.UtcNow;
                service.OnNetworkChanged(null, EventArgs.Empty);
                if (service.generation != oldGeneration + 1 || service.networkStableUtc < beforeChange.AddSeconds(8) || service.nextCheckUtc != service.networkStableUtc) throw new InvalidOperationException("Region network change debounce"); passed++;
                if (service.CurrentCode != "JP" || service.Mode != "OFF") throw new InvalidOperationException("Region network change preserves disabled theme"); passed++;
            }
            passed += RegionLookupCache.SelfTest();
            if (HashFingerprint(new[] { "a", "b" }) != HashFingerprint(new[] { "b", "a" }) || HashFingerprint(new[] { "a", "b" }) == HashFingerprint(new[] { "a", "c" })) throw new InvalidOperationException("Region stable fingerprint"); passed += 2;
            return "Region theme self-test: " + passed + " passed (no external query).";
        }
    }
    internal enum RegionLookupDecision { Query, Cached, Cooldown }
    internal sealed class RegionLookupCache
    {
        sealed class Entry { internal string Code; internal DateTime At; }
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        DateTime lastAttempt = DateTime.MinValue;
        internal RegionLookupDecision Decide(string fingerprint, DateTime now, bool explicitRequest, out string cached, out int remaining)
        {
            cached = null; remaining = 0; Entry entry;
            if (!explicitRequest && entries.TryGetValue(fingerprint, out entry))
            {
                if (entry.Code != null && now - entry.At < TimeSpan.FromMinutes(10)) { cached = entry.Code; return RegionLookupDecision.Cached; }
                if (entry.Code == null) remaining = Math.Max(0, (int)Math.Ceiling(60 - (now - entry.At).TotalSeconds));
            }
            remaining = Math.Max(remaining, Math.Max(0, (int)Math.Ceiling(60 - Math.Min(Int32.MaxValue, (now - lastAttempt).TotalSeconds))));
            return remaining > 0 ? RegionLookupDecision.Cooldown : RegionLookupDecision.Query;
        }
        internal void BeginAttempt(DateTime now) { lastAttempt = now; }
        internal void Record(string fingerprint, string code, DateTime now)
        {
            if (fingerprint == null) return;
            entries[fingerprint] = new Entry { Code = code, At = now };
            if (entries.Count > 8)
            {
                string oldest = null; DateTime time = DateTime.MaxValue;
                foreach (KeyValuePair<string, Entry> pair in entries) if (pair.Value.At < time) { oldest = pair.Key; time = pair.Value.At; }
                if (oldest != null) entries.Remove(oldest);
            }
        }
        internal static int SelfTest()
        {
            int count = 0; Action<bool, string> check = delegate(bool ok, string label) { if (!ok) throw new InvalidOperationException("Region cache: " + label); count++; };
            RegionLookupCache cache = new RegionLookupCache(); DateTime start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc); string code; int seconds;
            check(cache.Decide("A", start, false, out code, out seconds) == RegionLookupDecision.Query, "first fingerprint queries");
            cache.BeginAttempt(start); cache.Record("A", "JP", start.AddSeconds(2));
            check(cache.Decide("A", start.AddSeconds(30), false, out code, out seconds) == RegionLookupDecision.Cached && code == "JP", "same fingerprint cached");
            check(cache.Decide("B", start.AddSeconds(30), false, out code, out seconds) == RegionLookupDecision.Cooldown && seconds == 30, "global cooldown across fingerprints");
            check(cache.Decide("B", start.AddSeconds(61), false, out code, out seconds) == RegionLookupDecision.Query, "changed fingerprint bypasses old 10 minute cache");
            check(cache.Decide("A", start.AddSeconds(59), true, out code, out seconds) == RegionLookupDecision.Cooldown && seconds == 1, "manual refresh obeys minimum interval");
            check(cache.Decide("A", start.AddSeconds(61), true, out code, out seconds) == RegionLookupDecision.Query, "manual same TUN refresh bypasses positive cache");
            cache.BeginAttempt(start.AddSeconds(61)); cache.Record("B", null, start.AddSeconds(65));
            check(cache.Decide("B", start.AddSeconds(122), false, out code, out seconds) == RegionLookupDecision.Cooldown && seconds == 3, "failure negative cache lasts 60 seconds");
            check(cache.Decide("B", start.AddSeconds(125), false, out code, out seconds) == RegionLookupDecision.Query, "failed lookup retries without 10 minute delay");
            check(cache.Decide("A", start.AddSeconds(400), false, out code, out seconds) == RegionLookupDecision.Cached && code == "JP", "return to previous network uses its result");
            check(cache.Decide("A", start.AddSeconds(602), false, out code, out seconds) == RegionLookupDecision.Query, "positive cache expires at ten minutes");
            for (int i = 0; i < 12; i++) cache.Record("network-" + i, "OTHER", start.AddMinutes(i));
            check(cache.entries.Count == 8, "fingerprint cache is bounded and memory-only");
            return count;
        }
    }
}
