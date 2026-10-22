using System;
using System.Collections;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Windows.Forms;

// Hidden, stopped-clock UI fixture. These exports are layout previews, never
// evidence of native desktop play. The unconstructed session must not be disposed.
internal static class VerifyDesktopPanelLayout
{
    const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static int checks, failures;
    static void Check(bool ok, string message) { checks++; Console.WriteLine((ok ? "PASS " : "FAIL ") + message); if (!ok) failures++; }
    static object Field(object instance, string name) { return instance.GetType().GetField(name, Instance).GetValue(instance); }
    static object Call(object instance, string name, params object[] args) { return instance.GetType().GetMethod(name, Instance).Invoke(instance, args); }
    static void Set(object instance, string name, object value) { instance.GetType().GetField(name, Instance).SetValue(instance, value); }
    static void Layout(Control parent)
    {
        IntPtr handle = parent.Handle;
        foreach (Control child in parent.Controls) Layout(child);
        parent.PerformLayout();
    }
    static Control[] Descendants(Control parent)
    { return parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c))).ToArray(); }
    static Rectangle InPanel(Control control, Control panel)
    { return new Rectangle(panel.PointToClient(control.PointToScreen(Point.Empty)), control.Size); }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type controllerType = assembly.GetType("NetBunny.AdventureController", true);
            Type sessionType = assembly.GetType("NetBunny.DesktopDemoSession", true);
            Type panelType = assembly.GetType("NetBunny.DesktopPracticePanel", true);
            Type artType = assembly.GetType("NetBunny.ArtDemoPack", true);
            string artVariable = (string)artType.GetField("EnvironmentVariable", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            Environment.SetEnvironmentVariable(artVariable, Path.GetFullPath(args[2]));
            string output = Path.GetFullPath(args[3]); Directory.CreateDirectory(output);
            string profile = Path.Combine(output, "isolated-profile-" + Guid.NewGuid().ToString("N"));
            object session = FormatterServices.GetUninitializedObject(sessionType);
            using (Form owner = new Form { ShowInTaskbar = false, Location = new Point(-25000, -25000), StartPosition = FormStartPosition.Manual })
            using (IDisposable controller = (IDisposable)Activator.CreateInstance(controllerType, Instance, null,
                new object[] { owner, Path.GetFullPath(args[1]), false, null, false, profile }, null))
            {
                ((Timer)Field(controller, "timer")).Stop(); Call(controller, "ConfigureDesktopDemo", false); ((Timer)Field(controller, "timer")).Stop();
                Set(session, "<Game>k__BackingField", controller); Set(session, "<ProfileRoot>k__BackingField", profile);
                using (Form panel = (Form)Activator.CreateInstance(panelType, Instance, null, new object[] { session }, null))
                {
                    ((Timer)Field(panel, "refresh")).Stop(); Call(panel, "UpdateState");
                    ComboBox techniques = (ComboBox)Field(panel, "techniques");
                    Button practice = (Button)Field(panel, "practice"), awaken = (Button)Field(panel, "awaken"), automatic = (Button)Field(panel, "automatic");
                    Check(!techniques.Enabled && !practice.Enabled && !awaken.Enabled && !automatic.Enabled, "fresh mortal cannot use cultivation, technique, awakening, or automatic battle early");
                    ComboBox weapons = (ComboBox)Field(panel, "weapons");
                    object battle = controllerType.GetProperty("Battle", Instance).GetValue(controller, null);
                    object progress = Field(battle, "Progress");
                    Check(weapons.Items.Count == ((ICollection)Field(progress, "Inventory")).Count, "weapon options match the real owned inventory");
                    TabControl tabs = (TabControl)Field(panel, "tabs");
                    Size[] sizes = { new Size(470, 700), new Size(390, 490) };
                    foreach (Size size in sizes)
                    {
                        panel.ClientSize = size;
                        for (int tab = 0; tab < 3; tab++)
                        {
                            tabs.SelectedIndex = tab;
                            for (int i = 0; i < 3; i++) { Layout(panel); Call(panel, "ResizeColumns"); }
                            FlowLayoutPanel column = (FlowLayoutPanel)tabs.SelectedTab.Controls[0]; column.AutoScrollPosition = Point.Empty;
                            Layout(panel);
                            Check(column.AutoScroll && column.Controls.Cast<Control>().All(c => c.Right <= column.ClientSize.Width + 1), size + " tab " + tab + " no horizontal child overflow");
                            Check(column.DisplayRectangle.Width <= column.ClientSize.Width + 1, size + " tab " + tab + " does not need horizontal scrolling");
                            Button exit = Descendants(panel).OfType<Button>().First(b => b.Text == "保存并退出");
                            Check(panel.ClientRectangle.Contains(InPanel(exit, panel)), size + " tab " + tab + " save-exit footer is always reachable");
                            if (tab == 0)
                            {
                                column.ScrollControlIntoView(awaken); Layout(column);
                                Check(column.ClientRectangle.Contains(awaken.Bounds), size + " awakening button is fully reachable by vertical scrolling");
                                column.AutoScrollPosition = Point.Empty; Layout(column);
                            }
                            // A Form prints its non-client frame too. Render the
                            // full window, then export precisely its client area.
                            using (Bitmap full = new Bitmap(panel.Width, panel.Height, PixelFormat.Format32bppArgb))
                            {
                                panel.DrawToBitmap(full, new Rectangle(Point.Empty, panel.Size));
                                Point client = panel.PointToScreen(Point.Empty);
                                Rectangle clientCrop = new Rectangle(client.X - panel.Left, client.Y - panel.Top, size.Width, size.Height);
                                using (Bitmap image = full.Clone(clientCrop, PixelFormat.Format32bppArgb))
                                    image.Save(Path.Combine(output, size.Width + "x" + size.Height + "-tab" + tab + "-offscreen.png"), ImageFormat.Png);
                            }
                        }
                    }
                    object cultivation = controllerType.GetProperty("Cultivation", Instance).GetValue(controller, null);
                    Set(cultivation, "MortalKills", 10); Call(panel, "UpdateState");
                    Check(awaken.Enabled, "real ten-kill condition enables the awakening control");
                    Check((bool)Call(controller, "AwakenSpiritRoot"), "explicit UI fixture uses the real awakening transition in its private archive");
                    Call(panel, "UpdateState");
                    Check(techniques.Enabled && practice.Enabled && automatic.Enabled && !awaken.Enabled, "actual awakening enables cultivation, techniques and automation while retiring the awakening button");
                    object baiye = controllerType.GetProperty("Baiye", Instance).GetValue(controller, null);
                    Check((int)Field(baiye, "Mode") == 0 && !(bool)Field(baiye, "FirstRunExplained"), "constructing and inspecting all tabs never enables Baiye recognition or acknowledges privacy");
                    Check(!panel.Visible && !owner.Visible, "layout fixture never shows any form on the desktop");
                }
                GC.KeepAlive(session); // Do not dispose this unconstructed lifetime owner.
            }
            Console.WriteLine("Desktop panel layout: " + checks + " checks, " + failures + " failures. Exports are offscreen previews only.");
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
