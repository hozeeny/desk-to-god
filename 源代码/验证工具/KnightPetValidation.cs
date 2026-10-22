using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NetBunny;

internal static class KnightPetValidation
{
    const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process, uint flags);
    static FieldInfo Field(object value, string name) { return value.GetType().GetField(name, Hidden); }
    static object Read(object value, string name) { return Field(value, name).GetValue(value); }
    static void Set(object value, string name, object entry) { Field(value, name).SetValue(value, entry); }
    static void Call(object value, string name) { value.GetType().GetMethod(name, Hidden).Invoke(value, null); }
    static int passed;
    static void Check(bool condition, string text) { if (!condition) throw new Exception(text); passed++; }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            string atlasPath = args[0];
            if (args.Length > 1 && args[1] == "export")
            { KnightPetController.ExportAnimationFrames(atlasPath, args[2]); Console.WriteLine("80 storyboard frames exported."); return 0; }
            using (Form owner = new Form())
            using (KnightPetController pet = new KnightPetController(owner, atlasPath))
            {
                Form window = (Form)Read(pet, "window");
                Check(!pet.Enabled && !window.Visible, "Controller must start hidden.");
                // Place test-only overlay far outside every physical screen. It is
                // never visible on the user's desktop, but exercises native HWND,
                // alpha presentation, timers and no-activation display lifecycle.
                Rectangle testArea = new Rectangle(-24000, -24000, 1000, 700);
                Set(pet, "area", testArea); Set(pet, "x", -23900.0); Set(pet, "y", -23900.0);
                Call(pet, "ClampPosition"); Call(pet, "PickTarget");
                pet.UpdateTelemetry(4 * 1024 * 1024, true);
                pet.Enabled = true;
                Check(pet.Enabled && window.Visible, "Off-screen overlay must open.");
                Check(!window.ShowInTaskbar, "Pet must not create a taskbar button.");
                Check(window.DesktopTopMost, "Pet must be natively topmost.");
                Check(!owner.Visible, "Owner must remain hidden.");
                Check(window.Owner == null, "Pet must be independent of owner hide.");
                GC.Collect(); GC.WaitForPendingFinalizers();
                uint beforeGdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                for (int frame = 0; frame < 250; frame++) Call(pet, "Paint");
                GC.Collect(); GC.WaitForPendingFinalizers();
                uint afterGdi = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
                Check(afterGdi <= beforeGdi + 4, "Repeated layered rendering must release native GDI handles.");
                Console.WriteLine("Native GDI objects across 250 rendered frames: " + beforeGdi + " -> " + afterGdi + ".");
                Point first = window.Location;
                Stopwatch clock = Stopwatch.StartNew();
                while (clock.ElapsedMilliseconds < 850) { Application.DoEvents(); Thread.Sleep(12); }
                Check(window.Location != first, "High traffic must move the roaming pet.");
                Check(testArea.Contains(window.Bounds), "Movement must stay in test working area.");
                Bitmap current = (Bitmap)Read(window, "current");
                Check(current != null && current.GetPixel(0, 0).A == 0, "Layered render must preserve transparency.");
                Point transparent = window.PointToScreen(Point.Empty);
                long transparentL = ((long)(ushort)transparent.Y << 16) | (ushort)transparent.X;
                Check(SendMessage(window.Handle, 0x0084, IntPtr.Zero, new IntPtr(transparentL)).ToInt64() == -1, "Transparent pixels must pass hit testing.");
                Point solid = Point.Empty; bool found = false;
                for (int yy = 0; yy < current.Height && !found; yy++)
                for (int xx = 0; xx < current.Width && !found; xx++)
                    if (current.GetPixel(xx, yy).A > 200) { solid = new Point(xx, yy); found = true; }
                Check(found, "Rendered pet must contain visible pixels.");
                Point solidScreen = window.PointToScreen(solid);
                long solidL = ((long)(ushort)solidScreen.Y << 16) | (ushort)solidScreen.X;
                Check(SendMessage(window.Handle, 0x0084, IntPtr.Zero, new IntPtr(solidL)).ToInt64() == 1, "Visible sprite pixels must be draggable client area.");
                pet.Roaming = false; Point stationary = window.Location;
                clock.Restart(); while (clock.ElapsedMilliseconds < 220) { Application.DoEvents(); Thread.Sleep(12); }
                Check(window.Location == stationary, "Disabling roaming must stop position changes.");
                Set(pet, "x", -99999.0); Set(pet, "y", -99999.0); Call(pet, "ClampPosition");
                Check(testArea.Contains(window.Bounds), "Clamp must correct top-left overflow.");
                Set(pet, "x", 99999.0); Set(pet, "y", 99999.0); Call(pet, "ClampPosition");
                Check(testArea.Contains(window.Bounds), "Clamp must correct bottom-right overflow.");
                pet.PlayDemo(); Check(pet.StateText.Contains("演示"), "Demo must be distinguishable from telemetry.");
                pet.Enabled = false; Check(!window.Visible, "Hiding must remove pet overlay.");
                pet.Dispose(); Check(window.IsDisposed, "Dispose must release pet window.");
                Check(!((System.Windows.Forms.Timer)Read(pet, "timer")).Enabled, "Dispose must stop animation timer.");
            }
            Console.WriteLine("Pet native overlay: " + passed + " checks passed; no on-screen window, no network rule changes.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
