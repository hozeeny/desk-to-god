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
    // An explicitly launched, serial native test, never part of the game entry.
    // The normal host observes read-only. Its only mutation delegate has a
    // closed two-file allowlist and never falls back to the controller service.
    internal sealed class DesktopEcologyJourneyNative
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        const string Scope = "Source-compiled same-controller native ecology journey, not the final distributed EXE or long stability test. One fresh unseeded mortal private profile, ordinary observe=true host, x1. Only an explicitly bound batch from two owned GUID-directory Temp files may be cleaned. After native persisted revocation, nine ordinary manual kills, active awakening and zero-input automatic combat/cultivation continue in this same runtime and profile. Factory HP, realm, damage, cooldown, probability and game clock are unchanged; only the first owned source fixture's initial position is arranged. Other applications and raw desktop pixels remain local.";
        enum Stage { OpenEcology, ReadOff, Grant, ConfirmGrant, ReadGranted, SelectOwn, LockSelected, OpenOwnSource, ReadOwnSource, Fight, AwaitResult, OpenResults, ReadResults, Revoke, ReadRevoked, ReadBack, JourneyFight, OpenAwaken, ReadAwaken, AwaitAwaken, ObserveAutomatic, PanelsClosed, FinalReadBack }
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { internal int Left, Top, Right, Bottom; }
        [return: MarshalAs(UnmanagedType.Bool)] delegate bool ChildCallback(IntPtr window, IntPtr argument);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, ChildCallback callback, IntPtr argument);
        [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder text, int count);

        sealed class Identity
        {
            internal readonly string Path;
            internal readonly long Length, Written, Created;
            internal readonly uint Volume, IndexHigh, IndexLow;
            internal Identity(CleanupCandidate file)
            { Path = file.Path; Length = file.Length; Written = file.Written; Created = file.Created; Volume = file.Volume; IndexHigh = file.IndexHigh; IndexLow = file.IndexLow; }
            internal bool Matches(CleanupCandidate file)
            { return String.Equals(Path, file.Path, StringComparison.OrdinalIgnoreCase) && Length == file.Length && Written == file.Written && Created == file.Created && Volume == file.Volume && IndexHigh == file.IndexHigh && IndexLow == file.IndexLow && file.EligibleForCleanup; }
            internal object Evidence()
            { return new { path = Path, length = Length, writtenFileTimeUtc = Written, createdFileTimeUtc = Created, volume = Volume, indexHigh = IndexHigh, indexLow = IndexLow }; }
        }

        readonly DesktopDemoSession session;
        readonly DesktopDemoAcceptance native;
        readonly CleanupService service;
        readonly CleanupBatch originalBatch;
        readonly AdventureEnemy target;
        readonly AdventureController originalController;
        readonly CultivationProgress originalCultivation;
        readonly Identity[] identities;
        readonly string assets, output, ownedRoot, deletePath, heldPath, heldBefore;
        readonly Dictionary<string, string> realBefore;
        readonly Stopwatch wall = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        readonly Dictionary<string, object> report = new Dictionary<string, object>();
        readonly List<string> checks = new List<string>();
        readonly List<object> inputs = new List<object>(), setup = new List<object>(), states = new List<object>(), glyphs = new List<object>(), cleanInvocations = new List<object>(), journeyKills = new List<object>();
        readonly HashSet<string> captured = new HashSet<string>(StringComparer.Ordinal);
        Stage stage;
        double next;
        bool busy, finished, readBackVerified;
        string terminalFailure;
        AdventureEnemy journeyTarget;
        int lastJourneyKills, manualJourneyClicks, manualJourneyBlocks, naturalRests, autoInputCount, autoScore, previousPassiveScore, closedInputCount;
        long autoLifetimeQi, previousPassiveQi, closedLifetimeQi;
        double autoStarted, closedStarted, nextProgress, nextAutomaticSample;
        bool resting, passiveCultivationObserved, automaticKillObserved, automaticResting, automaticMovieStarted;
        int automaticRests, automaticResumes, automaticWindow;
        double automaticRestStarted;
        double lastJourneyStrike = -1;
        FileStream held;
        CleanupResult actualResult;
        int cleanCalls, acceptedCleanCalls, rejectedCleanCalls, dialogClicks, attackClicks, blockClicks, damageObserved, lastHealth;
        readonly int initialScore, initialKills, initialExperience;
        readonly long initialFreed, initialQi, initialDeferredQi, initialLifetimeQi;
        Form lastPanel;
        internal int ExitCode { get; private set; }
        AdventureController Game { get { return session.Game; } }
        object Native(string method, params object[] args) { return typeof(DesktopDemoAcceptance).GetMethod(method, Hidden).Invoke(native, args); }
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Hidden).GetValue(instance); }
        static void Set(object instance, string name, object value) { instance.GetType().GetField(name, Hidden).SetValue(instance, value); }
        static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, Hidden).Invoke(instance, args); }
        static string Hash(string path)
        { if (!File.Exists(path)) return "missing"; using (var hash = SHA256.Create()) using (var stream = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""); }
        static Dictionary<string, string> RealHashes()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            string[] names = { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt", "maintenance-consent.txt", "maintenance-consent.txt.revoked" };
            var result = names.ToDictionary(name => name, name => Hash(Path.Combine(root, name)), StringComparer.Ordinal);
            if (Directory.Exists(root)) foreach (string path in Directory.GetFiles(root))
                if (names.Any(name => Path.GetFileName(path).StartsWith(name + ".", StringComparison.Ordinal))) result[Path.GetFileName(path)] = Hash(path);
            return result;
        }
        static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        void Check(bool value, string text) { Require(value, text); checks.Add(text); Console.WriteLine("PASS " + text); }
        static IEnumerable<Control> Descendants(Control root)
        { foreach (Control control in root.Controls) { yield return control; foreach (Control nested in Descendants(control)) yield return nested; } }

        [STAThread] static int Main(string[] args)
        {
            Console.OutputEncoding=new UTF8Encoding(false);
            try
            {
                if (args.Length != 4) throw new ArgumentException("Expected Assets, ArtDemoAssets, fresh local evidence and fresh private profile.");
                string assets = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]), profile = Path.GetFullPath(args[3]);
                string allowed = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles") + Path.DirectorySeparatorChar;
                Require(profile.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(profile).StartsWith("verify-ecology-journey-", StringComparison.Ordinal)
                    && !Directory.Exists(profile) && !File.Exists(profile) && !Directory.Exists(output) && !File.Exists(output), "Fresh verify- profile inside the runner and fresh evidence are required.");
                DesktopDemoProfiles.ValidateRoot(profile); DesktopDemoProfiles.ValidateRoot(output);
                Dictionary<string, string> before = RealHashes(); Directory.CreateDirectory(output);
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[1]));
                // Exact known children only. No enumeration or deletion of any
                // pre-existing Temp content is performed by this setup.
                string temp = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
                string owned = Path.Combine(temp, "desk-native-" + Guid.NewGuid().ToString("N"));
                Require(!Directory.Exists(owned) && !File.Exists(owned), "The GUID source root must be new."); Directory.CreateDirectory(owned);
                string deleted = Path.Combine(owned, "owned-old.tmp"), locked = Path.Combine(owned, "owned-held.tmp");
                DateTime aged = DateTime.UtcNow.AddDays(-2);
                foreach (string file in new[] { deleted, locked })
                {
                    using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { byte[] bytes = new byte[file == deleted ? 4096 : 512]; for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 251); stream.Write(bytes, 0, bytes.Length); }
                    File.SetCreationTimeUtc(file, aged); File.SetLastWriteTimeUtc(file, aged);
                }
                CleanupService service = (CleanupService)Activator.CreateInstance(typeof(CleanupService), Hidden, null, new object[] { owned, null }, CultureInfo.InvariantCulture);
                CleanupSnapshot snapshot = service.Scan(CancellationToken.None);
                Require(snapshot.InventoryComplete && snapshot.Batches.Count == 1 && snapshot.CandidateFiles == 2 && snapshot.CandidateBytes == 4608
                    && snapshot.Batches[0].Candidates.All(file => file.Path == deleted || file.Path == locked), "The real private-root census must contain exactly the two eligible owned files.");
                using (DesktopDemoSession session = new DesktopDemoSession(assets, profile, false, null, true))
                {
                    // Constructor starts normal read-only observation. Before
                    // entering a message loop, install the closed mutation gate.
                    var fixture = new DesktopEcologyJourneyNative(session, assets, output, before, owned, deleted, locked, service, snapshot.Batches[0]);
                    ThreadExceptionEventHandler failure = delegate(object sender, ThreadExceptionEventArgs error) { fixture.Finish("Own UI thread failed: " + error.Exception); };
                    Application.ThreadException += failure;
                    try { Application.Run(session); return fixture.ExitCode; }
                    finally { Application.ThreadException -= failure; }
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }

        DesktopEcologyJourneyNative(DesktopDemoSession session, string assets, string output, Dictionary<string, string> before,
            string ownedRoot, string deletePath, string heldPath, CleanupService service, CleanupBatch batch)
        {
            this.session = session; this.assets = assets; this.output = output; realBefore = before; this.ownedRoot = Path.GetFullPath(ownedRoot);
            originalController = Game; originalCultivation = Game.Cultivation;
            this.deletePath = deletePath; this.heldPath = heldPath; this.service = service; originalBatch = batch;
            identities = batch.Candidates.Select(file => new Identity(file)).ToArray(); heldBefore = Hash(heldPath);
            initialScore = Game.Battle.Progress.Score; initialExperience = Game.Battle.Progress.Experience; initialFreed = Game.Battle.Progress.FreedBytes; initialKills = Game.Cultivation.MortalKills;
            initialQi = Game.Cultivation.Qi; initialDeferredQi = Game.Cultivation.DeferredQi; initialLifetimeQi = Game.Cultivation.LifetimeQi;
            Require(Game.Cultivation.Mortal && initialKills == 0 && !Game.Battle.Automatic && Field<int>(Game, "desktopAcceptanceSpeed") == 1
                && Game.DesktopObservesSystem && !File.Exists(Path.Combine(session.ProfileRoot,"desktop-demo-seed.txt"))
                && !Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance, "This fixture needs the normal fresh unseeded mortal x1 observing host with all grants off.");
            Set(Game, "simulatedCleaner", new Func<CleanupBatch, CancellationToken, Task<CleanupResult>>(CleanOwned));
            target = AdventureMonster.Create(batch, Field<Random>(Game, "random"), Game.Battle.Progress.Level);
            Call(Game, "Spawn", target);
            // Initial source placement only. Production spawn/bounds/waypoint
            // logic still applies; no subsequent target or hero teleportation.
            Size heroSize = Game.DesktopHeroWindow.Size, enemySize = (Size)Call(Game, "EnemySize", target);
            float scale = Field<float>(Game, "displayScale"); Rectangle area = Field<Rectangle>(Game, "area");
            double feetX = Field<double>(Game, "x") + heroSize.Width / 2.0, feetY = Field<double>(Game, "y") + heroSize.Height;
            double right = feetX + 95 * scale - enemySize.Width / 2.0;
            target.X = right + enemySize.Width <= area.Right ? right : feetX - 95 * scale - enemySize.Width / 2.0;
            target.Y = feetY - enemySize.Height; Call(Game, "BoundEnemy", target); Call(Game, "PickEnemyTarget", target);
            Game.Battle.Enemies.Add(target); Call(Game, "RenderWorld", true); lastHealth = target.Health;
            native = new DesktopDemoAcceptance(session, output, true); Field<Stopwatch>(native, "wall").Start(); ExitCode = 1;
            report["scope"] = Scope; report["seed"] = null; report["clockAcceleration"] = "x1, unchanged production Stopwatch/timer/cooldowns";
            report["processId"] = Process.GetCurrentProcess().Id; report["executable"] = Application.ExecutablePath; report["executableSHA256"] = Hash(Application.ExecutablePath);
            report["productionSourcesManifest"] = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "production-sources.json"); report["productionSourcesManifestSHA256"] = Hash((string)report["productionSourcesManifest"]);
            report["profile"] = session.ProfileRoot; report["ownedTempRoot"] = ownedRoot; report["inputDeadlineSeconds"] = 475; report["parentAbsoluteDeadlineSeconds"] = 480;
            report["sameControllerIdentity"] = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Game);
            report["journeyKillEvents"] = journeyKills;
            report["ownedFileSHA256Before"] = new Dictionary<string, string> { { deletePath, Hash(deletePath) }, { heldPath, heldBefore } };
            report["startedUtc"] = DateTime.UtcNow.ToString("o"); report["batchId"] = batch.Id; report["frozenCandidates"] = identities.Select(file => file.Evidence()).ToArray();
            report["countersBefore"] = Counters(Game.Battle.Progress, Game.Cultivation);
            report["checks"] = checks; report["nativeInputs"] = inputs; report["uiSetup"] = setup; report["states"] = states; report["glyphChecks"] = glyphs; report["cleanInvocations"] = cleanInvocations;
            setup.Add(new { action = "Explicit source fixture", method = "private-root real Scan -> AdventureMonster.Create -> production Spawn/bounds -> one initial source position; normal host global census remains read-only", targetId = target.Id, target.MonsterKind, target.Health, target.MaxHealth, target.X, target.Y });
            WriteReport(null, false); WriteProgress(); timer.Tick += Tick; timer.Start(); next = .5;
        }

        Task<CleanupResult> CleanOwned(CleanupBatch batch, CancellationToken token)
        {
            cleanCalls++;
            bool exact = batch != null && batch.Id == originalBatch.Id && batch.Candidates.Count == identities.Length && identities.Length == 2
                && batch.Candidates.Select(file => Path.GetFullPath(file.Path)).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2
                && batch.Candidates.All(file => Path.GetFullPath(file.Path).StartsWith(ownedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && identities.Any(expected => expected.Matches(file)))
                && String.Equals(Path.GetFullPath(service.RootPath), ownedRoot, StringComparison.OrdinalIgnoreCase)
                && held != null && held.CanRead && acceptedCleanCalls == 0 && Game.Battle.Enemies.Contains(target)
                && Object.ReferenceEquals(Field<AdventureEnemy>(Game, "activeCleanupEnemy"), target) && target.Authorized
                && target.FellInCombat && target.Health == 0 && target.Cleaning && target.Authorization == AdventureAuthorization.Manual && target.CleanupStarted
                && Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance && !Game.Battle.Automatic;
            cleanInvocations.Add(new { seconds = wall.Elapsed.TotalSeconds, batchId = batch == null ? null : batch.Id, candidateCount = batch == null ? 0 : batch.FilesCount,
                exactAllowlistAccepted = exact, candidatePathsDisclosed = "Only the frozen owned paths are reported; unrelated batch paths are never serialized." });
            if (!exact) { rejectedCleanCalls++; WriteReport("Mutation gate rejected an unexpected or changed batch before any Clean call.", false); throw new InvalidOperationException("Closed native fixture allowlist rejected a batch; no cleanup fallback."); }
            acceptedCleanCalls++; token.ThrowIfCancellationRequested();
            actualResult = service.Clean(batch, token); // Sole real mutation in the runner.
            report["actualCleanupResult"] = new { actualResult.DeletedFiles, actualResult.SkippedFiles, actualResult.FreedBytes, actualResult.Cancelled, actualResult.Message,
                remainingOwnedPaths = actualResult.RemainingCandidates.Select(file => file.Path).ToArray() };
            WriteReport(null, false); return Task.FromResult(actualResult);
        }

        void Next(Stage value, double delay = .2)
        { stage = value; next = wall.Elapsed.TotalSeconds + delay; WriteProgress(); Console.WriteLine("STAGE " + value + " " + wall.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)); }
        void UnattendedOff()
        {
            Require(Object.ReferenceEquals(Game,originalController) && Object.ReferenceEquals(Game.Cultivation,originalCultivation)
                && Field<int>(Game,"desktopAcceptanceSpeed")==1 && Game.DesktopObservesSystem
                && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance
                && Field<Task<MemoryReclaimResult>>(Game, "memoryReclaimTask") == null && !Game.DesktopProcessBusy && Field<object>(Game, "desktopProcessTask") == null,
                "Same x1 observing controller required; memory/process/unattended maintenance must remain off.");
            if(Game.Cultivation.Mortal) Require(!Game.Battle.Automatic,"Mortal combat must remain manual until actual native awakening.");
            if(stage<=Stage.ReadBack)
                foreach (AdventureEnemy enemy in Game.Battle.Enemies)
                    if (!Object.ReferenceEquals(enemy, target)) Require(!enemy.Authorized && !enemy.CleanupStarted && !enemy.FellInCombat, "An unrelated actor acquired authority during the single owned maintenance fight.");
            if(stage>=Stage.ReadBack)
                Require(!Game.MaintenanceConsentCleanup && Field<Task<CleanupResult>>(Game,"cleanupTask")==null
                    && cleanCalls==1 && acceptedCleanCalls==1 && rejectedCleanCalls==0,
                    "After native persisted revocation all later combat is read-only maintenance and the closed cleaner remains at exactly one call.");
        }
        void BatchUnchanged()
        { Require(target.Id == originalBatch.Id && target.Batch != null && target.Batch.Id == originalBatch.Id && target.Batch.FilesCount == 2 && target.Batch.Candidates.All(file => identities.Any(expected => expected.Matches(file))), "Read-only ecology changed the frozen owned batch; refuse to repair or forge it."); }
        static void Reveal(Control control)
        { for (Control parent = control.Parent; parent != null; parent = parent.Parent) { var scroll = parent as ScrollableControl; if (scroll != null) scroll.ScrollControlIntoView(control); } control.FindForm().PerformLayout(); control.FindForm().Refresh(); }
        bool Presented(Control control, string role)
        {
            Reveal(control); bool ok;
            if (!(control is TextBox)) ok = (bool)Native("NativePreviewContentMatches", control, true);
            else
            {
                // Native EDIT's PointToScreen(0,0) is its client origin. Match
                // DrawToBitmap's complete bordered window at the parent origin;
                // sample count and colour/match thresholds are unchanged.
                int samples = 0, matches = 0; Point origin = control.Parent.PointToScreen(control.Location);
                using (var expected = new Bitmap(control.Width, control.Height))
                using (var actual = DesktopFrameCapture.CaptureRegion(new Rectangle(origin, control.Size), true))
                {
                    control.DrawToBitmap(expected, new Rectangle(Point.Empty, control.Size));
                    for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
                    {
                        Color source = expected.GetPixel(x, y), key = control.ForeColor;
                        if (Math.Abs(source.R - key.R) + Math.Abs(source.G - key.G) + Math.Abs(source.B - key.B) > 25) continue;
                        Color pixel = actual.GetPixel(x, y); samples++;
                        if (Math.Abs(pixel.R - source.R) + Math.Abs(pixel.G - source.G) + Math.Abs(pixel.B - source.B) < 80) matches++;
                    }
                }
                ok = samples >= 60 && matches >= samples * .8; report["lastEditGlyphProbe"] = new { role, samples, matches, presented = ok, origin = origin.ToString() };
            }
            if (ok) glyphs.Add(new { stage = stage.ToString(), role, verified = "actual desktop glyphs: >=60 samples, >=80% match; source-key <=25, pixel difference <80" });
            return ok;
        }
        Dictionary<string, object> RootProbe(Rectangle region, IntPtr expected)
        {
            var samples = new List<object>(); bool matched = true;
            int insetX = Math.Min(8, Math.Max(0, region.Width / 4)), insetY = Math.Min(8, Math.Max(0, region.Height / 4));
            for (int y = region.Top + insetY; y < region.Bottom - insetY; y += Math.Max(30, region.Height / 8))
                for (int x = region.Left + insetX; x < region.Right - insetX; x += Math.Max(30, region.Width / 6))
                { Point point = new Point(x, y); IntPtr root = (IntPtr)Native("RootAt", point); matched &= root == expected; samples.Add(new { point = point.ToString(), root = root.ToInt64() }); }
            var probe = new Dictionary<string, object> { { "region", region.ToString() }, { "expectedRoot", expected.ToInt64() }, { "matched", matched && samples.Count > 0 }, { "samples", samples.ToArray() } };
            report["lastRootProbe"] = probe; return probe;
        }
        void Capture(Form panel, string name, string purpose)
        {
            lastPanel = panel; if (captured.Contains(name)) return;
            Require(panel.Visible && panel.WindowState == FormWindowState.Normal && SystemInformation.VirtualScreen.Contains(panel.Bounds)
                && (IntPtr)Native("GetForegroundWindow") == panel.Handle, "A passed panel must be the visible own foreground window inside the desktop: " + name);
            Dictionary<string, object> roots = RootProbe(panel.RectangleToScreen(panel.ClientRectangle), panel.Handle);
            Require((bool)roots["matched"], "The own active panel is occluded; refuse a passed capture: " + name);
            Native("CapturePanel", panel, name);
            var captures = Field<List<object>>(native, "captures"); var metadata = (Dictionary<string, object>)captures[captures.Count - 1];
            metadata["purpose"] = purpose + "; raw local pixels; " + Scope; metadata["nativeRootVerification"] = roots; captured.Add(name);
        }
        bool Click(Control control, string name)
        {
            Require(wall.Elapsed.TotalSeconds < 475 && control.Visible && control.Enabled, "Native input requires an enabled visible control before the input deadline."); Reveal(control);
            Point point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2)); Form panel = control.FindForm();
            if ((IntPtr)Native("RootAt", point) != panel.Handle || WindowFromPoint(point) != control.Handle) return false;
            if(name!="06-native-file-revocation-button") Capture(panel, name, "Verified actual native button before its UI handler");
            if (!(bool)Native("ClickControl", control)) return false;
            inputs.Add(new { seconds = wall.Elapsed.TotalSeconds, stage = stage.ToString(), kind = "production-control", control.Name, control.Text, point = point.ToString(), expectedRoot = panel.Handle.ToInt64(), expectedLeaf = control.Handle.ToInt64() }); return true;
        }
        bool ClickOwnActor(bool hero)
        {
            Require(wall.Elapsed.TotalSeconds < 475, "Actor input deadline expired."); BatchUnchanged();
            Require(Game.Battle.Enemies.Contains(target) && !target.Defeated && !target.Cleaning && !target.VisualDefeated && !Game.Battle.Progress.Dead, "Refuse actor input without the living exact owned target.");
            if (hero) Require(Game.Battle.Target == target, "A block requires the exact owned combat target.");
            KnightLayeredWindow window;
            if (hero) window = Game.DesktopHeroWindow;
            else if (!Game.DesktopEnemyWindows.TryGetValue(target.Id, out window)) return false;
            if (!window.Visible) return false;
            bool clicked;
            try { clicked = (bool)Native("ClickActor", window); }
            catch (TargetInvocationException error)
            { if (error.InnerException is InvalidOperationException && error.InnerException.Message.StartsWith("找不到未被遮挡的", StringComparison.Ordinal)) return false; throw; }
            if (!clicked) return false;
            if (hero) blockClicks++; else attackClicks++;
            inputs.Add(new { seconds = wall.Elapsed.TotalSeconds, stage = stage.ToString(), kind = hero ? "native-hero-block" : "native-owned-enemy-manual-strike", exactEnemyId = target.Id,
                expectedRoot = window.Handle.ToInt64(), point = Cursor.Position.ToString(), actualRoot = ((IntPtr)Native("RootAt", Cursor.Position)).ToInt64(), target.Health, target.MaxHealth, cooldown = Game.Battle.Cooldown });
            return true;
        }
        void CaptureOwnedCombat()
        {
            const string name = "04-owned-manual-target-and-lock";
            // This callback already runs synchronously on the UI thread. Do
            // not use Pause/ResumeArtDemoCapture: its Resume resets time marks.
            // Snapshot, background proof and restore do not pump the game loop.
            List<ArtDemoActorFrame> actors = Game.SnapshotArtDemoActors();
            Bitmap actual = null, background = null, expected = null;
            try
            {
                KnightLayeredWindow own; Require(Game.DesktopEnemyWindows.TryGetValue(target.Id, out own), "The exact owned actor window is missing.");
                ArtDemoActorFrame hero = actors.Single(actor => actor.Window == Game.DesktopHeroWindow);
                ArtDemoActorFrame enemy = actors.Single(actor => actor.Window == own && actor.Role == "enemy");
                Point ownPixel = (Point)Native("FindPixel", own, enemy.Frame, true, default(IntPtr));
                Require((IntPtr)Native("RootAt", ownPixel) == own.Handle, "The exact owned actor lacks a safe visible native hit point.");
                Rectangle bounds = Rectangle.Union(hero.Bounds, enemy.Bounds); bounds.Inflate(28, 28); bounds = Rectangle.Intersect(bounds, SystemInformation.VirtualScreen);
                actual = DesktopFrameCapture.CaptureRegion(bounds, true); actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                try { foreach (ArtDemoActorFrame actor in actors) actor.Window.Hide(); background = DesktopFrameCapture.CaptureRegion(bounds, false); }
                finally { foreach (ArtDemoActorFrame actor in actors.OrderByDescending(actor => actor.ZOrder)) if (!actor.Window.IsDisposed) actor.Window.Show(); }
                expected = ArtDemoPixelVerifier.ExpectedComposite(background, actors, bounds.Location);
                object heroProof = ArtDemoPixelVerifier.VerifyActor(hero, actors, actual, background, expected, bounds.Location);
                object enemyProof = ArtDemoPixelVerifier.VerifyActor(enemy, actors, actual, background, expected, bounds.Location);
                Field<List<object>>(native, "captures").Add(new Dictionary<string, object> { { "image", name + ".png" }, { "bounds", bounds.ToString() },
                    { "exactEnemyId", target.Id }, { "exactEnemyHandle", own.Handle.ToInt64() }, { "actors", new[] { heroProof, enemyProof } },
                    { "purpose", "Exact owned opponent + hero actual native composite, unchanged 90%/RGB45 verifier; only diagnostic background Hide/Show, no timer/time edit; raw local source fixture" } });
                captured.Add(name);
            }
            finally { if (actual != null) actual.Dispose(); if (background != null) background.Dispose(); if (expected != null) expected.Dispose(); foreach (ArtDemoActorFrame actor in actors) actor.Dispose(); }
        }
        bool ConfirmYes()
        {
            IntPtr dialog = (IntPtr)Native("OwnDialog", "真实电脑维护"); if (dialog == IntPtr.Zero) return false;
            Require(wall.Elapsed.TotalSeconds < 475, "Dialog input deadline expired.");
            var yes = new List<IntPtr>();
            EnumChildWindows(dialog, delegate(IntPtr child, IntPtr ignored)
            {
                var text = new StringBuilder(256); var name = new StringBuilder(64); GetWindowText(child, text, text.Capacity); GetClassName(child, name, name.Capacity);
                string caption = new String(text.ToString().Where(c => c != '&' && !Char.IsWhiteSpace(c)).ToArray());
                bool meaning = caption == "是" || caption == "是(Y)" || caption.Equals("Yes", StringComparison.OrdinalIgnoreCase) || caption.Equals("Yes(Y)", StringComparison.OrdinalIgnoreCase);
                if (name.ToString().Equals("Button", StringComparison.OrdinalIgnoreCase) && GetDlgCtrlID(child) == 6 && meaning && IsWindowVisible(child) && IsWindowEnabled(child)) yes.Add(child); return true;
            }, IntPtr.Zero);
            Require(yes.Count <= 1, "Refuse an ambiguous own Yes confirmation."); if (yes.Count == 0) return false;
            NativeRect bounds, button; if (!GetWindowRect(dialog, out bounds) || !GetWindowRect(yes[0], out button)) return false;
            Rectangle rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), interior = rectangle; interior.Inflate(-12, -12);
            Dictionary<string, object> roots = RootProbe(interior, dialog);
            if (!(bool)roots["matched"] || (IntPtr)Native("GetForegroundWindow") != dialog) return false;
            Point point = new Point((button.Left + button.Right) / 2, (button.Top + button.Bottom) / 2);
            if (WindowFromPoint(point) != yes[0]) return false;
            if (captured.Add("02-own-file-consent-confirmation"))
            {
                using (Bitmap image = DesktopFrameCapture.CaptureRegion(rectangle, true)) image.Save(Path.Combine(output, "02-own-file-consent-confirmation.png"), ImageFormat.Png);
                Field<List<object>>(native, "captures").Add(new Dictionary<string, object> { { "image", "02-own-file-consent-confirmation.png" }, { "bounds", rectangle.ToString() }, { "nativeRootVerification", roots }, { "purpose", "Own-PID actual file scope Yes/No dialog, native unique IDYES+caption+leaf verification; raw local only" } });
            }
            if (!(bool)Native("NativeClick", point, dialog, "owned-file-consent-yes")) return false;
            dialogClicks++; inputs.Add(new { seconds = wall.Elapsed.TotalSeconds, kind = "own-PID-dialog-IDYES", expectedRoot = dialog.ToInt64(), expectedLeaf = yes[0].ToInt64(), point = point.ToString() }); return true;
        }
        DesktopEcologyPanel Ecology { get { return Application.OpenForms.OfType<DesktopEcologyPanel>().Single(form => form.Visible && !form.IsDisposed); } }
        Label Heading(DesktopEcologyPanel panel, string text)
        { return Descendants(panel).OfType<Label>().Single(label => label.Text == text); }

        void Tick(object sender, EventArgs args)
        {
            if (busy || finished) return; busy = true;
            try
            {
                if (wall.Elapsed.TotalSeconds >= 475) throw new TimeoutException("Absolute 475-second input deadline reached; parent has a 480-second own-child deadline. This is an eight-minute explicit journey, not the paused long test.");
                Require(terminalFailure==null,"A previous terminal validation failure is retained: "+terminalFailure);
                UnattendedOff();
                Native("RecordMovie",Field<Stopwatch>(native,"wall").Elapsed.TotalSeconds);
                if(wall.Elapsed.TotalSeconds>=nextProgress){WriteProgress();nextProgress=wall.Elapsed.TotalSeconds+1;}
                if (target.Health < lastHealth) { damageObserved += lastHealth - target.Health; states.Add(new { seconds = wall.Elapsed.TotalSeconds, target.Health, target.MaxHealth, actualManualDamage = lastHealth - target.Health }); lastHealth = target.Health; }
                if (wall.Elapsed.TotalSeconds < next) return;
                switch (stage)
                {
                    case Stage.OpenEcology:
                        session.ClosePanels(); session.OpenPanel("ecology"); setup.Add(new { action = "Open existing ecology panel and dismiss first-run welcome", method = "existing session navigation, UI setup only" }); Next(Stage.ReadOff, .5); break;
                    case Stage.ReadOff:
                        BatchUnchanged(); Ecology.UpdateState();
                        if (!Presented(Heading(Ecology, "电脑负担化为妖物，处理结果如实记录"), "ecology heading before consent")) break;
                        Check(!Game.MaintenanceConsentCleanup && File.Exists(deletePath) && File.Exists(heldPath) && cleanCalls == 0, "Before native file consent, both owned files exist and no cleanup has run");
                        Next(Stage.Grant); break;
                    case Stage.Grant:
                        if (Click(Field<Button>(Ecology, "files"), "01-file-consent-off")) Next(Stage.ConfirmGrant, .1); break;
                    case Stage.ConfirmGrant:
                        if (ConfirmYes()) Next(Stage.ReadGranted, .3); break;
                    case Stage.ReadGranted:
                        if ((IntPtr)Native("OwnDialog", "真实电脑维护") != IntPtr.Zero) break;
                        Ecology.UpdateState(); if (!Game.MaintenanceConsentCleanup || !Presented(Field<Button>(Ecology, "files"), "file scope enabled button")) break;
                        MaintenanceConsentStore granted = MaintenanceConsentStore.Load(Path.Combine(session.ProfileRoot, "maintenance-consent.txt"));
                        Check(granted.CleanupEnabled && !granted.MemoryEnabled && !granted.AutoMaintenanceEnabled && cleanCalls == 0, "Only actual native Yes enables the private persisted file grant; no cleanup occurs at authorization");
                        Capture(Ecology, "03-file-consent-on", "Actual file scope enabled, other scopes remain off");
                        session.ClosePanels(); setup.Add(new { action = "Dismiss ecology to expose real desktop actors", method = "existing ClosePanels, before combat only" }); Next(Stage.SelectOwn, .3); break;
                    case Stage.SelectOwn:
                        BatchUnchanged(); if (ClickOwnActor(false)) Next(Stage.LockSelected, .1); break;
                    case Stage.LockSelected:
                        if (Game.Battle.Target != target) break; BatchUnchanged();
                        Check(target.Authorization == AdventureAuthorization.Manual && target.Authorized && target.Health > 0 && !target.FellInCombat, "First native exact-actor click selects a living full-rule manual opponent");
                        // Hold only after real selection: normal observation must
                        // not refresh an unauthorised batch around a locked file.
                        held = new FileStream(heldPath, FileMode.Open, FileAccess.Read, FileShare.None);
                        report["heldLock"] = new { openedSeconds = wall.Elapsed.TotalSeconds, share = "None", targetSelectedBeforeLock = true, target.Health, heldSHA256Before = heldBefore };
                        CaptureOwnedCombat(); Next(Stage.OpenOwnSource, .1); break;
                    case Stage.OpenOwnSource:
                        session.OpenPanel("ecology"); setup.Add(new { action = "Read exact selected owned batch in the existing source detail panel", method = "existing ecology navigation; no target, authority or file state is injected" }); Next(Stage.ReadOwnSource, .3); break;
                    case Stage.ReadOwnSource:
                        BatchUnchanged(); Require(Game.Battle.Target == target && target.Authorization == AdventureAuthorization.Manual && target.Health > 0, "Source inspection must retain the exact living manual target.");
                        Ecology.UpdateState(); TextBox source = Field<TextBox>(Ecology, "targetSource");
                        Require(source.Text.Contains(deletePath) && source.Text.Contains(heldPath) && source.Text.Contains("击败后尝试清理绑定文件"), "The actual selected-source UI must name both owned paths and the permitted attempted action.");
                        if (!Presented(source, "actual exact two-file source detail EDIT")) break;
                        report["selectedOwnedSourceText"] = source.Text;
                        Capture(Ecology, "04b-exact-owned-source-and-proposed-action", "Actual selected target detail names only the owned two-file batch and truthful attempted maintenance action");
                        session.ClosePanels(); setup.Add(new { action = "Return from source inspection to the same selected living target", method = "existing ClosePanels; no target or position changes" }); Next(Stage.Fight, .2); break;
                    case Stage.Fight:
                        BatchUnchanged();
                        if (target.FellInCombat) { Next(Stage.AwaitResult, .1); break; }
                        Require(!Game.Battle.Progress.Dead && Game.Battle.Target == target, "Manual combat lost the exact target or died; do not fake recovery or retarget another actor.");
                        if (Game.Battle.Cooldown <= 0) { if (ClickOwnActor(false)) next = wall.Elapsed.TotalSeconds + .1; }
                        else
                        {
                            double now = Field<Stopwatch>(Game, "clock").Elapsed.TotalSeconds;
                            if (target.AttackTime > AdventureMonster.Find(target.MonsterKind).AttackCooldown - .45 && now >= Field<double>(Game, "blockCooldownUntil"))
                                if (ClickOwnActor(true)) next = wall.Elapsed.TotalSeconds + .1;
                        }
                        break;
                    case Stage.AwaitResult:
                        if (Field<Task<CleanupResult>>(Game, "cleanupTask") != null || !Game.ReadDesktopPlayerView().MaintenanceResults.Any(entry => entry.Kind == "Files")) break;
                        Check(target.FellInCombat && target.Health == 0 && attackClicks >= 2 && damageObserved > 0, "The owned opponent fell through repeated actual native manual strikes, without HP/cooldown/time edits");
                        Check(cleanCalls == 1 && acceptedCleanCalls == 1 && rejectedCleanCalls == 0 && actualResult != null
                            && actualResult.DeletedFiles == 1 && actualResult.SkippedFiles == 1 && actualResult.FreedBytes == 4096 && !actualResult.Cancelled
                            && actualResult.RemainingCandidates.Count == 1 && actualResult.RemainingCandidates[0].Path == heldPath,
                            "Exactly one allowlisted real Clean deleted 4096 bytes and skipped the actually locked 512-byte file");
                        Check(!File.Exists(deletePath) && File.Exists(heldPath) && Game.Battle.Progress.FreedBytes == initialFreed + 4096
                            && target.KillExperience >= 2 && target.KillExperience <= 3 && Game.Battle.Progress.Score == initialScore + target.KillExperience * 5
                            && Game.Battle.Progress.Experience == initialExperience + target.KillExperience && Game.Cultivation.Mortal && Game.Cultivation.MortalKills == initialKills + 1
                            && target.CultivationRewardQi > 0 && Game.Cultivation.Qi == initialQi
                            && Game.Cultivation.DeferredQi == initialDeferredQi + target.CultivationRewardQi && Game.Cultivation.LifetimeQi == initialLifetimeQi + target.CultivationRewardQi,
                            "Actual disk result and one mortal kill agree; combat experience/score settle, while the real cultivation reward is deferred until awakening and Qi remains unchanged");
                        held.Dispose(); held = null; Check(Hash(heldPath) == heldBefore, "The skipped owned file retains its exact original SHA256 after releasing the exclusive lock");
                        report["heldSHA256After"] = Hash(heldPath); report["deletedFileAbsent"] = !File.Exists(deletePath); Next(Stage.OpenResults); break;
                    case Stage.OpenResults:
                        session.OpenPanel("ecology"); setup.Add(new { action = "Reopen existing ecology results", method = "existing session navigation after real maintenance completion" }); Next(Stage.ReadResults, .5); break;
                    case Stage.ReadResults:
                        Ecology.UpdateState(); TextBox results = Field<TextBox>(Ecology, "results");
                        Require(results.Text.Contains("已删除 1 个临时文件") && results.Text.Contains("跳过 1 个") && results.Text.Contains("4.0 KiB"), "The production UI must report the actual one-delete/one-skip result and 4096-byte total.");
                        if (!Presented(Heading(Ecology, "实际维护记录"), "actual maintenance heading") || !Presented(results, "actual deletion/skip result EDIT")) break;
                        Capture(Ecology, "05-actual-one-delete-one-locked-skip", "Production result text and actual deleted/skipped counters after natural manual kill"); Next(Stage.Revoke); break;
                    case Stage.Revoke:
                        if (Click(Field<Button>(Ecology, "files"), "06-native-file-revocation-button")) Next(Stage.ReadRevoked, .3); break;
                    case Stage.ReadRevoked:
                        Ecology.UpdateState(); if (Game.MaintenanceConsentCleanup) break;
                        if (!Presented(Field<Button>(Ecology, "files"), "file scope disabled after native revoke")) break;
                        MaintenanceConsentStore revoked = MaintenanceConsentStore.Load(Path.Combine(session.ProfileRoot, "maintenance-consent.txt"));
                        Check(!revoked.CleanupEnabled && !revoked.MemoryEnabled && !revoked.AutoMaintenanceEnabled
                            && !Field<bool>(Game, "consentRetryCleanup"), "Native file-scope revoke is immediately off and durably persisted with no retry pending");
                        Capture(Ecology, "07-file-scope-revoked", "Actual UI reflects persisted native file authorization revocation"); Next(Stage.ReadBack); break;
                    case Stage.ReadBack:
                        if (!readBackVerified)
                        {
                            Check(Game.DesktopSaveNow() && Game.DesktopArchivePaths.All(File.Exists), "The actual controller saves all five archives only in the new private profile");
                            VerifyReadBack(); readBackVerified = true;
                        }
                        Ecology.UpdateState(); TextBox finalResults = Field<TextBox>(Ecology, "results");
                        if (!Presented(finalResults, "maintenance result remains after native revoke and private reload")) break;
                        report["maintenancePhaseComplete"] = true;
                        report["maintenancePhaseCounters"] = Counters(Game.Battle.Progress,Game.Cultivation);
                        Check(Game.Cultivation.MortalKills==1 && Game.Cultivation.Mortal && !Game.Battle.Automatic,"The first owned maintenance kill is the first real kill of this uninterrupted mortal journey");
                        lastJourneyKills=Game.Cultivation.MortalKills;
                        session.ClosePanels(); Native("BeginMovie","ecology-mortal-manual",7d); Next(Stage.JourneyFight,.3); break;
                    case Stage.JourneyFight:
                        ContinueManualJourney(); break;
                    case Stage.OpenAwaken:
                        session.OpenPanel("practice"); Next(Stage.ReadAwaken,.4); break;
                    case Stage.ReadAwaken:
                        DesktopPracticePanel handbook=Application.OpenForms.OfType<DesktopPracticePanel>().Single(p=>p.Visible&&!p.IsDisposed);
                        handbook.UpdateState(); Button awaken=Field<Button>(handbook,"awaken");
                        Require(Game.Cultivation.CanAwaken && Game.Cultivation.MortalKills==10 && awaken.Visible && awaken.Enabled,"Ten actual mortal settlements must expose the real active awakening control.");
                        if(Click(awaken,"09-same-mortal-ten-kills-native-awakening")) Next(Stage.AwaitAwaken,.5); break;
                    case Stage.AwaitAwaken:
                        Check(!Game.Cultivation.Mortal && Game.Cultivation.MortalKills==10 && Game.ArtDemoHeroStage=="qishi"
                            && Game.Cultivation.AutoBattleUnlocked && Game.Cultivation.AutoBattle,
                            "The same unseeded mortal actively awakens through native input into qishi with the default automatic combat preference enabled");
                        Check(journeyKills.Count==9 && manualJourneyClicks>=9 && cleanCalls==1,"Nine additional actual manual settlements follow the one owned maintenance kill without another cleaner call");
                        report["awakenedIdentity"]=new{Game.Cultivation.DaoTitle,Game.Cultivation.SpiritRoot,Game.Cultivation.RealmIndex,Game.Cultivation.Layer,Game.ArtDemoHeroStage};
                        session.ClosePanels(); autoInputCount=InputCount; ResetAutomaticWindow("native-awakening");
                        Next(Stage.ObserveAutomatic,.2); break;
                    case Stage.ObserveAutomatic:
                        ObserveAutomaticJourney(); break;
                    case Stage.PanelsClosed:
                        bool closedResumed;
                        if(!AutomaticObservationReady(closedInputCount,out closedResumed))break;
                        if(closedResumed){ResetAutomaticWindow("natural-resume-after-closed-panel-rest");Next(Stage.ObserveAutomatic,.2);break;}
                        if(wall.Elapsed.TotalSeconds-closedStarted<12)break;
                        if(Game.Cultivation.LifetimeQi<=closedLifetimeQi)break;
                        Check(!Application.OpenForms.Cast<Form>().Any(p=>p.Visible&&!(p is KnightLayeredWindow))
                            && Game.DesktopHeroWindow.Visible && !Game.Battle.Progress.Dead && Game.Battle.Automatic && Game.Cultivation.LifetimeQi>closedLifetimeQi,
                            "With all panels closed, the same living active automatic qishi cultivates for at least twelve more seconds with zero inputs; rest cannot satisfy this interval");
                        CaptureJourneyActors("11-panels-closed-still-cultivating",false); Next(Stage.FinalReadBack,.1);break;
                    case Stage.FinalReadBack:
                        bool finalResumed;
                        if(!AutomaticObservationReady(closedInputCount,out finalResumed))break;
                        if(finalResumed){ResetAutomaticWindow("natural-resume-before-final-readback");Next(Stage.ObserveAutomatic,.2);break;}
                        VerifyJourneyFinal(); Finish(null); break;
                }
            }
            catch (Exception error) { Finish(error.ToString()); }
            finally { busy = false; }
        }

        int InputCount { get { return inputs.Count; } }
        bool OrdinaryCandidate(AdventureEnemy enemy)
        {
            return enemy!=null && !enemy.IsBoss && enemy.ProcessTarget==null && !enemy.NoFieldClear && !enemy.IsDiskWarning
                && !enemy.Defeated && !enemy.VisualDefeated && !enemy.Cleaning && !enemy.CleanupStarted && !enemy.Failed && enemy.Health>0;
        }
        bool ClickJourneyActor(AdventureEnemy enemy,bool hero)
        {
            Require(wall.Elapsed.TotalSeconds<475 && Game.Cultivation.Mortal && !Game.Battle.Automatic && !Game.Battle.Progress.Dead
                && OrdinaryCandidate(enemy) && Game.Battle.Enemies.Contains(enemy),"Journey native input requires a live ordinary manual target in this same mortal runtime.");
            KnightLayeredWindow window;
            if(hero){if(Game.Battle.Target!=enemy)return false;window=Game.DesktopHeroWindow;}
            else if(!Game.DesktopEnemyWindows.TryGetValue(enemy.Id,out window))return false;
            if(!window.Visible)return false;
            try{if(!(bool)Native("ClickActor",window))return false;}
            catch(TargetInvocationException error)
            {if(error.InnerException is InvalidOperationException && error.InnerException.Message.StartsWith("找不到未被遮挡的",StringComparison.Ordinal))return false;throw;}
            if(hero)manualJourneyBlocks++;else manualJourneyClicks++;
            inputs.Add(new{seconds=wall.Elapsed.TotalSeconds,stage=stage.ToString(),kind=hero?"native-journey-hero-block":"native-journey-manual-enemy",enemy.Id,enemy.IsVirtual,
                expectedRoot=window.Handle.ToInt64(),actualRoot=((IntPtr)Native("RootAt",Cursor.Position)).ToInt64(),point=Cursor.Position.ToString(),enemy.Health,enemy.MaxHealth,cooldown=Game.Battle.Cooldown});
            return true;
        }
        void ContinueManualJourney()
        {
            int kills=Game.Cultivation.MortalKills;
            if(kills!=lastJourneyKills)
            {
                Require(kills==lastJourneyKills+1 && journeyTarget!=null && journeyTarget.FellInCombat && journeyTarget.Health==0 && journeyTarget.KillRewarded,
                    "Each additional mortal kill must correspond to exactly one actual native-fought target settlement.");
                journeyKills.Add(new{seconds=wall.Elapsed.TotalSeconds,mortalKills=kills,journeyTarget.Id,journeyTarget.IsVirtual,journeyTarget.KillExperience,
                    journeyTarget.CultivationRewardQi,journeyTarget.KillRewarded,journeyTarget.CultivationRewarded,manualJourneyClicks,manualJourneyBlocks,
                    controllerIdentity=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Game),profile=session.ProfileRoot});
                lastJourneyKills=kills;journeyTarget=null;WriteReport(null,false);WriteProgress();
            }
            if(Game.Battle.Progress.Dead)
            {
                if(!resting){resting=true;naturalRests++;states.Add(new{seconds=wall.Elapsed.TotalSeconds,eventName="natural-rest-start",mortalKills=kills,Game.Battle.Progress.Health});WriteProgress();}
                next=wall.Elapsed.TotalSeconds+.3;return;
            }
            if(resting)
            {
                Check(!Game.AutoPlanning && Game.DesktopAcceptanceDeathTime<1,"A naturally exhausted mortal recovered through the unchanged rest timer and remains manual");
                resting=false;states.Add(new{seconds=wall.Elapsed.TotalSeconds,eventName="natural-rest-complete",mortalKills=kills,Game.Battle.Progress.Health});
            }
            if(Game.Cultivation.CanAwaken)
            {
                Check(kills==10 && journeyKills.Count==9 && Game.Cultivation.Mortal && !Game.AutoPlanning,
                    "The same uninterrupted x1 new profile reaches ten real mortal kills while awakening remains a player action");
                Next(Stage.OpenAwaken,.1);return;
            }
            Require(kills<10 && Game.Cultivation.Mortal,"Mortal journey cannot skip or exceed the real ten-kill awakening threshold.");
            if(!OrdinaryCandidate(journeyTarget) || !Game.Battle.Enemies.Contains(journeyTarget))
            {
                journeyTarget=Game.Battle.Enemies.Where(OrdinaryCandidate).OrderBy(e=>e.IsVirtual?0:1)
                    .ThenBy(e=>(double)Call(Game,"EnemyHeroDistance",e)).FirstOrDefault();
                if(journeyTarget==null){next=wall.Elapsed.TotalSeconds+.3;return;}
                states.Add(new{seconds=wall.Elapsed.TotalSeconds,eventName="journey-target-observed",journeyTarget.Id,journeyTarget.IsVirtual,journeyTarget.Health,journeyTarget.MaxHealth,
                    source="Existing production ordinary target; all maintenance scopes off; no source paths serialized, no injected encounter or position change"});
            }
            if(Game.Battle.Target!=journeyTarget)
            {
                if(wall.Elapsed.TotalSeconds-lastJourneyStrike>=.75 && ClickJourneyActor(journeyTarget,false))lastJourneyStrike=wall.Elapsed.TotalSeconds;
                next=wall.Elapsed.TotalSeconds+.1;return;
            }
            DesktopPlayerView view=Game.ReadDesktopPlayerView();
            if(view.BlockReady && view.BlockText.Contains("即将出手"))
            {if(ClickJourneyActor(journeyTarget,true))next=wall.Elapsed.TotalSeconds+.1;return;}
            if(Game.Battle.Cooldown<=0 && wall.Elapsed.TotalSeconds-lastJourneyStrike>=.75)
                if(ClickJourneyActor(journeyTarget,false))lastJourneyStrike=wall.Elapsed.TotalSeconds;
            next=wall.Elapsed.TotalSeconds+.1;
        }
        void ResetAutomaticWindow(string reason)
        {
            automaticWindow++;autoScore=Game.Battle.Progress.Score;autoLifetimeQi=Game.Cultivation.LifetimeQi;
            previousPassiveScore=autoScore;previousPassiveQi=autoLifetimeQi;autoStarted=wall.Elapsed.TotalSeconds;nextAutomaticSample=autoStarted+1;
            automaticKillObserved=false;passiveCultivationObserved=false;
            states.Add(new{seconds=autoStarted,eventName="automatic-evidence-window-start",reason,automaticWindow,inputs=InputCount,score=autoScore,lifetimeQi=autoLifetimeQi,
                note="Observation baselines and proof flags only; total journey wall time and all game values/timers remain untouched"});
            report["automaticStart"]=new{seconds=autoStarted,inputs=autoInputCount,score=autoScore,lifetimeQi=autoLifetimeQi,automaticWindow,reason};
            WriteProgress();
        }
        bool AutomaticObservationReady(int expectedInputs,out bool resumed)
        {
            resumed=false;
            Require(Object.ReferenceEquals(Game,originalController) && Object.ReferenceEquals(Game.Cultivation,originalCultivation)
                && !Game.Cultivation.Mortal && Game.Cultivation.AutoBattleUnlocked && Game.Cultivation.AutoBattle
                && InputCount==expectedInputs && expectedInputs==autoInputCount,
                "Automatic observation requires the same awakened controller, unchanged enabled preference and zero new native inputs, including during natural rest.");
            if(Game.Battle.Progress.Dead)
            {
                if(!automaticResting)
                {
                    automaticResting=true;automaticRests++;automaticRestStarted=wall.Elapsed.TotalSeconds;
                    states.Add(new{seconds=automaticRestStarted,eventName="automatic-natural-rest-start",stage=stage.ToString(),automaticRests,automaticWindow,
                        preference=Game.Cultivation.AutoBattle,activeAutomatic=Game.Battle.Automatic,inputs=InputCount,Game.Battle.Progress.Health,deathTime=Game.DesktopAcceptanceDeathTime,
                        claim="Natural rest only; no active-attack or successful automatic evidence is claimed while dead"});WriteProgress();
                }
                next=wall.Elapsed.TotalSeconds+.3;return false;
            }
            Require(Game.Battle.Automatic,"A living awakened cultivator with enabled automatic preference must resume active automatic combat through the production recovery path.");
            if(automaticResting)
            {
                Check(Game.DesktopAcceptanceDeathTime<1 && InputCount==autoInputCount,
                    "The same qishi naturally recovers and resumes active automatic combat with no native input; preference remains enabled");
                automaticResting=false;automaticResumes++;resumed=true;
                states.Add(new{seconds=wall.Elapsed.TotalSeconds,eventName="automatic-natural-resume",stage=stage.ToString(),automaticRests,automaticResumes,
                    elapsedRestSeconds=wall.Elapsed.TotalSeconds-automaticRestStarted,activeAutomatic=Game.Battle.Automatic,preference=Game.Cultivation.AutoBattle,
                    inputs=InputCount,Game.Battle.Progress.Health,deathTime=Game.DesktopAcceptanceDeathTime});WriteProgress();
            }
            if(!automaticMovieStarted || resumed)
            {
                Native("BeginMovie","ecology-qishi-automatic",8d);automaticMovieStarted=true;
                states.Add(new{seconds=wall.Elapsed.TotalSeconds,eventName="native-automatic-movie-start",afterNaturalResume=resumed,inputs=InputCount});
            }
            return true;
        }
        void ObserveAutomaticJourney()
        {
            bool resumed;
            if(!AutomaticObservationReady(autoInputCount,out resumed))return;
            if(resumed)ResetAutomaticWindow("natural-resume-during-automatic-observation");
            if(!captured.Contains("10-same-qishi-zero-input-automatic-start"))CaptureJourneyActors("10-same-qishi-zero-input-automatic-start",false);
            if(wall.Elapsed.TotalSeconds>=nextAutomaticSample)
            {
                int score=Game.Battle.Progress.Score;long qi=Game.Cultivation.LifetimeQi;
                bool passive=score==previousPassiveScore && qi>previousPassiveQi;
                passiveCultivationObserved|=passive;automaticKillObserved|=score>autoScore;
                states.Add(new{seconds=wall.Elapsed.TotalSeconds,eventName="zero-input-automatic-sample",automaticWindow,activeAutomatic=Game.Battle.Automatic,score,lifetimeQi=qi,
                    scoreDelta=score-previousPassiveScore,lifetimeQiDelta=qi-previousPassiveQi,passiveQiWithUnchangedScore=passive,inputs=InputCount,
                    Game.Battle.Progress.Health,Game.Battle.Progress.Dead});
                previousPassiveScore=score;previousPassiveQi=qi;nextAutomaticSample=wall.Elapsed.TotalSeconds+1;
            }
            if(!automaticKillObserved || !passiveCultivationObserved || Game.Cultivation.LifetimeQi<=autoLifetimeQi || wall.Elapsed.TotalSeconds-autoStarted<8)return;
            Check(automaticKillObserved && InputCount==autoInputCount,"The current living automatic observation window records an actual kill with zero native battle inputs; any preceding natural rest has ended");
            Check(passiveCultivationObserved,"Two consecutive zero-input samples show LifetimeQi growth while combat score is unchanged, proving continued cultivation separately from kill rewards");
            session.ClosePanels();closedInputCount=InputCount;closedLifetimeQi=Game.Cultivation.LifetimeQi;closedStarted=wall.Elapsed.TotalSeconds;
            Next(Stage.PanelsClosed,.2);
        }
        void CaptureJourneyActors(string name,bool requireEnemy)
        {
            if(captured.Contains(name))return;
            // Identical native-composite proof approach to the owned fight:
            // synchronous UI callback, no Pause/Resume/Advance or game-time edit.
            List<ArtDemoActorFrame> actors=Game.SnapshotArtDemoActors();
            Bitmap actual=null,background=null,expected=null;
            try
            {
                ArtDemoActorFrame hero=actors.Single(a=>a.Window==Game.DesktopHeroWindow);
                ArtDemoActorFrame enemy=actors.Where(a=>a.Role=="enemy").OrderBy(a=>AdventureBattle.Distance(hero.Bounds.Left,hero.Bounds.Top,a.Bounds.Left,a.Bounds.Top)).FirstOrDefault();
                Require(!requireEnemy || enemy!=null,"A required natural enemy is missing from the actual desktop capture.");
                Rectangle bounds=enemy==null?hero.Bounds:Rectangle.Union(hero.Bounds,enemy.Bounds);bounds.Inflate(28,28);bounds=Rectangle.Intersect(bounds,SystemInformation.VirtualScreen);
                actual=DesktopFrameCapture.CaptureRegion(bounds,true);actual.Save(Path.Combine(output,name+".png"),ImageFormat.Png);
                try{foreach(ArtDemoActorFrame actor in actors)actor.Window.Hide();background=DesktopFrameCapture.CaptureRegion(bounds,false);}
                finally{foreach(ArtDemoActorFrame actor in actors.OrderByDescending(a=>a.ZOrder))if(!actor.Window.IsDisposed)actor.Window.Show();}
                expected=ArtDemoPixelVerifier.ExpectedComposite(background,actors,bounds.Location);
                var proofs=new List<object>{ArtDemoPixelVerifier.VerifyActor(hero,actors,actual,background,expected,bounds.Location)};
                if(enemy!=null)proofs.Add(ArtDemoPixelVerifier.VerifyActor(enemy,actors,actual,background,expected,bounds.Location));
                Field<List<object>>(native,"captures").Add(new Dictionary<string,object>{{"image",name+".png"},{"bounds",bounds.ToString()},{"actors",proofs},
                    {"purpose","Same runtime native desktop pixels and independent actor/frame proof; unchanged verifier, no pause/resume, timer changes, relocation or gameplay input"}});
                captured.Add(name);
            }
            finally{if(actual!=null)actual.Dispose();if(background!=null)background.Dispose();if(expected!=null)expected.Dispose();foreach(ArtDemoActorFrame actor in actors)actor.Dispose();}
        }
        void VerifyJourneyFinal()
        {
            UnattendedOff();
            Check(Object.ReferenceEquals(Game,originalController) && Object.ReferenceEquals(Game.Cultivation,originalCultivation)
                && Game.Cultivation.MortalKills==10 && !Game.Cultivation.Mortal && Game.ArtDemoHeroStage=="qishi" && Game.Cultivation.AutoBattle && Game.AutoPlanning
                && !Game.Battle.Progress.Dead && !automaticResting && automaticRests==automaticResumes
                && automaticKillObserved && passiveCultivationObserved && journeyKills.Count==9 && actualResult.FreedBytes==4096 && Hash(heldPath)==heldBefore,
                "One same-controller profile links owned source maintenance, nine further native mortal kills, active awakening, automatic kill and passive cultivation");
            Check(Game.DesktopSaveNow() && Game.DesktopArchivePaths.All(p=>File.Exists(p)&&String.Equals(Path.GetDirectoryName(Path.GetFullPath(p)),session.ProfileRoot,StringComparison.OrdinalIgnoreCase)),
                "All five final journey archives persist under the one fresh private profile");
            AdventureProgress saved=AdventureProgress.Load(Path.Combine(session.ProfileRoot,"adventure-v1.txt"));
            CultivationProgress cultivation=MainArchive<CultivationProgress>("cultivation-v2.txt");
            MainArchive<BaiyeProgress>("baiye-v1.txt");MainArchive<LingyuProgress>("lingyu-v1.txt");MainArchive<DongtianProgress>("dongtian-v1.txt");
            Check(saved.Score==Game.Battle.Progress.Score && saved.Experience==Game.Battle.Progress.Experience && saved.FreedBytes==4096
                && !cultivation.Mortal && cultivation.MortalKills==10 && cultivation.AutoBattle && cultivation.DaoTitle==Game.Cultivation.DaoTitle
                && cultivation.SpiritRoot==Game.Cultivation.SpiritRoot && cultivation.LifetimeQi==Game.Cultivation.LifetimeQi,
                "Actual final main archives preserve the same awakened identity, natural rewards, automatic preference and exact real disk total");
            Dictionary<string,object> marker=Game.DesktopAcceptanceRestartMarker();
            string markerPath=Path.Combine(session.ProfileRoot,"acceptance-expected-restart.json");
            Require(!File.Exists(markerPath),"Preserve any existing restart marker; this fresh journey must create it only once.");
            File.WriteAllText(markerPath,json.Serialize(marker),new UTF8Encoding(true));
            report["restartMarker"]=new{path=markerPath,sha256=Hash(markerPath),writtenAfterSuccessfulFiveArchiveSave=true,source="Read-only Game.DesktopAcceptanceRestartMarker; a real new-process restore is a separate validation, not claimed here"};
            report["privateArchivesSHA256Final"]=Game.DesktopArchivePaths.ToDictionary(Path.GetFileName,Hash);
            report["journeyComplete"]=true;report["automaticZeroInput"]=true;report["passiveCultivationObserved"]=passiveCultivationObserved;
            report["automaticKillObserved"]=automaticKillObserved;
        }
        void WriteProgress()
        {
            var progress=new{processId=Process.GetCurrentProcess().Id,executable=Application.ExecutablePath,profile=session.ProfileRoot,
                controllerIdentity=System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Game),stage=stage.ToString(),elapsedSeconds=wall.Elapsed.TotalSeconds,
                running=!finished,terminalFailure,Game.Cultivation.Mortal,Game.Cultivation.MortalKills,Game.Cultivation.LifetimeQi,
                Game.Battle.Progress.Health,Game.Battle.Progress.Dead,automatic=Game.Battle.Automatic,cleanCalls,acceptedCleanCalls,rejectedCleanCalls,
                inputs=InputCount,manualJourneyClicks,manualJourneyBlocks,naturalRests,automaticRests,automaticResumes,automaticResting,automaticWindow,
                automaticPreference=Game.Cultivation.AutoBattle,automaticUnlocked=Game.Cultivation.AutoBattleUnlocked,automaticKillObserved,passiveCultivationObserved};
            File.WriteAllText(Path.Combine(output,"progress.json"),json.Serialize(progress),new UTF8Encoding(true));
        }

        T MainArchive<T>(string name)
        {
            object[] args = { Path.Combine(session.ProfileRoot, name), null };
            Require((bool)typeof(T).GetMethod("TryLoad", Hidden).Invoke(null, args), "Actual main private archive must parse without backup fallback: " + name); return (T)args[1];
        }
        void VerifyReadBack()
        {
            string battlePath = Path.Combine(session.ProfileRoot, "adventure-v1.txt"); string[] battleLines = File.ReadAllLines(battlePath, Encoding.UTF8);
            Require(battleLines.Contains("version=1") && battleLines.Contains("freed=" + (initialFreed + 4096))
                && battleLines.Contains("score=" + (initialScore + target.KillExperience * 5)) && battleLines.Contains("experience=" + (initialExperience + target.KillExperience)),
                "The actual adventure main file must contain the exact combat and deletion counters; it has no TryLoad/backup path.");
            AdventureProgress battle = AdventureProgress.Load(battlePath); CultivationProgress cultivation = MainArchive<CultivationProgress>("cultivation-v2.txt");
            MainArchive<BaiyeProgress>("baiye-v1.txt"); MainArchive<LingyuProgress>("lingyu-v1.txt"); MainArchive<DongtianProgress>("dongtian-v1.txt");
            report["countersSavedMain"] = Counters(battle, cultivation);
            Check(battle.FreedBytes == initialFreed + 4096 && battle.Score == initialScore + target.KillExperience * 5
                && battle.Experience == initialExperience + target.KillExperience && cultivation.Mortal && cultivation.MortalKills == initialKills + 1
                && cultivation.Qi == initialQi && cultivation.DeferredQi == initialDeferredQi + target.CultivationRewardQi
                && cultivation.LifetimeQi == initialLifetimeQi + target.CultivationRewardQi,
                "The actual main archives preserve deletion, one mortal kill, combat rewards and deferred cultivation reward without adding usable Qi");
            DesktopMaintenanceEntry[] entries=Game.ReadDesktopPlayerView().MaintenanceResults;
            Check(entries.Length==1 && entries[0].Kind=="Files" && entries[0].DeletedFiles==1 && entries[0].FreedBytes==4096 && entries[0].Summary.Contains("跳过 1 个"),
                "The same running controller retains the actual one-delete/one-skip history; no second gameplay controller is constructed");
            report["privateArchivesSHA256"] = Game.DesktopArchivePaths.ToDictionary(Path.GetFileName, Hash);
            report["maintenanceLogSHA256"] = Hash(Path.Combine(session.ProfileRoot, "maintenance-results-v1.log"));
            report["privateConsentSHA256"] = Hash(Path.Combine(session.ProfileRoot, "maintenance-consent.txt"));
        }
        static object Counters(AdventureProgress battle, CultivationProgress cultivation)
        { return new { battle.Score, battle.Experience, battle.FreedBytes, cultivation.Mortal, cultivation.MortalKills, cultivation.Qi, cultivation.DeferredQi, cultivation.LifetimeQi }; }
        void WriteReport(string error, bool passed)
        {
            if(!String.IsNullOrEmpty(error) && terminalFailure==null)terminalFailure=error;
            report["countersAfter"] = Counters(Game.Battle.Progress, Game.Cultivation);
            report["actualTargetRewards"] = new { target.KillExperience, target.CultivationRewardQi, target.KillRewarded, target.CultivationRewarded,
                settlement = "Mortal cultivation rewards increase DeferredQi and LifetimeQi; usable Qi is unchanged until actual awakening." };
            report["cleanCalls"] = cleanCalls; report["acceptedCleanCalls"] = acceptedCleanCalls; report["rejectedCleanCalls"] = rejectedCleanCalls;
            report["nativeControlAndActorClicks"] = native == null ? 0 : Field<int>(native, "clicks"); report["nativeDialogClicks"] = dialogClicks;
            report["nativeClicks"] = (int)report["nativeControlAndActorClicks"] + dialogClicks; report["nativeOwnedEnemyClicks"] = attackClicks; report["nativeBlockClicks"] = blockClicks;
            report["manualJourneyClicks"]=manualJourneyClicks;report["manualJourneyBlocks"]=manualJourneyBlocks;report["naturalRests"]=naturalRests;
            report["automaticRests"]=automaticRests;report["automaticResumes"]=automaticResumes;report["automaticObservationWindows"]=automaticWindow;
            report["nativeMovieFrames"]=native==null?new object[0]:(object)Field<List<object>>(native,"movieFrames");
            report["observedManualDamage"] = damageObserved; report["stage"] = stage.ToString(); report["elapsedSeconds"] = wall.Elapsed.TotalSeconds; report["passed"] = passed && terminalFailure==null; report["error"] = terminalFailure;
            report["captures"] = native == null ? new object[0] : (object)Field<List<object>>(native, "captures");
            File.WriteAllText(Path.Combine(output, "ecology-journey-native.json"), json.Serialize(report), new UTF8Encoding(true));
        }
        void Finish(string error)
        {
            if (finished) return; finished = true; timer.Stop();
            if(terminalFailure!=null && error==null)error=terminalFailure;
            try
            {
                if (held != null) { held.Dispose(); held = null; }
                if (error != null && lastPanel != null && lastPanel.Visible && SystemInformation.VirtualScreen.IntersectsWith(lastPanel.Bounds))
                    using (Bitmap image = DesktopFrameCapture.CaptureRegion(Rectangle.Intersect(lastPanel.Bounds, SystemInformation.VirtualScreen), true)) image.Save(Path.Combine(output, "rejected-own-panel-local-only.png"), ImageFormat.Png);
                // Failure cleanup is explicitly not a native-flow success. It
                // only removes this private test grant before its own exit.
                if (Game.MaintenanceConsentCleanup) { report["emergencyTeardownRevocation"] = true; Game.SetCleanupConsent(false); Require(error != null, "A passing run must revoke through native UI, never teardown."); }
                UnattendedOff(); Check(Game.DesktopSaveNow(), "Final five-archive private save succeeds");
                Dictionary<string, string> after = RealHashes(); bool same = realBefore.Count == after.Count && realBefore.All(pair => after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value);
                report["realArchivesBefore"] = realBefore; report["realArchivesAfter"] = after; report["realArchivesUnchanged"] = same;
                Require(same, "Real user progress/consent hashes changed."); report["heldSHA256Final"] = Hash(heldPath);
                report["allScopesOffAtExit"] = !Game.MaintenanceConsentCleanup && !Game.MaintenanceConsentMemory && !Game.MaintenanceConsentAutoMaintenance;
                if (error == null) Require(cleanCalls == 1 && acceptedCleanCalls == 1 && rejectedCleanCalls == 0 && (bool)report["allScopesOffAtExit"], "Final cleanup count or consent boundary failed.");
            }
            catch (Exception finalError) { error = (error == null ? "" : error + "\n") + "Final diagnostics: " + finalError; }
            ExitCode = error == null ? 0 : 1;
            try
            {
                WriteReport(error, error == null);WriteProgress();
                File.WriteAllText(Path.Combine(output, "ecology-journey-native-input.log"), Field<StringBuilder>(native, "log").ToString(), new UTF8Encoding(true));
                Console.WriteLine("Ecology journey native passed=" + (error == null) + "; seconds=" + wall.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture) + "; native clicks=" + report["nativeClicks"]);
            }
            catch (Exception writeError) { ExitCode = 1; Console.Error.WriteLine(writeError); }
            finally { timer.Dispose(); session.ExitAfterAcceptance(ExitCode); }
        }
    }
}
