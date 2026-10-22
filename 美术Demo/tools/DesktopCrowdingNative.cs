using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Opt-in source runner only. One synthetic initial layout, then the unchanged
    // production x1 timer/movement/combat. No visible host, input or maintenance.
    internal sealed class DesktopCrowdingNative : IDisposable
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        const string Scope = "Synthetic crowded initial layout, not natural discovery/growth or the final distributable EXE. Five byte-identical main archives cloned from a previously verified normal x1 awakened profile, with no consent/seed/marker copied. Production factories, RenderWorld/UpdateLayeredWindow and normal x1 Tick/Move/combat run for 12 wall-clock seconds. Only initial hero/enemy geometry and normal Boss combat authorization are set; HP/realm/damage/cooldowns/clocks are not edited. No scan, real maintenance, native input, foreground activation, other-app move/hide or timer pause. Raw other-app background pixels remain local QA evidence only. Short resource samples do not replace the paused long test.";
        [StructLayout(LayoutKind.Sequential)] struct NativePoint { internal int X, Y; internal NativePoint(int x, int y) { X = x; Y = y; } }
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(NativePoint point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process, int flags);
        readonly Form owner;
        readonly AdventureController game;
        readonly ApplicationContext context;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 50 };
        readonly Stopwatch wall = new Stopwatch();
        readonly System.Threading.Timer deadline;
        readonly Process self = Process.GetCurrentProcess();
        readonly Dictionary<string, object> report = new Dictionary<string, object>();
        readonly List<object> samples = new List<object>(), captures = new List<object>(), checks = new List<object>();
        readonly List<string> errors = new List<string>();
        readonly string output;
        readonly IntPtr foreground;
        readonly AdventureEnemy boss;
        readonly AdventureEnemy[] ordinary;
        readonly Rectangle area;
        double nextSample;
        double cpuStart, peakCpu, peakGdi, peakUser, peakHandles, captureCpuMs, captureWallSeconds;
        bool busy, finished, captured8, captured12, capturedNaturalEffect;
        int capturesWithVisibleEffect;
        internal int ExitCode { get; private set; }

        static object Call(object target, string name, params object[] args)
        { return target.GetType().GetMethod(name, Hidden).Invoke(target, args); }
        static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
        static void Set(object target, string name, object value) { target.GetType().GetField(name, Hidden).SetValue(target, value); }
        static IntPtr RootAt(int x, int y) { return GetAncestor(WindowFromPoint(new NativePoint(x, y)), 2); }
        void Check(bool pass, string text)
        { checks.Add(new { seconds = wall.Elapsed.TotalSeconds, passed = pass, message = text }); if (!pass) errors.Add(text); Console.WriteLine((pass ? "PASS " : "FAIL ") + text); }

        [STAThread] static int Main(string[] args)
        {
            try
            {
                if (args.Length != 4) throw new ArgumentException("Expected Assets, ArtDemoAssets, new evidence directory and cloned private profile.");
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[1]));
                string output = Path.GetFullPath(args[2]), profile = Path.GetFullPath(args[3]);
                if (Directory.Exists(output)) throw new InvalidOperationException("Evidence must be new; existing failures are retained.");
                Directory.CreateDirectory(output);
                using (ApplicationContext context = new ApplicationContext())
                using (DesktopCrowdingNative probe = new DesktopCrowdingNative(Path.GetFullPath(args[0]), profile, output, context))
                { Application.Run(context); return probe.ExitCode; }
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }

        DesktopCrowdingNative(string assets, string profile, string output, ApplicationContext context)
        {
            this.output = output; this.context = context; ExitCode = 1; foreground = GetForegroundWindow();
            // Independent absolute child bound even if a native/UI callback stalls.
            deadline = new System.Threading.Timer(delegate { Console.Error.WriteLine("Owned crowding fixture absolute 24-second deadline."); Environment.Exit(124); }, null, 24000, Timeout.Infinite);
            owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) };
            game = new AdventureController(owner, assets, false, null, false, profile, false);
            Require(!game.Cultivation.Mortal && game.Cultivation.AutoBattleUnlocked && game.Battle.Automatic && !game.Battle.Progress.Dead,
                "Cloned source must already be a living, naturally awakened automatic-combat profile; no state injection is allowed.");
            Require(game.DesktopAcceptanceMaintenanceOff() && game.DesktopRealWorkIdle && !game.DesktopObservesSystem, "No observation, scan or maintenance may start.");
            Require(game.Battle.Enemies.Count == 0, "Expected a new controller with no pre-existing actors.");
            area = Field<Rectangle>(game, "area");
            Require(area.Width >= 1000 && area.Height >= 700, "The actual working area is too small for this unscaled dense fixture.");
            Size heroSize = game.DesktopHeroWindow.Size;
            PointF heroFeet = new PointF(area.Left + area.Width * .5f, area.Top + area.Height * .67f);
            Set(game, "x", (double)heroFeet.X - heroSize.Width / 2.0); Set(game, "y", (double)heroFeet.Y - heroSize.Height);
            // Use the unchanged virtual factory for every normal actor's kind,
            // HP, elite flag and level; adjust its initial geometry only.
            for (int i = 0; i < 6; i++) Call(game, "SpawnVirtualFiend");
            ordinary = game.Battle.Enemies.ToArray();
            foreach (AdventureEnemy enemy in ordinary) PlaceInitially(enemy, heroFeet);
            boss = EnemyBalance.MemoryBoss(game.Battle.Progress, game.Battle.Weapon, Field<Random>(game, "random"));
            Call(game, "Spawn", boss); PlaceInitially(boss, heroFeet); game.Battle.Enemies.Add(boss);
            Require(game.Battle.Authorize(boss) && game.Battle.Target == boss, "The last-appended exact memory Boss must receive normal Manual combat authorization.");
            report["scope"] = Scope; report["startedUtc"] = DateTime.UtcNow.ToString("o"); report["profile"] = profile;
            report["sourceProfileCloneManifest"] = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile-clone.json");
            report["productionSourceManifest"] = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "production-sources.json");
            report["nativeInputs"] = 0; report["clockMultiplier"] = 1; report["workingArea"] = area.ToString();
            report["foregroundBefore"] = foreground.ToInt64(); report["ordinaryIds"] = ordinary.Select(e => e.Id).ToArray(); report["bossId"] = boss.Id;
            report["initialLayout"] = "Hero feet at actual working-area (50%,67%); six production virtual fiends share feet at hero feet minus 80 x; factory memory Boss appended last at the same enemy feet. Initial placement only, no subsequent teleport.";
            report["initialState"] = State(); report["checks"] = checks; report["captures"] = captures; report["resourceSamples"] = samples;
            game.ConfigureDesktopDemo(); // Starts visible production actors; normal timer was never paused.
            wall.Start(); self.Refresh(); cpuStart = self.TotalProcessorTime.TotalMilliseconds;
            Sample(); WriteReport(false); timer.Tick += Tick; timer.Start();
        }

        void PlaceInitially(AdventureEnemy enemy, PointF heroFeet)
        {
            Size size = (Size)Call(game, "EnemySize", enemy);
            enemy.X = heroFeet.X - 80 - size.Width / 2.0; enemy.Y = heroFeet.Y - size.Height;
            enemy.TargetX = enemy.X + 200; enemy.TargetY = enemy.Y;
            Call(game, "BoundEnemy", enemy);
        }
        static void Require(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
        object State()
        {
            AdventureProgress p = game.Battle.Progress;
            return new { hero = new { x = Field<double>(game, "x"), y = Field<double>(game, "y"), p.Health, p.MaxHealth, p.Dead, p.Level, p.Experience, p.Score, p.FreedBytes },
                cultivation = new { game.Cultivation.Mortal, game.Cultivation.RealmIndex, game.Cultivation.Layer, game.Cultivation.Qi, game.Cultivation.LifetimeQi },
                automatic = game.Battle.Automatic, targetId = game.Battle.Target == null ? null : game.Battle.Target.Id,
                maintenanceOff = game.DesktopAcceptanceMaintenanceOff(), workersIdle = game.DesktopRealWorkIdle,
                enemies = game.Battle.Enemies.Select(e => new { e.Id, e.MonsterKind, e.IsBoss, e.IsVirtual, e.Elite, e.X, e.Y, e.TargetX, e.TargetY,
                    e.MoveSpeed, e.Health, e.MaxHealth, e.SpawnLevel, e.Authorized, authorization = e.Authorization.ToString(), e.QueueNumber, e.Defeated, e.VisualDefeated, e.AttackTime }).ToArray() };
        }
        void Sample()
        {
            self.Refresh(); double cpu = self.TotalProcessorTime.TotalMilliseconds, gdi = GetGuiResources(self.Handle, 0), user = GetGuiResources(self.Handle, 1), handles = self.HandleCount;
            peakCpu = Math.Max(peakCpu, cpu); peakGdi = Math.Max(peakGdi, gdi); peakUser = Math.Max(peakUser, user); peakHandles = Math.Max(peakHandles, handles);
            samples.Add(new { seconds = wall.Elapsed.TotalSeconds, cpuTotalMs = cpu, cpuObservationMs = cpu - cpuStart, gdi, user, handles, workingSetBytes = self.WorkingSet64,
                privateBytes = self.PrivateMemorySize64, foreground = GetForegroundWindow().ToInt64(), state = State() });
        }
        void Tick(object sender, EventArgs args)
        {
            if (busy || finished) return; busy = true;
            try
            {
                Require(wall.Elapsed.TotalSeconds < 23, "Observation exceeded the owned child's bounded run.");
                Require(game.DesktopAcceptanceMaintenanceOff() && game.DesktopRealWorkIdle && !game.DesktopObservesSystem, "Maintenance/observation boundary changed.");
                if (wall.Elapsed.TotalSeconds >= nextSample) { Sample(); nextSample = wall.Elapsed.TotalSeconds + 1; }
                if (!capturedNaturalEffect && wall.Elapsed.TotalSeconds >= 4
                    && Field<AdventureEffects>(game, "effects").ActiveWindows.Any(w => !w.IsDisposed && w.Visible && w.IsHandleCreated))
                {
                    capturedNaturalEffect = true; Capture("00-first-natural-effect-after-4s", 4); return;
                }
                if (!captured8 && wall.Elapsed.TotalSeconds >= 8) { captured8 = true; Capture("01-crowded-at-8s", 8); }
                if (!captured12 && wall.Elapsed.TotalSeconds >= 12) { captured12 = true; Capture("02-crowded-at-12s", 12); Finish(null); }
            }
            catch (Exception error) { Finish((error.InnerException ?? error).ToString()); }
            finally { busy = false; }
        }

        void Capture(string name, double requestedSeconds)
        {
            // Synchronous capture of already presented frames: no DoEvents,
            // actor hide/show, artificial repaint, timer pause or time-mark reset.
            List<ArtDemoActorFrame> actors = game.SnapshotArtDemoActors();
            KnightLayeredWindow[] effectWindows = Field<AdventureEffects>(game, "effects").ActiveWindows;
            ArtDemoActorFrame[] effectFrames = actors.Where(a => effectWindows.Contains(a.Window)).ToArray();
            object[] effectStates = ((System.Collections.IEnumerable)Field<object>(Field<AdventureEffects>(game, "effects"), "shots")).Cast<object>()
                .Select(s => (object)new { handle = Field<KnightLayeredWindow>(s, "Window").Handle.ToInt64(), weapon = Field<string>(s, "Weapon"),
                    age = Field<double>(s, "Age"), duration = Field<double>(s, "Duration"), from = Field<PointF>(s, "From").ToString(), to = Field<PointF>(s, "To").ToString() }).ToArray();
            var entries = new List<object>(); double at = wall.Elapsed.TotalSeconds, captureCpuStart = self.TotalProcessorTime.TotalMilliseconds;
            bool visibleEffectPixels = false;
            try
            {
                using (Bitmap actual = DesktopFrameCapture.CaptureRegion(area))
                {
                    actual.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                    foreach (ArtDemoActorFrame actor in actors)
                    {
                        int sourceOpaque = 0, ownCovered = 0, samplesCount = 0, matches = 0, rootMatches = 0, compositedSamples = 0, unverifiableTranslucent = 0, coveredTranslucent = 0, opaqueVisible = 0, opaqueMatches = 0;
                        bool combatEffect = effectWindows.Contains(actor.Window);
                        HashSet<int> colors = new HashSet<int>(), opaqueColors = new HashSet<int>();
                        foreach (Point point in OpaquePoints(actor, combatEffect))
                        {
                            Color source = actor.Frame.GetPixel(point.X, point.Y), expected = source;
                            if (source.A >= 245) sourceOpaque++; int sx = actor.Bounds.X + point.X, sy = actor.Bounds.Y + point.Y;
                            bool covered = actors.Any(a => a != actor && a.ZOrder < actor.ZOrder && a.Bounds.Contains(sx, sy) && a.Frame.GetPixel(sx - a.Bounds.X, sy - a.Bounds.Y).A >= 20);
                            if (covered) { if (source.A >= 245) ownCovered++; else coveredTranslucent++; continue; }
                            if (!area.Contains(sx, sy)) continue;
                            if (combatEffect && source.A < 245)
                            {
                                if (!OwnedComposite(actor, actors, sx, sy, source, out expected)) { unverifiableTranslucent++; continue; }
                                compositedSamples++;
                            }
                            samplesCount++; Color pixel = actual.GetPixel(sx - area.X, sy - area.Y);
                            colors.Add((expected.R >> 4) << 8 | (expected.G >> 4) << 4 | (expected.B >> 4));
                            bool match = Math.Abs(expected.R - pixel.R) + Math.Abs(expected.G - pixel.G) + Math.Abs(expected.B - pixel.B) <= 45;
                            if (match) matches++;
                            if (source.A >= 245) { opaqueVisible++; if (match) opaqueMatches++; opaqueColors.Add((source.R >> 4) << 8 | (source.G >> 4) << 4 | (source.B >> 4)); }
                            if (RootAt(sx, sy) == actor.Window.Handle) rootMatches++;
                        }
                        double ratio = samplesCount == 0 ? 0 : matches / (double)samplesCount;
                        bool decoration = actor.Role == "decoration", passThrough = actor.Window.PassThrough;
                        string rootPolicy = decoration ? "pass-through-decoration: zero samples hit this window" : "interactive-actor: every visible sample hits this exact window";
                        bool rootPass = decoration ? passThrough && rootMatches == 0 : rootMatches == samplesCount;
                        bool pass = (combatEffect || sourceOpaque >= 100) && samplesCount >= 100 && colors.Count >= 8 && ratio >= .90 && rootPass;
                        bool strictOpaqueEffectProof = combatEffect && opaqueVisible >= 100 && opaqueColors.Count >= 8 && opaqueMatches >= opaqueVisible * .90 && rootPass;
                        if (combatEffect && pass && strictOpaqueEffectProof) visibleEffectPixels = true;
                        string id = actor.Window == game.DesktopHeroWindow ? "hero" : game.DesktopEnemyWindows.FirstOrDefault(p => p.Value == actor.Window).Key;
                        entries.Add(new { id, role = actor.Role, handle = actor.Window.Handle.ToInt64(), title = actor.Window.Text, bounds = actor.Bounds.ToString(), actor.ZOrder,
                            sourceOpaqueSamples = sourceOpaque, ownCoveredSamples = ownCovered, ownCoverage = sourceOpaque == 0 ? 0 : ownCovered / (double)sourceOpaque,
                            visibleSamples = samplesCount, matchingSamples = matches, rootMatches, rootPolicy, passThrough, combatEffect, compositedSamples, unverifiableTranslucent, coveredTranslucent,
                            opaqueVisible, opaqueMatches, opaqueDistinctColors = opaqueColors.Count, strictOpaqueEffectProof,
                            comparisonMode = combatEffect ? "A>=245 source RGB; lower alpha only RGBA-composited over native-hit-verified owned A=255 actor; unknown desktop skipped" : "original A>=245 source RGB",
                            distinctColors = colors.Count, matchRatio = ratio, passed = pass, rgbToleranceSum = 45, minimumRatio = .90 });
                        Check(pass, name + ": exact presented actor pixels/native root " + id + " samples=" + samplesCount + " matches=" + matches + " roots=" + rootMatches + "; " + rootPolicy + "; PassThrough=" + passThrough);
                    }
                }
                Check(actors.Count(a => a.Role == "enemy") == 7 && actors.Count(a => a.Role == "hero") == 1, name + ": all initial hero + six ordinary + last-appended Boss remain visibly present");
                ArtDemoActorFrame hero = actors.FirstOrDefault(a => a.Window == game.DesktopHeroWindow);
                foreach (ArtDemoActorFrame effect in effectFrames)
                    Check(hero != null && effect.Window.PassThrough && effect.ZOrder < hero.ZOrder,
                        name + ": exact real combat effect is pass-through and in front of hero with protected alpha zones; effect=" + effect.Window.Handle + ", Z=" + effect.ZOrder + ", heroZ=" + (hero == null ? -1 : hero.ZOrder));
                var effectProtection = new
                {
                    face = EffectCoverage(name + " face", hero, effectFrames, new Rectangle(0, 25, 220, 85)),
                    topHint = EffectCoverage(name + " top hint", hero, effectFrames, new Rectangle(0, 0, 220, 32)),
                    bottomHud = EffectCoverage(name + " bottom HUD", hero, effectFrames, new Rectangle(0, 200, 220, 40)),
                    denominator = "All original hero alpha>=20 pixels inside each fixed local rectangle, step 1; never subtract covered pixels. Only effects in front by actual native Z can cover."
                };
                if (visibleEffectPixels) capturesWithVisibleEffect++;
                KnightLayeredWindow bossWindow; game.DesktopEnemyWindows.TryGetValue(boss.Id, out bossWindow);
                ArtDemoActorFrame bossFrame = actors.FirstOrDefault(a => a.Window == bossWindow);
                ArtDemoActorFrame[] peers = actors.Where(a => a.Role == "enemy" && a != bossFrame).ToArray();
                double head = CoveredFraction(hero, peers, new Rectangle(0, 0, 220, 132));
                double hud = CoveredFraction(hero, peers, new Rectangle(0, 200, 220, 40));
                double bossCover = CoveredFraction(bossFrame, peers, new Rectangle(0, 0, 190, 220));
                double peerCover = peers.Length == 0 ? 1 : peers.Select(p => CoveredFraction(p, peers.Where(o => o != p).ToArray(), new Rectangle(Point.Empty, p.Frame.Size))).Max();
                var coverage = new { heroHead = head, heroHud = hud, bossByOrdinary = bossCover, worstOrdinaryByOtherOrdinary = peerCover,
                    denominator = "Each region's full original alpha>=20 source samples, step 2; no surviving-visible denominator or Z-order exclusion" };
                if (requestedSeconds >= 8)
                    Check(head <= .05 && hud <= .05 && bossCover <= .10 && peerCover <= .20, name + ": unchanged spacing thresholds on original alpha; head=" + head.ToString("F3") + ", HUD=" + hud.ToString("F3") + ", Boss=" + bossCover.ToString("F3") + ", peers=" + peerCover.ToString("F3"));
                Check(!game.Battle.Progress.Dead && !boss.Defeated && !boss.VisualDefeated && game.Battle.Target == boss, name + ": natural battle remains living with the exact original Boss selected");
                Check(GetForegroundWindow() == foreground, name + ": foreground remains unchanged without any activation call");
                captures.Add(new { image = name + ".png", requestedSeconds, actualSeconds = at, bounds = area.ToString(), capture = "Unmodified native desktop BitBlt SRCCOPY|CAPTUREBLT; raw local only", actors = entries, coverage, effectProtection,
                    ordinarySpacingDeadlineApplied = requestedSeconds >= 8, actualCombatEffectCount = effectFrames.Length, effectStates, visibleEffectPixels,
                    effectObservation = name.StartsWith("00-", StringComparison.Ordinal) ? "First natural effect observed at/after 4s; no Fire or state/timer injection; ordinary convergence remains due at its original 8s deadline" : "Normal scheduled snapshot", state = State() });
            }
            finally
            {
                foreach (ArtDemoActorFrame actor in actors) actor.Dispose();
                self.Refresh(); captureCpuMs += self.TotalProcessorTime.TotalMilliseconds - captureCpuStart; captureWallSeconds += wall.Elapsed.TotalSeconds - at; WriteReport(false);
            }
        }
        object EffectCoverage(string label, ArtDemoActorFrame hero, ArtDemoActorFrame[] effects, Rectangle region)
        {
            int source = 0, covered = 0;
            ArtDemoActorFrame[] front = hero == null ? effects : effects.Where(e => e.ZOrder < hero.ZOrder).ToArray();
            if (hero != null)
            {
                region = Rectangle.Intersect(region, new Rectangle(Point.Empty, hero.Frame.Size));
                for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
                {
                    if (hero.Frame.GetPixel(x, y).A < 20) continue;
                    source++; int sx = hero.Bounds.X + x, sy = hero.Bounds.Y + y;
                    foreach (ArtDemoActorFrame effect in front)
                        if (effect.Bounds.Contains(sx, sy) && effect.Frame.GetPixel(sx - effect.Bounds.X, sy - effect.Bounds.Y).A >= 20) { covered++; break; }
                }
            }
            Check(source > 0 && covered == 0, label + ": zero actual-Z foreground-effect coverage of full original alpha; covered=" + covered + "/" + source);
            return new { fixedHeroLocalBounds = region.ToString(), originalAlphaPixels = source, coveredPixels = covered, fraction = source == 0 ? 1 : covered / (double)source, foregroundEffectCount = front.Length };
        }
        static double CoveredFraction(ArtDemoActorFrame victim, ArtDemoActorFrame[] others, Rectangle region)
        {
            if (victim == null) return 1;
            int total = 0, covered = 0; region = Rectangle.Intersect(region, new Rectangle(Point.Empty, victim.Frame.Size));
            for (int y = region.Top; y < region.Bottom; y += 2) for (int x = region.Left; x < region.Right; x += 2)
            {
                if (victim.Frame.GetPixel(x, y).A < 20) continue;
                total++; int sx = victim.Bounds.X + x, sy = victim.Bounds.Y + y;
                if (others.Any(o => o.Bounds.Contains(sx, sy) && o.Frame.GetPixel(sx - o.Bounds.X, sy - o.Bounds.Y).A >= 20)) covered++;
            }
            return total == 0 ? 1 : covered / (double)total;
        }
        static bool OwnedComposite(ArtDemoActorFrame actor, IList<ArtDemoActorFrame> actors, int x, int y, Color source, out Color expected)
        {
            expected = Color.Empty; IntPtr hit = IntPtr.Zero;
            foreach (ArtDemoActorFrame lower in actors.Where(a => a.ZOrder > actor.ZOrder && a.Bounds.Contains(x, y)).OrderByDescending(a => a.ZOrder))
            {
                Color pixel = lower.Frame.GetPixel(x - lower.Bounds.X, y - lower.Bounds.Y);
                if (pixel.A == 255 && (lower.Role == "hero" || lower.Role == "enemy")) expected = Color.FromArgb(pixel.R, pixel.G, pixel.B);
                else if (!expected.IsEmpty) expected = Over(pixel, expected);
                if (pixel.A >= 20 && !lower.Window.PassThrough) hit = lower.Window.Handle;
            }
            if (expected.IsEmpty || hit == IntPtr.Zero || RootAt(x, y) != hit) return false;
            expected = Over(source, expected); return true;
        }
        static Color Over(Color a, Color b)
        {
            int inverse = 255 - a.A;
            return Color.FromArgb((a.R * a.A + b.R * inverse + 127) / 255, (a.G * a.A + b.G * inverse + 127) / 255, (a.B * a.A + b.B * inverse + 127) / 255);
        }
        static IEnumerable<Point> OpaquePoints(ArtDemoActorFrame actor, bool includeTranslucent)
        {
            for (int y = 0; y < actor.Frame.Height; y += 2)
                for (int x = 0; x < actor.Frame.Width; x += 2)
                    if (actor.Frame.GetPixel(x, y).A >= (includeTranslucent ? 1 : 245)) yield return new Point(x, y);
        }
        void Finish(string error)
        {
            if (finished) return; finished = true; timer.Stop();
            if (error != null) errors.Add(error);
            Check(capturedNaturalEffect && captured8 && captured12 && captures.Count == 3, "Natural effect after 4s and actual 8s/12s native desktop observations were all retained.");
            Check(capturesWithVisibleEffect >= 1, "At least one retained native screenshot contains an actual production effect with the unchanged pixel/root gates satisfied.");
            Check(GetForegroundWindow() == foreground, "Foreground before/after the complete observation is identical.");
            Check(game.DesktopAcceptanceMaintenanceOff() && game.DesktopRealWorkIdle && !owner.Visible, "All maintenance/scans remain off and the host remains hidden.");
            Sample(); report["finalState"] = State(); report["privateFiveSavesSucceeded"] = game.DesktopSaveNow();
            if (!(bool)report["privateFiveSavesSucceeded"]) errors.Add("Private five-archive save failed.");
            WriteReport(errors.Count == 0); ExitCode = errors.Count == 0 ? 0 : 1;
            Console.WriteLine("Crowding native passed=" + (ExitCode == 0) + "; seconds=" + wall.Elapsed.TotalSeconds.ToString("F3"));
            deadline.Dispose(); context.ExitThread();
        }
        void WriteReport(bool passed)
        {
            report["passed"] = passed; report["errors"] = errors.ToArray(); report["elapsedSeconds"] = wall.Elapsed.TotalSeconds;
            report["foregroundAfter"] = GetForegroundWindow().ToInt64(); report["cpuObservationMs"] = self.TotalProcessorTime.TotalMilliseconds - cpuStart;
            report["averageLogicalCoreFraction"] = wall.Elapsed.TotalSeconds <= 0 ? 0 : (self.TotalProcessorTime.TotalMilliseconds - cpuStart) / wall.Elapsed.TotalMilliseconds;
            report["captureValidationCpuMs"] = captureCpuMs; report["captureValidationWallSeconds"] = captureWallSeconds;
            report["capturesWithVisibleEffect"] = capturesWithVisibleEffect; report["expectedCaptures"] = 3;
            report["cpuScope"] = "Process CPU includes fixture capture/validation. The approximately one-second samples before each capture and explicit capture costs distinguish that work from production Tick/Move; no eight-hour or baseline performance claim.";
            report["resourcePeaks"] = new { cumulativeCpuMs = peakCpu, gdi = peakGdi, user = peakUser, handles = peakHandles, sampleInterval = "about one wall-clock second plus start/end; short observation only" };
            File.WriteAllText(Path.Combine(output, "crowding-native.json"), new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }.Serialize(report));
        }
        public void Dispose() { timer.Stop(); timer.Dispose(); deadline.Dispose(); game.Dispose(); owner.Dispose(); self.Dispose(); }
    }
}
