// Read-only network diagnostics. .NET Framework 4.8 / C# 5.
// Native Wi-Fi API: https://learn.microsoft.com/windows/win32/api/wlanapi/nf-wlanapi-wlanqueryinterface
// Ping: https://learn.microsoft.com/dotnet/api/system.net.networkinformation.ping.sendpingasync
// Explicit DNS follows RFC 1035. No credentials, packet history or saved Wi-Fi keys are collected.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NetBunny
{
    internal sealed class NetworkToolsView : UserControl
    {
        private readonly bool preview;
        private readonly TabControl tabs = new TabControl();
        private readonly TextBox target = new TextBox();
        private readonly SoftButton start = new SoftButton(), stop = new SoftButton(), refresh = new SoftButton();
        private readonly Label status = new Label(), infoStatus = new Label();
        private readonly DataGridView results = new DataGridView();
        private readonly FlowLayoutPanel cards = new FlowLayoutPanel();
        private CancellationTokenSource run;
        private bool dead, loading;

        internal NetworkToolsView(bool preview)
        {
            this.preview = preview; Dock = DockStyle.Fill; BackColor = Art.Bg; Font = Art.Font(9);
            tabs.Dock = DockStyle.Fill; tabs.Font = Art.Font(10); Controls.Add(tabs);
            TabPage health = new TabPage("网络体检") { BackColor = Art.Bg, Padding = new Padding(14) };
            TabPage info = new TabPage("网络信息") { BackColor = Art.Bg, Padding = new Padding(14) };
            tabs.TabPages.Add(health); tabs.TabPages.Add(info);
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Art.Bg };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            health.Controls.Add(layout);
            Label intro = MakeLabel("小兔逐项检查：本机网卡 → 网关 → DNS → 目标网站。只检查，不修改设置。", Art.Ink); intro.Dock = DockStyle.Fill; layout.Controls.Add(intro, 0, 0);
            TableLayoutPanel bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            target.Text = "https://www.microsoft.com"; target.Dock = DockStyle.Fill; target.Margin = new Padding(0, 8, 12, 0); target.Font = Art.Font(10);
            start.Text = "开始体检"; start.Dock = DockStyle.Fill; start.Margin = new Padding(0, 2, 8, 8); start.Click += async delegate { await StartCheck(); };
            stop.Text = "停止"; stop.FillColor = Art.Coral; stop.Dock = DockStyle.Fill; stop.Margin = new Padding(0, 2, 0, 8); stop.Enabled = false; stop.Click += delegate { if (run != null) { run.Cancel(); status.Text = "正在停止…"; } };
            bar.Controls.Add(target, 0, 0); bar.Controls.Add(start, 1, 0); bar.Controls.Add(stop, 2, 0); layout.Controls.Add(bar, 0, 1);
            ConfigureGrid(); layout.Controls.Add(results, 0, 2);
            Label note = MakeLabel("网关不回复 Ping 不代表断网；DNS 会向当前网卡配置的服务器发送目标域名查询。网站检查只请求响应头，不下载页面、不提交登录凭据。", Art.Muted); note.Dock = DockStyle.Fill; layout.Controls.Add(note, 0, 3);
            status.Dock = DockStyle.Fill; status.ForeColor = Art.Purple; status.TextAlign = ContentAlignment.MiddleLeft; status.Text = "等待开始 · 最长约 25 秒 · 可随时停止"; layout.Controls.Add(status, 0, 4);
            TableLayoutPanel information = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            information.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); information.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); info.Controls.Add(information);
            Panel infoBar = new Panel { Dock = DockStyle.Fill }; information.Controls.Add(infoBar, 0, 0);
            refresh.Text = "刷新信息"; refresh.Width = 112; refresh.Height = 35; refresh.Dock = DockStyle.Right; refresh.Click += async delegate { await RefreshInformation(); }; infoBar.Controls.Add(refresh);
            infoStatus.Dock = DockStyle.Fill; infoStatus.ForeColor = Art.Muted; infoStatus.TextAlign = ContentAlignment.MiddleLeft; infoStatus.Text = "只读取本机配置；链路速度不是实际宽带测速结果。"; infoBar.Controls.Add(infoStatus); infoStatus.BringToFront();
            cards.Dock = DockStyle.Fill; cards.FlowDirection = FlowDirection.TopDown; cards.WrapContents = false; cards.AutoScroll = true; cards.BackColor = Art.Bg; cards.Resize += delegate { SizeCards(); }; information.Controls.Add(cards, 0, 1);
            if (preview) LoadDemo(); else Load += async delegate { await RefreshInformation(); };
        }

        private static Label MakeLabel(string text, Color color) { return new Label { Text = text, ForeColor = color, Font = Art.Font(9), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false, BackColor = Color.Transparent }; }
        private void ConfigureGrid()
        {
            results.Dock = DockStyle.Fill; results.ReadOnly = true; results.AllowUserToAddRows = false; results.AllowUserToDeleteRows = false; results.AllowUserToResizeRows = false; results.RowHeadersVisible = false;
            results.BackgroundColor = Color.White; results.BorderStyle = BorderStyle.None; results.GridColor = Art.Line; results.SelectionMode = DataGridViewSelectionMode.FullRowSelect; results.MultiSelect = false; results.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            results.DefaultCellStyle.Font = Art.Font(9); results.DefaultCellStyle.ForeColor = Art.Ink; results.DefaultCellStyle.WrapMode = DataGridViewTriState.True; results.DefaultCellStyle.Padding = new Padding(7); results.DefaultCellStyle.SelectionBackColor = Color.FromArgb(237,232,255); results.DefaultCellStyle.SelectionForeColor = Art.Ink;
            results.EnableHeadersVisualStyles = false; results.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237,232,255); results.ColumnHeadersDefaultCellStyle.ForeColor = Art.Ink; results.ColumnHeadersDefaultCellStyle.Font = Art.Font(9, FontStyle.Bold); results.ColumnHeadersHeight = 38;
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Stage", HeaderText = "检查项目", Width = 130 });
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "状态", Width = 82 });
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Time", HeaderText = "耗时", Width = 85 });
            results.Columns.Add(new DataGridViewTextBoxColumn { Name = "Detail", HeaderText = "结果与建议", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 210 });
            foreach (DataGridViewColumn c in results.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        public void ShowInformation() { tabs.SelectedIndex = 1; }
        public void ShowDiagnostics() { tabs.SelectedIndex = 0; }
        public void LoadDemo()
        {
            if (!preview) return;
            results.Rows.Clear();
            AddResult(new HealthResult("本机网卡", "正常", 0, "演示数据：以太网已连接，具有本机 IP。", false));
            AddResult(new HealthResult("网关 Ping", "有响应", 2, "192.0.2.1 · 本机到网关往返 2 ms。", false));
            AddResult(new HealthResult("配置 DNS", "有响应", 18, "192.0.2.53 · A / AAAA 查询收到有效应答。", false));
            AddResult(new HealthResult("系统解析", "成功", 11, "目标域名解析成功；此结果可能来自缓存。", false));
            AddResult(new HealthResult("目标网站", "有响应", 94, "HTTPS 200 · 这里只表示到所选目标的表现。", false));
            status.Text = "演示数据 · 未读取本机标识，未发送网络探测";
            ClearCards();
            AddCard("以太网 · 已连接", "演示数据\r\nIPv4：192.0.2.10\r\nIPv6：2001:db8::10\r\n网关：192.0.2.1\r\nDNS：192.0.2.53\r\n链路速度：1.00 Gbps（不是宽带实测速率）", Art.Purple);
            AddCard("Wi-Fi", "演示数据\r\n网络名称：Knight Home\r\n信号质量：86%\r\n接收 / 发送链路速率：866 / 866 Mbps", Art.Mint);
            infoStatus.Text = "演示数据 · 本页没有读取真实网络信息"; SizeCards();
        }

        private async Task StartCheck()
        {
            if (dead || run != null) return;
            if (preview) { LoadDemo(); return; }
            Uri url; string error;
            if (!HealthEngine.TryTarget(target.Text, out url, out error)) { status.Text = error; target.Focus(); return; }
            results.Rows.Clear(); start.Enabled = false; target.Enabled = false; stop.Enabled = true;
            CancellationTokenSource current = new CancellationTokenSource(); run = current; current.CancelAfter(25000);
            Progress<HealthResult> progress = new Progress<HealthResult>(delegate(HealthResult result) { if (!dead && run == current) { AddResult(result); status.Text = "正在检查 · " + result.Stage; } });
            status.Text = "正在读取本机网络配置…";
            try
            {
                await HealthEngine.Run(url, (IProgress<HealthResult>)progress, current.Token);
                if (!dead) status.Text = "检查完成 · " + DateTime.Now.ToString("HH:mm:ss") + " · 各项结果只代表本次检查";
            }
            catch (OperationCanceledException) { if (!dead) status.Text = "检查已停止或达到 25 秒上限 · 已完成的结果保留"; }
            catch (Exception e) { if (!dead) status.Text = "检查未完成：" + HealthEngine.ShortError(e); }
            finally { if (run == current) run = null; current.Dispose(); if (!dead) { start.Enabled = true; target.Enabled = true; stop.Enabled = false; } }
        }

        private void AddResult(HealthResult result)
        {
            int i = results.Rows.Add(result.Stage, result.Status, result.Milliseconds < 0 ? "—" : result.Milliseconds.ToString(CultureInfo.InvariantCulture) + " ms", result.Detail);
            results.Rows[i].Cells[1].Style.ForeColor = result.Warning ? Art.Coral : Art.Mint;
        }
        private async Task RefreshInformation()
        {
            if (dead || loading) return; if (preview) { LoadDemo(); return; }
            loading = true; refresh.Enabled = false; infoStatus.Text = "正在读取本机网卡与 Wi-Fi 状态…";
            try
            {
                NetworkSnapshot snapshot = await Task.Run(delegate { return HealthEngine.ReadSnapshot(true); });
                if (dead) return; ClearCards();
                foreach (AdapterData a in snapshot.Adapters)
                {
                    string text = a.Description + "\r\n类型：" + a.Type + "  ·  状态：" + a.Status + "\r\n本机 IP：" + Join(a.Addresses) + "\r\n网关：" + Join(a.Gateways) + "\r\nDNS：" + Join(a.Dns) + "\r\n链路速度：" + LinkSpeed(a.Speed) + "（不是宽带实测速率）";
                    if (!String.IsNullOrEmpty(a.Error)) text += "\r\n读取提示：" + a.Error;
                    AddCard(a.Name + (a.Up ? " · 已连接" : " · 未连接"), text, a.Up ? Art.Purple : Art.Muted);
                }
                if (snapshot.Adapters.Count == 0) AddCard("网卡信息", "未读取到可用网卡。" + snapshot.Error, Art.Coral);
                AddCard("Wi-Fi 状态", snapshot.Wifi, Art.Mint);
                infoStatus.Text = "更新于 " + DateTime.Now.ToString("HH:mm:ss") + " · 只读取本机配置，没有发出网络探测"; SizeCards();
            }
            catch (Exception e) { if (!dead) infoStatus.Text = "读取未完成：" + HealthEngine.ShortError(e); }
            finally { loading = false; if (!dead) refresh.Enabled = true; }
        }
        private static string Join(IEnumerable<IPAddress> addresses) { string s = String.Join("  /  ", addresses.Select(a => a.ToString())); return s.Length == 0 ? "未配置" : s; }
        private static string LinkSpeed(long speed) { return speed <= 0 ? "未知" : speed >= 1000000000 ? (speed / 1000000000.0).ToString("0.##") + " Gbps" : (speed / 1000000.0).ToString("0.##") + " Mbps"; }
        private void ClearCards() { while (cards.Controls.Count > 0) { Control c = cards.Controls[0]; cards.Controls.RemoveAt(0); c.Dispose(); } }
        private void AddCard(string title, string text, Color accent)
        {
            RoundPanel card = new RoundPanel { Margin = new Padding(0,0,0,12), Height = 208, Padding = new Padding(18), FillColor = Color.White };
            Label body = MakeLabel(text, Art.Ink); body.Dock = DockStyle.Fill; body.TextAlign = ContentAlignment.TopLeft; body.Padding = new Padding(0,8,0,0); card.Controls.Add(body);
            Label heading = MakeLabel(title, accent); heading.Font = Art.Font(11, FontStyle.Bold); heading.Dock = DockStyle.Top; heading.Height = 31; card.Controls.Add(heading);
            card.Tag = body; cards.Controls.Add(card);
        }
        private void SizeCards()
        {
            foreach (Control card in cards.Controls)
            {
                card.Width = Math.Max(250, cards.ClientSize.Width - 24);
                Label body = card.Tag as Label;
                if (body != null) { Size measured = TextRenderer.MeasureText(body.Text, body.Font, new Size(Math.Max(210, card.Width - 36), 2000), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl); card.Height = Math.Max(138, measured.Height + 85); }
            }
        }
        public void Shutdown() { if (dead) return; dead = true; if (run != null) run.Cancel(); }
        protected override void Dispose(bool disposing) { if (disposing) Shutdown(); base.Dispose(disposing); }
        internal static string SelfTest() { return HealthEngine.SelfTest(); }
    }

    internal sealed class HealthResult
    {
        internal readonly string Stage, Status, Detail; internal readonly long Milliseconds; internal readonly bool Warning;
        internal HealthResult(string stage, string status, long ms, string detail, bool warning) { Stage = stage; Status = status; Milliseconds = ms; Detail = detail; Warning = warning; }
    }
    internal sealed class AdapterData
    {
        internal string Name, Description, Type, Status, Error; internal bool Up; internal long Speed;
        internal List<IPAddress> Addresses = new List<IPAddress>(), Gateways = new List<IPAddress>(), Dns = new List<IPAddress>();
    }
    internal sealed class NetworkSnapshot
    {
        internal readonly List<AdapterData> Adapters = new List<AdapterData>(); internal string Wifi = "未查询", Error = "";
    }
    internal sealed class DnsAnswer
    {
        internal bool Success; internal string Status, Detail;
    }

    internal static class HealthEngine
    {
        internal static bool TryTarget(string input, out Uri uri, out string error)
        {
            uri = null; error = "请输入完整的 http:// 或 https:// 网站地址。";
            if (String.IsNullOrWhiteSpace(input) || input.Length > 2048 || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (String.IsNullOrWhiteSpace(uri.Host) || !String.IsNullOrEmpty(uri.UserInfo)) { error = "地址必须包含主机名，且不能包含用户名或密码。"; return false; }
            try { IPAddress ip; if (!IPAddress.TryParse(uri.DnsSafeHost, out ip)) NormalizeHost(uri.DnsSafeHost); }
            catch (Exception) { error = "目标域名格式不正确。"; return false; }
            UriBuilder builder = new UriBuilder(uri); builder.Fragment = ""; uri = builder.Uri; error = null; return true;
        }
        internal static string NormalizeHost(string host)
        {
            string ascii;
            try { ascii = new IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant(); }
            catch (ArgumentException e) { throw new FormatException("域名格式不正确", e); }
            if (ascii.Length < 1 || ascii.Length > 253) throw new FormatException("域名长度不正确");
            foreach (string label in ascii.Split('.'))
            {
                if (label.Length < 1 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') throw new FormatException("域名标签不正确");
                foreach (char c in label) if (!(c >= 'a' && c <= 'z') && !(c >= '0' && c <= '9') && c != '-') throw new FormatException("域名含无效字符");
            }
            return ascii;
        }
        internal static NetworkSnapshot ReadSnapshot(bool includeWifi)
        {
            NetworkSnapshot snapshot = new NetworkSnapshot();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces().OrderByDescending(a => a.OperationalStatus == OperationalStatus.Up))
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    AdapterData a = new AdapterData { Name = nic.Name, Description = nic.Description, Type = nic.NetworkInterfaceType.ToString(), Up = nic.OperationalStatus == OperationalStatus.Up, Status = nic.OperationalStatus.ToString() };
                    try
                    {
                        a.Speed = nic.Speed; IPInterfaceProperties p = nic.GetIPProperties();
                        a.Addresses.AddRange(p.UnicastAddresses.Select(v => v.Address)); a.Gateways.AddRange(p.GatewayAddresses.Select(v => v.Address).Where(UsableAddress)); a.Dns.AddRange(p.DnsAddresses.Where(UsableAddress));
                    }
                    catch (Exception e) { a.Error = ShortError(e); }
                    snapshot.Adapters.Add(a);
                }
            }
            catch (Exception e) { snapshot.Error = ShortError(e); }
            if (includeWifi) snapshot.Wifi = WifiReader.Read();
            return snapshot;
        }
        private static bool UsableAddress(IPAddress a) { return !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any) && !a.Equals(IPAddress.None) && !a.IsIPv6Multicast; }
        internal static async Task Run(Uri target, IProgress<HealthResult> progress, CancellationToken token)
        {
            Stopwatch watch = Stopwatch.StartNew();
            NetworkSnapshot snapshot = await Task.Run(delegate { return ReadSnapshot(false); }, token); token.ThrowIfCancellationRequested();
            List<AdapterData> active = snapshot.Adapters.Where(a => a.Up && a.Addresses.Any(UsableAddress)).ToList();
            progress.Report(new HealthResult("本机网卡", active.Count > 0 ? "已连接" : "需检查", watch.ElapsedMilliseconds, active.Count > 0 ? String.Join("；", active.Select(a => a.Name + "（" + a.Addresses.Count + " 个本机 IP）")) + "。网卡已连接不等于外网一定可用。" : "未读取到处于连接状态且有 IP 的网卡。检查网线、Wi-Fi 或 VPN。" + snapshot.Error, active.Count == 0));
            List<IPAddress> gateways = active.SelectMany(a => a.Gateways).Distinct().Take(4).ToList();
            if (gateways.Count == 0) progress.Report(new HealthResult("网关 Ping", "未配置", -1, "活动网卡未报告网关；部分 VPN / 点对点连接可能正常。继续检查 DNS 和网站。", true));
            else await Task.WhenAll(gateways.Select(a => PingGateway(a, progress, token)));
            token.ThrowIfCancellationRequested();
            IPAddress literal;
            if (IPAddress.TryParse(target.DnsSafeHost, out literal)) progress.Report(new HealthResult("DNS", "无需解析", -1, "目标使用 IP 地址，跳过配置 DNS 和系统域名解析。", false));
            else
            {
                string host = NormalizeHost(target.DnsSafeHost);
                List<IPAddress> allDns = active.SelectMany(a => a.Dns).Distinct().ToList(); List<IPAddress> servers = allDns.Take(4).ToList();
                if (servers.Count == 0) progress.Report(new HealthResult("配置 DNS", "未配置", -1, "活动网卡未报告 DNS 服务器；系统解析可能使用其他策略。继续检查系统解析。", true));
                if (allDns.Count > 4) progress.Report(new HealthResult("配置 DNS", "范围说明", -1, "本次检查前 4 个不同服务器；其余 " + (allDns.Count - 4) + " 个未探测。", true));
                using (SemaphoreSlim limit = new SemaphoreSlim(3))
                {
                    List<Task> jobs = new List<Task>();
                    foreach (IPAddress server in servers) foreach (ushort type in new ushort[] { 1, 28 }) jobs.Add(DnsCheck(server, host, type, limit, progress, token));
                    await Task.WhenAll(jobs);
                }
                token.ThrowIfCancellationRequested(); await SystemDns(host, progress, token);
            }
            token.ThrowIfCancellationRequested(); await Website(target, progress, token); token.ThrowIfCancellationRequested();
        }
        private static async Task PingGateway(IPAddress gateway, IProgress<HealthResult> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Stopwatch watch = Stopwatch.StartNew();
            using (Ping ping = new Ping())
            {
                try
                {
                    PingReply reply = await ping.SendPingAsync(gateway, 1500, new byte[16]); token.ThrowIfCancellationRequested();
                    bool ok = reply.Status == IPStatus.Success;
                    progress.Report(new HealthResult("网关 Ping", ok ? "有响应" : "未确认", ok ? reply.RoundtripTime : watch.ElapsedMilliseconds, gateway + " · " + (ok ? "收到 ICMP 回复，仅表示网关可达。" : reply.Status + "；设备可能禁用 Ping，不能据此判定断网。结合网站结果检查。"), !ok));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception e) { token.ThrowIfCancellationRequested(); progress.Report(new HealthResult("网关 Ping", "未确认", watch.ElapsedMilliseconds, gateway + " · " + ShortError(e) + "。可能是 ICMP 被限制，不能单独判定断网。", true)); }
            }
        }
        private static async Task DnsCheck(IPAddress server, string host, ushort type, SemaphoreSlim limit, IProgress<HealthResult> progress, CancellationToken token)
        {
            await limit.WaitAsync(token); Stopwatch watch = Stopwatch.StartNew();
            try
            {
                DnsAnswer answer = await QueryDns(server, 53, host, type, token, 2200);
                progress.Report(new HealthResult("DNS " + (type == 1 ? "A" : "AAAA"), answer.Status, watch.ElapsedMilliseconds, server + " · " + answer.Detail, !answer.Success));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { token.ThrowIfCancellationRequested(); progress.Report(new HealthResult("DNS " + (type == 1 ? "A" : "AAAA"), "未确认", watch.ElapsedMilliseconds, server + " · " + ShortError(e) + "。检查 DNS / VPN 策略；UDP 53 可能被限制，与系统解析结果对照。", true)); }
            finally { limit.Release(); }
        }
        internal static async Task<DnsAnswer> QueryDns(IPAddress server, int port, string host, ushort type, CancellationToken token, int timeout)
        {
            byte[] idBytes = new byte[2]; using (RandomNumberGenerator random = RandomNumberGenerator.Create()) random.GetBytes(idBytes); ushort id = (ushort)(idBytes[0] * 256 + idBytes[1]);
            byte[] query = BuildDns(host, type, id);
            using (UdpClient udp = new UdpClient(server.AddressFamily))
            using (CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timer.CancelAfter(timeout);
                using (timer.Token.Register(delegate { udp.Close(); }))
                {
                    try
                    {
                        token.ThrowIfCancellationRequested(); udp.Connect(new IPEndPoint(server, port)); await udp.SendAsync(query, query.Length);
                        UdpReceiveResult received = await udp.ReceiveAsync(); token.ThrowIfCancellationRequested(); return ParseDns(received.Buffer, host, type, id);
                    }
                    catch (Exception) { token.ThrowIfCancellationRequested(); if (timer.IsCancellationRequested) throw new TimeoutException("DNS 应答超时"); throw; }
                }
            }
        }
        internal static byte[] BuildDns(string host, ushort type, ushort id)
        {
            host = NormalizeHost(host); List<byte> b = new List<byte>(300);
            b.Add((byte)(id >> 8)); b.Add((byte)id); b.Add(1); b.Add(0); b.Add(0); b.Add(1); for (int i = 0; i < 6; i++) b.Add(0);
            foreach (string part in host.Split('.')) { b.Add((byte)part.Length); b.AddRange(Encoding.ASCII.GetBytes(part)); }
            b.Add(0); b.Add((byte)(type >> 8)); b.Add((byte)type); b.Add(0); b.Add(1); return b.ToArray();
        }
        private static int Read16(byte[] b, int offset) { if (offset < 0 || offset + 2 > b.Length) throw new FormatException("DNS 数据不完整"); return (b[offset] << 8) | b[offset + 1]; }
        private static string ReadDnsName(byte[] b, ref int offset)
        {
            List<string> names = new List<string>(); int cursor = offset, jumped = -1, hops = 0, total = 0;
            while (true)
            {
                if (cursor >= b.Length || ++hops > 128) throw new FormatException("DNS 名称越界或压缩指针循环");
                int size = b[cursor++];
                if (size == 0) { offset = jumped >= 0 ? jumped : cursor; return String.Join(".", names).ToLowerInvariant(); }
                if ((size & 192) == 192) { if (cursor >= b.Length) throw new FormatException("DNS 指针缺失"); int next = ((size & 63) << 8) | b[cursor++]; if (jumped < 0) jumped = cursor; cursor = next; continue; }
                if ((size & 192) != 0 || size > 63 || cursor + size > b.Length || (total += size + 1) > 254) throw new FormatException("DNS 标签无效");
                names.Add(Encoding.ASCII.GetString(b, cursor, size)); cursor += size;
            }
        }
        internal static DnsAnswer ParseDns(byte[] b, string host, ushort type, ushort id)
        {
            if (b == null || b.Length < 12 || Read16(b,0) != id || (b[2] & 128) == 0 || (b[2] & 120) != 0 || Read16(b,4) != 1) throw new FormatException("DNS 应答标识或类型不匹配");
            int offset = 12; string question = ReadDnsName(b, ref offset);
            if (question != NormalizeHost(host) || Read16(b,offset) != type || Read16(b,offset + 2) != 1) throw new FormatException("DNS 应答问题与本次查询不匹配"); offset += 4;
            if ((b[2] & 2) != 0) return new DnsAnswer { Success = false, Status = "需复核", Detail = "收到截断的 UDP 应答；本检查不回退 TCP，请参考系统解析。" };
            int rcode = b[3] & 15;
            if (rcode != 0) return new DnsAnswer { Success = false, Status = "收到错误", Detail = "服务器已响应，DNS 返回 " + (rcode == 3 ? "NXDOMAIN（域名不存在）" : rcode == 2 ? "SERVFAIL（解析失败）" : rcode == 5 ? "REFUSED（拒绝查询）" : "错误码 " + rcode) + "。核对域名与 DNS 策略。" };
            int answers = Read16(b,6); if (answers > 256) throw new FormatException("DNS 记录数量异常"); List<string> ips = new List<string>();
            for (int i = 0; i < answers; i++)
            {
                ReadDnsName(b, ref offset); int answerType = Read16(b, offset), answerClass = Read16(b, offset + 2), length = Read16(b, offset + 8); offset += 10;
                if (offset + length > b.Length) throw new FormatException("DNS 记录不完整");
                if (answerClass == 1 && answerType == type && ((type == 1 && length == 4) || (type == 28 && length == 16))) { byte[] raw = new byte[length]; Buffer.BlockCopy(b, offset, raw, 0, length); ips.Add(new IPAddress(raw).ToString()); }
                offset += length;
            }
            return ips.Count > 0 ? new DnsAnswer { Success = true, Status = "解析成功", Detail = String.Join(" / ", ips.Take(4)) + "；该配置服务器收到查询并返回地址。" } : new DnsAnswer { Success = false, Status = "无此记录", Detail = "收到有效应答，但没有所查地址记录。仅缺少 AAAA / IPv6 记录不代表网络异常。" };
        }
        private static async Task SystemDns(string host, IProgress<HealthResult> progress, CancellationToken token)
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                Task<IPAddress[]> pending = Dns.GetHostAddressesAsync(host);
                ObserveFailure(pending);
                Task completed = await Task.WhenAny(pending, Task.Delay(4000, token)); token.ThrowIfCancellationRequested();
                if (completed != pending) throw new TimeoutException("系统解析在 4 秒内未完成（系统后台调用由 Windows 收尾）");
                IPAddress[] ips = await pending; bool ok = ips.Length > 0;
                progress.Report(new HealthResult("系统解析", ok ? "成功" : "无地址", watch.ElapsedMilliseconds, ok ? String.Join(" / ", ips.Take(4).Select(a => a.ToString())) + "。可能使用缓存、hosts 或系统 DNS 策略，不能证明每个配置服务器都可用。" : "系统解析未返回地址。检查域名拼写和 DNS / VPN 设置。", !ok));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { token.ThrowIfCancellationRequested(); progress.Report(new HealthResult("系统解析", "未完成", watch.ElapsedMilliseconds, ShortError(e) + "。检查域名、系统 DNS 或代理设置。", true)); }
        }
        private static void ObserveFailure(Task<IPAddress[]> pending) { pending.ContinueWith(delegate(Task<IPAddress[]> t) { var observed = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        internal static async Task Website(Uri target, IProgress<HealthResult> progress, CancellationToken token)
        {
            Stopwatch watch = Stopwatch.StartNew(); HttpWebRequest request = (HttpWebRequest)WebRequest.Create(target);
            request.Method = "HEAD"; request.AllowAutoRedirect = false; request.Timeout = 5500; request.ReadWriteTimeout = 5500; request.UseDefaultCredentials = false; request.Credentials = null;
            request.UserAgent = "KnightAssistant-NetworkCheck/4"; request.KeepAlive = false; request.AutomaticDecompression = DecompressionMethods.None; request.MaximumResponseHeadersLength = 32;
            using (CancellationTokenSource timer = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timer.CancelAfter(5500);
                using (timer.Token.Register(request.Abort))
                {
                    try
                    {
                        using (HttpWebResponse response = (HttpWebResponse)await request.GetResponseAsync()) { token.ThrowIfCancellationRequested(); ReportHttp(response, watch.ElapsedMilliseconds, progress); }
                    }
                    catch (WebException e)
                    {
                        token.ThrowIfCancellationRequested();
                        using (HttpWebResponse response = e.Response as HttpWebResponse)
                        {
                            if (response != null) ReportHttp(response, watch.ElapsedMilliseconds, progress);
                            else progress.Report(new HealthResult("目标网站", "未确认", watch.ElapsedMilliseconds, (timer.IsCancellationRequested ? "响应头请求在 5.5 秒内未完成" : ShortError(e)) + "。检查目标网站、代理、VPN 或证书；单个网站失败不等于整机断网。", true));
                        }
                    }
                }
            }
        }
        private static void ReportHttp(HttpWebResponse response, long milliseconds, IProgress<HealthResult> progress)
        {
            int code = (int)response.StatusCode; bool ok = code >= 200 && code < 300;
            string detail = response.ResponseUri.Host + " · HTTP " + code + " · ";
            detail += ok ? "已收到响应头，目标可达；耗时包含域名解析、连接及服务器处理，并非 Ping 延迟。" : code >= 300 && code < 400 ? "目标返回重定向；未自动跟随，请用浏览器检查登录页或跳转。" : code == 405 || code == 501 ? "目标不支持 HEAD 检查，但已收到服务器回复。" : "已收到服务器回复；检查网站服务、访问权限或代理。";
            progress.Report(new HealthResult("目标网站", ok ? "有响应" : "需查看", milliseconds, detail, !ok));
        }
        internal static string ShortError(Exception e)
        {
            if (e is AggregateException && e.InnerException != null) e = e.InnerException;
            string message = e.Message.Replace("\r", " ").Replace("\n", " "); return message.Length > 180 ? message.Substring(0,180) + "…" : message;
        }
        internal static string SelfTest()
        {
            int count = 0; Action<bool,string> check = delegate(bool condition, string name) { if (!condition) throw new Exception("NetworkTools selftest: " + name); count++; };
            Uri uri; string error;
            check(TryTarget("https://example.com/a#b", out uri, out error) && uri.Fragment == "", "HTTPS and fragment");
            check(TryTarget("http://127.0.0.1:8123/", out uri, out error), "local HTTP supported");
            check(TryTarget("http://[::1]:8123/", out uri, out error), "IPv6 local HTTP supported");
            check(!TryTarget("ftp://example.com", out uri, out error), "reject FTP");
            check(!TryTarget("https://user:password@example.com", out uri, out error), "reject URL credentials");
            check(!TryTarget("file:///C:/secret", out uri, out error), "reject local file");
            check(!TryTarget("javascript:alert(1)", out uri, out error), "reject script");
            check(!TryTarget("", out uri, out error), "reject blank");
            check(NormalizeHost("EXAMPLE.COM.") == "example.com", "DNS normalization");
            check(NormalizeHost("例子.测试").StartsWith("xn--"), "IDN conversion");
            bool bad = false; try { NormalizeHost("a..com"); } catch (FormatException) { bad = true; } check(bad, "empty DNS label");
            byte[] query = BuildDns("example.com",1,1234); check(query.Length == 29 && Read16(query,0) == 1234 && Read16(query,4) == 1, "DNS query header");
            List<byte> response = new List<byte>(query); response[2] = 129; response[3] = 128; response[7] = 1;
            response.AddRange(new byte[] { 192,12,0,1,0,1,0,0,0,60,0,4,192,0,2,10 });
            check(ParseDns(response.ToArray(),"example.com",1,1234).Success, "valid compressed A response");
            bad=false; try { ParseDns(response.ToArray(),"other.com",1,1234); } catch (FormatException) { bad=true; } check(bad,"reject mismatched name");
            bad=false; try { ParseDns(response.ToArray(),"example.com",1,4444); } catch (FormatException) { bad=true; } check(bad,"reject mismatched transaction");
            bad=false; try { ParseDns(response.Take(response.Count-1).ToArray(),"example.com",1,1234); } catch (FormatException) { bad=true; } check(bad,"reject truncated record");
            byte[] nxdomain = (byte[])query.Clone(); nxdomain[2]=129; nxdomain[3]=131; check(!ParseDns(nxdomain,"example.com",1,1234).Success,"NXDOMAIN not success");
            byte[] trunc = response.ToArray(); trunc[2]=131; check(!ParseDns(trunc,"example.com",1,1234).Success,"truncated UDP not success");
            byte[] noData = (byte[])query.Clone(); noData[2]=129; noData[3]=128; check(ParseDns(noData,"example.com",1,1234).Status=="无此记录","empty answer separated");
            byte[] loop = new byte[] {192,0}; int pos=0; bad=false; try { ReadDnsName(loop,ref pos); } catch(FormatException) {bad=true;} check(bad,"compressed pointer loop bounded");
            byte[] aaaaQuery = BuildDns("example.com",28,200); List<byte> aaaa=new List<byte>(aaaaQuery); aaaa[2]=129; aaaa[3]=128; aaaa[7]=1;
            aaaa.AddRange(new byte[]{192,12,0,28,0,1,0,0,0,60,0,16}); aaaa.AddRange(IPAddress.Parse("2001:db8::1").GetAddressBytes()); check(ParseDns(aaaa.ToArray(),"example.com",28,200).Success,"AAAA response");
            check(Marshal.SizeOf(typeof(WifiReader.WifiConnection))==600,"native Wi-Fi connection layout");
            return "NetworkTools: " + count + " checks passed";
        }
    }

    internal static class WifiReader
    {
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct WifiInterface { internal Guid Id; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] internal string Description; internal int State; }
        [StructLayout(LayoutKind.Sequential)] internal struct Ssid { internal uint Length; [MarshalAs(UnmanagedType.ByValArray,SizeConst=32)] internal byte[] Bytes; }
        [StructLayout(LayoutKind.Sequential)] internal struct WifiAssociation { internal Ssid Ssid; internal uint BssType; [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)] internal byte[] Bssid; internal uint PhyType, Signal, RxRate, TxRate; }
        [StructLayout(LayoutKind.Sequential)] internal struct WifiSecurity { internal int Enabled, OneXEnabled; internal uint Auth, Cipher; }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct WifiConnection { internal int State, Mode; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] internal string Profile; internal WifiAssociation Association; internal WifiSecurity Security; }
        [DllImport("wlanapi.dll")] private static extern uint WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] private static extern uint WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] private static extern uint WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] private static extern uint WlanQueryInterface(IntPtr handle, ref Guid id, int opcode, IntPtr reserved, out uint size, out IntPtr data, out int valueType);
        [DllImport("wlanapi.dll")] private static extern void WlanFreeMemory(IntPtr memory);
        internal static string Read()
        {
            IntPtr handle=IntPtr.Zero, list=IntPtr.Zero;
            try
            {
                uint negotiated; uint error = WlanOpenHandle(2,IntPtr.Zero,out negotiated,out handle); if(error!=0) return Explain(error);
                error=WlanEnumInterfaces(handle,IntPtr.Zero,out list); if(error!=0) return Explain(error);
                int count=Marshal.ReadInt32(list); if(count<0 || count>128) return "Wi-Fi 返回了无法识别的接口信息。"; if(count==0) return "没有检测到 Wi-Fi 网卡；有线网络仍可正常使用。";
                List<string> output = new List<string>(); int stride=Marshal.SizeOf(typeof(WifiInterface));
                for(int i=0;i<count;i++)
                {
                    WifiInterface wifi=(WifiInterface)Marshal.PtrToStructure(IntPtr.Add(list,8+i*stride),typeof(WifiInterface));
                    if(wifi.State!=1) { output.Add(wifi.Description+"：未连接 Wi-Fi"); continue; }
                    IntPtr data=IntPtr.Zero;
                    try
                    {
                        uint size; int valueType; error=WlanQueryInterface(handle,ref wifi.Id,7,IntPtr.Zero,out size,out data,out valueType);
                        if(error!=0) {output.Add(wifi.Description+"："+Explain(error));continue;}
                        if(size<Marshal.SizeOf(typeof(WifiConnection))) {output.Add(wifi.Description+"：Wi-Fi 状态结构不完整。");continue;}
                        WifiConnection connection=(WifiConnection)Marshal.PtrToStructure(data,typeof(WifiConnection)); uint length=Math.Min(32u,connection.Association.Ssid.Length);
                        string ssid=Encoding.UTF8.GetString(connection.Association.Ssid.Bytes,0,(int)length); ssid=new string(ssid.Select(c=>Char.IsControl(c)?' ':c).ToArray());
                        output.Add(wifi.Description+"\r\n网络名称："+(ssid.Length==0?"未提供":ssid)+"\r\n信号质量："+Math.Min(100u,connection.Association.Signal)+"%\r\n接收 / 发送链路速率："+(connection.Association.RxRate/1000.0).ToString("0.#")+" / "+(connection.Association.TxRate/1000.0).ToString("0.#")+" Mbps");
                    }
                    finally {if(data!=IntPtr.Zero)WlanFreeMemory(data);}
                }
                return String.Join("\r\n\r\n",output)+"\r\n仅显示当前连接，不读取保存的 Wi-Fi 密码。";
            }
            catch(Exception e) {return "Wi-Fi 信息暂不可用："+HealthEngine.ShortError(e);}
            finally {if(list!=IntPtr.Zero)WlanFreeMemory(list);if(handle!=IntPtr.Zero)WlanCloseHandle(handle,IntPtr.Zero);}
        }
        private static string Explain(uint code)
        {
            if(code==5) return "Windows 未允许读取 Wi-Fi 信息（访问被拒绝）。部分 Windows 版本需要位置权限；不会自动修改权限。";
            if(code==1062 || code==1058) return "Windows WLAN 服务未运行或被禁用，Wi-Fi 详情不可用。";
            return "Wi-Fi 信息不可用，Windows 错误码 "+code+"。";
        }
    }
}
