using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using NetBunny;

internal static class EquipmentRulePreview
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    [STAThread] private static int Main(string[] args)
    {
        try
        {
            SetProcessDPIAware(); Application.EnableVisualStyles(); Directory.CreateDirectory(args[1]);
            using (Form owner = new Form())
            {
                IntPtr handle = owner.Handle;
                using (AdventureController controller = new AdventureController(owner, args[0], true))
                {
                    Check((bool)typeof(AdventureController).GetField("demonstrationOnly", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(controller), "controller is isolated from real services and persistence");
                    controller.Battle.LastReward = "纯演示预览：不读取或清理真实文件，不保存真实进度。";
                    foreach (int width in new[] { 760, 650 })
                    using (AdventureEquipmentForm panel = new AdventureEquipmentForm(controller))
                    {
                        panel.Width = width; panel.StartPosition = FormStartPosition.Manual; panel.Location = new Point(-24000, -24000);
                        panel.Show(); panel.RefreshData(); Application.DoEvents();
                        FlowLayoutPanel content = panel.Controls.OfType<FlowLayoutPanel>().Single();
                        Label heading = content.Controls.OfType<Label>().First(label => label.Text.StartsWith("作战方式"));
                        content.AutoScrollPosition = new Point(0, heading.Top - 12); Application.DoEvents();
                        Label[] rules = content.Controls.OfType<Label>().Where(label => label.Text.StartsWith("每击杀一只怪物") || label.Text.StartsWith("无需解锁武器") || label.Text.StartsWith("桌面敌人总数")).ToArray();
                        Check(rules.Length == 3, width + ": all three revised rule paragraphs are present");
                        Check(rules.Any(label => label.Text.Contains("待重扫") && label.Text.Contains("不会让死怪复活")), width + ": cleanup failure behavior is explicit");
                        foreach (Label rule in rules)
                        {
                            Rectangle rectangle = content.RectangleToClient(rule.RectangleToScreen(rule.ClientRectangle));
                            Check(rectangle.Left >= 0 && rectangle.Right <= content.ClientSize.Width && rectangle.Top >= 0 && rectangle.Bottom <= content.ClientSize.Height, width + ": rule paragraph is completely inside viewport");
                            Size text = TextRenderer.MeasureText(rule.Text, rule.Font, new Size(rule.ClientSize.Width, Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                            Check(text.Height <= rule.ClientSize.Height, width + ": wrapped rule text fits its label");
                        }
                        using (Bitmap screenshot = new Bitmap(panel.Width, panel.Height))
                        {
                            panel.DrawToBitmap(screenshot, new Rectangle(Point.Empty, panel.Size));
                            screenshot.Save(Path.Combine(args[1], "装备栏-新规则说明-" + width + ".png"), ImageFormat.Png);
                        }
                        Check(!controller.IsScanning && controller.Battle.QueueCount == 0, width + ": synthetic preview has no cleanup or scan");
                    }
                }
            }
            Console.WriteLine("Equipment rule preview: " + checks + " checks passed at 760px and 650px; synthetic demonstration only, no real cleanup/network/progress writes."); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
