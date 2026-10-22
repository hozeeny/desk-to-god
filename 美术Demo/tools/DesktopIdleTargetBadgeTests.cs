using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetBunny
{
    // Real RenderWorld/Present frames in a stopped, private Virtual controller.
    // Actor windows stay outside every physical screen; no input or game ticks.
    internal static class DesktopIdleTargetBadgeTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        static int checks, failures;
        static T Field<T>(object item, string name) { return (T)item.GetType().GetField(name, Hidden).GetValue(item); }
        static void Set(object item, string name, object value) { item.GetType().GetField(name, Hidden).SetValue(item, value); }
        static void Check(bool okay, string detail) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + detail); }
        static void Render(AdventureController game)
        { typeof(AdventureController).GetMethod("RenderWorld", Hidden).Invoke(game, new object[] { true }); }
        static Bitmap Frame(AdventureController game, AdventureEnemy enemy)
        { return Field<Bitmap>(game.DesktopEnemyWindows[enemy.Id], "current"); }
        static Rectangle Badge(Bitmap frame)
        {
            float scale = Math.Min(frame.Width / 220f, frame.Height / 220f), dx = (frame.Width - 220 * scale) / 2, dy = (frame.Height - 220 * scale) / 2;
            return Rectangle.Intersect(new Rectangle(0, 0, frame.Width, frame.Height), Rectangle.FromLTRB((int)Math.Floor(dx + 159 * scale) - 1,
                (int)Math.Floor(dy + 41 * scale) - 1, (int)Math.Ceiling(dx + 210 * scale) + 1, (int)Math.Ceiling(dy + 69 * scale) + 1));
        }
        static int Difference(Bitmap before, Bitmap after, bool badgeOnly)
        {
            Rectangle region = badgeOnly ? Badge(after) : new Rectangle(Point.Empty, after.Size); int changed = 0;
            for (int y = region.Top; y < region.Bottom; y++) for (int x = region.Left; x < region.Right; x++)
                if (before.GetPixel(x, y).ToArgb() != after.GetPixel(x, y).ToArgb()) changed++;
            return changed;
        }
        sealed class Host : IDisposable
        {
            internal readonly Form Owner;
            internal readonly AdventureController Game;
            internal readonly AdventureEnemy A, B;
            internal Host(string assets, string root)
            {
                Owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) };
                Game = new AdventureController(Owner, assets, false, null, false, root);
                Field<Timer>(Game, "timer").Stop(); Game.ConfigureDesktopDemo(false); Field<Timer>(Game, "timer").Stop();
                A = Game.Battle.Enemies[0]; B = Game.Battle.Enemies[1];
                Rectangle screenBounds = Screen.AllScreens.Select(s => s.Bounds).Aggregate(Rectangle.Union);
                Rectangle area = new Rectangle(screenBounds.Left - 20000, screenBounds.Top - 20000, 1800, 1000);
                Set(Game, "area", area); Set(Game, "x", area.Left + 80d); Set(Game, "y", area.Top + 120d);
                A.X = area.Left + 700; A.Y = area.Top + 120; B.X = area.Left + 1200; B.Y = area.Top + 120;
                Set(Game, "desktopActorsVisible", true);
            }
            internal void VerifyScope()
            {
                Check(!Field<Timer>(Game, "timer").Enabled && !Game.DesktopObservesSystem && Game.DesktopRealWorkIdle && !Owner.Visible
                    && Game.DesktopEnemyWindows.Values.Concat(new[] { Game.DesktopHeroWindow }).All(window => Screen.AllScreens.All(screen => !screen.Bounds.IntersectsWith(window.Bounds))),
                    "stopped Virtual fixture owns only screen-external actor windows, with no real worker, visible host or game tick");
            }
            public void Dispose() { Game.Dispose(); Owner.Dispose(); }
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string assets = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                IntPtr foreground = GetForegroundWindow();
                using (Host h = new Host(assets, Path.Combine(output, "private-select")))
                {
                    Render(h.Game); h.VerifyScope(); Bitmap original = Frame(h.Game, h.A);
                    using (Bitmap before = (Bitmap)original.Clone())
                    {
                        before.Save(Path.Combine(output, "01-idle-unselected-frame.png"), ImageFormat.Png);
                        int hp = h.A.Health; double cooldown = h.Game.Battle.Cooldown;
                        Check(h.Game.DesktopChooseEnemy(h.A.Id, false) && h.A.QueueNumber == 1 && h.Game.Battle.Target == h.A
                            && h.A.Health == hp && h.Game.Battle.Cooldown == cooldown, "out-of-range selection uses the production entry and changes only combat focus, without a strike");
                        Bitmap after = Frame(h.Game, h.A); after.Save(Path.Combine(output, "02-idle-selected-frame.png"), ImageFormat.Png);
                        Check(!Object.ReferenceEquals(original, after) && Difference(before, after, true) > 20,
                            "a stationary merely-selected enemy immediately gains target-badge pixels in the real presented frame");
                    }
                }
                using (Host h = new Host(assets, Path.Combine(output, "private-preempt")))
                {
                    h.Game.Battle.SetAutomatic(true); Check(h.Game.Battle.PlanNext(Field<double>(h.Game, "x"), Field<double>(h.Game, "y")) == h.A,
                        "real planner establishes the nearest automatic target before a manual preemption");
                    Render(h.Game);
                    using (Bitmap beforeA = (Bitmap)Frame(h.Game, h.A).Clone()) using (Bitmap beforeB = (Bitmap)Frame(h.Game, h.B).Clone())
                    {
                        Check(h.Game.Battle.Authorize(h.B) && h.B.QueueNumber == 1 && h.A.QueueNumber == 2 && h.Game.Battle.Target == h.B,
                            "real manual authorization renumbers the old automatic target instead of cancelling its queue");
                        Render(h.Game);
                        Check(Difference(beforeA, Frame(h.Game, h.A), true) > 20, "stationary old automatic target replaces its target word with its new queue-number pixels");
                        Check(Difference(beforeB, Frame(h.Game, h.B), true) > 20, "stationary new manual target immediately receives target-badge pixels");
                    }
                }
                using (Host h = new Host(assets, Path.Combine(output, "private-cancel")))
                {
                    h.Game.Battle.Authorize(h.A); Render(h.Game);
                    using (Bitmap before = (Bitmap)Frame(h.Game, h.A).Clone())
                    {
                        h.Game.Battle.CancelQueue(); Render(h.Game);
                        Check(h.A.QueueNumber == 0 && h.Game.Battle.Target == null && Difference(before, Frame(h.Game, h.A), true) > 20,
                            "cancelling a stationary target removes its badge immediately through the existing invalidation path");
                    }
                }
                using (Host h = new Host(assets, Path.Combine(output, "private-stable")))
                {
                    h.Game.Battle.Authorize(h.A); Render(h.Game); Bitmap original = Frame(h.Game, h.A);
                    h.Game.Battle.Authorize(h.B); Render(h.Game);
                    Check(h.A.QueueNumber == 1 && h.B.QueueNumber == 2 && Object.ReferenceEquals(original, Frame(h.Game, h.A)),
                        "renumbering another actor does not rerasterize an unchanged stationary target");
                    Bitmap second = Frame(h.Game, h.B); Render(h.Game);
                    Check(Object.ReferenceEquals(original, Frame(h.Game, h.A)) && Object.ReferenceEquals(second, Frame(h.Game, h.B)),
                        "a second frame with unchanged stationary semantics retains both original Bitmap instances");
                }
                Check(GetForegroundWindow() == foreground, "off-screen production rendering and disposal never change foreground focus");
                Console.WriteLine("Idle target badge: " + checks + " checks, " + failures + " failures. Real source frames; no native screenshot, input, HP edit, Advance or production-archive write.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
