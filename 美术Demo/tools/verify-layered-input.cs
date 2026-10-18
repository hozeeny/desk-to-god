using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Hidden-window regression: invoking a left press must not announce a drag.
// Real desktop movement, alpha hit-testing and focus are separately exercised
// by --desktop-demo-accept with native mouse input.
internal static class VerifyLayeredInput
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 1) throw new ArgumentException("Expected path to compiled game EXE.");
            Type type = Assembly.LoadFrom(args[0]).GetType("NetBunny.KnightLayeredWindow", true);
            using (Form window = (Form)Activator.CreateInstance(type, true))
            {
                int began = 0, ended = 0;
                type.GetField("DragChanged", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window,
                    new Action<bool>(delegate(bool active) { if (active) began++; else ended++; }));
                Action<string, MouseEventArgs> invoke = delegate(string method, MouseEventArgs mouse)
                { type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { mouse }); };
                invoke("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                if (began != 0) throw new InvalidOperationException("Left press incorrectly begins dragging before the system drag threshold.");
                invoke("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                if (ended != 0) throw new InvalidOperationException("Click incorrectly ends a drag which never began.");
                int clicks = 0;
                window.MouseClick += delegate { clicks++; };
                invoke("OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                if (clicks != 1) throw new InvalidOperationException("Ordinary click was suppressed.");
                invoke("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                // Move the stored press anchor rather than the user's pointer.
                // This is the same threshold comparison, with no visible UI.
                type.GetField("mouseStart", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window,
                    new Point(Cursor.Position.X - SystemInformation.DragSize.Width - 20, Cursor.Position.Y));
                invoke("OnMouseMove", new MouseEventArgs(MouseButtons.Left, 0, 40, 20, 0));
                if (began != 1) throw new InvalidOperationException("Crossing the threshold did not begin exactly one drag.");
                invoke("OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, 40, 20, 0));
                if (clicks != 1) throw new InvalidOperationException("Dragging incorrectly activated click action before release.");
                invoke("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 40, 20, 0));
                if (ended != 1) throw new InvalidOperationException("Release did not end exactly one drag.");
                invoke("OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, 40, 20, 0));
                if (clicks != 1) throw new InvalidOperationException("Dragging incorrectly activated click action after release.");
                invoke("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                window.Capture = false;
                invoke("OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                if (clicks != 1) throw new InvalidOperationException("Capture loss incorrectly activated click action.");
                invoke("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                invoke("OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                invoke("OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, 20, 20, 0));
                if (clicks != 2) throw new InvalidOperationException("Ordinary click did not recover after drag/capture loss.");
                window.Location = new Point(-1800, -1300);
                Bitmap rgba = new Bitmap(120, 120, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (Graphics paint = Graphics.FromImage(rgba)) paint.FillRectangle(Brushes.DarkRed, 40, 40, 40, 40);
                type.GetMethod("Present", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { rgba });
                Func<int, int, IntPtr> hit = delegate(int x, int y)
                {
                    Point screen = window.PointToScreen(new Point(x, y));
                    return SendMessage(window.Handle, 0x84, IntPtr.Zero,
                        new IntPtr(((long)(ushort)screen.Y << 16) | (ushort)screen.X));
                };
                if (hit(5, 5).ToInt64() != -1) throw new InvalidOperationException("Transparent hit did not pass through at negative screen coordinates.");
                if (hit(60, 60).ToInt64() != 1) throw new InvalidOperationException("Opaque actor hit was not accepted at negative screen coordinates.");
                if (SendMessage(window.Handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt64() != 3) throw new InvalidOperationException("Mouse activation must return MA_NOACTIVATE.");
                long style = GetWindowLongPtr(window.Handle, -20).ToInt64();
                if ((style & 0x80000) == 0 || (style & 0x8000000) == 0 || (style & 0x80) == 0)
                    throw new InvalidOperationException("Expected LAYERED, NOACTIVATE and TOOLWINDOW native styles.");
                if (window.Visible) throw new InvalidOperationException("Hidden input regression unexpectedly showed a window.");
                Console.WriteLine("Layered input regression: 14 checks passed; hidden windows only, pointer unchanged; negative coordinates and native alpha hit-testing verified.");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
