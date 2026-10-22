using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NetBunny
{
    // Feature panels share the existing actor windows, never a desktop overlay.
    // Only the foreground panel (or its owned dialog) outranks our own actors.
    internal sealed class DesktopPanelLayerCoordinator : IDisposable
    {
        readonly Dictionary<Form, bool> panels = new Dictionary<Form, bool>();
        readonly Timer timer = new Timer { Interval = 160 };
        bool synchronizing, disposed;
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

        internal DesktopPanelLayerCoordinator()
        {
            timer.Tick += Synchronize;
            KnightLayeredWindow.DesktopVisibilityChanged += Synchronize;
        }
        internal void Register(Form panel, bool initialWelcome)
        {
            if (disposed || panel == null || panel.IsDisposed) return;
            if (!panels.ContainsKey(panel))
            {
                panels.Add(panel, initialWelcome);
                panel.Activated += PanelActivated;
                panel.Deactivate += Synchronize;
                panel.VisibleChanged += Synchronize;
                panel.Resize += Synchronize;
                panel.FormClosed += PanelClosed;
            }
            else if (!initialWelcome) panels[panel] = false;
            Synchronize(null, EventArgs.Empty);
        }
        void PanelActivated(object sender, EventArgs e)
        {
            Form panel = (Form)sender;
            if (panels.ContainsKey(panel)) panels[panel] = false;
            Synchronize(sender, e);
        }
        void PanelClosed(object sender, FormClosedEventArgs e) { Unregister((Form)sender); Synchronize(null, EventArgs.Empty); }
        void Unregister(Form panel)
        {
            panels.Remove(panel);
            panel.Activated -= PanelActivated; panel.Deactivate -= Synchronize;
            panel.VisibleChanged -= Synchronize; panel.Resize -= Synchronize;
            panel.FormClosed -= PanelClosed;
        }
        void Synchronize(object sender, EventArgs e)
        {
            if (disposed || synchronizing) return;
            synchronizing = true;
            try
            {
                IntPtr foreground = GetForegroundWindow(), anchor = IntPtr.Zero;
                int anchorDepth = -1;
                // Walk the actual native owner chain. GetAncestor alone can
                // return the owned dialog itself on this Framework runtime.
                Dictionary<IntPtr, int> owners = new Dictionary<IntPtr, int>();
                IntPtr owner = foreground;
                while (owner != IntPtr.Zero && owners.Count < 128 && !owners.ContainsKey(owner))
                { owners.Add(owner, owners.Count); owner = GetWindow(owner, 4); } // GW_OWNER
                bool visible = false;
                foreach (Form panel in panels.Keys.ToArray())
                {
                    if (panel.IsDisposed) { Unregister(panel); continue; }
                    bool shown = panel.Visible && panel.WindowState != FormWindowState.Minimized;
                    bool active = shown && panel.IsHandleCreated && owners.ContainsKey(panel.Handle);
                    bool protect = shown && (active || panels[panel]);
                    if (!shown) panels[panel] = false;
                    // Framework Form.TopMost can activate the window while
                    // changing bands. Native NOACTIVATE is essential here.
                    if (panel.IsHandleCreated && ((GetWindowLongPtr(panel.Handle, -20).ToInt64() & 8) != 0) != protect)
                        SetWindowPos(panel.Handle, new IntPtr(protect ? -1 : -2), 0, 0, 0, 0, 0x213);
                    // Actors go below the lowest protected owner, preserving
                    // both the dialog and the owner's exposed controls.
                    if (active && owners[panel.Handle] > anchorDepth)
                    { anchor = panel.Handle; anchorDepth = owners[panel.Handle]; }
                    else if (panels[panel] && anchor == IntPtr.Zero && shown) anchor = panel.Handle;
                    visible |= shown;
                }
                if (anchor != IntPtr.Zero)
                {
                    // Framework dialogs can be created non-topmost even when
                    // their native owner is already topmost. Keep both in the
                    // same band before inserting actors behind the dialog.
                    IntPtr foregroundDialog = anchorDepth >= 0 ? foreground : anchor;
                    if ((GetWindowLongPtr(foregroundDialog, -20).ToInt64() & 8) == 0)
                        SetWindowPos(foregroundDialog, new IntPtr(-1), 0, 0, 0, 0, 0x213);
                    // Reorder only our actor handles currently above the anchor.
                    // Do not repeatedly raise a panel over an IME or another app.
                    HashSet<IntPtr> above = new HashSet<IntPtr>();
                    IntPtr previous = GetWindow(anchor, 3); // GW_HWNDPREV
                    while (previous != IntPtr.Zero && above.Count < 512 && above.Add(previous)) previous = GetWindow(previous, 3);
                    foreach (KnightLayeredWindow actor in Application.OpenForms.OfType<KnightLayeredWindow>().ToArray())
                        if (!actor.IsDisposed && actor.Visible && actor.IsHandleCreated && above.Contains(actor.Handle))
                            SetWindowPos(actor.Handle, anchor, 0, 0, 0, 0, 0x213); // NOSIZE|NOMOVE|NOACTIVATE|NOOWNERZORDER
                }
                timer.Enabled = visible;
            }
            finally { synchronizing = false; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            KnightLayeredWindow.DesktopVisibilityChanged -= Synchronize;
            timer.Stop(); timer.Dispose();
            foreach (Form panel in panels.Keys.ToArray()) Unregister(panel);
        }
    }
}
