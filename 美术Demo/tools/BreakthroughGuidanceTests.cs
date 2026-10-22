using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace NetBunny
{
    // Hidden private UI fixture: validates guidance against the real configured
    // opportunity pool. Never advances time, grants live rewards or shows actors.
    internal static class BreakthroughGuidanceTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool value, string message) { checks++; if (!value) failures++; Console.WriteLine((value ? "PASS " : "FAIL ") + message); }
        static T Field<T>(object instance, string name) { return (T)instance.GetType().GetField(name, Hidden).GetValue(instance); }
        static bool LocalVisible(Control control) { return (bool)typeof(Control).GetMethod("GetState", Hidden).Invoke(control, new object[] { 2 }); }
        static string AllText(Control control) { return control.Text + "\n" + String.Join("\n", control.Controls.Cast<Control>().Select(AllText)); }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
                using (AdventureController game = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, Path.Combine(output, "private-profile")))
                {
                    Field<Timer>(game, "timer").Stop(); game.ConfigureDesktopDemo(false); Field<Timer>(game, "timer").Stop();
                    using (BreakthroughForm panel = new BreakthroughForm(game))
                    {
                        Field<Timer>(panel, "refresh").Stop();
                        CultivationProgress progress = game.Cultivation;
                        panel.RefreshData();
                        Label[] rows = Field<Label[]>(panel, "opportunityLines");
                        Check(progress.Mortal && rows.All(r => !LocalVisible(r) && r.Text.Length == 0), "mortal guidance does not offer any premature breakthrough path");
                        Check(!Field<Button>(panel, "attempt").Enabled, "mortal cannot attempt breakthrough through its guidance panel");
                        progress.Mortal = false; progress.RealmIndex = 0; progress.Layer = progress.LayersMax;
                        progress.BreakthroughOpportunities.Clear(); panel.RefreshData();
                        Check(rows.Count(LocalVisible) == progress.RealmDef.OpportunityPool.Length && rows.All(r => r.Text.Length > 0), "qi guidance exposes its four actually configured paths");
                        Check(rows[2].Text.Contains("本境") && rows[2].Text.Contains("升级") && rows[2].Text.Contains("机缘"), "Baiye guidance explains the new event required in this realm, not merely historical level three");
                        Check(rows[1].Text.Contains("本境") && rows[1].Text.Contains("通关"), "mirage guidance explains a fresh clear in this realm");
                        Check(Field<Label>(panel, "chanceCard").Text.Contains("本境") && !Field<Label>(panel, "chanceCard").Text.Contains("四途"), "missing-opportunity prompt refers to the current configured paths");
                        Check(progress.GrantOpportunity("realm", "guidance-qi-clear"), "fixture uses the actual qi opportunity grant rule");
                        panel.RefreshData(); Check(rows[1].Text.StartsWith("✓ ") && Field<Button>(panel, "attempt").Enabled, "actual current-realm opportunity unlocks the real attempt button and owned marker");
                        game.Lingyu.Materials["shuiyue-lingsui"] = 1; panel.RefreshData();
                        Check(Field<CheckBox>(panel, "useMaterial").Enabled, "configured qi auxiliary material remains usable when truly owned");
                        Field<CheckBox>(panel, "useMaterial").Checked = true;
                        for (int realm = 1; realm < CultivationRealms.Catalog.Length - 1; realm++)
                        {
                            progress.RealmIndex = realm; progress.Layer = progress.LayersMax; panel.RefreshData();
                            Check(!LocalVisible(rows[0]) && rows[0].Text.Length == 0 && rows.Count(LocalVisible) == progress.RealmDef.OpportunityPool.Length,
                                progress.RealmName + " only displays paths supported by its opportunity pool");
                            Check(!AllText(panel).Contains("水月灵髓") && !AllText(panel).Contains("四途"), progress.RealmName + " never sends the player toward unsupported treasure");
                            Check(!Field<CheckBox>(panel, "useMaterial").Enabled && !Field<CheckBox>(panel, "useMaterial").Checked,
                                progress.RealmName + " clears the old qi auxiliary-material selection");
                            Check(!rows[1].Text.StartsWith("✓ ") && !Field<Button>(panel, "attempt").Enabled,
                                progress.RealmName + " does not label the prior realm's clear as a new opportunity");
                        }
                        progress.RealmIndex = 1; progress.Layer = progress.LayersMax;
                        Check(progress.GrantOpportunity("realm", "guidance-zhuji-clear"), "fixture uses the actual new-realm opportunity grant rule");
                        panel.RefreshData(); Check(rows[1].Text.StartsWith("✓ ") && Field<Button>(panel, "attempt").Enabled, "a real new-realm grant changes both guidance and actual eligibility");
                        progress.RealmIndex = CultivationRealms.Catalog.Length - 1; progress.Layer = progress.LayersMax; panel.RefreshData();
                        Check(rows.All(r => !LocalVisible(r) && r.Text.Length == 0) && !Field<Button>(panel, "attempt").Enabled, "highest realm has no imaginary next-breakthrough paths");
                        Check(Field<Label>(panel, "chanceCard").Text.Contains("巅峰"), "highest realm explains why there is no further attempt");
                        Check(!panel.Visible && !owner.Visible && !game.DesktopHeroWindow.Visible && game.DesktopRealWorkIdle, "guidance fixtures show no windows and start no real maintenance");
                    }
                }
                Console.WriteLine("Breakthrough guidance: " + checks + " checks, " + failures + " failures; hidden/private functional fixtures only.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
