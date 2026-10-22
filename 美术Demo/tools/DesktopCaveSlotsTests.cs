using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Private, stopped and hidden real Form/controller. Artificial ready ticks
    // are fixture data, never a natural-growth or elapsed-hours acceptance.
    internal static class DesktopCaveSlotsTests
    {
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool okay, string text) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + text); }
        static T Get<T>(object value, string name) { return (T)value.GetType().GetField(name, F).GetValue(value); }
        static void Set(object value, string name, object field) { value.GetType().GetField(name, F).SetValue(value, field); }
        static Control[] All(Control root) { return root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(All(c))).ToArray(); }
        static Control Named(Control root, string name) { return All(root).FirstOrDefault(c => c.Name == name); }
        static Button Button(DongtianForm form, string text) { return All(form).OfType<Button>().FirstOrDefault(b => b.Text == text); }
        static bool OwnVisible(Control control) { return control != null && (bool)typeof(Control).GetMethod("GetState", F).Invoke(control, new object[] { 2 }); }
        static string TextOf(Control root) { return root == null ? "" : root.Text + "\n" + String.Join("\n", All(root).OfType<Label>().Select(l => l.Text).ToArray()); }
        static void Click(Button button) { if (button != null) typeof(Button).GetMethod("OnClick", F).Invoke(button, new object[] { EventArgs.Empty }); }
        static void Layout(Control root) { IntPtr h = root.Handle; foreach (Control child in root.Controls) Layout(child); root.PerformLayout(); }
        static Rectangle InForm(Control child, Form form) { return new Rectangle(form.PointToClient(child.PointToScreen(Point.Empty)), child.Size); }
        static string Files(string root)
        {
            using (SHA256 sha = SHA256.Create()) return String.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p)
                .Select(p => p.Substring(root.Length) + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))).ToArray());
        }
        static void LayoutCases(DongtianForm form, AdventureController game, string output)
        {
            game.Dongtian.Fields[0] = null; form.RefreshData();
            foreach (Size size in new[] { new Size(700, 520), new Size(390, 490), new Size(470, 700) })
            {
                form.ClientSize = size;
                for (int i = 0; i < 3; i++) Layout(form);
                FlowLayoutPanel column = Get<FlowLayoutPanel>(form, "content"); column.AutoScrollPosition = Point.Empty; Layout(form);
                Check(column.DisplayRectangle.Width <= column.ClientSize.Width + 1, "slot layout only scrolls vertically at " + size);
                Check(All(column).OfType<FlowLayoutPanel>().All(row => row.Controls.Cast<Control>().Where(OwnVisible).All(c => c.Right + c.Margin.Right <= row.ClientSize.Width - row.Padding.Right + 1)), "every slot/action stays inside its row at " + size);
                Check(All(column).OfType<Label>().All(l => l.GetPreferredSize(new Size(l.Width, 0)).Height <= l.Height + 2), "slot and benefit text wraps without clipping at " + size);
                Button seed = Button(form, "种灵谷→田1");
                Check(seed != null && form.ClientRectangle.Contains(InForm(seed, form)), "first viewport still offers a real seed operation at " + size);
                game.Dongtian.Fields[0] = "linggu,1"; form.RefreshData(); Layout(form);
                Button harvest = Button(form, "收获田1");
                Check(harvest != null && OwnVisible(harvest) && form.ClientRectangle.Contains(InForm(harvest, form)), "mature first field can be harvested without scrolling at " + size);
                using (Bitmap image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size)); image.Save(Path.Combine(output, "cave-slots-" + size.Width + "x" + size.Height + "-hidden.png")); }
                game.Dongtian.Fields[0] = null; form.RefreshData();
            }
        }
        [STAThread] static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                string profile = Path.Combine(output, "private-profile");
                using (Form owner = new Form { ShowInTaskbar = false })
                using (AdventureController game = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, profile))
                {
                    Get<Timer>(game, "timer").Stop(); game.Cultivation.Mortal = false; game.Cultivation.RealmIndex = 1;
                    using (DongtianForm form = new DongtianForm(game))
                    {
                        Get<Timer>(form, "refresh").Stop(); form.Location = new Point(-25000, -25000); form.RefreshData();
                        DongtianProgress d = game.Dongtian;
                        Control first = Named(form, "cave-field-0"), second = Named(form, "cave-field-1");
                        Button seed1 = Button(form, "种灵谷→田1"), seed2 = Button(form, "种凝神花→田2");
                        Check(seed1 != null && seed2 != null, "each of the two unlocked empty fields has its own seed operation, including field two first");
                        Check(first != null && second != null && first != second && second.Contains(seed2), "field-two state and its operation are grouped in the same independent row");
                        Check(!OwnVisible(Named(form, "cave-field-2")) && !OwnVisible(Named(form, "cave-field-3")), "locked third/fourth fields do not claim to be usable");
                        Click(seed2); form.RefreshData();
                        Check(d.Fields[0] == null && d.Fields[1] != null && d.Fields[1].StartsWith("ningshen,"), "clicking field two plants only field two through the real controller");
                        Check(d.Fields[1] != null && DongtianProgress.Load(game.DongtianWritePath).Fields[1] == d.Fields[1], "the actual selected field survives private archive reload");
                        Check(Object.ReferenceEquals(second, Named(form, "cave-field-1")) && seed2 != null && Object.ReferenceEquals(seed2, Button(form, "种凝神花→田2")) && !seed2.IsDisposed, "field and seed controls survive the empty-to-growing transition");
                        d.Fields[1] = "ningshen," + DateTime.UtcNow.AddSeconds(75).Ticks; form.RefreshData();
                        Check(TextOf(second).Contains("生长") && TextOf(second).Contains("剩余") && TextOf(second).Contains("分"), "growing field shows its actual short remaining time instead of a rounded six-minute minimum");
                        Check(!OwnVisible(seed2) && !OwnVisible(Button(form, "收获田2")), "growing field does not offer planting or early harvest");
                        d.Fields[1] = "ningshen,1"; form.RefreshData(); Button harvest2 = Button(form, "收获田2");
                        Check(second != null && TextOf(second).Contains("成熟") && OwnVisible(harvest2) && second.Contains(harvest2), "mature harvest is beside the corresponding field state");
                        Click(harvest2); form.RefreshData();
                        Check(d.Fields[0] == null && d.Fields[1] == null && d.CountOf("ningshen") == 2, "harvesting field two gives its original yield and clears only that field");
                        Check(seed2 != null && Object.ReferenceEquals(seed2, Button(form, "种凝神花→田2")) && OwnVisible(seed2), "the original seed control returns after harvesting");
                        d.Bag["linggu"] = 4; d.Bag["ningshen"] = 2; form.RefreshData(); Click(Button(form, "扩建洞天→Lv.2")); form.RefreshData();
                        Check(d.Level == 2 && OwnVisible(Named(form, "cave-field-2")) && Button(form, "种剑心草→田3") != null, "real paid expansion unlocks a separately actionable third field");
                        d.Bag["linggu"] = 8; d.Bag["ningshen"] = 4; d.Bag["jianxin"] = 1; form.RefreshData(); Click(Button(form, "扩建洞天→Lv.3")); form.RefreshData();
                        Check(d.Level == 3 && OwnVisible(Named(form, "cave-field-3")) && Button(form, "种灵谷→田4") != null, "second real paid expansion exposes the fourth field without rebuilding earlier fields");
                        Check(first != null && second != null && Object.ReferenceEquals(first, Named(form, "cave-field-0")) && Object.ReferenceEquals(second, Named(form, "cave-field-1")), "expansion preserves existing field-row instances");
                        Set(game, "dongtianPersistPending", true); form.RefreshData(); Click(seed1);
                        Check(form.ActionControlsSnapshot().All(c => !c.Enabled) && d.Fields[0] == null, "pending persistence disables all operations including a queued slot click");
                        Set(game, "dongtianPersistPending", false); d.Bag["linggu"] = 2; form.RefreshData(); Click(Button(form, "炼聚气散")); form.RefreshData();
                        Check(d.Furnace != null && d.CountOf("linggu") == 0, "original alchemy recipe and exact material consumption remain available");
                        d.Furnace = "juqi,1"; d.Forge = "jianwan,1"; form.RefreshData();
                        Control furnace = Named(form, "cave-furnace"), forge = Named(form, "cave-forge");
                        Check(furnace != null && furnace.Contains(Button(form, "收丹")) && TextOf(furnace).Contains("可收"), "ready furnace retains its real collection action next to furnace state");
                        Check(forge != null && forge.Contains(Button(form, "收器")) && TextOf(forge).Contains("可收"), "ready forge retains its real collection action next to forge state");
                        Click(Button(form, "收丹")); Click(Button(form, "收器")); form.RefreshData();
                        Check(d.Furnace == null && d.Forge == null && d.BuffActive("buff-juqi", DateTime.UtcNow) && d.HasGear("jianwan"), "collection applies the original real buff/gear and clears their own production slots");
                        d.Gears.Add("putuan"); d.Gears.Add("jinglu"); d.Buffs["buff-wuzhen"] = DateTime.UtcNow.AddHours(2).Ticks;
                        d.Buffs["buff-ninglu"] = DateTime.UtcNow.AddHours(1).Ticks; d.Buffs["buff-gushen"] = 1; form.RefreshData();
                        string effects = TextOf(Named(form, "cave-effects"));
                        Check(effects.Contains("洞天贡献") && effects.Contains("剩余") && effects.Contains("小时"), "benefit summary identifies cave contribution and actual remaining buff lifetime");
                        Check(new[] { "putuan", "jinglu", "jianwan" }.All(id => effects.Contains(DongtianRecipes.Gear(id).EffectText)), "collected gear shows its actual permanent effect rather than only its name");
                        Check(effects.Contains(d.QiRateFactor(DateTime.UtcNow).ToString("0.##", CultureInfo.InvariantCulture)) && effects.Contains(d.DamageFactor(DateTime.UtcNow).ToString("0.##", CultureInfo.InvariantCulture)) && effects.Contains(d.FurnaceTimeFactor.ToString("0.##", CultureInfo.InvariantCulture)), "displayed cave factors read the existing actual contribution APIs");
                        Check(!effects.Contains("固神丹"), "expired buff is not presented as an active benefit");
                        string files = Files(profile); string bag = String.Join("|", d.Bag.Select(p => p.Key + ":" + p.Value));
                        for (int i = 0; i < 20; i++) form.RefreshData();
                        Check(Files(profile) == files && String.Join("|", d.Bag.Select(p => p.Key + ":" + p.Value)) == bag, "repeated state/benefit refreshes do not write archives or consume/reward inventory");
                        Check(first != null && furnace != null && Object.ReferenceEquals(first, Named(form, "cave-field-0")) && Object.ReferenceEquals(furnace, Named(form, "cave-furnace")), "stable and state-changing refreshes retain fixed field/furnace rows");
                        LayoutCases(form, game, output);
                        Check(!owner.Visible && !form.Visible && !game.DesktopHeroWindow.Visible && game.PresentedFrames == 0, "every fixture and actor remains hidden with zero presented frames");
                        Check(game.DesktopRealWorkIdle && !game.MaintenanceConsentCleanup && !game.MaintenanceConsentMemory && !game.MaintenanceConsentAutoMaintenance && game.Baiye.Mode == 0, "slot operations start no real worker, observation or maintenance authorization");
                    }
                }
            }
            catch (Exception error) { Check(false, "unexpected fixture error: " + error); }
            File.WriteAllText(Path.Combine(output, "cave-slots.json"), new JavaScriptSerializer().Serialize(new { checks, failures, scope = "Hidden, stopped real controller and Form, private Virtual profile, artificially ready production ticks for bounded function checks. Actual controller writes and private reload, never natural elapsed hours, visible GUI, native input, real maintenance or real player archives." }), new UTF8Encoding(true));
            Console.WriteLine("Cave slots: " + checks + " checks, " + failures + " failures."); return failures == 0 ? 0 : 1;
        }
    }
}
