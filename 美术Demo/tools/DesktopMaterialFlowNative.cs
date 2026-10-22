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
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Explicit, serial native UI fixture. Only the official qishi-full seed
    // creates resources. All later state changes come from real UI handlers.
    internal sealed class DesktopMaterialFlowNative
    {
        const string Material = "shuiyue-lingsui";
        const string Scope = "Source-compiled native UI runner, not the final distributed EXE or natural growth. Fresh official qishi-full seed; x1 throughout; no HP, realm, reward, probability, crop, furnace or game-clock injection. No real maintenance.";
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        enum Stage { OpenInventory, ReadSource, ClickBreakthroughRoute, ReadBreakthrough, ClickAttempt, ConfirmAttempt, ConfirmResult,
            AfterBreakthrough, ReadSourceAsCave, ClickSourceCaveRoute, ReadCaveImport, ClickImport, AfterImport,
            ReadSplitSource, ReadSplitStored, ClickStoredRoute, ReadCaveRecipe, ClickBrew, AfterBrew, ObserveBrewRefresh }
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr GetFocus();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
        [StructLayout(LayoutKind.Sequential)] struct NativeRect { internal int Left, Top, Right, Bottom; }

        readonly DesktopDemoSession session;
        readonly DesktopDemoAcceptance native;
        readonly string output;
        readonly Dictionary<string, string> realBefore;
        readonly Stopwatch wall = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 50 };
        readonly Dictionary<string, object> report = new Dictionary<string, object>();
        readonly List<string> checks = new List<string>();
        readonly List<object> states = new List<object>(), inputs = new List<object>(), glyphs = new List<object>(), setup = new List<object>();
        readonly List<object> focusEvents = new List<object>();
        readonly HashSet<Form> tracedForms = new HashSet<Form>();
        readonly HashSet<Button> tracedButtons = new HashSet<Button>();
        readonly HashSet<string> captured = new HashSet<string>(StringComparer.Ordinal);
        readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
        Stage stage;
        double next;
        bool busy, finished, tracingStopped;
        int dialogClicks;
        DateTime brewClickUtc;
        double brewSettlementObserved = -1;
        bool brewingObservation, sourceActivatedDuringBrew;
        DesktopPracticePanel brewSource;
        DongtianForm brewCave;
        Control originalSource;
        Form lastPanel;
        internal int ExitCode { get; private set; }
        AdventureController Game { get { return session.Game; } }
        object Native(string method, params object[] args) { return typeof(DesktopDemoAcceptance).GetMethod(method, Hidden).Invoke(native, args); }
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Hidden).GetValue(instance); }
        static IEnumerable<Control> Descendants(Control control)
        { foreach (Control child in control.Controls) { yield return child; foreach (Control nested in Descendants(child)) yield return nested; } }
        static string Hash(string path)
        { if (!File.Exists(path)) return "missing"; using (var hash = SHA256.Create()) using (var input = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""); }
        internal static Dictionary<string, string> RealHashes()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            string[] names = { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt", "maintenance-consent.txt", "maintenance-consent.txt.revoked" };
            var result = names.ToDictionary(name => name, name => Hash(Path.Combine(root, name)), StringComparer.Ordinal);
            if (Directory.Exists(root)) foreach (string file in Directory.GetFiles(root))
                if (names.Any(name => Path.GetFileName(file).StartsWith(name + ".", StringComparison.Ordinal))) result[Path.GetFileName(file)] = Hash(file);
            return result;
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                if (args.Length != 4) throw new ArgumentException("Expected Assets, ArtDemoAssets, fresh evidence directory and fresh private profile.");
                string evidence = Path.GetFullPath(args[2]), profile = Path.GetFullPath(args[3]);
                string allowed = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profiles") + Path.DirectorySeparatorChar;
                if (!profile.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(profile).StartsWith("verify-", StringComparison.Ordinal)
                    || Directory.Exists(profile) || File.Exists(profile) || Directory.Exists(evidence) || File.Exists(evidence))
                    throw new IOException("The native material fixture requires new evidence and a new verify- profile inside this runner's profiles directory.");
                DesktopDemoProfiles.ValidateRoot(profile); DesktopDemoProfiles.ValidateRoot(evidence);
                Dictionary<string, string> realBefore = RealHashes();
                Directory.CreateDirectory(evidence);
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[1]));
                using (DesktopDemoSession session = new DesktopDemoSession(Path.GetFullPath(args[0]), profile, true, "qishi-full"))
                {
                    var fixture = new DesktopMaterialFlowNative(session, evidence, realBefore);
                    Application.Run(session); return fixture.ExitCode;
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
        DesktopMaterialFlowNative(DesktopDemoSession session, string output, Dictionary<string, string> realBefore)
        {
            this.session = session; this.output = output; this.realBefore = realBefore; ExitCode = 1;
            native = new DesktopDemoAcceptance(session, output, true);
            // This is only the input helper's diagnostic timeout stopwatch.
            // The production game timer and acceptance-speed field are untouched.
            Field<Stopwatch>(native, "wall").Start();
            report["scope"] = Scope; report["processId"] = Process.GetCurrentProcess().Id;
            report["executable"] = Application.ExecutablePath; report["executableSHA256"] = Hash(Application.ExecutablePath);
            report["productionSourcesManifest"] = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "production-sources.json");
            report["productionSourcesManifestSHA256"] = Hash((string)report["productionSourcesManifest"]);
            report["profile"] = session.ProfileRoot; report["seed"] = "qishi-full";
            report["startedUtc"] = DateTime.UtcNow.ToString("o"); report["clockAcceleration"] = "x1; no time advancement or timestamp edits";
            report["inputDeadlineSeconds"] = 85; report["parentAbsoluteDeadlineSeconds"] = 90;
            report["checks"] = checks; report["states"] = states; report["nativeInputs"] = inputs; report["glyphChecks"] = glyphs; report["uiSetup"] = setup;
            report["focusEvents"] = focusEvents;
            timer.Tick += Tick; timer.Start(); next = .5;
        }
        void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        void Check(bool value, string text) { Require(value, text); checks.Add(text); Console.WriteLine("PASS " + text); }
        void Next(Stage value, double delay = .2)
        { stage = value; next = wall.Elapsed.TotalSeconds + delay; TraceFocus("stage", null, false); Console.WriteLine("STAGE " + stage + " at " + wall.Elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)); }
        static string ControlIdentity(Control control)
        {
            if (control == null) return null;
            string text = control.Text ?? ""; if (text.Length > 100) text = text.Substring(0, 100);
            return control.GetType().Name + "[" + control.Name + "] hwnd=" + (control.IsHandleCreated && !control.IsDisposed ? control.Handle.ToInt64() : 0)
                + " disposed=" + control.IsDisposed + " text=" + text;
        }
        void TraceFocus(string reason, Control control, bool stack = true)
        {
            if (tracingStopped) return;
            try
            {
                object item = new { seconds = wall.Elapsed.TotalSeconds, utc = DateTime.UtcNow.ToString("o"), stage = stage.ToString(), reason,
                    sender = ControlIdentity(control), foreground = ((IntPtr)Native("GetForegroundWindow")).ToInt64(), threadFocus = GetFocus().ToInt64(),
                    ownForms = Application.OpenForms.Cast<Form>().Where(form => form is DesktopPracticePanel || form is DongtianForm || form is BreakthroughForm)
                        .Select(form => new { identity = ControlIdentity(form), form.Visible, bounds = form.Bounds.ToString(), activeControl = ControlIdentity(form.ActiveControl) }).ToArray(),
                    stack = stack ? Environment.StackTrace : null };
                focusEvents.Add(item);
                // Only three own forms and the two relevant buttons are watched.
                // Persist each event so a bounded parent stop retains its stack.
                File.AppendAllText(Path.Combine(output, "material-focus-events.jsonl"), json.Serialize(item) + Environment.NewLine, new UTF8Encoding(false));
            }
            catch (Exception error) { report["focusTraceError"] = error.ToString(); }
        }
        void TraceOwnForms()
        {
            foreach (Form form in Application.OpenForms.Cast<Form>().Where(form => form is DesktopPracticePanel || form is DongtianForm || form is BreakthroughForm))
                if (tracedForms.Add(form)) form.Activated += delegate(object sender, EventArgs args)
                {
                    if (brewingObservation && sender is DesktopPracticePanel) sourceActivatedDuringBrew = true;
                    TraceFocus("Form.Activated", (Control)sender);
                };
        }
        void TraceButton(Button button, string role)
        {
            if (!tracedButtons.Add(button)) return;
            button.Enter += delegate(object sender, EventArgs args) { TraceFocus(role + ".Enter", (Control)sender); };
            button.Disposed += delegate(object sender, EventArgs args) { TraceFocus(role + ".Disposed", (Control)sender); };
            TraceFocus(role + ".watch", button, false);
        }
        int Spirit { get { return Game.LingyuMaterialCount(Material); } }
        int Stored { get { return Game.Dongtian.CountOf(Material); } }
        void SafeState()
        {
            Require(Game.DesktopAcceptanceMaintenanceOff() && Game.DesktopRealWorkIdle && !Game.DesktopObservesSystem && Game.Baiye.Mode == 0,
                "A maintenance scope, observer or real worker became active.");
            Require(Field<int>(Game, "desktopAcceptanceSpeed") == 1, "The production game must remain at x1.");
        }
        void BrewFocusRemainsOwned()
        {
            Require(brewSource != null && !brewSource.IsDisposed && brewSource.Visible,
                "The source inventory must remain visible; the fixture cannot hide it to avoid the refresh regression.");
            Require(brewCave != null && !brewCave.IsDisposed && brewCave.Visible && (IntPtr)Native("GetForegroundWindow") == brewCave.Handle
                && !sourceActivatedDuringBrew, "The cave must retain native foreground throughout the source-card removal, without inventory reactivation.");
        }
        T Panel<T>() where T : Form
        {
            TraceOwnForms();
            T panel = Application.OpenForms.OfType<T>().SingleOrDefault(form => form.Visible && !form.IsDisposed);
            Require(panel != null, "The expected own panel is not visible: " + typeof(T).Name); lastPanel = panel; return panel;
        }
        Control Card(DesktopPracticePanel panel, string source)
        {
            Control card = panel.Controls.Find("material-" + source + "-" + Material, true).SingleOrDefault();
            Require(card != null, "Expected separate material card: " + source); return card;
        }
        Button Action(Control card, string source, string route)
        {
            Button action = card.Controls.Find("material-action-" + source + "-" + Material, true).OfType<Button>().SingleOrDefault();
            Require(action != null && (string)action.Tag == route && action.Enabled, "Material action is not the expected existing-panel route: " + source + " -> " + route);
            return action;
        }
        static void Reveal(Control control)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
            { ScrollableControl scroll = parent as ScrollableControl; if (scroll != null) scroll.ScrollControlIntoView(control); }
            control.FindForm().PerformLayout(); control.FindForm().Refresh();
        }
        bool Presented(Label label, string role)
        {
            bool result = (bool)Native("NativePreviewContentMatches", label, true);
            report["lastGlyphProbe"] = new { role, text = label.Text, bounds = new Rectangle(label.PointToScreen(Point.Empty), label.Size).ToString(),
                root = ((IntPtr)Native("RootAt", label.PointToScreen(new Point(label.Width / 2, label.Height / 2)))).ToInt64(), presented = result };
            if (result) glyphs.Add(new { stage = stage.ToString(), role, text = label.Text, verification = "unchanged existing native glyph verifier: >=60 samples, >=80% matches, existing colour tolerances" });
            return result;
        }
        bool ReadCard(Control card, int count, string role)
        {
            Label title = Field<Label>(card, "title"), location = Field<Label>(card, "location");
            Require(title.Text == "水月灵髓 × " + count, "The actual card title has the wrong separate quantity: " + title.Text);
            Reveal(title); Reveal(location);
            return Presented(title, role + " title") && Presented(location, role + " location");
        }
        bool ReadHeading(Form panel, string startsWith)
        {
            Label label = Descendants(panel).OfType<Label>().FirstOrDefault(item => item.Text.StartsWith(startsWith, StringComparison.Ordinal));
            Require(label != null, "The expected production heading is missing: " + startsWith); Reveal(label); return Presented(label, startsWith);
        }
        Button Button(Form panel, string text)
        {
            Button button = Descendants(panel).OfType<Button>().SingleOrDefault(item => item.Visible && item.Text == text);
            Require(button != null && button.Enabled, "Expected enabled production button: " + text); return button;
        }
        Dictionary<string, object> RootProbe(Rectangle region, IntPtr expected)
        {
            var samples = new List<object>(); bool matched = true;
            int insetX = Math.Min(8, Math.Max(0, region.Width / 4)), insetY = Math.Min(8, Math.Max(0, region.Height / 4));
            for (int y = region.Top + insetY; y < region.Bottom - insetY; y += Math.Max(30, region.Height / 8))
                for (int x = region.Left + insetX; x < region.Right - insetX; x += Math.Max(30, region.Width / 6))
                {
                    Point point = new Point(x, y); IntPtr root = (IntPtr)Native("RootAt", point); matched &= root == expected;
                    samples.Add(new { point = point.ToString(), root = root.ToInt64() });
                }
            var probe = new Dictionary<string, object> { { "region", region.ToString() }, { "expectedRoot", expected.ToInt64() },
                { "matched", matched && samples.Count > 0 }, { "samples", samples.ToArray() } };
            report["lastCaptureRootProbe"] = probe; return probe;
        }
        void Capture(Form panel, string name, string purpose)
        {
            if (captured.Contains(name)) return;
            Require(panel.Visible && panel.WindowState == FormWindowState.Normal && SystemInformation.VirtualScreen.Contains(panel.Bounds),
                "A passed panel capture must be visible, restored and inside the real desktop.");
            Dictionary<string, object> roots = RootProbe(panel.RectangleToScreen(panel.ClientRectangle), panel.Handle);
            Require((bool)roots["matched"], "Refuse a passed screenshot whose sampled native roots do not belong to the expected own panel: " + name);
            Native("CapturePanel", panel, name);
            var captures = Field<List<object>>(native, "captures");
            ((Dictionary<string, object>)captures[captures.Count - 1])["purpose"] = purpose + "; native pixels, source-runner/seed fixture, not natural growth";
            ((Dictionary<string, object>)captures[captures.Count - 1])["nativeRootVerification"] = roots;
            captured.Add(name);
        }
        bool Click(Control control, string captureName)
        {
            Require(control.Visible && control.Enabled, "Refuse input to a hidden or disabled control.");
            Point point = control.PointToScreen(new Point(control.Width / 2, control.Height / 2));
            IntPtr root = (IntPtr)Native("RootAt", point), hit = WindowFromPoint(point), foreground = (IntPtr)Native("GetForegroundWindow");
            var observation = new { stage = stage.ToString(), seconds = wall.Elapsed.TotalSeconds, control.Name, control.Text,
                point = point.ToString(), controlHandle = control.Handle.ToInt64(), expectedRoot = control.FindForm().Handle.ToInt64(),
                root = root.ToInt64(), hit = hit.ToInt64(), foreground = foreground.ToInt64() };
            report["lastInputProbe"] = observation;
            if (root != control.FindForm().Handle || hit != control.Handle) return false;
            Capture(control.FindForm(), captureName, "Actual native button and own-root hit verified before this interaction");
            if (!(bool)Native("ClickControl", control)) return false;
            inputs.Add(observation); return true;
        }
        IntPtr Dialog(string title) { return (IntPtr)Native("OwnDialog", title); }
        bool Confirm(IntPtr dialog, string name)
        {
            if (dialog == IntPtr.Zero) return false;
            NativeRect bounds;
            Require(GetWindowRect(dialog, out bounds), "The own confirmation dialog has no native rectangle.");
            Rectangle rectangle = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), interior = rectangle;
            interior.Inflate(-12, -12);
            Dictionary<string, object> roots = RootProbe(interior, dialog);
            if (!(bool)roots["matched"]) return false;
            if (captured.Add(name))
            {
                using (Bitmap bitmap = DesktopFrameCapture.CaptureRegion(rectangle, true)) bitmap.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                Field<List<object>>(native, "captures").Add(new { image = name + ".png", bounds = rectangle.ToString(),
                    nativeRootVerification = roots, purpose = "This process's actual native MessageBox; confirmation uses existing unique-visible-确定-button + own-root gate" });
            }
            if (!(bool)Native("ClickDialogOk", dialog)) return false;
            dialogClicks++; inputs.Add(new { stage = stage.ToString(), seconds = wall.Elapsed.TotalSeconds, ownDialog = dialog.ToInt64(),
                method = "OwnDialog PID check + existing ClickDialogOk unique caption and NativeClick root gate" });
            return true;
        }
        void State(string name)
        {
            states.Add(new { name, seconds = wall.Elapsed.TotalSeconds, realmIndex = Game.Cultivation.RealmIndex, layer = Game.Cultivation.Layer,
                lingyuLingsui = Spirit, dongtianLingsui = Stored, furnace = Game.Dongtian.Furnace, importSeq = Game.Dongtian.ImportSeq,
                pending = Game.DongtianPersistPending, archiveSHA256 = Game.DesktopArchivePaths.ToDictionary(Path.GetFileName, Hash) });
        }
        T MainArchive<T>(string name)
        {
            string path = Path.Combine(session.ProfileRoot, name); object[] args = { path, null };
            Require((bool)typeof(T).GetMethod("TryLoad", Hidden).Invoke(null, args), "The actual main archive must parse without a backup fallback: " + name);
            return (T)args[1];
        }
        void DiskPair(int spirit, int stored)
        {
            LingyuProgress l = MainArchive<LingyuProgress>("lingyu-v1.txt"); DongtianProgress d = MainArchive<DongtianProgress>("dongtian-v1.txt");
            int count; l.Materials.TryGetValue(Material, out count);
            Check(count == spirit && d.CountOf(Material) == stored && l.Grants.Keys.Count(id => id.StartsWith("dtin-" + Material + "-", StringComparison.Ordinal)) == 1
                && d.Grants.Count(id => id.StartsWith("dtin-" + Material + "-", StringComparison.Ordinal)) == 1 && d.ImportSeq == 1,
                "Real lingyu/dongtian main archives preserve the one transfer: " + spirit + "/" + stored);
        }
        void OpenInventory(string reason)
        {
            session.ClosePanels(); session.OpenPanel("inventory");
            TraceOwnForms(); TraceFocus("OpenInventory UI setup returned", null, false);
            setup.Add(new { seconds = wall.Elapsed.TotalSeconds, action = "OpenPanel(inventory), UI setup only", reason });
        }
        void Tick(object sender, EventArgs e)
        {
            if (busy || finished) return; busy = true;
            try
            {
                report["lastStage"] = stage.ToString();
                // Leave five seconds for the bounded final snapshot/save/report
                // before the wrapper stops this own child at absolute 90 seconds.
                Require(wall.Elapsed.TotalSeconds < 85, "Native input reached its 85-second deadline before the parent's absolute 90-second stop.");
                SafeState();
                // Also sample while waiting the mandatory half-second. The own
                // Form.Activated hook catches brief source activation between ticks.
                if (stage == Stage.ObserveBrewRefresh) BrewFocusRemainsOwned();
                if (wall.Elapsed.TotalSeconds < next) return;
                if (stage == Stage.OpenInventory)
                {
                    Check(!Game.Cultivation.Mortal && Game.Cultivation.RealmIndex == 0 && Game.Cultivation.CanAttemptBreakthrough
                        && Game.Cultivation.BreakthroughPercent(false, false) == 100 && Spirit == 2 && Stored == 0 && Game.Dongtian.Furnace == null,
                        "Official fresh qishi-full seed supplies two lingsui and the existing guaranteed breakthrough, with an empty cave");
                    State("seed-before-input"); OpenInventory("initial material entry"); Next(Stage.ReadSource);
                }
                else if (stage == Stage.ReadSource)
                {
                    DesktopPracticePanel panel = Panel<DesktopPracticePanel>(); originalSource = Card(panel, "lingyu");
                    if (!ReadCard(originalSource, 2, "initial lingyu")) return;
                    Check(Spirit == 2 && Stored == 0, "Inventory reading does not transfer or consume the seed material");
                    Capture(panel, "01-lingyu-two-before-navigation", "Inventory opened as declared UI setup; actual title/location glyphs verified");
                    Reveal(Action(originalSource, "lingyu", "breakthrough")); Next(Stage.ClickBreakthroughRoute);
                }
                else if (stage == Stage.ClickBreakthroughRoute)
                { if (Click(Action(Card(Panel<DesktopPracticePanel>(), "lingyu"), "lingyu", "breakthrough"), "02-native-material-to-breakthrough")) Next(Stage.ReadBreakthrough); }
                else if (stage == Stage.ReadBreakthrough)
                {
                    BreakthroughForm panel = Panel<BreakthroughForm>();
                    if (!ReadHeading(panel, "破境仪式 ·")) return;
                    Check(Spirit == 2 && Stored == 0 && !Field<CheckBox>(panel, "useMaterial").Checked,
                        "Native material navigation opens the real breakthrough panel without consumption; optional material remains unchecked");
                    Capture(panel, "03-breakthrough-open-without-consumption", "Reached by the actual material-card button; unchanged optional-material choice");
                    Reveal(Field<Button>(panel, "attempt")); Next(Stage.ClickAttempt);
                }
                else if (stage == Stage.ClickAttempt)
                { if (Click(Field<Button>(Panel<BreakthroughForm>(), "attempt"), "04-native-breakthrough-attempt")) Next(Stage.ConfirmAttempt); }
                else if (stage == Stage.ConfirmAttempt)
                { if (Confirm(Dialog("破境仪式"), "05-own-breakthrough-confirmation")) Next(Stage.ConfirmResult); }
                else if (stage == Stage.ConfirmResult)
                {
                    Require(Dialog("破境未成") == IntPtr.Zero, "The original guaranteed seed breakthrough unexpectedly failed.");
                    if (Confirm(Dialog("破境成功"), "06-own-breakthrough-success")) Next(Stage.AfterBreakthrough);
                }
                else if (stage == Stage.AfterBreakthrough)
                {
                    Check(Game.Cultivation.RealmIndex == 1 && Game.DongtianUnlocked && Spirit == 2 && Stored == 0,
                        "The confirmed production breakthrough reaches foundation establishment while retaining both unselected lingsui");
                    Check(MainArchive<CultivationProgress>("cultivation-v2.txt").RealmIndex == 1, "The real cultivation main archive records the completed breakthrough");
                    State("after-real-breakthrough"); OpenInventory("verify the same source card changes its route"); Next(Stage.ReadSourceAsCave);
                }
                else if (stage == Stage.ReadSourceAsCave)
                {
                    DesktopPracticePanel panel = Panel<DesktopPracticePanel>(); Control card = Card(panel, "lingyu");
                    if (!ReadCard(card, 2, "post-breakthrough lingyu")) return;
                    Check(Object.ReferenceEquals(card, originalSource), "The existing source card is reused across the actual realm transition");
                    Capture(panel, "07-same-source-card-now-cave", "Actual two-portion source card after breakthrough; its existing action now targets the cave");
                    Reveal(Action(card, "lingyu", "dongtian")); Next(Stage.ClickSourceCaveRoute);
                }
                else if (stage == Stage.ClickSourceCaveRoute)
                { if (Click(Action(Card(Panel<DesktopPracticePanel>(), "lingyu"), "lingyu", "dongtian"), "08-native-source-to-cave")) Next(Stage.ReadCaveImport); }
                else if (stage == Stage.ReadCaveImport)
                {
                    DongtianForm panel = Panel<DongtianForm>(); if (!ReadHeading(panel, "洞天经营")) return;
                    Check(Spirit == 2 && Stored == 0, "Native source-card cave navigation does not perform an implicit import");
                    Capture(panel, "09-cave-before-explicit-import", "Actual cave reached through native source-card navigation; import still awaits its own click");
                    Reveal(Button(panel, "灵髓入库")); Next(Stage.ClickImport);
                }
                else if (stage == Stage.ClickImport)
                { if (Click(Button(Panel<DongtianForm>(), "灵髓入库"), "10-native-one-portion-import")) Next(Stage.AfterImport); }
                else if (stage == Stage.AfterImport)
                {
                    if (Spirit == 2 && Stored == 0) return;
                    Require(Spirit == 1 && Stored == 1, "One native import must move exactly one portion: Lingyu 2->1, cave 0->1.");
                    if (Game.DongtianPersistPending) return;
                    DiskPair(1, 1); State("after-one-native-import");
                    Capture(Panel<DongtianForm>(), "11-cave-one-import-committed", "Actual one-portion transfer verified against both independently parsed main archives");
                    OpenInventory("show the same material as separate source and cave stocks"); Next(Stage.ReadSplitSource);
                }
                else if (stage == Stage.ReadSplitSource)
                {
                    DesktopPracticePanel panel = Panel<DesktopPracticePanel>(); if (!ReadCard(Card(panel, "lingyu"), 1, "split lingyu")) return;
                    Check(Spirit == 1 && Stored == 1, "The source card shows its own one portion after the real import");
                    Capture(panel, "12-independent-lingyu-one", "Actual Lingyu title/location glyphs prove its independent count of one"); Next(Stage.ReadSplitStored);
                }
                else if (stage == Stage.ReadSplitStored)
                {
                    DesktopPracticePanel panel = Panel<DesktopPracticePanel>(); Control stored = Card(panel, "dongtian"); if (!ReadCard(stored, 1, "split cave")) return;
                    Check(!Object.ReferenceEquals(stored, Card(panel, "lingyu")), "The same material ID occupies two distinct controls and inventories, both one");
                    Capture(panel, "13-independent-cave-one", "Actual cave title/location glyphs prove its separate count of one; no combined count is claimed");
                    Button action = Action(stored, "dongtian", "dongtian"); TraceButton(action, "stored-material-source-action");
                    Reveal(action); Next(Stage.ClickStoredRoute);
                }
                else if (stage == Stage.ClickStoredRoute)
                { if (Click(Action(Card(Panel<DesktopPracticePanel>(), "dongtian"), "dongtian", "dongtian"), "14-native-stored-material-to-cave")) Next(Stage.ReadCaveRecipe); }
                else if (stage == Stage.ReadCaveRecipe)
                {
                    DongtianForm panel = Panel<DongtianForm>(); if (!ReadHeading(panel, "洞天经营")) return;
                    Check(Spirit == 1 && Stored == 1 && Game.Dongtian.Furnace == null && !Game.Dongtian.HasGear("jinglu") && DongtianRecipes.Pill("ninglu").BrewSeconds == 1800,
                        "Stored-card navigation preserves both counts and exposes the original thirty-minute ninglu recipe");
                    brewCave = panel; brewSource = Application.OpenForms.OfType<DesktopPracticePanel>().SingleOrDefault(form => form.Visible && !form.IsDisposed);
                    Require(brewSource != null, "The original inventory source must still be visible before brewing.");
                    Button brew = Button(panel, "炼凝露丹"); TraceButton(brew, "ninglu-brew-action");
                    Reveal(brew); Next(Stage.ClickBrew);
                }
                else if (stage == Stage.ClickBrew)
                {
                    brewClickUtc = DateTime.UtcNow;
                    brewingObservation = true;
                    if (Click(Button(Panel<DongtianForm>(), "炼凝露丹"), "15-native-start-ninglu")) Next(Stage.AfterBrew);
                }
                else if (stage == Stage.AfterBrew)
                {
                    if (Game.Dongtian.Furnace == null) return;
                    string[] slot = Game.Dongtian.Furnace.Split(','); long ready = 0;
                    Require(slot.Length == 2 && slot[0] == "ninglu" && Int64.TryParse(slot[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out ready), "The production furnace must contain an actual ninglu ready-UTC timestamp.");
                    DateTime readyUtc = new DateTime(ready, DateTimeKind.Utc), observed = DateTime.UtcNow;
                    Check(Spirit == 1 && Stored == 0 && readyUtc >= brewClickUtc.AddSeconds(1799) && readyUtc <= observed.AddSeconds(1801)
                        && !Game.Dongtian.FurnaceReady(observed) && !Game.Dongtian.BuffActive("buff-ninglu", observed),
                        "Native alchemy consumes only the one stored portion and starts the real ~1800-second furnace; no mature pill or buff is injected");
                    DiskPair(1, 0);
                    Check(MainArchive<DongtianProgress>("dongtian-v1.txt").Furnace == Game.Dongtian.Furnace, "The real cave main archive preserves the exact production furnace timestamp");
                    report["furnaceTiming"] = new { clickBeforeUtc = brewClickUtc.ToString("o"), observedUtc = observed.ToString("o"), readyUtc = readyUtc.ToString("o"),
                        remainingSeconds = (readyUtc - observed).TotalSeconds, configuredSeconds = DongtianRecipes.Pill("ninglu").BrewSeconds, observedNotMature = true };
                    State("native-ninglu-started-not-mature"); BrewFocusRemainsOwned();
                    brewSettlementObserved = wall.Elapsed.TotalSeconds;
                    TraceFocus("AfterBrew begin mandatory passive refresh observation", null, false);
                    Next(Stage.ObserveBrewRefresh, .5);
                }
                else if (stage == Stage.ObserveBrewRefresh)
                {
                    double observedFor = wall.Elapsed.TotalSeconds - brewSettlementObserved;
                    Require(observedFor >= .5, "The final frame cannot precede the mandatory passive refresh delay.");
                    bool removed = brewSource.Controls.Find("material-dongtian-" + Material, true).Length == 0;
                    if (!removed && observedFor < 1) return;
                    Check(removed && Spirit == 1 && Stored == 0, "The still-visible source inventory naturally refreshes away its zero-stock cave card within one second");
                    BrewFocusRemainsOwned();
                    Check(!sourceActivatedDuringBrew, "Removing the background source card never activates its still-visible inventory or steals cave foreground");
                    report["afterBrewRefreshObservation"] = new { seconds = observedFor, minimumDelaySeconds = .5, maximumCardRemovalWaitSeconds = 1,
                        sourceVisible = brewSource.Visible, sourceStockCardAbsent = removed, sourceActivatedDuringBrew,
                        caveHandle = brewCave.Handle.ToInt64(), foreground = ((IntPtr)Native("GetForegroundWindow")).ToInt64() };
                    TraceFocus("AfterBrew refresh removal observed before unchanged capture gate", null, false);
                    Capture(Panel<DongtianForm>(), "16-real-furnace-running", "Actual production furnace after >=0.5s passive refresh: source inventory stays visible, zero-stock card is absent, and cave keeps foreground; no reactivation or time injection"); Finish(null);
                }
            }
            catch (Exception error) { Finish((error is TargetInvocationException && error.InnerException != null ? error.InnerException : error).ToString()); }
            finally { busy = false; }
        }
        void Finish(string error)
        {
            if (finished) return; finished = true; timer.Stop(); timer.Dispose();
            TraceFocus(error == null ? "Finish success" : "Finish failure", null, false); tracingStopped = true;
            try
            {
                if (error != null && lastPanel != null && !lastPanel.IsDisposed && lastPanel.Visible)
                    try
                    {
                        Rectangle bounds = lastPanel.Bounds; IntPtr expected = lastPanel.Handle;
                        IntPtr dialog = Dialog("破境成功"); if (dialog == IntPtr.Zero) dialog = Dialog("破境仪式"); if (dialog == IntPtr.Zero) dialog = Dialog("破境未成");
                        NativeRect nativeBounds;
                        if (dialog != IntPtr.Zero && GetWindowRect(dialog, out nativeBounds))
                        { bounds = Rectangle.FromLTRB(nativeBounds.Left, nativeBounds.Top, nativeBounds.Right, nativeBounds.Bottom); expected = dialog; }
                        Dictionary<string, object> roots = RootProbe(bounds, expected);
                        using (Bitmap bitmap = DesktopFrameCapture.CaptureRegion(bounds, true)) bitmap.Save(Path.Combine(output, "00-failure-own-region.png"), ImageFormat.Png);
                        Field<List<object>>(native, "captures").Add(new { image = "00-failure-own-region.png", bounds = bounds.ToString(), nativeRootVerification = roots,
                            purpose = "Rejected raw native pixels at the expected own window region. Other application pixels may appear when blocked; local failure diagnosis only, never a passed frame." });
                    }
                    catch (Exception captureError) { report["failureScreenshotError"] = captureError.ToString(); }
                SafeState(); Check(Game.DesktopSaveNow() && Game.DesktopArchivePaths.All(File.Exists), "All five private business archives save successfully");
                Require(Game.DesktopArchivePaths.All(path => String.Equals(Path.GetDirectoryName(path), session.ProfileRoot, StringComparison.OrdinalIgnoreCase)), "A business archive escaped the private profile.");
                Game.DesktopAcceptanceVerifyFiles(Game.DesktopAcceptanceRestartMarker());
                Check(Game.DesktopAcceptanceMaintenanceOff(), "All maintenance scopes and real workers remain off through the final save");
                if (error == null) Check(Field<int>(native, "clicks") == 6 && dialogClicks == 2,
                    "The complete flow uses exactly six native control clicks and the two explicit own-dialog confirmations");
                Dictionary<string, string> after = RealHashes();
                bool same = realBefore.Count == after.Count && realBefore.All(pair => after.ContainsKey(pair.Key) && after[pair.Key] == pair.Value);
                report["realArchivesBefore"] = realBefore; report["realArchivesAfter"] = after; report["realArchivesUnchanged"] = same;
                Require(same, "A real user business/consent archive hash changed.");
                report["privateArchivesSHA256"] = Game.DesktopArchivePaths.ToDictionary(Path.GetFileName, Hash);
            }
            catch (Exception finalError) { error = (error == null ? "" : error + "\n") + "Final diagnostics: " + finalError; }
            report["nativeControlClicks"] = Field<int>(native, "clicks"); report["nativeDialogClicks"] = dialogClicks;
            report["nativeClicks"] = Field<int>(native, "clicks") + dialogClicks; report["captures"] = Field<List<object>>(native, "captures");
            report["passed"] = error == null; report["error"] = error; report["elapsedSeconds"] = wall.Elapsed.TotalSeconds;
            report["scopeNote"] = "Only small material title/location and feature-heading glyphs are claimed verified. Whole long material-body glyphs and natural growth are not claimed.";
            ExitCode = error == null ? 0 : 1;
            try
            {
                File.WriteAllText(Path.Combine(output, "material-flow-native.json"), json.Serialize(report), new UTF8Encoding(true));
                File.WriteAllText(Path.Combine(output, "material-native-input.log"), Field<StringBuilder>(native, "log").ToString(), new UTF8Encoding(true));
                Console.WriteLine("Material native passed=" + report["passed"] + "; seconds=" + wall.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + "; clicks=" + report["nativeClicks"]);
            }
            catch (Exception writeError) { ExitCode = 1; Console.Error.WriteLine(writeError); }
            finally { session.ExitAfterAcceptance(ExitCode); }
        }
    }
}
