using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NetBunny;

internal static class CenterPreviewValidation
{
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool PrintWindow(IntPtr hwnd, IntPtr target, uint flags);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static int combos;
    static void Inspect(Control control)
    {
        ComboBox combo = control as ComboBox;
        if (combo != null)
        {
            Console.WriteLine("Combo " + (++combos) + ": SelectedIndex=" + combo.SelectedIndex + "; Text=" + combo.Text + "; SelectedItem=" + combo.SelectedItem);
            if (combo.SelectedIndex < 0 || String.IsNullOrEmpty(combo.Text)) throw new Exception("Selected combo value missing.");
        }
        foreach (Control child in control.Controls) Inspect(child);
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            SetProcessDPIAware(); Application.EnableVisualStyles();
            using (TrafficLedger ledger = new TrafficLedger(true))
            using (NetworkCenterForm center = new NetworkCenterForm(true, ledger, delegate { return new List<AppInfo>(); }, delegate { return "演示数据"; }, delegate(bool active) { }))
            {
                center.StartPosition = FormStartPosition.Manual; center.Location = new Point(-20000, -20000);
                center.ShowPage(0);
                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 550) { Application.DoEvents(); Thread.Sleep(12); }
                Inspect(center);
                using (Bitmap image = new Bitmap(center.Width, center.Height))
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    IntPtr hdc = graphics.GetHdc(); bool ok;
                    try { ok = PrintWindow(center.Handle, hdc, 0); }
                    finally { graphics.ReleaseHdc(hdc); }
                    if (!ok) throw new Exception("PrintWindow failed.");
                    image.Save(args[0]);
                }
                Console.WriteLine("PrintWindow captured only the preview HWND; real history/network backend disabled.");
                center.Shutdown();
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
