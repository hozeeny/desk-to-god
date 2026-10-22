using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Short, opt-in, new-profile native battle. No HP, attack timer, reward,
    // cultivation, maintenance, or clock multiplier is injected.
    internal sealed class DesktopCombatFeedbackNative
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        readonly DesktopDemoSession session;
        readonly DesktopDemoAcceptance native;
        readonly Timer timer = new Timer { Interval = 40 };
        readonly Stopwatch wall = Stopwatch.StartNew();
        readonly Dictionary<string, object> report = new Dictionary<string, object>();
        readonly string output;
        string targetId;
        int phase, healthBefore;
        bool busy, finished;
        internal int ExitCode { get; private set; }
        object Call(string name, params object[] args) { return typeof(DesktopDemoAcceptance).GetMethod(name, Hidden).Invoke(native, args); }
        void CaptureClosedPanelResult(AdventureController game)
        {
            string text = (string)typeof(AdventureController).GetProperty("DesktopHudHint", Hidden).GetValue(game, null);
            if (!text.Contains("挡下") || Application.OpenForms.OfType<DesktopPracticePanel>().Any(f => f.Visible))
                throw new InvalidOperationException("Real guard result must be on the desktop with handbook closed.");
            KnightLayeredWindow hero = game.DesktopHeroWindow;
            int samples = 0, matches = 0;
            using (Bitmap expected = new Bitmap(hero.Width, hero.Height))
            using (Bitmap actual = DesktopFrameCapture.CaptureRegion(hero.Bounds))
            {
                using (Graphics g = Graphics.FromImage(expected))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    float scale = Math.Min(hero.Width / 220f, hero.Height / 220f);
                    g.TranslateTransform((hero.Width - 220 * scale) / 2, (hero.Height - 220 * scale) / 2); g.ScaleTransform(scale, scale);
                    object renderer = typeof(AdventureController).GetField("renderer", Hidden).GetValue(game);
                    typeof(AdventureRenderer).GetMethod("DrawActionHint", Hidden).Invoke(renderer, new object[] { g, text });
                }
                Color ink = Color.FromArgb(247, 241, 218);
                for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
                {
                    Color p = expected.GetPixel(x, y);
                    if (p.A < 245 || Math.Abs(p.R - ink.R) + Math.Abs(p.G - ink.G) + Math.Abs(p.B - ink.B) > 25) continue;
                    Color pixel = actual.GetPixel(x, y); samples++;
                    if (Math.Abs(pixel.R - p.R) + Math.Abs(pixel.G - p.G) + Math.Abs(pixel.B - p.B) <= 45) matches++;
                }
                actual.Save(Path.Combine(output, "02a-closed-panel-guard-hud.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            report["closedPanelHud"] = new { text, samples, matches, minimumSamples = 60, minimumRatio = .8, rgbToleranceSum = 45,
                image = "02a-closed-panel-guard-hud.png", bounds = hero.Bounds.ToString(), handbookClosed = true,
                capture = "Unmodified native desktop pixels matched to exact result glyphs; no actor hide, timer pause or game-state injection" };
            if (samples < 60 || matches < samples * .8) throw new InvalidOperationException("Closed-panel guard HUD glyphs were not actually readable.");
        }
        void VerifyIdleWindowsClear(AdventureController game, string moment)
        {
            var windows=game.DesktopEnemyWindows;int idle=0,overlap=0;
            foreach(AdventureEnemy enemy in game.Battle.Enemies)
            {
                KnightLayeredWindow window;
                if(enemy.Id==targetId||enemy.Authorized||enemy.Defeated||enemy.VisualDefeated||enemy.IsVisuallyHidden
                    ||!windows.TryGetValue(enemy.Id,out window)||!window.Visible)continue;
                idle++;if(window.Bounds.IntersectsWith(game.DesktopHeroWindow.Bounds))overlap++;
            }
            report[moment+"VisibleIdleActors"]=idle;report[moment+"IdleHeroWindowIntersections"]=overlap;
            if(overlap>0)throw new InvalidOperationException("Idle actor still overlaps the complete native hero/HUD window at "+moment+".");
        }
        [STAThread] static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[2]);
            if (Directory.Exists(output)) return 2;
            Directory.CreateDirectory(output);
            try
            {
                SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[1]));
                using (DesktopDemoSession session = new DesktopDemoSession(Path.GetFullPath(args[0]), Path.Combine(output, "private-profile"), true))
                {
                    DesktopCombatFeedbackNative probe = new DesktopCombatFeedbackNative(session, output);
                    Application.Run(session); return probe.ExitCode;
                }
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
        DesktopCombatFeedbackNative(DesktopDemoSession session, string output)
        {
            this.session = session; this.output = output; ExitCode = 1;
            native = new DesktopDemoAcceptance(session, output, true);
            report["scope"] = "short normal x1 native manual selection, real enemy windup, real guard click and actual health change; no progression injection or real maintenance";
            report["privateProfile"] = session.ProfileRoot;
            report["startedUtc"] = DateTime.UtcNow.ToString("o");
            timer.Tick += Tick; timer.Start();
        }
        void Tick(object sender, EventArgs e)
        {
            if (busy || finished) return; busy = true;
            try
            {
                if (wall.Elapsed.TotalSeconds > 45) throw new TimeoutException("No real readable attack/guard result within 45 seconds.");
                AdventureController game = session.Game;
                if (!game.DesktopAcceptanceMaintenanceOff()) throw new InvalidOperationException("Maintenance must remain off.");
                if (phase == 0)
                {
                    session.ClosePanels();
                    AdventureEnemy enemy = game.Battle.Enemies.FirstOrDefault(a => a.IsVirtual && !a.Defeated && !a.VisualDefeated);
                    if (enemy == null) return;
                    if (!(bool)Call("ClickEnemy", enemy)) return;
                    targetId = enemy.Id; report["target"] = targetId; phase = 1; return;
                }
                DesktopPlayerView view = game.ReadDesktopPlayerView();
                if (phase == 3)
                {
                    Form handbook = Application.OpenForms.OfType<DesktopPracticePanel>().Single(f => f.Visible);
                    handbook.Refresh();
                    Label notice = (Label)typeof(DesktopPracticePanel).GetField("notice", Hidden).GetValue(handbook);
                    int samples = 0, matches = 0;
                    using (Bitmap expected = new Bitmap(notice.Width, notice.Height))
                    using (Bitmap actual = DesktopFrameCapture.CaptureRegion(new Rectangle(notice.PointToScreen(Point.Empty), notice.Size), true))
                    {
                        notice.DrawToBitmap(expected, new Rectangle(Point.Empty, notice.Size));
                        Color ink = notice.ForeColor;
                        for (int y = 0; y < expected.Height; y++) for (int x = 0; x < expected.Width; x++)
                        {
                            Color source = expected.GetPixel(x, y);
                            if (Math.Abs(source.R - ink.R) + Math.Abs(source.G - ink.G) + Math.Abs(source.B - ink.B) > 25) continue;
                            Color pixel = actual.GetPixel(x, y); samples++;
                            if (Math.Abs(pixel.R - source.R) + Math.Abs(pixel.G - source.G) + Math.Abs(pixel.B - source.B) < 80) matches++;
                        }
                    }
                    if (samples < 60 || matches < samples * .8) return; // Real native glyphs must be presented; white placeholders fail.
                    report["nativeNoticeGlyphSamples"] = samples; report["nativeNoticeGlyphMatches"] = matches;
                    report["nativeNoticeText"] = notice.Text;
                    Call("CapturePanel", handbook, "03-guard-history-handbook");
                    report["captures"] = typeof(DesktopDemoAcceptance).GetField("captures", Hidden).GetValue(native);
                    report["nativeClicks"] = typeof(DesktopDemoAcceptance).GetField("clicks", Hidden).GetValue(native);
                    Finish(null); return;
                }
                if (phase == 1)
                {
                    if (!view.BlockReady || !view.BlockText.Contains("即将出手")) return;
                    healthBefore = game.Battle.Progress.Health;
                    report["warning"] = view.BlockText;
                    report["hud"] = typeof(AdventureController).GetProperty("DesktopHudHint", Hidden).GetValue(game, null);
                    report["warningSeconds"] = wall.Elapsed.TotalSeconds;
                    VerifyIdleWindowsClear(game,"warning");
                    Call("Capture", "01-real-windup-before-impact", true, false, false);
                    if (!(bool)Call("ClickActor", game.DesktopHeroWindow)) return;
                    report["healthBeforeGuard"] = healthBefore; phase = 2; return;
                }
                if (!view.RecentEvents.Any(s => s.Contains("格挡成功"))) return;
                string result = view.RecentEvents.First(s => s.Contains("格挡成功"));
                if (game.Battle.Progress.Health >= healthBefore || game.Battle.Progress.Dead) throw new InvalidOperationException("Expected original positive post-guard damage, not an injected no-hit result.");
                report["result"] = result; report["healthAfterGuard"] = game.Battle.Progress.Health;
                VerifyIdleWindowsClear(game,"guardResult");
                CaptureClosedPanelResult(game);
                Call("Capture", "02-real-guard-result", true, false, false);
                session.OpenPanel("practice");
                phase = 3;
            }
            catch (Exception error) { Finish((error is TargetInvocationException && error.InnerException != null ? error.InnerException : error).ToString()); }
            finally { busy = false; }
        }
        void Finish(string error)
        {
            if (finished) return; finished = true; timer.Stop(); timer.Dispose();
            report["passed"] = error == null; report["error"] = error; report["elapsedSeconds"] = wall.Elapsed.TotalSeconds;
            report["fiveSavesSucceeded"] = session.Game.DesktopSaveNow();
            if (!(bool)report["fiveSavesSucceeded"]) { report["passed"] = false; report["error"] = "Private five-save failed."; }
            File.WriteAllText(Path.Combine(output, "combat-feedback-native.json"), new JavaScriptSerializer().Serialize(report));
            ExitCode = (bool)report["passed"] ? 0 : 1;
            Console.WriteLine("Native combat feedback passed=" + report["passed"] + "; seconds=" + wall.Elapsed.TotalSeconds.ToString("F2"));
            session.ExitAfterAcceptance(ExitCode);
        }
    }
}
