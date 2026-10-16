using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace NetBunny
{
    internal sealed class TrafficTotalRow
    {
        internal DateTime Day;
        internal string Key, Name, Path;
        internal bool SpeedTest;
        internal long Download, Upload;
        internal TrafficTotalRow Copy() { return (TrafficTotalRow)MemberwiseClone(); }
    }

    internal sealed class TrafficPoint
    {
        internal DateTime Time;
        internal double Download, Upload;
        internal bool Available, SpeedTest;
    }

    // Counts cumulative captured IP bytes, not samples of a rounded speed label.
    // Stored totals survive app restarts; session baselines intentionally do not:
    // a new NetworkEngine has a new ID and counters starting at zero.
    internal sealed class TrafficLedger : IDisposable
    {
        private sealed class Baseline { internal long Down, Up; }
        private readonly Dictionary<string, Baseline> baseline = new Dictionary<string, Baseline>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TrafficTotalRow> totals = new Dictionary<string, TrafficTotalRow>(StringComparer.OrdinalIgnoreCase);
        private readonly List<TrafficPoint> samples = new List<TrafficPoint>();
        private readonly bool preview;
        private readonly string savePath, ownPath;
        private DateTime lastObserved, lastSave, displayDay;
        private string currentSession;
        private bool speedTest, disposed, dirty, needsRecoveryBackup;
        internal bool Capturing { get; private set; }
        internal string PersistenceWarning { get; private set; }
        internal DateTime FirstTracked { get; private set; }
        internal DateTime DisplayDay { get { return displayDay; } }
        internal IList<TrafficPoint> Samples { get { return samples.AsReadOnly(); } }

        internal TrafficLedger(bool preview)
            : this(preview, System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetBunny", "traffic-v3.tsv"), Application.ExecutablePath, DateTime.Now) { }

        private TrafficLedger(bool preview, string path, string executable, DateTime now)
        {
            this.preview = preview; savePath = path; ownPath = executable;
            lastObserved = now; lastSave = now; displayDay = now.Date; FirstTracked = now;
            if (!preview) Load(now);
        }

        internal void SetSpeedTestActive(bool active) { speedTest = active; }

        internal void Observe(List<AppInfo> apps, bool capturing) { ObserveAt(apps, capturing, DateTime.Now); }

        private void ObserveAt(List<AppInfo> apps, bool capturing, DateTime now)
        {
            if (disposed) return;
            Capturing = capturing; displayDay = now.Date;
            double down = 0, up = 0, reportedDown = 0, reportedUp = 0;
            bool newSession = false;
            if (apps == null) apps = new List<AppInfo>();
            foreach (AppInfo app in apps)
            {
                string session = app.TrafficSessionId ?? "legacy";
                if (!String.Equals(currentSession, session, StringComparison.Ordinal))
                {
                    currentSession = session; baseline.Clear(); newSession = true;
                }
                string key = app.TrafficKey ?? app.Path ?? app.Name ?? "unknown";
                Baseline previous;
                bool seen = baseline.TryGetValue(key, out previous);
                long currentDown = Math.Max(0, app.DownloadBytes), currentUp = Math.Max(0, app.UploadBytes);
                long deltaDown = seen && currentDown >= previous.Down ? currentDown - previous.Down : currentDown;
                long deltaUp = seen && currentUp >= previous.Up ? currentUp - previous.Up : currentUp;
                baseline[key] = new Baseline { Down = currentDown, Up = currentUp };
                reportedDown += Math.Max(0, app.DownloadBps); reportedUp += Math.Max(0, app.UploadBps);
                // These are bytes actually captured by the backend. A driver
                // failure can precede the UI poll: retain that final delta even
                // when current availability has already changed to false.
                down += deltaDown; up += deltaUp;
                bool testTraffic = speedTest && !String.IsNullOrEmpty(app.Path) && String.Equals(app.Path, ownPath, StringComparison.OrdinalIgnoreCase);
                AddAcrossDays(key, app.Name, app.Path, testTraffic, deltaDown, deltaUp, lastObserved, now);
            }
            double seconds = (now - lastObserved).TotalSeconds;
            samples.Add(new TrafficPoint { Time = now, Available = capturing, SpeedTest = speedTest,
                Download = capturing ? (seconds > 0 && !newSession ? down / seconds : reportedDown) : 0,
                Upload = capturing ? (seconds > 0 && !newSession ? up / seconds : reportedUp) : 0 });
            lastObserved = now;
            samples.RemoveAll(delegate(TrafficPoint p) { return p.Time < now.AddMinutes(-5); });
            if (samples.Count > 600) samples.RemoveRange(0, samples.Count - 600);
            Trim(now);
            if (!preview && dirty && (now - lastSave).TotalSeconds >= 15) { Save(); lastSave = now; }
        }

        private void AddAcrossDays(string key, string name, string path, bool test, long down, long up, DateTime from, DateTime to)
        {
            if (down == 0 && up == 0) return;
            // A poll spanning midnight has no packet timestamps. Split only its
            // delta by time; never assign the entire lifetime counter to today.
            if (to > from && from.Date < to.Date && (to - from).TotalDays <= 31)
            {
                double duration = (to - from).TotalSeconds;
                DateTime cursor = from; long remainingDown = down, remainingUp = up;
                while (cursor.Date < to.Date)
                {
                    DateTime end = cursor.Date.AddDays(1);
                    double fraction = (end - cursor).TotalSeconds / duration;
                    long pieceDown = Math.Min(remainingDown, (long)(down * fraction));
                    long pieceUp = Math.Min(remainingUp, (long)(up * fraction));
                    Add(cursor.Date, key, name, path, test, pieceDown, pieceUp);
                    remainingDown -= pieceDown; remainingUp -= pieceUp; cursor = end;
                }
                Add(to.Date, key, name, path, test, remainingDown, remainingUp);
            }
            else Add(to.Date, key, name, path, test, down, up);
        }

        private void Add(DateTime day, string key, string name, string path, bool test, long down, long up)
        {
            if (down == 0 && up == 0) return;
            string id = RecordKey(day, key, test);
            TrafficTotalRow row;
            if (!totals.TryGetValue(id, out row))
            {
                int dayCount = 0;
                foreach (TrafficTotalRow entry in totals.Values) if (entry.Day == day) dayCount++;
                if (dayCount >= 4094)
                {
                    key = "@ledger-overflow"; name = "其他程序（统计上限合并）"; path = null;
                    id = RecordKey(day, key, test);
                }
                if (!totals.TryGetValue(id, out row))
                    totals[id] = row = new TrafficTotalRow { Day = day, Key = key, Name = name ?? "未知程序", Path = path, SpeedTest = test };
            }
            row.Download += down; row.Upload += up; dirty = true;
        }

        private static string RecordKey(DateTime day, string key, bool test) { return day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + (test ? "|T|" : "|N|") + key; }

        internal List<TrafficTotalRow> Today()
        {
            var result = new List<TrafficTotalRow>();
            foreach (TrafficTotalRow row in totals.Values) if (row.Day == displayDay) result.Add(row.Copy());
            return result;
        }

        private void Trim(DateTime now)
        {
            var remove = new List<string>();
            foreach (KeyValuePair<string, TrafficTotalRow> pair in totals)
                if (pair.Value.Day < now.Date.AddDays(-30) || pair.Value.Day > now.Date.AddDays(1)) remove.Add(pair.Key);
            foreach (string key in remove) { totals.Remove(key); dirty = true; }
            // Drop complete oldest days, preserving today's totals intact.
            while (totals.Count > 12000)
            {
                DateTime oldest = now.Date;
                foreach (TrafficTotalRow row in totals.Values) if (row.Day < oldest) oldest = row.Day;
                if (oldest >= now.Date) break;
                remove.Clear();
                foreach (KeyValuePair<string, TrafficTotalRow> pair in totals) if (pair.Value.Day == oldest) remove.Add(pair.Key);
                foreach (string key in remove) totals.Remove(key);
                dirty = true;
            }
        }

        private static string Encode(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? "")); }
        private static string Decode(string value) { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }

        private void Load(DateTime now)
        {
            try
            {
                if (!File.Exists(savePath)) return;
                if (new FileInfo(savePath).Length > 48L * 1024 * 1024) throw new IOException("记录文件超过安全读取上限");
                string[] lines = File.ReadAllLines(savePath, Encoding.UTF8);
                if (lines.Length == 0 || !lines[0].StartsWith("NetBunnyTraffic\t1\t", StringComparison.Ordinal)) throw new IOException("记录格式无法识别");
                DateTime start;
                if (DateTime.TryParse(lines[0].Substring("NetBunnyTraffic\t1\t".Length), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out start)) FirstTracked = start;
                int invalid = 0;
                foreach (string line in lines)
                {
                    if (line.StartsWith("NetBunnyTraffic\t", StringComparison.Ordinal) || line.Length == 0) continue;
                    try
                    {
                        string[] p = line.Split('\t'); DateTime day; long down, up;
                        if (p.Length != 7 || !DateTime.TryParseExact(p[0], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) ||
                            !long.TryParse(p[5], NumberStyles.None, CultureInfo.InvariantCulture, out down) || !long.TryParse(p[6], NumberStyles.None, CultureInfo.InvariantCulture, out up) ||
                            down < 0 || up < 0 || (p[4] != "0" && p[4] != "1")) throw new FormatException();
                        if (day < now.Date.AddDays(-30) || day > now.Date.AddDays(1)) continue;
                        string key = Decode(p[1]), name = Decode(p[2]), path = Decode(p[3]);
                        if (key.Length > 4096 || name.Length > 512 || path.Length > 4096 || totals.Count >= 12000) throw new FormatException();
                        bool test = p[4] == "1";
                        totals[RecordKey(day, key, test)] = new TrafficTotalRow { Day = day, Key = key, Name = name, Path = path, SpeedTest = test, Download = down, Upload = up };
                    }
                    catch { invalid++; }
                }
                if (invalid > 0) { needsRecoveryBackup = true; PersistenceWarning = "部分损坏的历史记录已跳过；保存前将保留原文件副本。"; }
                Trim(now);
            }
            catch (Exception ex) { needsRecoveryBackup = File.Exists(savePath); PersistenceWarning = "历史记录读取失败：" + ex.Message; }
        }

        private void Save()
        {
            if (preview || !dirty) return;
            string temporary = savePath + ".tmp";
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(savePath));
                if (needsRecoveryBackup && File.Exists(savePath))
                {
                    // Preserve the exact unreadable input before any repair.
                    // Failure to make this copy aborts Save and leaves it intact.
                    string backup = savePath + ".unreadable-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                    File.Copy(savePath, backup, false);
                    needsRecoveryBackup = false;
                }
                using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
                {
                    writer.WriteLine("NetBunnyTraffic\t1\t" + FirstTracked.ToString("o", CultureInfo.InvariantCulture));
                    foreach (TrafficTotalRow row in totals.Values)
                        writer.WriteLine(row.Day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "\t" + Encode(row.Key) + "\t" + Encode(row.Name) + "\t" + Encode(row.Path) + "\t" + (row.SpeedTest ? "1" : "0") + "\t" + row.Download.ToString(CultureInfo.InvariantCulture) + "\t" + row.Upload.ToString(CultureInfo.InvariantCulture));
                }
                if (File.Exists(savePath)) File.Replace(temporary, savePath, null);
                else File.Move(temporary, savePath);
                dirty = false; PersistenceWarning = null;
            }
            catch (Exception ex) { PersistenceWarning = "流量记录暂时无法保存：" + ex.Message; }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }

        internal void LoadDemo()
        {
            if (!preview) return;
            totals.Clear(); samples.Clear(); baseline.Clear();
            DateTime now = DateTime.Now; displayDay = now.Date; Capturing = true;
            Add(now.Date, "demo-edge", "浏览器", "C:\\Program Files\\Browser\\browser.exe", false, 2412314211L, 89318244);
            Add(now.Date, "demo-cloud", "云盘同步", "C:\\Program Files\\Cloud\\cloud.exe", false, 842531123, 632381332);
            Add(now.Date, "demo-chat", "视频通话", "C:\\Program Files\\Chat\\chat.exe", false, 456913882, 312421542);
            Add(now.Date, "demo-test", "骑士助手", Application.ExecutablePath, true, 738193110, 249624221);
            for (int i = 0; i <= 150; i++)
            {
                double activity = 0.3 + 0.7 * Math.Pow(Math.Sin(i * .053), 2);
                samples.Add(new TrafficPoint { Time = now.AddSeconds((i - 150) * 2), Available = i < 60 || i > 64,
                    Download = (1.2 + 3.7 * Math.Abs(Math.Sin(i * .147))) * activity * 1024 * 1024,
                    Upload = (.25 + 1.3 * Math.Abs(Math.Cos(i * .091))) * activity * 1024 * 1024, SpeedTest = i > 102 && i < 125 });
            }
        }

        public void Dispose() { if (disposed) return; Save(); disposed = true; }

        internal static string SelfTest()
        {
            int count = 0;
            Action<bool, string> check = delegate(bool value, string name) { count++; if (!value) throw new InvalidOperationException("TrafficLedger: " + name); };
            DateTime t = new DateTime(2026, 10, 13, 12, 0, 0);
            Func<string, long, long, List<AppInfo>> snapshot = delegate(string session, long down, long up) {
                return new List<AppInfo> { new AppInfo { Name = "sample", Path = "C:\\sample.exe", TrafficKey = "C:\\sample.exe", TrafficSessionId = session, DownloadBytes = down, UploadBytes = up } }; };
            using (var ledger = new TrafficLedger(true, "not-written", "C:\\sample.exe", t))
            {
                ledger.ObserveAt(snapshot("first", 1000, 200), true, t.AddSeconds(1));
                check(ledger.Today()[0].Download == 1000, "first captured interval included");
                ledger.ObserveAt(snapshot("first", 1500, 300), true, t.AddSeconds(2));
                check(ledger.Today()[0].Download == 1500 && ledger.Today()[0].Upload == 300, "cumulative delta, no double count");
                ledger.ObserveAt(new List<AppInfo>(), true, t.AddSeconds(3));
                ledger.ObserveAt(snapshot("first", 1600, 320), true, t.AddSeconds(4));
                check(ledger.Today()[0].Download == 1600, "missing process retains baseline");
                ledger.ObserveAt(snapshot("first", 40, 10), true, t.AddSeconds(5));
                check(ledger.Today()[0].Download == 1640, "counter reset only adds new counter");
                ledger.SetSpeedTestActive(true);
                ledger.ObserveAt(snapshot("first", 140, 50), true, t.AddSeconds(6));
                check(ledger.Today().Find(delegate(TrafficTotalRow r) { return r.SpeedTest; }).Download == 100, "speed test period separated");
                ledger.SetSpeedTestActive(false);
                ledger.ObserveAt(snapshot("first", 170, 60), true, t.AddSeconds(7));
                check(ledger.Today().Find(delegate(TrafficTotalRow r) { return !r.SpeedTest; }).Download == 1670, "cancel restores ordinary classification");
                ledger.ObserveAt(snapshot("first", 270, 60), false, t.AddSeconds(8));
                check(!ledger.Samples[ledger.Samples.Count - 1].Available, "unavailable sample differs from zero");
                check(ledger.Today().Find(delegate(TrafficTotalRow r) { return !r.SpeedTest; }).Download == 1770, "last captured delta survives driver stop before poll");
                ledger.ObserveAt(snapshot("first", 280, 60), true, t.AddSeconds(9));
                check(ledger.Today().Find(delegate(TrafficTotalRow r) { return !r.SpeedTest; }).Download == 1780, "capture availability transition does not double count");
                ledger.ObserveAt(snapshot("second", 20, 0), true, t.AddSeconds(10));
                check(ledger.Today().Find(delegate(TrafficTotalRow r) { return !r.SpeedTest; }).Download == 1800, "new engine baseline starts at zero");
            }
            DateTime midnight = t.Date.AddDays(1);
            using (var ledger = new TrafficLedger(true, "not-written", "C:\\sample.exe", midnight.AddSeconds(-2)))
            {
                ledger.ObserveAt(snapshot("one", 1000, 200), true, midnight.AddSeconds(-1));
                ledger.ObserveAt(snapshot("one", 1200, 240), true, midnight.AddSeconds(1));
                check(ledger.Today()[0].Download == 100 && ledger.Today()[0].Upload == 20, "midnight apportions interval delta only");
                check(ledger.totals[RecordKey(t.Date, "C:\\sample.exe", false)].Download == 1100, "previous day retains its share");
                ledger.ObserveAt(snapshot("one", 1200, 240), true, midnight.AddMinutes(6));
                check(ledger.Samples.Count == 1 && ledger.Samples[0].Download == 0 && ledger.Samples[0].Available, "five minute curve retention and measured zero");
            }
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NetBunny-ledger-test-" + Guid.NewGuid().ToString("N"));
            string file = System.IO.Path.Combine(dir, "traffic.tsv");
            try
            {
                using (var first = new TrafficLedger(false, file, "C:\\sample.exe", t)) { first.ObserveAt(snapshot("one", 100, 30), true, t.AddSeconds(1)); }
                check(File.Exists(file), "disposal persists captured totals");
                using (var second = new TrafficLedger(false, file, "C:\\sample.exe", t.AddSeconds(2)))
                {
                    second.ObserveAt(snapshot("new-run", 40, 10), true, t.AddSeconds(3));
                    check(second.Today()[0].Download == 140, "restart adds new session without counting history twice");
                }
                using (var third = new TrafficLedger(false, file, "C:\\sample.exe", t.AddSeconds(4))) check(third.Today()[0].Upload == 40, "atomic replacement remains readable");
                File.AppendAllText(file, "broken-record\r\n", Encoding.UTF8);
                string damaged = File.ReadAllText(file, Encoding.UTF8);
                using (var recovery = new TrafficLedger(false, file, "C:\\sample.exe", t.AddSeconds(5)))
                {
                    check(recovery.PersistenceWarning != null, "corrupt history is reported");
                    recovery.ObserveAt(snapshot("after-damage", 10, 5), true, t.AddSeconds(6));
                }
                string[] backups = Directory.GetFiles(dir, "traffic.tsv.unreadable-*");
                check(backups.Length == 1 && File.ReadAllText(backups[0], Encoding.UTF8) == damaged, "repair preserves exact corrupt input before replacing");
                using (var repaired = new TrafficLedger(false, file, "C:\\sample.exe", t.AddSeconds(7)))
                    check(repaired.PersistenceWarning == null && repaired.Today()[0].Download == 150, "repaired history retains valid rows and new increment");
                using (var readOnly = new TrafficLedger(true, System.IO.Path.Combine(dir, "preview.tsv"), "C:\\sample.exe", t)) { readOnly.ObserveAt(snapshot("preview", 20, 1), true, t.AddSeconds(30)); }
                check(!File.Exists(System.IO.Path.Combine(dir, "preview.tsv")), "preview never persists");
            }
            finally { try { if (File.Exists(file)) File.Delete(file); if (Directory.Exists(dir)) { foreach (string backup in Directory.GetFiles(dir, "traffic.tsv.unreadable-*")) File.Delete(backup); Directory.Delete(dir); } } catch { } }
            return "TrafficLedger: " + count + " deterministic checks passed (no network traffic or system setting changes).";
        }
    }

    internal static class TrafficText
    {
        internal static string Bytes(double value)
        {
            string[] units = { "B", "KiB", "MiB", "GiB", "TiB" }; int i = 0;
            while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
            return value.ToString(i == 0 ? "0" : "0.00", CultureInfo.CurrentCulture) + " " + units[i];
        }
        internal static string Rate(double value) { return Bytes(value) + "/s"; }
    }

    internal sealed class TrafficCurve : Control
    {
        internal IList<TrafficPoint> Points;
        internal TrafficCurve() { DoubleBuffered = true; BackColor = Color.White; Font = Art.Font(9); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle plot = new Rectangle(72, 34, Math.Max(10, Width - 96), Math.Max(10, Height - 70));
            var points = Points;
            double maximum = 1024;
            if (points != null) foreach (TrafficPoint p in points) if (p.Available) maximum = Math.Max(maximum, Math.Max(p.Download, p.Upload));
            maximum *= 1.12;
            using (var grid = new Pen(Art.Line))
            using (var muted = new SolidBrush(Art.Muted))
            {
                for (int i = 0; i < 4; i++)
                {
                    int y = plot.Top + i * plot.Height / 3; g.DrawLine(grid, plot.Left, y, plot.Right, y);
                    g.DrawString(TrafficText.Rate(maximum * (3 - i) / 3), Font, muted, 2, y - 7);
                }
                g.DrawString("5 分钟前", Font, muted, plot.Left, plot.Bottom + 9);
                g.DrawString("现在", Font, muted, plot.Right - 35, plot.Bottom + 9);
                using (var mint = new SolidBrush(Art.Mint)) g.DrawString("↓ 下载", Font, mint, plot.Left, 7);
                using (var purple = new SolidBrush(Art.Purple)) g.DrawString("↑ 上传", Font, purple, plot.Left + 85, 7);
                g.DrawString("浅紫：测速期间    曲线断开：监控不可用", Font, muted, plot.Left + 170, 7);
            }
            if (points == null || points.Count == 0)
            {
                TextRenderer.DrawText(g, "等待监控数据…", Font, plot, Art.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            DateTime end = points[points.Count - 1].Time, start = end.AddMinutes(-5);
            using (var downPen = new Pen(Art.Mint, 2.4f))
            using (var upPen = new Pen(Art.Purple, 2.2f))
            using (var shade = new SolidBrush(Color.FromArgb(239, 234, 253)))
            {
                for (int i = 1; i < points.Count; i++)
                {
                    TrafficPoint a = points[i - 1], b = points[i];
                    float ax = plot.Left + (float)((a.Time - start).TotalSeconds / 300 * plot.Width);
                    float bx = plot.Left + (float)((b.Time - start).TotalSeconds / 300 * plot.Width);
                    if (a.SpeedTest || b.SpeedTest) g.FillRectangle(shade, ax, plot.Top, Math.Max(1, bx - ax), plot.Height);
                }
                for (int i = 1; i < points.Count; i++)
                {
                    TrafficPoint a = points[i - 1], b = points[i];
                    if (!a.Available || !b.Available || (b.Time - a.Time).TotalSeconds > 5) continue;
                    float ax = plot.Left + (float)((a.Time - start).TotalSeconds / 300 * plot.Width);
                    float bx = plot.Left + (float)((b.Time - start).TotalSeconds / 300 * plot.Width);
                    g.DrawLine(downPen, ax, plot.Bottom - (float)(a.Download / maximum * plot.Height), bx, plot.Bottom - (float)(b.Download / maximum * plot.Height));
                    g.DrawLine(upPen, ax, plot.Bottom - (float)(a.Upload / maximum * plot.Height), bx, plot.Bottom - (float)(b.Upload / maximum * plot.Height));
                }
            }
        }
    }

    internal sealed class TrafficView : UserControl
    {
        private readonly TrafficLedger ledger;
        private readonly Label summary, note, status;
        private readonly TrafficCurve curve;
        private readonly DataGridView grid;
        private int sortColumn = 3;
        private bool descending = true;
        private bool painting;
        internal TrafficView(bool preview, TrafficLedger ledger)
        {
            this.ledger = ledger; BackColor = Art.Bg; Font = Art.Font(9); Padding = new Padding(18); AutoScroll = true;
            var page = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, Padding = Padding.Empty };
            page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            summary = new Label { Dock = DockStyle.Fill, Height = 34, AutoSize = false, ForeColor = Art.Ink, Font = Art.Font(15, FontStyle.Bold), Text = "今天的流量排行", Margin = new Padding(0, 0, 0, 4) };
            note = new Label { Dock = DockStyle.Fill, Height = 53, AutoSize = false, ForeColor = Art.Muted, Text = "只统计本工具运行且监控成功期间、可归属程序的 IP 字节。\r\n测速期间，本工具的全部流量单独列出，包含测速、连通检查等；不等同于测试数据净流量。", Margin = new Padding(0, 0, 0, 8) };
            curve = new TrafficCurve { Dock = DockStyle.Fill, Height = 218, Margin = new Padding(0, 0, 0, 10) };
            grid = new DataGridView { Dock = DockStyle.Fill, Height = 268, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false, AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, BorderStyle = BorderStyle.None, BackgroundColor = Color.White, GridColor = Art.Line, EnableHeadersVisualStyles = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, ColumnHeadersHeight = 39, RowTemplate = { Height = 36 } };
            grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Art.Ink, SelectionBackColor = Color.FromArgb(236, 231, 252), SelectionForeColor = Art.Ink, Padding = new Padding(8, 0, 4, 0) };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(237, 232, 253), ForeColor = Art.Purple, Font = Art.Font(9, FontStyle.Bold), Padding = new Padding(8, 0, 0, 0) };
            string[] names = { "程序", "下载", "上传", "合计", "程序位置" };
            float[] widths = { 27, 13, 13, 14, 33 };
            for (int i = 0; i < names.Length; i++) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = names[i], FillWeight = widths[i], SortMode = DataGridViewColumnSortMode.Programmatic });
            grid.ColumnHeaderMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.ColumnIndex < 0) return; descending = sortColumn == e.ColumnIndex ? !descending : e.ColumnIndex != 0 && e.ColumnIndex != 4; sortColumn = e.ColumnIndex; RefreshData(); };
            grid.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0) OpenSelectedLocation(); };
            grid.CellMouseDown += delegate(object sender, DataGridViewCellMouseEventArgs e) { if (e.Button == MouseButtons.Right && e.RowIndex >= 0) { grid.ClearSelection(); grid.Rows[e.RowIndex].Selected = true; } };
            var context = new ContextMenuStrip(); context.Items.Add("打开程序所在位置", null, delegate { OpenSelectedLocation(); });
            context.Items.Add("复制完整路径", null, delegate { string path = SelectedPath(); if (!String.IsNullOrEmpty(path)) { try { Clipboard.SetText(path); } catch { } } });
            grid.ContextMenuStrip = context;
            status = new Label { Dock = DockStyle.Fill, Height = 52, AutoSize = false, ForeColor = Art.Muted, Margin = new Padding(0, 10, 0, 0) };
            page.Controls.Add(summary); page.Controls.Add(note); page.Controls.Add(curve); page.Controls.Add(grid); page.Controls.Add(status); Controls.Add(page);
            RefreshData();
        }

        internal void LoadDemo() { ledger.LoadDemo(); RefreshData(); }

        internal void RefreshData()
        {
            if (IsDisposed || painting) return; painting = true;
            try
            {
                List<TrafficTotalRow> rows = ledger.Today(); long down = 0, up = 0;
                foreach (TrafficTotalRow row in rows) { down += row.Download; up += row.Upload; }
                summary.Text = "今日流量  ↓ " + TrafficText.Bytes(down) + "    ↑ " + TrafficText.Bytes(up);
                rows.Sort(delegate(TrafficTotalRow a, TrafficTotalRow b) {
                    int comparison;
                    if (sortColumn == 0) comparison = StringComparer.CurrentCultureIgnoreCase.Compare(DisplayName(a), DisplayName(b));
                    else if (sortColumn == 4) comparison = StringComparer.CurrentCultureIgnoreCase.Compare(a.Path, b.Path);
                    else if (sortColumn == 1) comparison = a.Download.CompareTo(b.Download);
                    else if (sortColumn == 2) comparison = a.Upload.CompareTo(b.Upload);
                    else comparison = (a.Download + a.Upload).CompareTo(b.Download + b.Upload);
                    return descending ? -comparison : comparison;
                });
                string selected = grid.SelectedRows.Count == 0 ? null : (string)grid.SelectedRows[0].Tag;
                int scroll = grid.FirstDisplayedScrollingRowIndex;
                grid.Rows.Clear();
                foreach (TrafficTotalRow row in rows)
                {
                    int index = grid.Rows.Add(DisplayName(row), TrafficText.Bytes(row.Download), TrafficText.Bytes(row.Upload), TrafficText.Bytes(row.Download + row.Upload), String.IsNullOrEmpty(row.Path) ? "无法读取路径" : row.Path);
                    grid.Rows[index].Tag = row.Path;
                    grid.Rows[index].Cells[0].ToolTipText = row.SpeedTest ? "测速期间本工具的全部 IP 流量，包含同时发生的连通检查等。" : row.Name;
                    grid.Rows[index].Cells[4].ToolTipText = row.Path ?? "";
                    if (selected != null && String.Equals(selected, row.Path, StringComparison.OrdinalIgnoreCase)) grid.Rows[index].Selected = true;
                }
                if (scroll >= 0 && scroll < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = scroll;
                foreach (DataGridViewColumn column in grid.Columns) column.HeaderCell.SortGlyphDirection = column.Index == sortColumn ? (descending ? SortOrder.Descending : SortOrder.Ascending) : SortOrder.None;
                curve.Points = ledger.Samples; curve.Invalidate();
                status.Text = (ledger.Capturing ? "● 监控中" : "○ 监控不可用，当前速率未知") + "  ·  " + ledger.DisplayDay.ToString("yyyy-MM-dd") + "（电脑本地日期）  ·  双击行打开程序位置\r\n" +
                    (String.IsNullOrEmpty(ledger.PersistenceWarning) ? (rows.Count == 0 ? "还没有捕获到可归属程序的流量。" : "累计从 " + ledger.FirstTracked.ToString("MM-dd HH:mm") + " 开始记录；退出工具期间不统计。") + "  跨零点的采样增量按时间分摊。" : ledger.PersistenceWarning);
            }
            finally { painting = false; }
        }

        private static string DisplayName(TrafficTotalRow row) { return row.SpeedTest ? "测速期间 · " + row.Name : row.Name; }
        private string SelectedPath() { return grid.SelectedRows.Count == 0 ? null : grid.SelectedRows[0].Tag as string; }
        private void OpenSelectedLocation()
        {
            string path = SelectedPath(); if (String.IsNullOrEmpty(path)) return;
            if (!File.Exists(path)) { MessageBox.Show(this, "程序文件已移动或当前无法访问。\r\n" + path, "程序位置", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path.Replace("\"", "") + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "无法打开位置：" + ex.Message, "程序位置", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }

    internal sealed class SpeedMiniWindow : Form
    {
        private readonly Action openMain;
        private readonly Label downLabel, upLabel, statusLabel;
        private bool dragging;
        private Point dragOffset;
        internal SpeedMiniWindow(Action openMain)
        {
            this.openMain = openMain;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
            Size = new Size(286, 124); BackColor = Color.FromArgb(239, 233, 254); Font = Art.Font(9); DoubleBuffered = true;
            Rectangle area = Screen.PrimaryScreen.WorkingArea; Location = new Point(area.Right - Width - 24, area.Bottom - Height - 24);
            downLabel = new Label { AutoSize = false, Location = new Point(65, 20), Size = new Size(205, 24), Font = Art.Font(11, FontStyle.Bold), ForeColor = Art.Mint, Text = "↓ 等待监控" };
            upLabel = new Label { AutoSize = false, Location = new Point(65, 45), Size = new Size(205, 24), Font = Art.Font(11, FontStyle.Bold), ForeColor = Art.Purple, Text = "↑ 等待监控" };
            statusLabel = new Label { AutoSize = false, Location = new Point(16, 88), Size = new Size(151, 22), Font = Art.Font(8), ForeColor = Art.Muted, Text = "拖动小窗 · 右键隐藏" };
            var manage = new SoftButton { Text = "打开管理", Location = new Point(173, 83), Size = new Size(97, 29), Font = Art.Font(9, FontStyle.Bold) };
            manage.Click += delegate { if (this.openMain != null) this.openMain(); };
            Controls.Add(downLabel); Controls.Add(upLabel); Controls.Add(statusLabel); Controls.Add(manage);
            var menu = new ContextMenuStrip(); menu.Items.Add("打开管理", null, delegate { if (this.openMain != null) this.openMain(); }); menu.Items.Add("隐藏悬浮窗", null, delegate { Hide(); }); ContextMenuStrip = menu;
            HookDrag(this); HookDrag(downLabel); HookDrag(upLabel); HookDrag(statusLabel);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            Shown += delegate { KeepInScreen(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; } }
        private void HookDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragOffset = new Point(Cursor.Position.X - Left, Cursor.Position.Y - Top); control.Capture = true; } };
            control.MouseMove += delegate { if (dragging) Location = new Point(Cursor.Position.X - dragOffset.X, Cursor.Position.Y - dragOffset.Y); };
            control.MouseUp += delegate { dragging = false; control.Capture = false; KeepInScreen(); };
            control.MouseDoubleClick += delegate { if (openMain != null) openMain(); };
            control.ContextMenuStrip = ContextMenuStrip;
        }

        private void KeepInScreen()
        {
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
        }

        internal void UpdateRates(double down, double up, bool available, bool testing)
        {
            if (IsDisposed) return;
            downLabel.Text = "↓ " + (available ? TrafficText.Rate(Math.Max(0, down)) : "速率未知");
            upLabel.Text = "↑ " + (available ? TrafficText.Rate(Math.Max(0, up)) : "速率未知");
            statusLabel.Text = !available ? "监控不可用" : testing ? "测速中 · 含测速流量" : "拖动小窗 · 右键隐藏";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Art.Fill(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), 18, Color.FromArgb(239, 233, 254));
            KnightBrand.DrawHelmet(e.Graphics, new Rectangle(8, 12, 53, 59));
            using (var pen = new Pen(Color.FromArgb(217, 207, 246))) using (GraphicsPath p = Art.Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 18)) e.Graphics.DrawPath(pen, p);
        }
    }
}
