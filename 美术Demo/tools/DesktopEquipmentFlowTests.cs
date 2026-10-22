using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Hidden/private controller and real battle reward/UI handlers. This is not
    // a natural-growth test and never shows a window or maintains the computer.
    internal static class DesktopEquipmentFlowTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static void Check(bool okay, string message) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); }
        static T Field<T>(object target, string name) { FieldInfo f = target.GetType().GetField(name, Hidden); return f == null ? default(T) : (T)f.GetValue(target); }
        static void Set(object target, string name, object value) { target.GetType().GetField(name, Hidden).SetValue(target, value); }
        static string Selected(ComboBox box) { return box.SelectedItem == null ? "" : (string)box.SelectedItem.GetType().GetField("Id", Hidden).GetValue(box.SelectedItem); }
        static void Select(ComboBox box, string id)
        { for (int i = 0; i < box.Items.Count; i++) if ((string)box.Items[i].GetType().GetField("Id", Hidden).GetValue(box.Items[i]) == id) { box.SelectedIndex = i; return; } throw new Exception("Owned fixture weapon missing: " + id); }
        static Control[] Children(Control root) { return root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c))).ToArray(); }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                string profile = Path.Combine(output, "private-profile");
                Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
                {
                    using (AdventureController game = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, profile))
                    {
                        Field<Timer>(game, "timer").Stop(); game.ConfigureDesktopDemo(false); Field<Timer>(game, "timer").Stop();
                        game.DesktopAdvanceForTest(.01); game.Battle.Enemies.Clear();
                        game.Battle.Progress.Inventory.Add("greatsword");
                        object session = FormatterServices.GetUninitializedObject(typeof(DesktopDemoSession));
                        Set(session, "<Game>k__BackingField", game); Set(session, "<ProfileRoot>k__BackingField", profile);
                        using (DesktopPracticePanel panel = new DesktopPracticePanel((DesktopDemoSession)session))
                        {
                            Field<Timer>(panel, "refresh").Stop(); panel.UpdateState(); panel.OpenSection("inventory");
                            ComboBox weapons = Field<ComboBox>(panel, "weapons");
                            Select(weapons, "greatsword");
                            Check(Selected(weapons) == "greatsword" && game.Battle.Progress.WeaponId == "spear", "previewing another owned weapon does not equip it");
                            game.Battle.Progress.Experience = game.Battle.Progress.NextLevelExperience - 2;
                            Point hero = game.DesktopHeroWindow.Location;
                            AdventureEnemy enemy = new AdventureEnemy { Id = "equipment-reward-fixture", IsVirtual = true, MonsterKind = "skeleton", Health = 1, MaxHealth = 1, X = hero.X + 45, Y = hero.Y + 50 };
                            game.Battle.Enemies.Add(enemy);
                            Check(game.DesktopChooseEnemy(enemy.Id, true) && enemy.FellInCombat && game.Battle.Progress.Inventory.Contains("wave"), "a real manual strike and level reward earn the next weapon in the hidden fixture");
                            panel.UpdateState();
                            Check(Selected(weapons) == "greatsword", "earning another weapon preserves the player's in-progress preview");
                            DesktopPlayerView view = game.ReadDesktopPlayerView();
                            Check(view.RecentEvents.Any(s => s.Contains("破空剑气") && s.Contains("行囊")), "a hidden-actor weapon reward remains discoverable in handbook history with its canonical name");
                            game.Battle.LastReward = "普通妖物现身"; panel.UpdateState();
                            Check(game.ReadDesktopPlayerView().RecentEvents.Any(s => s.Contains("破空剑气") && s.Contains("行囊")), "a later transient battle message cannot erase the equipment discovery event");
                            Label info = Field<Label>(panel, "weaponInfo"), progress = Field<Label>(panel, "weaponProgress");
                            Check(info.Text.Contains("青钢重剑") && !info.Text.Contains("三阶共用"), "weapon preview identifies the actual illustrated weapon instead of an obsolete shared-sword claim");
                            Check(progress != null && progress.Text.Contains("3/7") && progress.Text.Contains("灵铁短剑"), "owned count and real current equipment remain visible while previewing something else");
                            PictureBox picture = Field<PictureBox>(panel, "weaponPreview");
                            Check(picture != null && picture.Image != null && (string)picture.Image.Tag == "weapon-zhuji.png", "preview uses the same controlled heavy-sword illustration as the actor renderer");
                            Button equip = Field<Button>(panel, "equipWeapon") ?? Children(panel).OfType<Button>().First(b => b.Text == "装备所选兵器");
                            Check(equip.Enabled && equip.Text.Contains("青钢重剑"), "equip action explicitly names the still-selected preview");
                            typeof(Button).GetMethod("OnClick", Hidden).Invoke(equip, new object[] { EventArgs.Empty });
                            Check(game.Battle.Progress.WeaponId == "greatsword" && game.Battle.Weapon.Id == "greatsword", "the previewed weapon actually becomes the battle weapon after a concurrent drop");
                            panel.UpdateState();
                            Check(!equip.Enabled && equip.Text.Contains("已装备"), "equipped preview cannot misleadingly offer another equip action");
                            Check(game.ReadDesktopPlayerView().RecentEvents.Any(s => s.Contains("装备") && s.Contains("青钢重剑")), "equipment change records the real selected identity");
                            Check(AdventureProgress.Load(game.DesktopArchivePaths[0]).WeaponId == "greatsword", "actual equipment change persists to the private production archive");
                            Select(weapons, "wave"); panel.UpdateState();
                            Check(Selected(weapons) == "wave" && game.Battle.Progress.WeaponId == "greatsword", "ordinary panel refreshes preserve a new preview independently of current equipment");
                            Image sameImage = picture == null ? null : picture.Image;
                            for (int i = 0; i < 10; i++) panel.UpdateState();
                            Check(picture != null && Object.ReferenceEquals(picture.Image, sameImage), "stable preview refreshes reuse their owned image instead of allocating each tick");
                            game.ChooseWeapon("not-owned");
                            Check(game.Battle.Progress.WeaponId == "greatsword", "the existing ownership gate rejects unowned equipment");
                            Check(!owner.Visible && !panel.Visible && !game.DesktopHeroWindow.Visible && game.DesktopRealWorkIdle, "equipment tests keep all windows hidden and all real maintenance workers absent");
                        }
                    }
                    using (AdventureController loaded = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, profile))
                    {
                        Field<Timer>(loaded, "timer").Stop();
                        Check(loaded.Battle.Progress.WeaponId == "greatsword" && loaded.Battle.Progress.Inventory.Contains("wave"), "controller reconstruction retains both the earned weapon and the player's actual equipment choice");
                    }
                }
                Console.WriteLine("Desktop equipment flow: " + checks + " checks, " + failures + " failures; hidden/private fixtures only.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
