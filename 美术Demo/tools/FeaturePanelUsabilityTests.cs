using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Bounded hidden-window fixture. All writes stay under the supplied fresh
    // verification directory; no message loop, desktop actor, or live archive.
    internal static class FeaturePanelUsabilityTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool okay, string message) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); if (!okay) failures++; }
        static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, Hidden).GetValue(target); }
        static Control[] Descendants(Control root) { return root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c))).ToArray(); }
        static bool OwnVisible(Control control) { return (bool)typeof(Control).GetMethod("GetState", Hidden).Invoke(control, new object[] { 2 }); }
        static void Layout(Control root) { IntPtr handle = root.Handle; foreach (Control child in root.Controls) Layout(child); root.PerformLayout(); }
        static Rectangle InForm(Control child, Form form) { return new Rectangle(form.PointToClient(child.PointToScreen(Point.Empty)), child.Size); }
        static Button Action(DongtianForm form, string label) { return form.ActionControlsSnapshot().OfType<Button>().FirstOrDefault(b => b.Text == label); }
        static void Click(Button button) { typeof(Button).GetMethod("OnClick", Hidden).Invoke(button, new object[] { EventArgs.Empty }); }
        static void Inspect(Form form, string name, string output)
        {
            Size[] sizes = { new Size(700, 520), new Size(390, 490) };
            foreach (Size size in sizes)
            {
                form.ClientSize = size;
                for (int i = 0; i < 3; i++) Layout(form);
                FlowLayoutPanel column = (FlowLayoutPanel)form.Controls[0]; column.AutoScrollPosition = Point.Empty; Layout(form);
                Check(form.ClientSize == size, name + " accepts client size " + size);
                Check(column.Controls.Cast<Control>().All(c => c.Right + c.Margin.Right <= column.ClientSize.Width - column.Padding.Right + 1), name + " fits top-level content at " + size);
                Check(column.DisplayRectangle.Width <= column.ClientSize.Width + 1, name + " needs only vertical scrolling at " + size);
                Check(Descendants(column).OfType<FlowLayoutPanel>().All(row => row.Controls.Cast<Control>().Where(OwnVisible).All(c => c.Right + c.Margin.Right <= row.ClientSize.Width - row.Padding.Right + 1)), name + " wraps every visible action row at " + size);
                Check(Descendants(column).OfType<Label>().All(label => label.GetPreferredSize(new Size(label.Width, 0)).Height <= label.Height + 2), name + " shows wrapped explanatory text at " + size);
                Control[] visible = column.Controls.Cast<Control>().Where(OwnVisible).ToArray();
                Check(visible.Zip(visible.Skip(1), (before, after) => before.Bottom <= after.Top).All(ok => ok), name + " keeps consecutive sections from overlapping at " + size);
                using (Bitmap full = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                {
                    form.DrawToBitmap(full, new Rectangle(Point.Empty, form.Size));
                    Point client = form.PointToScreen(Point.Empty);
                    using (Bitmap image = full.Clone(new Rectangle(client.X - form.Left, client.Y - form.Top, size.Width, size.Height), PixelFormat.Format32bppArgb))
                        image.Save(Path.Combine(output, name + "-" + size.Width + "x" + size.Height + "-hidden.png"), ImageFormat.Png);
                }
            }
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                string profile = Path.Combine(output, "private-profile-" + Guid.NewGuid().ToString("N"));
                using (Form owner = new Form { ShowInTaskbar = false })
                using (AdventureController controller = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, profile))
                {
                    Field<Timer>(controller, "timer").Stop();
                    controller.Cultivation.Mortal = false; controller.Cultivation.RealmIndex = 1;
                    Form[] forms = { new DongtianForm(controller), new BaiyeSteleForm(controller), new BreakthroughForm(controller), new LingyuForm(controller) };
                    string[] names = { "dongtian", "baiye", "breakthrough", "lingyu" };
                    try
                    {
                        foreach (Form form in forms) { Field<Timer>(form, "refresh").Stop(); form.ShowInTaskbar = false; form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-25000, -25000); form.GetType().GetMethod("RefreshData", Hidden).Invoke(form, null); }
                        DongtianForm cave = (DongtianForm)forms[0];
                        cave.ClientSize = new Size(700, 520); Layout(cave);
                        Button seed = cave.ActionControlsSnapshot().OfType<Button>().First(b => b.Text.StartsWith("种灵谷", StringComparison.Ordinal));
                        Check(cave.ClientRectangle.Contains(InForm(seed, cave)), "first viewport exposes planting in an empty newly unlocked cave");
                        Button pill = Action(cave, "炼聚气散");
                        Check(pill != null && !pill.Enabled, "insufficient real inventory disables alchemy before a click");
                        Check(Descendants(cave).OfType<Label>().Any(l => l.Text.Contains("吐纳修为 +25%") && l.Text.Contains("灵谷") && l.Text.Contains("缺")), "recipe benefit, ingredients, and shortage are readable without hover");
                        Control[] original = cave.ActionControlsSnapshot();
                        for (int i = 0; i < 12; i++) cave.RefreshData();
                        Check(original.SequenceEqual(cave.ActionControlsSnapshot()), "unchanged refreshes preserve all button instances");
                        controller.Dongtian.Bag["linggu"] = 2; cave.RefreshData();
                        Check(Object.ReferenceEquals(pill, Action(cave, "炼聚气散")) && pill.Enabled, "inventory refresh enables the existing recipe button in place");
                        typeof(AdventureController).GetField("dongtianPersistPending", Hidden).SetValue(controller, true); cave.RefreshData();
                        Check(cave.ActionControlsSnapshot().All(c => !c.Enabled), "pending archive disables every new cave operation");
                        Check(Object.ReferenceEquals(pill, Action(cave, "炼聚气散")), "pending state retains the same action controls");
                        Click(pill);
                        Check(controller.Dongtian.CountOf("linggu") == 2 && controller.Dongtian.Furnace == null, "a queued disabled click cannot consume inventory while pending");
                        typeof(AdventureController).GetField("dongtianPersistPending", Hidden).SetValue(controller, false); cave.RefreshData(); Click(pill);
                        Check(controller.Dongtian.Furnace != null && controller.Dongtian.CountOf("linggu") == 0, "an enabled recipe still uses the real controller and consumes its original recipe once");
                        controller.Dongtian.Fields[0] = "linggu,1"; cave.RefreshData(); Layout(cave);
                        Button harvest = Action(cave, "收获田1");
                        Check(harvest != null && cave.ClientRectangle.Contains(InForm(harvest, cave)), "mature harvest appears in the first viewport");
                        Click(harvest);
                        Check(controller.Dongtian.Fields[0] == null && controller.Dongtian.CountOf("linggu") == 2, "harvest uses the original yield and clears only its real field");
                        cave.RefreshData();
                        for (int i = 0; i < forms.Length; i++) Inspect(forms[i], names[i], output);
                        Check(controller.Baiye.Mode == 0 && !controller.Baiye.FirstRunExplained, "layout inspection never authorizes activity recognition");
                        Check(!owner.Visible && forms.All(f => !f.Visible) && !Field<KnightLayeredWindow>(controller, "heroWindow").Visible, "all UI fixtures and desktop actors remain hidden");
                    }
                    finally { foreach (Form form in forms) form.Dispose(); }
                }
                Console.WriteLine("Feature panel usability: " + checks + " checks, " + failures + " failures. Images are hidden-window previews only.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
