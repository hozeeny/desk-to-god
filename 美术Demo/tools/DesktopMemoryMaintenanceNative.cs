using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Explicit source runner only. No production entry point or system-wide
    // maintenance enumeration is replaced or enabled by this test tool.
    internal sealed class DesktopMemoryMaintenanceNative
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        const long MiB = 1024L * 1024;
        const string Scope = "Source-compiled native fixture, not final EXE or natural RAM-pressure discovery. Five byte-identical main archives copied from a verified natural x1 ten-kill/awakening profile; no consent/marker/seed copy. Normal Observe host and its normal qishi continuous automatic attacks remain intact. Exact factory memory Boss with one initial source placement only; HP/damage/cooldown/realm/time untouched. One restricted three-argument Reclaim may apply actual TrimWorkingSet only to the retained, identity-verified, self-created hidden 96 MiB child. Other real maintenance is refused. Short native resource samples, not an eight-hour soak; raw other-app desktop pixels stay local.";
        enum Stage { OpenEcology, ReadOff, Grant, ConfirmGrant, ReadGranted, SelectBoss, ReadSelected, OpenSource, ReadSource, Fight, AwaitResult, VerifyData, OpenResults, ReadResults, Revoke, ReadRevoked, ReadBack }
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { internal int Left, Top, Right, Bottom; }
        [return: MarshalAs(UnmanagedType.Bool)] delegate bool ChildCallback(IntPtr window, IntPtr argument);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, ChildCallback callback, IntPtr argument);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process, uint flag);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        static readonly string[] Archives = { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt" };
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };

        static string Hash(string path)
        { if (!File.Exists(path)) return "missing"; using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); }
        static string BlockHash(byte[] block)
        { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(block)).Replace("-", ""); }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Hidden).GetValue(instance); }
        static void Set(object instance, string name, object value) { instance.GetType().GetField(name, Hidden).SetValue(instance, value); }
        static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, Hidden).Invoke(instance, args); }
        static Dictionary<string, string> ArchiveHashes(string profile) { return Archives.ToDictionary(name => name, name => Hash(Path.Combine(profile, name)), StringComparer.Ordinal); }
        static Dictionary<string, string> RealHashes()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            string[] names = Archives.Concat(new[] { "maintenance-consent.txt", "maintenance-consent.txt.revoked", "maintenance-results-v1.log" }).ToArray();
            var result = names.ToDictionary(name => name, name => Hash(Path.Combine(root, name)), StringComparer.Ordinal);
            if (Directory.Exists(root)) foreach (string path in Directory.GetFiles(root))
                if (names.Any(name => Path.GetFileName(path).StartsWith(name + ".", StringComparison.Ordinal))) result[Path.GetFileName(path)] = Hash(path);
            return result;
        }
        static bool Same(Dictionary<string, string> a, Dictionary<string, string> b)
        { return a.Count == b.Count && a.All(pair => b.ContainsKey(pair.Key) && b[pair.Key] == pair.Value); }

        sealed class OwnedMemoryChild : IDisposable
        {
            internal readonly Process Process;
            internal readonly IntPtr RetainedHandle;
            internal readonly int Id;
            internal readonly long StartedTicks;
            internal readonly string Path, SHA256, Nonce, DataHashBefore;
            internal readonly DateTime LeaseUntil;
            internal bool Stopped;
            internal int ExitCode = -1;
            internal string StopAcknowledgement, DataHashAfter;
            internal int VerifiedPages;
            volatile bool verifyReadPending;
            internal OwnedMemoryChild(DateTime leaseUntil)
            {
                Path = System.IO.Path.GetFullPath(Application.ExecutablePath); SHA256 = Hash(Path); Nonce = Guid.NewGuid().ToString("N"); LeaseUntil = leaseUntil;
                Process = System.Diagnostics.Process.Start(new ProcessStartInfo(Path, "--own-memory-child " + Nonce + " " + leaseUntil.Ticks.ToString(CultureInfo.InvariantCulture))
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true });
                RetainedHandle = Process.Handle; Id = Process.Id;
                try
                {
                    StartedTicks = Process.StartTime.ToUniversalTime().Ticks;
                    Task<string> ready = Task.Factory.StartNew(() => Process.StandardOutput.ReadLine());
                    Require(ready.Wait(7000), "Owned 96 MiB child READY timed out."); string[] parts = (ready.Result ?? "").Split('|');
                    Require(parts.Length == 8 && String.Join("|", parts.Take(5).ToArray()) == "READY|" + Nonce + "|" + Id + "|" + StartedTicks + "|" + SHA256
                        && parts[5] == (96 * MiB).ToString(CultureInfo.InvariantCulture) && parts[6] == "24576" && parts[7].Length == 64,
                        "Owned child failed its nonce/PID/start/executable-SHA/full-96MiB-pattern READY protocol."); DataHashBefore = parts[7];
                    Validate(Process); Process.Refresh(); Require(Process.WorkingSet64 >= 32 * MiB && RetainedHandle != IntPtr.Zero, "Owned child is not a live, warm >=32 MiB explicit candidate.");
                }
                catch { Dispose(); throw; }
            }
            internal void Validate(Process candidate)
            {
                candidate.Refresh();
                Require(!Stopped && !Process.HasExited && candidate.Id == Id && !candidate.HasExited && candidate.StartTime.ToUniversalTime().Ticks == StartedTicks
                    && String.Equals(System.IO.Path.GetFullPath(candidate.MainModule.FileName), Path, StringComparison.OrdinalIgnoreCase) && Hash(Path) == SHA256,
                    "Owned memory candidate identity changed; refuse native trim.");
            }
            internal Process Candidate()
            {
                Process candidate = System.Diagnostics.Process.GetProcessById(Id);
                try { Validate(candidate); return candidate; } catch { candidate.Dispose(); throw; }
            }
            internal void VerifyData()
            {
                Validate(Process); Process.StandardInput.WriteLine("VERIFY|" + Nonce); Process.StandardInput.Flush(); verifyReadPending = true;
                Task<string> line = Task.Factory.StartNew(() => Process.StandardOutput.ReadLine());
                Require(line.Wait(7000), "Owned child full data VERIFY timed out."); verifyReadPending = false;
                string[] parts = (line.Result ?? "").Split('|');
                Require(parts.Length == 5 && parts[0] == "VERIFIED" && parts[1] == Nonce && parts[2] == Id.ToString(CultureInfo.InvariantCulture)
                    && parts[3] == "24576" && parts[4] == DataHashBefore, "After native trim, every byte of all 24576 owned pages must retain its original pattern and full SHA256.");
                VerifiedPages = 24576; DataHashAfter = parts[4];
            }
            internal object Evidence()
            { return new { pid = Id, startUtcTicks = StartedTicks, executable = Path, executableSHA256 = SHA256, nonce = Nonce, retainedHandle = RetainedHandle.ToInt64(), leaseUntilUtc = LeaseUntil.ToString("o"), stopped = Stopped, exitCode = ExitCode, stopAcknowledgement = StopAcknowledgement,
                dataSHA256Before = DataHashBefore, dataSHA256After = DataHashAfter, verifiedPages = VerifiedPages, verificationScope = "VERIFY reads every byte and re-residents pages; earlier measured working-set reduction is instantaneous, not permanent release." }; }
            public void Dispose()
            {
                if (Stopped) return; Stopped = true;
                try
                {
                    if (!Process.HasExited)
                    {
                        if (verifyReadPending) { Process.Kill(); Process.WaitForExit(2000); throw new TimeoutException("Own data verification did not finish; only the retained owned memory child was stopped, with no normal-STOP claim."); }
                        Process.StandardInput.WriteLine("STOP|" + Nonce); Process.StandardInput.Flush();
                        if (!Process.WaitForExit(3000)) { Process.Kill(); Process.WaitForExit(2000); throw new TimeoutException("Only the retained owned memory child required emergency cleanup after its STOP timeout."); }
                    }
                    ExitCode = Process.ExitCode; StopAcknowledgement = Process.StandardOutput.ReadLine();
                    Require(ExitCode == 0 && StopAcknowledgement == "STOPPED|" + Nonce, "Owned child did not acknowledge STOP and exit normally; no other process is stopped.");
                }
                finally { Process.Dispose(); }
            }
        }
        static int MemoryChild(string nonce, string leaseText)
        {
            Guid parsed; long ticks;
            if (!Guid.TryParseExact(nonce, "N", out parsed) || !Int64.TryParse(leaseText, out ticks)) return 3;
            DateTime lease = new DateTime(ticks, DateTimeKind.Utc);
            if (lease <= DateTime.UtcNow || lease > DateTime.UtcNow.AddMinutes(5)) return 3;
            using (var expiry = new System.Threading.Timer(delegate { Environment.Exit(124); }, null, Math.Max(1, (int)(lease - DateTime.UtcNow).TotalMilliseconds), Timeout.Infinite))
            {
                const int pageBytes = 4096; byte[] block = new byte[96 * 1024 * 1024]; int pages = block.Length / pageBytes;
                for (int page = 0; page < pages; page++) for (int offset = 0; offset < pageBytes; offset++) block[page * pageBytes + offset] = (byte)(page * 37 + offset * 13 + 19);
                string originalHash = BlockHash(block); int pid;
                using (Process self = Process.GetCurrentProcess()) { pid = self.Id; Console.WriteLine("READY|" + nonce + "|" + pid + "|" + self.StartTime.ToUniversalTime().Ticks + "|" + Hash(Application.ExecutablePath) + "|" + block.Length + "|" + pages + "|" + originalHash); }
                Console.Out.Flush();
                while (true)
                {
                    string command = Console.ReadLine();
                    if (command == "STOP|" + nonce) { Console.WriteLine("STOPPED|" + nonce); Console.Out.Flush(); GC.KeepAlive(block); return 0; }
                    if (command != "VERIFY|" + nonce) { GC.KeepAlive(block); return 3; }
                    int verified = 0;
                    for (int page = 0; page < pages; page++)
                    {
                        for (int offset = 0; offset < pageBytes; offset++) if (block[page * pageBytes + offset] != (byte)(page * 37 + offset * 13 + 19)) { Console.WriteLine("CORRUPT|" + page + "|" + offset); Console.Out.Flush(); return 4; }
                        verified++;
                    }
                    Console.WriteLine("VERIFIED|" + nonce + "|" + pid + "|" + verified + "|" + BlockHash(block)); Console.Out.Flush(); GC.KeepAlive(block);
                }
            }
        }

        [STAThread] static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--own-memory-child") return MemoryChild(args[1], args[2]);
            try
            {
                Require(args.Length == 5, "Expected Assets, ArtDemoAssets, fresh evidence, copied private profile and clone provenance JSON.");
                string assets = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]), profile = Path.GetFullPath(args[3]), provenancePath = Path.GetFullPath(args[4]);
                string allowed = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles") + Path.DirectorySeparatorChar;
                Require(profile.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(profile).StartsWith("verify-", StringComparison.Ordinal)
                    && Directory.Exists(profile) && !Directory.Exists(output) && !File.Exists(output), "A new copied verify- profile and fresh evidence directory are required.");
                DesktopDemoProfiles.ValidateRoot(profile); DesktopDemoProfiles.ValidateRoot(output);
                Require(Directory.GetFiles(profile).Length == 5 && Directory.GetDirectories(profile).Length == 0 && Archives.All(name => File.Exists(Path.Combine(profile, name))), "Copy only the exact five main archives; no grants, markers, backups or seed files.");
                var provenance = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(provenancePath, Encoding.UTF8));
                Require(String.Equals((string)provenance["Profile"], profile, StringComparison.OrdinalIgnoreCase), "Clone provenance names a different profile.");
                var copies = (Dictionary<string, object>)provenance["CopyHashes"];
                Require(Archives.All(name => copies.ContainsKey(name) && (string)copies[name] == Hash(Path.Combine(profile, name))), "Copied archive bytes differ from the recorded source copy.");
                Dictionary<string, string> before = RealHashes(); Directory.CreateDirectory(output);
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[1]));
                DateTime lease; using (Process self = Process.GetCurrentProcess()) lease = self.StartTime.ToUniversalTime().AddSeconds(234);
                using (var child = new OwnedMemoryChild(lease))
                using (var session = new DesktopDemoSession(assets, profile, false, null, true))
                {
                    var fixture = new DesktopMemoryMaintenanceNative(session, child, assets, output, before, provenance, provenancePath);
                    ThreadExceptionEventHandler failure = delegate(object sender, ThreadExceptionEventArgs error) { fixture.Finish("Own UI thread failed: " + error.Exception); };
                    Application.ThreadException += failure;
                    try { Application.Run(session); return fixture.ExitCode; }
                    finally { Application.ThreadException -= failure; }
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }

        readonly DesktopDemoSession session;
        readonly DesktopDemoAcceptance native;
        readonly OwnedMemoryChild child;
        readonly AdventureEnemy boss;
        readonly string assets, output, sourceProfile;
        readonly Dictionary<string, string> realBefore, sourceBefore;
        readonly Stopwatch wall = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
        readonly Dictionary<string, object> report = new Dictionary<string, object>();
        readonly List<string> checks = new List<string>();
        readonly List<object> inputs = new List<object>(), setup = new List<object>(), samples = new List<object>(), glyphs = new List<object>(), workerProofs = new List<object>();
        readonly HashSet<string> captured = new HashSet<string>();
        readonly object workerGate = new object();
        readonly long initialFreed, initialLifetime;
        Stage stage; double next, sampleAt;
        bool busy, finished, readBackVerified, wasDead, nativeGuardActiveObserved, successfulBlockObserved;
        int callbacks, acceptedCallbacks, rejectedCallbacks, nativeTrimCalls, nativeTrimRejected, dialogClicks, bossSelectionClicks, blockClicks, deaths;
        CancellationTokenSource ownCancellation;
        Task<MemoryReclaimResult> ownWork;
        Task dataVerification;
        MemoryReclaimResult actualResult;
        MemoryTrimObservation actualTrim;
        string businessError;
        Form lastPanel;
        internal int ExitCode { get; private set; }
        AdventureController Game { get { return session.Game; } }
        object Native(string name, params object[] args) { return typeof(DesktopDemoAcceptance).GetMethod(name, Hidden).Invoke(native, args); }
        void Check(bool okay, string message) { Require(okay, message); checks.Add(message); Console.WriteLine("PASS " + message); }
        static IEnumerable<Control> Descendants(Control root)
        { foreach (Control control in root.Controls) { yield return control; foreach (Control nested in Descendants(control)) yield return nested; } }
        DesktopEcologyPanel Ecology { get { return Application.OpenForms.OfType<DesktopEcologyPanel>().Single(form => form.Visible && !form.IsDisposed); } }
        Label Heading(string text) { return Descendants(Ecology).OfType<Label>().Single(label => label.Text == text); }

        DesktopMemoryMaintenanceNative(DesktopDemoSession session, OwnedMemoryChild child, string assets, string output, Dictionary<string, string> before, Dictionary<string, object> provenance, string provenancePath)
        {
            this.session = session; this.child = child; this.assets = assets; this.output = output; realBefore = before; sourceProfile = (string)provenance["SourceProfile"];
            sourceBefore = ((Dictionary<string, object>)provenance["SourceBefore"]).ToDictionary(pair => pair.Key, pair => (string)pair.Value, StringComparer.Ordinal);
            Require(Same(sourceBefore, ArchiveHashes(sourceProfile)), "The source five archives changed before the native host started.");
            Require(Game.DesktopObservesSystem && !Game.Cultivation.Mortal && Game.Cultivation.MortalKills == 10 && Game.Cultivation.AutoBattleUnlocked
                && Game.Cultivation.AutoBattle && Game.Battle.Automatic && Field<int>(Game, "desktopAcceptanceSpeed") == 1
                && !Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance,
                "The copied real awakening profile must use its unchanged normal x1 Observe/automatic preference, with all new grants off.");
            initialFreed = Game.Battle.Progress.FreedBytes; initialLifetime = Game.Cultivation.LifetimeQi;
            // Both callbacks are installed before any message pumping. There is
            // no default/global maintenance fallback from this process.
            Set(Game, "simulatedCleaner", new Func<CleanupBatch, CancellationToken, Task<CleanupResult>>(delegate { throw new InvalidOperationException("File cleanup is forbidden throughout this memory-only native fixture."); }));
            Set(Game, "simulatedMemoryReclaimer", new Func<CancellationToken, Task<MemoryReclaimResult>>(ReclaimOwned));
            boss = EnemyBalance.MemoryBoss(Game.Battle.Progress, Game.Battle.Weapon, Field<Random>(Game, "random")); Call(Game, "Spawn", boss);
            Size enemySize = (Size)Call(Game, "EnemySize", boss), heroSize = Game.DesktopHeroWindow.Size; Rectangle area = Field<Rectangle>(Game, "area"); float scale = Field<float>(Game, "displayScale");
            double feetX = Field<double>(Game, "x") + heroSize.Width / 2.0, feetY = Field<double>(Game, "y") + heroSize.Height;
            double right = feetX + 120 * scale - enemySize.Width / 2.0;
            boss.X = right + enemySize.Width <= area.Right ? right : feetX - 120 * scale - enemySize.Width / 2.0; boss.Y = feetY - enemySize.Height;
            Call(Game, "BoundEnemy", boss); Call(Game, "PickEnemyTarget", boss); Game.Battle.Enemies.Add(boss); Call(Game, "RenderWorld", true);
            native = new DesktopDemoAcceptance(session, output, true); Field<Stopwatch>(native, "wall").Start(); ExitCode = 1;
            report["scope"] = Scope; report["seed"] = null; report["profile"] = session.ProfileRoot; report["cloneProvenance"] = provenance; report["cloneProvenanceSHA256"] = Hash(provenancePath);
            report["executable"] = Application.ExecutablePath; report["executableSHA256"] = Hash(Application.ExecutablePath); report["processId"] = Process.GetCurrentProcess().Id;
            report["productionSourcesManifest"] = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "production-sources.json"); report["productionSourcesManifestSHA256"] = Hash((string)report["productionSourcesManifest"]);
            report["inputDeadlineSeconds"] = 235; report["parentAbsoluteDeadlineSeconds"] = 240; report["startedUtc"] = DateTime.UtcNow.ToString("o"); report["clockAcceleration"] = "normal x1; no game timer or time mark edit";
            report["checks"] = checks; report["nativeInputs"] = inputs; report["uiSetup"] = setup; report["stateAndResourceSamples"] = samples; report["glyphChecks"] = glyphs; report["countersBefore"] = Counters(Game.Battle.Progress, Game.Cultivation);
            setup.Add(new { action = "Explicit memory source fixture", method = "EnemyBalance.MemoryBoss -> production Spawn/bounds -> one initial position only", boss.Id, boss.EvilTheme, boss.BossWeapon, boss.Health, boss.MaxHealth, boss.SpawnLevel, boss.X, boss.Y,
                normalCombat = "Qishi continuously attacks through normal product automatic behavior. Native selection makes this exact target Manual; individual blows are not claimed to be native clicks." });
            WriteReport(null, false); timer.Tick += Tick; timer.Start(); next = .5;
        }
        Task<MemoryReclaimResult> ReclaimOwned(CancellationToken token)
        {
            callbacks++; CancellationTokenSource production = Field<CancellationTokenSource>(Game, "memoryReclaimCancellation");
            bool exact = acceptedCallbacks == 0 && boss != null && Game.Battle.Enemies.Contains(boss) && boss.IsBoss && !boss.IsDiskBoss && !boss.IsVirtual && boss.Batch == null && boss.ProcessTarget == null
                && boss.Defeated && boss.FellInCombat && boss.Health == 0 && boss.KillRewarded && boss.CultivationRewarded && boss.Authorization == AdventureAuthorization.Manual
                && Game.MaintenanceConsentMemory && !Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentAutoMaintenance && production != null && production.Token == token;
            lock (workerGate) workerProofs.Add(new { seconds = wall.Elapsed.TotalSeconds, eventKind = "production-memory-callback", exactBossAccepted = exact, bossId = boss.Id });
            if (!exact) { rejectedCallbacks++; if (ownCancellation != null) ownCancellation.Cancel(); WriteReport("Unexpected memory callback refused before candidate creation or native trim.", false); throw new InvalidOperationException("Memory fixture refuses every callback except the exact manually selected, genuinely defeated Boss, once."); }
            acceptedCallbacks++; ownCancellation = CancellationTokenSource.CreateLinkedTokenSource(token); CancellationToken restricted = ownCancellation.Token;
            ownWork = Task.Run(delegate
            {
                try
                {
                    // A fresh wrapper belongs to Reclaim; the retained lifetime
                    // Process object is never passed to its disposing census.
                    MemoryReclaimResult result = MemoryReclaimer.Reclaim(restricted, new[] { child.Candidate() }, TrimOwned);
                    lock (workerGate) actualResult = result;
                    if (result.Trimmed != 1 || result.Unobserved != 0 || result.Cancelled || result.TimedOut || result.ProcessLimited || result.Error != null) ownCancellation.Cancel();
                    return result;
                }
                catch { ownCancellation.Cancel(); throw; }
            });
            WriteReport(null, false); return ownWork;
        }
        MemoryTrimObservation TrimOwned(Process candidate, CancellationToken token)
        {
            bool exact = false;
            try { child.Validate(candidate); exact = nativeTrimCalls == 0 && !token.IsCancellationRequested; }
            catch { }
            if (!exact) { Interlocked.Increment(ref nativeTrimRejected); ownCancellation.Cancel(); throw new InvalidOperationException("Exact owned child/one-call identity guard refused native trim."); }
            Interlocked.Increment(ref nativeTrimCalls); MemoryTrimObservation result = MemoryReclaimer.TrimWorkingSet(candidate, token);
            lock (workerGate) { actualTrim = result; workerProofs.Add(new { eventKind = "actual-native-TrimWorkingSet", candidatePid = child.Id, nonce = child.Nonce, result.Applied, result.Observed, result.Before, result.After, cancelled = token.IsCancellationRequested }); }
            if (!result.Applied || !result.Observed) ownCancellation.Cancel(); // Real last-candidate cancellation is retained by production Reclaim.
            return result;
        }
        void Boundaries()
        {
            Require(!Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentAutoMaintenance && !Game.DesktopProcessBusy && Field<object>(Game, "cleanupTask") == null
                && Game.Battle.Progress.FreedBytes == initialFreed && Game.Cultivation.AutoBattle && !Game.Cultivation.Mortal,
                "File/process/automatic-maintenance scope or unchanged copied automatic preference boundary failed.");
            Require(rejectedCallbacks == 0 && nativeTrimRejected == 0, "A forbidden or duplicate real worker was attempted; no retry is permitted.");
        }
        void Next(Stage value, double delay = .2) { stage = value; next = wall.Elapsed.TotalSeconds + delay; Console.WriteLine("STAGE " + stage + " " + wall.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)); }
        static object Counters(AdventureProgress battle, CultivationProgress cultivation)
        { return new { battle.Level, battle.Score, battle.Experience, battle.Health, battle.FreedBytes, cultivation.Mortal, cultivation.MortalKills, cultivation.Qi, cultivation.DeferredQi, cultivation.LifetimeQi, cultivation.RealmIndex, cultivation.Layer, cultivation.AutoBattle }; }
        static object Resources(Process process)
        {
            try { process.Refresh(); return new { pid = process.Id, cpuTotalMilliseconds = process.TotalProcessorTime.TotalMilliseconds, workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64,
                handles = process.HandleCount, gdiObjects = GetGuiResources(process.Handle, 0), userObjects = GetGuiResources(process.Handle, 1) }; }
            catch (Exception error) { return new { unavailable = error.GetType().Name }; }
        }
        void Sample()
        {
            if (wall.Elapsed.TotalSeconds < sampleAt) return; sampleAt = wall.Elapsed.TotalSeconds + .8;
            if (Game.Battle.Progress.Dead && !wasDead) deaths++; wasDead = Game.Battle.Progress.Dead;
            DesktopPlayerView view = Game.ReadDesktopPlayerView();
            if (blockClicks > 0 && Field<Stopwatch>(Game, "clock").Elapsed.TotalSeconds < Field<double>(Game, "blockUntil")) nativeGuardActiveObserved = true;
            if (blockClicks > 0 && view.RecentEvents.Any(text => text.StartsWith("格挡成功", StringComparison.Ordinal))) successfulBlockObserved = true;
            object self; using (Process process = Process.GetCurrentProcess()) self = Resources(process);
            samples.Add(new { seconds = wall.Elapsed.TotalSeconds, stage = stage.ToString(), game = self, ownedChild = Resources(child.Process), counters = Counters(Game.Battle.Progress, Game.Cultivation),
                target = new { boss.Id, boss.Health, boss.MaxHealth, boss.Defeated, boss.FellInCombat, boss.Authorization, boss.KillExperience, boss.CultivationRewardQi }, Game.Battle.Automatic,
                selectedBoss = Game.Battle.Target == boss, view.BlockText, view.RecentEvents, nativeGuardActiveObserved, successfulBlockObserved,
                Game.MaintenanceConsentMemory, Game.MaintenanceConsentCleanup, Game.MaintenanceConsentAutoMaintenance });
            WriteReport(null, false);
        }

        static void Reveal(Control control)
        { for (Control parent = control.Parent; parent != null; parent = parent.Parent) { var scroll = parent as ScrollableControl; if (scroll != null) scroll.ScrollControlIntoView(control); } control.FindForm().PerformLayout(); control.FindForm().Refresh(); }
        bool Presented(Control control, string role)
        {
            Reveal(control); bool okay;
            if (!(control is TextBox)) okay = (bool)Native("NativePreviewContentMatches", control, true);
            else
            {
                int samples = 0, matches = 0; Point origin = control.Parent.PointToScreen(control.Location);
                using (var expected = new Bitmap(control.Width, control.Height)) using (var actual = DesktopFrameCapture.CaptureRegion(new Rectangle(origin, control.Size), true))
                {
                    control.DrawToBitmap(expected, new Rectangle(Point.Empty, control.Size));
                    for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
                    {
                        Color source = expected.GetPixel(x, y), key = control.ForeColor;
                        if (Math.Abs(source.R - key.R) + Math.Abs(source.G - key.G) + Math.Abs(source.B - key.B) > 25) continue;
                        Color pixel = actual.GetPixel(x, y); samples++; if (Math.Abs(pixel.R - source.R) + Math.Abs(pixel.G - source.G) + Math.Abs(pixel.B - source.B) < 80) matches++;
                    }
                }
                okay = samples >= 60 && matches >= samples * .8; report["lastEditGlyphProbe"] = new { role, samples, matches, presented = okay, origin = origin.ToString() };
            }
            if (okay) glyphs.Add(new { stage = stage.ToString(), role, verification = "Unchanged >=60 samples/>=80% match/key25/pixel80; EDIT uses its complete bordered-window origin." }); return okay;
        }
        Dictionary<string, object> RootProbe(Rectangle region, IntPtr expected)
        {
            var samples = new List<object>(); bool matched = true; int insetX = Math.Min(8, Math.Max(0, region.Width / 4)), insetY = Math.Min(8, Math.Max(0, region.Height / 4));
            for (int y = region.Top + insetY; y < region.Bottom - insetY; y += Math.Max(30, region.Height / 8))
                for (int x = region.Left + insetX; x < region.Right - insetX; x += Math.Max(30, region.Width / 6))
                { Point point = new Point(x, y); IntPtr root = (IntPtr)Native("RootAt", point); matched &= root == expected; samples.Add(new { point = point.ToString(), root = root.ToInt64() }); }
            var proof = new Dictionary<string, object> { { "region", region.ToString() }, { "expectedRoot", expected.ToInt64() }, { "matched", matched && samples.Count > 0 }, { "samples", samples.ToArray() } }; report["lastRootProbe"] = proof; return proof;
        }
        void Capture(Form panel, string name, string purpose)
        {
            lastPanel = panel; if (captured.Contains(name)) return;
            Require(panel.Visible && panel.WindowState == FormWindowState.Normal && SystemInformation.VirtualScreen.Contains(panel.Bounds) && (IntPtr)Native("GetForegroundWindow") == panel.Handle, "A passed panel must be the visible own foreground window.");
            Dictionary<string, object> roots = RootProbe(panel.RectangleToScreen(panel.ClientRectangle), panel.Handle); Require((bool)roots["matched"], "Own foreground panel is occluded; refuse a passed capture.");
            Native("CapturePanel", panel, name); var captures = Field<List<object>>(native, "captures"); var metadata = (Dictionary<string, object>)captures[captures.Count - 1]; metadata["nativeRootVerification"] = roots; metadata["purpose"] = purpose + "; " + Scope; captured.Add(name);
        }
        bool Click(Control control, string name)
        {
            Require(wall.Elapsed.TotalSeconds < 235 && control.Visible && control.Enabled, "Refuse late, disabled or invisible native control input."); Reveal(control);
            Point point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2)); Form panel = control.FindForm();
            if ((IntPtr)Native("RootAt", point) != panel.Handle || WindowFromPoint(point) != control.Handle) return false;
            Capture(panel, name, "Exact own root/leaf production control before native input"); if (!(bool)Native("ClickControl", control)) return false;
            inputs.Add(new { seconds = wall.Elapsed.TotalSeconds, stage = stage.ToString(), kind = "native-control", control.Text, control.Name, root = panel.Handle.ToInt64(), leaf = control.Handle.ToInt64(), point = point.ToString() }); return true;
        }
        bool ClickActor(bool hero)
        {
            Require(wall.Elapsed.TotalSeconds < 235 && !Game.Battle.Progress.Dead && !boss.Defeated && !boss.VisualDefeated && Game.Battle.Enemies.Contains(boss), "Native actor input needs the exact living owned Boss, before deadline.");
            KnightLayeredWindow window;
            if (hero) { if (Game.Battle.Target != boss) return false; window = Game.DesktopHeroWindow; }
            else if (!Game.DesktopEnemyWindows.TryGetValue(boss.Id, out window)) return false;
            if (!window.Visible) return false;
            try { if (!(bool)Native("ClickActor", window)) return false; }
            catch (TargetInvocationException error) { if (error.InnerException is InvalidOperationException && error.InnerException.Message.StartsWith("找不到未被遮挡的", StringComparison.Ordinal)) return false; throw; }
            if (hero) blockClicks++; else bossSelectionClicks++;
            inputs.Add(new { seconds = wall.Elapsed.TotalSeconds, stage = stage.ToString(), kind = hero ? "native-hero-guard" : "native-exact-Boss-selection", exactBossId = boss.Id, expectedRoot = window.Handle.ToInt64(), point = Cursor.Position.ToString(), actualRoot = ((IntPtr)Native("RootAt", Cursor.Position)).ToInt64(), boss.Health, boss.MaxHealth }); return true;
        }
        bool ConfirmYes()
        {
            IntPtr dialog = (IntPtr)Native("OwnDialog", "真实电脑维护"); if (dialog == IntPtr.Zero) return false;
            Require(wall.Elapsed.TotalSeconds < 235, "Own dialog input deadline expired."); var yes = new List<IntPtr>();
            EnumChildWindows(dialog, delegate(IntPtr child, IntPtr ignored)
            {
                var text = new StringBuilder(256); var name = new StringBuilder(64); GetWindowText(child, text, text.Capacity); GetClassName(child, name, name.Capacity);
                string caption = new String(text.ToString().Where(c => c != '&' && !Char.IsWhiteSpace(c)).ToArray()); bool meaning = caption == "是" || caption == "是(Y)" || caption.Equals("Yes", StringComparison.OrdinalIgnoreCase) || caption.Equals("Yes(Y)", StringComparison.OrdinalIgnoreCase);
                if (name.ToString().Equals("Button", StringComparison.OrdinalIgnoreCase) && GetDlgCtrlID(child) == 6 && meaning && IsWindowVisible(child) && IsWindowEnabled(child)) yes.Add(child); return true;
            }, IntPtr.Zero);
            Require(yes.Count <= 1, "Refuse ambiguous own Yes confirmation."); if (yes.Count == 0) return false;
            NativeRect bounds, button; if (!GetWindowRect(dialog, out bounds) || !GetWindowRect(yes[0], out button)) return false;
            Rectangle rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), interior = rectangle; interior.Inflate(-12, -12); Dictionary<string, object> roots = RootProbe(interior, dialog);
            if (!(bool)roots["matched"] || (IntPtr)Native("GetForegroundWindow") != dialog) return false;
            Point point = new Point((button.Left + button.Right) / 2, (button.Top + button.Bottom) / 2); if (WindowFromPoint(point) != yes[0]) return false;
            if (captured.Add("02-own-memory-confirmation"))
            {
                using (Bitmap image = DesktopFrameCapture.CaptureRegion(rectangle, true)) image.Save(Path.Combine(output, "02-own-memory-confirmation.png"), ImageFormat.Png);
                Field<List<object>>(native, "captures").Add(new Dictionary<string, object> { { "image", "02-own-memory-confirmation.png" }, { "bounds", rectangle.ToString() }, { "nativeRootVerification", roots }, { "purpose", "Own-PID native memory-scope Yes/No, unique IDYES+meaning+root/leaf verified, raw local only" } });
            }
            if (!(bool)Native("NativeClick", point, dialog, "owned-memory-consent-yes")) return false;
            dialogClicks++; inputs.Add(new { seconds = wall.Elapsed.TotalSeconds, kind = "own-PID-IDYES-memory-only", root = dialog.ToInt64(), leaf = yes[0].ToInt64(), point = point.ToString() }); return true;
        }
        void CaptureCombat(string name)
        {
            List<ArtDemoActorFrame> actors = Game.SnapshotArtDemoActors(); Bitmap actual = null, background = null, expected = null;
            try
            {
                KnightLayeredWindow own; Require(Game.DesktopEnemyWindows.TryGetValue(boss.Id, out own), "Exact Boss window missing.");
                ArtDemoActorFrame hero = actors.Single(actor => actor.Window == Game.DesktopHeroWindow), enemy = actors.Single(actor => actor.Window == own && actor.Role == "enemy");
                Point point = (Point)Native("FindPixel", own, enemy.Frame, true, default(IntPtr)); Require((IntPtr)Native("RootAt", point) == own.Handle, "Exact Boss lacks a safe visible opaque hit point.");
                Rectangle bounds = Rectangle.Union(hero.Bounds, enemy.Bounds); bounds.Inflate(28, 28); bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
                actual = DesktopFrameCapture.CaptureRegion(bounds, true); actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                try { foreach (ArtDemoActorFrame actor in actors) actor.Window.Hide(); background = DesktopFrameCapture.CaptureRegion(bounds, false); }
                finally { foreach (ArtDemoActorFrame actor in actors.OrderByDescending(actor => actor.ZOrder)) if (!actor.Window.IsDisposed) actor.Window.Show(); }
                expected = ArtDemoPixelVerifier.ExpectedComposite(background, actors, bounds.Location);
                object heroProof = ArtDemoPixelVerifier.VerifyActor(hero, actors, actual, background, expected, bounds.Location), bossProof = ArtDemoPixelVerifier.VerifyActor(enemy, actors, actual, background, expected, bounds.Location);
                Field<List<object>>(native, "captures").Add(new Dictionary<string, object> { { "image", name + ".png" }, { "bounds", bounds.ToString() }, { "exactBossId", boss.Id }, { "exactBossHandle", own.Handle.ToInt64() }, { "actors", new[] { heroProof, bossProof } }, { "purpose", "Exact Boss+hero native composite, original 90%/RGB45 verifier, synchronous UI callback; no timer pause/time edits, raw local only" } }); captured.Add(name);
            }
            finally { if (actual != null) actual.Dispose(); if (background != null) background.Dispose(); if (expected != null) expected.Dispose(); foreach (ArtDemoActorFrame actor in actors) actor.Dispose(); }
        }

        void Tick(object sender, EventArgs args)
        {
            if (busy || finished) return; busy = true;
            try
            {
                if (wall.Elapsed.TotalSeconds >= 235) throw new TimeoutException("235-second input deadline; parent owns the 240-second process deadline.");
                Boundaries(); Sample(); if (wall.Elapsed.TotalSeconds < next) return;
                switch (stage)
                {
                    case Stage.OpenEcology:
                        session.ClosePanels(); session.OpenPanel("ecology"); setup.Add(new { action = "Open existing ecology panel", method = "UI setup only; original qishi auto behavior keeps running" }); Next(Stage.ReadOff, .5); break;
                    case Stage.ReadOff:
                        Ecology.UpdateState(); if (!Presented(Heading("电脑负担化为妖物，处理结果如实记录"), "ecology heading before grant")) break;
                        Check(!Game.MaintenanceConsentMemory && callbacks == 0 && nativeTrimCalls == 0, "Memory scope begins off and no maintenance callback or native trim ran"); Next(Stage.Grant); break;
                    case Stage.Grant:
                        if (Click(Field<Button>(Ecology, "memory"), "01-memory-scope-off")) Next(Stage.ConfirmGrant, .1); break;
                    case Stage.ConfirmGrant:
                        if (ConfirmYes()) Next(Stage.ReadGranted, .3); break;
                    case Stage.ReadGranted:
                        if ((IntPtr)Native("OwnDialog", "真实电脑维护") != IntPtr.Zero) break;
                        Ecology.UpdateState(); if (!Game.MaintenanceConsentMemory || !Presented(Field<Button>(Ecology, "memory"), "memory scope enabled")) break;
                        MaintenanceConsentStore grant = MaintenanceConsentStore.Load(Path.Combine(session.ProfileRoot, "maintenance-consent.txt"));
                        Check(grant.MemoryEnabled && !grant.CleanupEnabled && !grant.AutoMaintenanceEnabled && callbacks == 0, "Native memory Yes persists only its private scope and never runs maintenance by itself");
                        Capture(Ecology, "03-memory-scope-on", "Actual memory grant; file and unattended maintenance remain off"); session.ClosePanels(); Next(Stage.SelectBoss, .3); break;
                    case Stage.SelectBoss:
                        if (Game.Battle.Progress.Dead) break; if (ClickActor(false)) Next(Stage.ReadSelected, .2); break;
                    case Stage.ReadSelected:
                        if (Game.Battle.Target != boss || boss.Authorization != AdventureAuthorization.Manual) { Next(Stage.SelectBoss); break; }
                        Check(Game.Battle.Automatic && boss.Health > 0 && !boss.FellInCombat, "Native exact-Boss selection is Manual while the copied qishi normal continuous attack preference stays on");
                        CaptureCombat("04-exact-memory-boss-selected"); Next(Stage.OpenSource); break;
                    case Stage.OpenSource:
                        session.OpenPanel("ecology"); Next(Stage.ReadSource, .3); break;
                    case Stage.ReadSource:
                        Require(Game.Battle.Target == boss && boss.Authorization == AdventureAuthorization.Manual && !boss.Defeated, "Source reader lost the exact living manual Boss.");
                        Ecology.UpdateState(); TextBox source = Field<TextBox>(Ecology, "targetSource"); Require(source.Text.Contains("系统可用内存采样") && source.Text.Contains("击败后尝试整理工作集"), "The actual Boss source/action UI must truthfully describe an attempted work-set action.");
                        if (!Presented(source, "actual memory Boss source/action EDIT")) break;
                        Capture(Ecology, "04b-memory-source-and-proposed-action", "Actual production source/action reader; Boss instantiation is an explicit source fixture, not natural pressure discovery"); session.ClosePanels(); Next(Stage.Fight, .2); break;
                    case Stage.Fight:
                        if (boss.FellInCombat) { Next(Stage.AwaitResult, .1); break; }
                        if (Game.Battle.Progress.Dead) break; // Natural 60s recovery only; no revival/HP/time edits.
                        if (Game.Battle.Target != boss || boss.Authorization != AdventureAuthorization.Manual) { if (ClickActor(false)) next = wall.Elapsed.TotalSeconds + .2; break; }
                        double now = Field<Stopwatch>(Game, "clock").Elapsed.TotalSeconds; double cooldown = (double)Call(Game, "EnemyAttackCooldown", boss);
                        if (boss.AttackTime > cooldown - .45 && now >= Field<double>(Game, "blockCooldownUntil")) if (ClickActor(true)) next = wall.Elapsed.TotalSeconds + .1;
                        if (!captured.Contains("04c-memory-combat-midpoint") && boss.Health < boss.MaxHealth / 2) CaptureCombat("04c-memory-combat-midpoint");
                        break;
                    case Stage.AwaitResult:
                        if (callbacks == 0 && boss.Defeated) throw new InvalidOperationException("Boss settled without the exact Manual memory callback; refuse to spawn a replacement or retry.");
                        if (Field<Task<MemoryReclaimResult>>(Game, "memoryReclaimTask") != null) break;
                        Require(ownWork != null && ownWork.IsCompleted && actualResult != null, "The exact memory worker failed before returning a measurable result; no retry.");
                        Check(callbacks == 1 && acceptedCallbacks == 1 && rejectedCallbacks == 0 && boss.Defeated && boss.FellInCombat && boss.KillRewarded && boss.CultivationRewarded
                            && bossSelectionClicks >= 1 && Game.Battle.Progress.FreedBytes == initialFreed && Game.Cultivation.LifetimeQi >= initialLifetime + boss.CultivationRewardQi,
                            "Only the genuinely defeated selected Boss invoked one restricted memory worker; its normal rewards remain separate from disk bytes");
                        bool observed = actualTrim != null && actualTrim.Applied && actualTrim.Observed;
                        if (nativeTrimCalls != 1 || !observed || actualResult.Trimmed != 1 || actualResult.Unobserved != 0 || actualResult.Cancelled || actualResult.Error != null)
                            businessError = "Actual owned native trim failed or could not be measured; cancellation prevents retry, and the real result is retained.";
                        if (observed) Check(actualResult.WorkingSetReduction == Math.Max(0, actualTrim.Before - actualTrim.After), "Reduction equals only actual before/after working-set observations, including an honest zero if it grew");
                        dataVerification = Task.Run(() => child.VerifyData()); Next(Stage.VerifyData, .1); break;
                    case Stage.VerifyData:
                        if (!dataVerification.IsCompleted) break;
                        if (dataVerification.IsFaulted) throw dataVerification.Exception.GetBaseException();
                        Check(child.VerifiedPages == 24576 && child.DataHashBefore == child.DataHashAfter, "After actual native trim, the owned child re-reads every byte of all 24576 pages and retains its full original data SHA256");
                        report["dataVerificationNote"] = "VERIFY deliberately touches every page again, so pages may become resident again. The earlier actual working-set delta is an instantaneous observation, not permanent free memory.";
                        Next(Stage.OpenResults); break;
                    case Stage.OpenResults:
                        session.OpenPanel("ecology"); Next(Stage.ReadResults, .5); break;
                    case Stage.ReadResults:
                        Ecology.UpdateState(); TextBox results = Field<TextBox>(Ecology, "results"); Require(results.Text == actualResult.Summary, "Actual production result UI differs from the real restricted worker summary.");
                        if (!Presented(Heading("实际维护记录"), "actual maintenance heading") || !Presented(results, "actual memory result EDIT")) break;
                        Capture(Ecology, "05-actual-owned-memory-worker-result", "Exact production summary from actual restricted native trim, with measured bytes/unknown/cancellation preserved"); Next(Stage.Revoke); break;
                    case Stage.Revoke:
                        if (Click(Field<Button>(Ecology, "memory"), "06-native-memory-revoke-button")) Next(Stage.ReadRevoked, .3); break;
                    case Stage.ReadRevoked:
                        Ecology.UpdateState(); if (Game.MaintenanceConsentMemory || !Presented(Field<Button>(Ecology, "memory"), "memory scope disabled after native revoke")) break;
                        MaintenanceConsentStore off = MaintenanceConsentStore.Load(Path.Combine(session.ProfileRoot, "maintenance-consent.txt"));
                        Check(!off.MemoryEnabled && !off.CleanupEnabled && !off.AutoMaintenanceEnabled && !Field<bool>(Game, "consentRetryMemory"), "Native memory revoke is durably off with every other maintenance scope still off");
                        Capture(Ecology, "07-memory-scope-revoked", "Actual persisted native memory authorization revocation"); Next(Stage.ReadBack); break;
                    case Stage.ReadBack:
                        if (!readBackVerified) { Check(Game.DesktopSaveNow() && Game.DesktopArchivePaths.All(File.Exists), "All five private archives save through the actual controller"); VerifyReadBack(); readBackVerified = true; }
                        Ecology.UpdateState(); if (!Presented(Field<TextBox>(Ecology, "results"), "memory result after native revoke/private Load")) break;
                        Capture(Ecology, "08-memory-result-after-private-Load", "Same-process stopped new controller Load verified exact history; not a true new-process restart"); Finish(businessError); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
            finally { busy = false; }
        }

        T MainArchive<T>(string name)
        { object[] args = { Path.Combine(session.ProfileRoot, name), null }; Require((bool)typeof(T).GetMethod("TryLoad", Hidden).Invoke(null, args), "Actual main private archive must parse without fallback: " + name); return (T)args[1]; }
        void VerifyReadBack()
        {
            string battlePath = Path.Combine(session.ProfileRoot, "adventure-v1.txt"); Require(File.ReadAllLines(battlePath).Contains("version=1"), "Actual adventure main file is present and v1.");
            AdventureProgress battle = AdventureProgress.Load(battlePath); CultivationProgress cultivation = MainArchive<CultivationProgress>("cultivation-v2.txt"); MainArchive<BaiyeProgress>("baiye-v1.txt"); MainArchive<LingyuProgress>("lingyu-v1.txt"); MainArchive<DongtianProgress>("dongtian-v1.txt");
            object live = Counters(Game.Battle.Progress, Game.Cultivation), saved = Counters(battle, cultivation); Require(Json.Serialize(live) == Json.Serialize(saved), "Actual main combat/cultivation counters differ from the just-saved runtime."); report["countersSavedMain"] = saved;
            using (var owner = new Form())
            {
                IntPtr handle = owner.Handle;
                using (var restored = new AdventureController(owner, assets, false, null, false, session.ProfileRoot, false))
                {
                    Field<System.Windows.Forms.Timer>(restored, "timer").Stop(); var entries = restored.ReadDesktopPlayerView().MaintenanceResults;
                    Check(entries.Length == 1 && entries[0].Kind == "Memory" && entries[0].Summary == actualResult.Summary && entries[0].TrimmedProcesses == actualResult.Trimmed
                        && entries[0].WorkingSetReduction == actualResult.WorkingSetReduction && Json.Serialize(live) == Json.Serialize(Counters(restored.Battle.Progress, restored.Cultivation))
                        && !owner.Visible && !restored.DesktopHeroWindow.Visible, "A stopped unshown non-observing new controller loads exact maintenance history and live counters in the same process");
                    report["countersRestoredController"] = Counters(restored.Battle.Progress, restored.Cultivation);
                }
            }
            report["privateArchivesSHA256"] = ArchiveHashes(session.ProfileRoot); report["maintenanceLogSHA256"] = Hash(Path.Combine(session.ProfileRoot, "maintenance-results-v1.log")); report["privateConsentSHA256"] = Hash(Path.Combine(session.ProfileRoot, "maintenance-consent.txt"));
        }
        void WriteReport(string error, bool passed)
        {
            report["memoryCallbacks"] = callbacks; report["acceptedMemoryCallbacks"] = acceptedCallbacks; report["rejectedMemoryCallbacks"] = rejectedCallbacks; report["actualNativeTrimCalls"] = nativeTrimCalls; report["rejectedNativeTrimCalls"] = nativeTrimRejected;
            report["nativeControlAndActorClicks"] = native == null ? 0 : Field<int>(native, "clicks"); report["nativeDialogClicks"] = dialogClicks; report["nativeClicks"] = (int)report["nativeControlAndActorClicks"] + dialogClicks;
            report["nativeBossSelectionClicks"] = bossSelectionClicks; report["nativeBlockClicks"] = blockClicks; report["nativeGuardActiveObserved"] = nativeGuardActiveObserved;
            report["successfulBlockObserved"] = successfulBlockObserved; report["naturalDeathsObserved"] = deaths; report["stage"] = stage.ToString(); report["elapsedSeconds"] = wall.Elapsed.TotalSeconds; report["passed"] = passed; report["error"] = error;
            report["ownedMemoryChild"] = child.Evidence(); report["countersAfter"] = Counters(Game.Battle.Progress, Game.Cultivation); report["actualBossRewards"] = new { boss.KillExperience, boss.CultivationRewardQi, boss.KillRewarded, boss.CultivationRewarded };
            lock (workerGate)
            {
                report["workerProofs"] = workerProofs.ToArray();
                if (actualTrim != null) report["actualTrim"] = new { actualTrim.Applied, actualTrim.Observed, actualTrim.Before, actualTrim.After };
                if (actualResult != null) report["actualMemoryResult"] = new { actualResult.Trimmed, actualResult.Skipped, actualResult.Unobserved, actualResult.WorkingSetReduction, actualResult.Cancelled, actualResult.TimedOut, actualResult.ProcessLimited, actualResult.Error, actualResult.Summary,
                    systemBeforeAvailable = actualResult.Before != null && actualResult.Before.Available, systemBeforePercent = actualResult.Before == null ? 0 : actualResult.Before.UsedPercent,
                    systemAfterAvailable = actualResult.After != null && actualResult.After.Available, systemAfterPercent = actualResult.After == null ? 0 : actualResult.After.UsedPercent };
            }
            report["captures"] = native == null ? new object[0] : (object)Field<List<object>>(native, "captures"); File.WriteAllText(Path.Combine(output, "memory-maintenance-native.json"), Json.Serialize(report), new UTF8Encoding(true));
        }
        void Finish(string error)
        {
            if (finished) return; finished = true; timer.Stop();
            try
            {
                if (ownCancellation != null) ownCancellation.Cancel();
                if (error != null && lastPanel != null && lastPanel.Visible && SystemInformation.VirtualScreen.IntersectsWith(lastPanel.Bounds))
                    using (Bitmap image = DesktopFrameCapture.CaptureRegion(Rectangle.Intersect(lastPanel.Bounds, SystemInformation.VirtualScreen), true)) image.Save(Path.Combine(output, "rejected-own-panel-local-only.png"), ImageFormat.Png);
                if (Game.MaintenanceConsentMemory) { report["emergencyTeardownRevocation"] = true; Game.SetMemoryConsent(false); Require(error != null, "A passing run must revoke through native UI, never teardown."); }
                if (ownWork != null && !ownWork.IsCompleted) ownWork.Wait(1500);
                Boundaries(); Check(Game.DesktopSaveNow(), "Final private save succeeds");
                var after = RealHashes(); bool same = Same(realBefore, after); report["realArchivesBefore"] = realBefore; report["realArchivesAfter"] = after; report["realArchivesUnchanged"] = same; Require(same, "Real user archive hashes changed.");
                var sourceAfter = ArchiveHashes(sourceProfile); report["sourceArchiveHashesAfter"] = sourceAfter; report["sourceArchivesUnchanged"] = Same(sourceBefore, sourceAfter); Require((bool)report["sourceArchivesUnchanged"], "Source natural-profile bytes changed.");
                report["allScopesOffAtExit"] = !Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance;
                child.Dispose(); report["ownedChildStoppedNormally"] = child.ExitCode == 0 && child.Stopped;
                if (error == null) Require(callbacks == 1 && acceptedCallbacks == 1 && rejectedCallbacks == 0 && nativeTrimCalls == 1 && nativeTrimRejected == 0 && blockClicks > 0 && nativeGuardActiveObserved
                    && actualTrim != null && actualTrim.Applied && actualTrim.Observed && actualResult != null && actualResult.Trimmed == 1 && actualResult.Unobserved == 0
                    && !actualResult.Cancelled && !actualResult.TimedOut && !actualResult.ProcessLimited && actualResult.Error == null && child.VerifiedPages == 24576 && child.DataHashBefore == child.DataHashAfter
                    && (bool)report["allScopesOffAtExit"] && (bool)report["ownedChildStoppedNormally"], "Final real trim/count/data-integrity/consent/owned-child boundary failed.");
            }
            catch (Exception finalError) { error = (error == null ? "" : error + "\n") + "Final diagnostics: " + finalError; }
            ExitCode = error == null ? 0 : 1;
            try { WriteReport(error, error == null); File.WriteAllText(Path.Combine(output, "memory-maintenance-native-input.log"), Field<StringBuilder>(native, "log").ToString(), new UTF8Encoding(true)); Console.WriteLine("Memory native passed=" + (error == null) + "; seconds=" + wall.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "; clicks=" + report["nativeClicks"]); }
            catch (Exception writeError) { ExitCode = 1; Console.Error.WriteLine(writeError); }
            finally { timer.Dispose(); session.ExitAfterAcceptance(ExitCode); }
        }
    }
}
