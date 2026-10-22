using System;
using System.Drawing;
using System.Windows.Forms;

namespace NetBunny
{
    // Isolated desktop windows share the first-steps journal palette. No sizing,
    // text, interaction, consent, or persistence is owned by this helper.
    internal static class DesktopFeatureStyle
    {
        internal static readonly Color Paper = Color.FromArgb(244, 237, 218);
        internal static readonly Color Surface = Color.FromArgb(251, 246, 232);
        internal static readonly Color Ink = Color.FromArgb(40, 67, 60);
        internal static readonly Color Gold = Color.FromArgb(132, 101, 49);
        internal static readonly Color Border = Color.FromArgb(191, 177, 141);

        internal static void Apply(Form form)
        {
            Style(form);
        }

        private static void Style(Control control)
        {
            Color original = control.ForeColor;
            control.Font = new Font("Microsoft YaHei UI", control.Font.Size, control.Font.Style);
            control.ForeColor = ToneColor(original);
            if (control is Form || control is FlowLayoutPanel) control.BackColor = Paper;
            else if (control is ListBox || control is ComboBox) control.BackColor = Surface;
            else if (control is Label)
            {
                if (control.BackColor == Color.White) control.BackColor = Surface;
                else control.BackColor = Paper;
                if (control.Font.Size >= 13) control.ForeColor = Ink;
                else if (control.Height == 24 && control.Text.Length > 0)
                {
                    control.Font = new Font("Microsoft YaHei UI", control.Font.Size, FontStyle.Bold);
                    control.ForeColor = Gold;
                }
            }
            else if (control is Panel) control.BackColor = Color.FromArgb(233, 233, 210);
            else if (control is BaiyePathGrid) control.BackColor = Paper;
            Button button = control as Button;
            if (button != null) ApplyButton(button);
            foreach (Control child in control.Controls) Style(child);
        }

        internal static void ApplyButton(Button button)
        {
            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(58, 91, 78);
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(27, 55, 47);
            button.EnabledChanged += delegate { UpdateButtonState(button); };
            UpdateButtonState(button);
            button.ForeColor = Surface;
            button.Cursor = Cursors.Hand;
        }

        private static void UpdateButtonState(Button button)
        {
            button.BackColor = button.Enabled ? Ink : Color.FromArgb(228, 220, 199);
            button.FlatAppearance.BorderColor = button.Enabled ? Border : Color.FromArgb(198, 187, 159);
        }

        internal static Color Tone(Control control, Color legacy)
        {
            return control.BackColor == Paper ? ToneColor(legacy) : legacy;
        }

        private static Color ToneColor(Color color)
        {
            if (color == Color.White) return Surface;
            if (color == Color.FromArgb(245, 244, 251)) return Paper;
            if (color == Color.FromArgb(115, 78, 191) || color == Color.FromArgb(73, 49, 132)) return Ink;
            if (color == Color.FromArgb(239, 232, 252)) return Color.FromArgb(231, 233, 209);
            if (color == Color.FromArgb(224, 220, 236) || color == Color.FromArgb(235, 230, 244)) return Border;
            if (color == Color.FromArgb(214, 210, 226)) return Color.FromArgb(170, 161, 134);
            if (color == Color.FromArgb(150, 146, 168)) return Color.FromArgb(113, 105, 83);
            if (color == Color.FromArgb(173, 113, 37) || color == Color.FromArgb(130, 84, 32)) return Gold;
            if (color == Color.FromArgb(91, 87, 107) || color == SystemColors.ControlText) return Ink;
            return color;
        }
    }
}
