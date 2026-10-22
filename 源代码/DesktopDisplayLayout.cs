using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace NetBunny
{
    // Snapshots keep the geometry contract independent of live display settings.
    internal sealed class DesktopDisplaySnapshot
    {
        internal readonly string DeviceName;
        internal readonly Rectangle WorkingArea;
        internal readonly bool Primary;
        internal DesktopDisplaySnapshot(string deviceName, Rectangle workingArea, bool primary)
        { DeviceName = deviceName; WorkingArea = workingArea; Primary = primary; }
    }

    internal static class DesktopDisplayLayout
    {
        internal static bool Valid(DesktopDisplaySnapshot display)
        { return display != null && display.WorkingArea.Width > 0 && display.WorkingArea.Height > 0; }

        internal static DesktopDisplaySnapshot Select(DesktopDisplaySnapshot[] displays, string deviceName, Rectangle current, PointF anchor)
        {
            DesktopDisplaySnapshot[] valid = (displays ?? new DesktopDisplaySnapshot[0]).Where(Valid).ToArray();
            DesktopDisplaySnapshot same = valid.FirstOrDefault(d => !String.IsNullOrEmpty(deviceName) && String.Equals(d.DeviceName, deviceName, StringComparison.Ordinal));
            if (same != null) return same;
            if (String.IsNullOrEmpty(deviceName))
            {
                same = valid.FirstOrDefault(d => d.WorkingArea == current);
                if (same != null) return same;
            }
            return valid.OrderBy(d => DistanceSquared(anchor, d.WorkingArea)).ThenByDescending(d => d.Primary).ThenBy(d => d.DeviceName, StringComparer.Ordinal).FirstOrDefault();
        }

        private static double DistanceSquared(PointF point, Rectangle bounds)
        {
            double dx = Math.Max(bounds.Left - (double)point.X, Math.Max(0, point.X - (double)bounds.Right));
            double dy = Math.Max(bounds.Top - (double)point.Y, Math.Max(0, point.Y - (double)bounds.Bottom));
            return dx * dx + dy * dy;
        }

        internal static PointF Translate(double x, double y, Rectangle oldArea, Rectangle nextArea, Size size)
        { return KnightPetController.BoundPosition(x + nextArea.Left - (double)oldArea.Left, y + nextArea.Top - (double)oldArea.Top, size, nextArea); }

        internal static Rectangle FitPanelBounds(Rectangle bounds, Rectangle work)
        {
            if (work.Width <= 0 || work.Height <= 0) return bounds;
            int width = Math.Min(bounds.Width, work.Width), height = Math.Min(bounds.Height, work.Height);
            return new Rectangle(Math.Max(work.Left, Math.Min(work.Right - width, bounds.Left)), Math.Max(work.Top, Math.Min(work.Bottom - height, bounds.Top)), width, height);
        }

        internal static bool ShouldRestorePanel(bool visible, bool disposed, FormWindowState state)
        { return visible && !disposed && state == FormWindowState.Normal; }

        internal static bool IsFeaturePanel(Type type)
        {
            return type == typeof(DesktopPracticePanel) || type == typeof(DesktopEcologyPanel) || type == typeof(BaiyeSteleForm)
                || type == typeof(BreakthroughForm) || type == typeof(LingyuForm) || type == typeof(DongtianForm) || type == typeof(AdventureEquipmentForm);
        }

        internal static string TopologyKey(DesktopDisplaySnapshot[] displays)
        {
            return String.Join("\n", (displays ?? new DesktopDisplaySnapshot[0]).Where(Valid).Select(d => String.Format(CultureInfo.InvariantCulture,
                "{0}:{1}:{2},{3},{4},{5}:{6}", (d.DeviceName ?? "").Length, d.DeviceName, d.WorkingArea.Left, d.WorkingArea.Top, d.WorkingArea.Width, d.WorkingArea.Height, d.Primary)).OrderBy(key => key, StringComparer.Ordinal));
        }

        internal static bool ShouldRestorePanelBounds(bool topologyChanged, bool newlyNormal, Rectangle bounds, DesktopDisplaySnapshot[] displays)
        {
            DesktopDisplaySnapshot[] valid = (displays ?? new DesktopDisplaySnapshot[0]).Where(Valid).ToArray();
            return valid.Length > 0 && (topologyChanged || newlyNormal || !valid.Any(d => d.WorkingArea.IntersectsWith(bounds)));
        }

        internal static DesktopDisplaySnapshot PanelDisplay(DesktopDisplaySnapshot[] displays, Rectangle bounds)
        {
            DesktopDisplaySnapshot contained = (displays ?? new DesktopDisplaySnapshot[0]).FirstOrDefault(d => Valid(d) && d.WorkingArea.Contains(bounds));
            return contained ?? Select(displays, null, Rectangle.Empty, new PointF(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f));
        }
    }

    internal sealed partial class AdventureController
    {
        private string desktopDisplayDeviceName;
        private Rectangle desktopDisplayTrackedArea;
        private readonly Dictionary<Form, Size> desktopPanelMinimumSizes = new Dictionary<Form, Size>();
        private string desktopPanelTopology;
        private HashSet<Form> desktopVisibleNormalPanels = new HashSet<Form>();

        // The session's existing UI timer is the only automatic caller. Screen
        // access stays here; injected overloads never query or change the OS.
        internal void RefreshDesktopDisplayLayout()
        {
            if (!IsolatedDesktop || disposed || !timer.Enabled || owner.IsDisposed || owner.InvokeRequired) return;
            DesktopDisplaySnapshot[] displays = ReadDesktopDisplays();
            RefreshDesktopDisplayLayout(displays);
            RestoreDesktopPanels(displays);
        }

        private static DesktopDisplaySnapshot[] ReadDesktopDisplays()
        { return Screen.AllScreens.Select(s => new DesktopDisplaySnapshot(s.DeviceName, s.WorkingArea, s.Primary)).ToArray(); }

        private bool DesktopDisplayDragging()
        { return dragging || battle.Enemies.Any(e => e.Dragging); }

        internal bool RefreshDesktopDisplayLayout(DesktopDisplaySnapshot[] displays)
        {
            if (!IsolatedDesktop || disposed || DesktopDisplayDragging()) return false;
            bool alreadyDragged = desktopDisplayTrackedArea.Width > 0 && desktopDisplayTrackedArea != area;
            string identity = desktopDisplayDeviceName;
            if (alreadyDragged)
            {
                DesktopDisplaySnapshot draggedDisplay = (displays ?? new DesktopDisplaySnapshot[0]).FirstOrDefault(d => DesktopDisplayLayout.Valid(d) && d.WorkingArea == area);
                if (draggedDisplay != null) identity = draggedDisplay.DeviceName;
            }
            DesktopDisplaySnapshot selected = DesktopDisplayLayout.Select(displays, identity, area, new PointF((float)x + HeroSize.Width / 2f, (float)y + HeroSize.Height / 2f));
            if (selected == null) return false;
            bool changed = selected.WorkingArea != area || alreadyDragged;
            if (changed)
            {
                // Drag callbacks and ordinary ticks already use the new area;
                // clamp those coordinates without a second origin translation.
                RebaseDesktopLayout(area, selected.WorkingArea);
            }
            desktopDisplayDeviceName = selected.DeviceName;
            desktopDisplayTrackedArea = selected.WorkingArea;
            return changed;
        }

        private void ChangeDesktopScreen(Screen requested)
        {
            if (disposed || requested == null || DesktopDisplayDragging()) return;
            DesktopDisplaySnapshot[] displays = ReadDesktopDisplays();
            DesktopDisplaySnapshot selected = DesktopDisplayLayout.Select(displays, requested.DeviceName, area, new PointF((float)x + HeroSize.Width / 2f, (float)y + HeroSize.Height / 2f));
            if (selected == null) return;
            if (selected.WorkingArea != area) RebaseDesktopLayout(area, selected.WorkingArea);
            desktopDisplayDeviceName = selected.DeviceName;
            desktopDisplayTrackedArea = selected.WorkingArea;
            RestoreDesktopPanels(displays);
        }

        private void RebaseDesktopLayout(Rectangle oldArea, Rectangle nextArea)
        {
            PointF hero = DesktopDisplayLayout.Translate(x, y, oldArea, nextArea, HeroSize);
            PointF target = DesktopDisplayLayout.Translate(targetX, targetY, oldArea, nextArea, HeroSize);
            area = nextArea; x = hero.X; y = hero.Y; targetX = target.X; targetY = target.Y; ClampHero();
            foreach (AdventureEnemy enemy in battle.Enemies)
            {
                PointF position = DesktopDisplayLayout.Translate(enemy.X, enemy.Y, oldArea, nextArea, EnemySize(enemy));
                PointF destination = DesktopDisplayLayout.Translate(enemy.TargetX, enemy.TargetY, oldArea, nextArea, EnemySize(enemy));
                enemy.X = position.X; enemy.Y = position.Y; enemy.TargetX = destination.X; enemy.TargetY = destination.Y; BoundEnemy(enemy);
            }
            PointF pull = DesktopDisplayLayout.Translate(blackholeX, blackholeY, oldArea, nextArea, Size.Empty);
            blackholeX = pull.X; blackholeY = pull.Y;
            foreach (LootView loot in lootViews)
            {
                PointF position = DesktopDisplayLayout.Translate(loot.X, loot.Y, oldArea, nextArea, new Size(160, 155));
                loot.X = position.X; loot.Y = position.Y;
            }
            PositionLandmark(); RenderWorld(true);
        }

        private void RestoreDesktopPanels(DesktopDisplaySnapshot[] displays)
        {
            if (!(displays ?? new DesktopDisplaySnapshot[0]).Any(DesktopDisplayLayout.Valid)) return;
            string topology = DesktopDisplayLayout.TopologyKey(displays);
            bool topologyChanged = desktopPanelTopology != topology;
            HashSet<Form> visibleNormal = new HashSet<Form>();
            foreach (Form retired in desktopPanelMinimumSizes.Keys.Where(f => f.IsDisposed).ToArray()) desktopPanelMinimumSizes.Remove(retired);
            foreach (Form form in Application.OpenForms.Cast<Form>().ToArray())
            {
                if (!DesktopDisplayLayout.IsFeaturePanel(form.GetType()) || !DesktopDisplayLayout.ShouldRestorePanel(form.Visible, form.IsDisposed, form.WindowState)) continue;
                visibleNormal.Add(form);
                // Leave ordinary edge/cross-monitor placement alone. Only a
                // display change, restored panel or wholly lost window needs fit.
                if (!DesktopDisplayLayout.ShouldRestorePanelBounds(topologyChanged, !desktopVisibleNormalPanels.Contains(form), form.Bounds, displays)) continue;
                DesktopDisplaySnapshot display = DesktopDisplayLayout.PanelDisplay(displays, form.Bounds);
                if (display == null) continue;
                Size originalMinimum;
                if (!desktopPanelMinimumSizes.TryGetValue(form, out originalMinimum)) { originalMinimum = form.MinimumSize; desktopPanelMinimumSizes.Add(form, originalMinimum); }
                Size minimum = new Size(Math.Min(originalMinimum.Width, display.WorkingArea.Width), Math.Min(originalMinimum.Height, display.WorkingArea.Height));
                if (form.MinimumSize != minimum) form.MinimumSize = minimum;
                Rectangle bounds = DesktopDisplayLayout.FitPanelBounds(form.Bounds, display.WorkingArea);
                if (form.Bounds != bounds) form.Bounds = bounds;
            }
            desktopPanelTopology = topology;
            desktopVisibleNormalPanels = visibleNormal;
        }
    }
}
