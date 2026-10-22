using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace NetBunny
{
    // 百业道碑：展示已发现的修行道途、各道途熟练度与等级、当前识别或
    // 手动选择的活动、道韵获取记录，以及尚未解锁的机缘与传承。
    // 结构上先保证清晰可读，配色沿用修行面板的国风淡紫底，预留后续美术升级。
    internal sealed class BaiyeSteleForm : Form
    {
        private readonly AdventureController controller;
        private readonly RadioButton modeOff = new RadioButton(), modeCategory = new RadioButton(), modeManual = new RadioButton();
        private readonly ComboBox manualMarks = new ComboBox();
        private readonly Button cancelMark = new Button();
        private readonly Label activity = new Label(), notice = new Label();
        private readonly BaiyePathGrid grid = new BaiyePathGrid();
        private readonly ListBox gains = new ListBox(), encounters = new ListBox();
        private readonly Button explain = new Button(), closeStele = new Button();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private readonly ToolTip hints = new ToolTip { ShowAlways = true, AutoPopDelay = 10000 };
        private bool updating;

        internal BaiyeSteleForm(AdventureController controller)
        {
            this.controller = controller; Text = "桌面修仙传 · 百业道碑";
            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            Width = Math.Min(720, workArea.Width - 24); Height = Math.Min(880, workArea.Height - 40);
            MinimumSize = new Size(Math.Min(620, workArea.Width - 24), Math.Min(520, workArea.Height - 40));
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(245, 244, 251);
            Font = new Font("Microsoft YaHei UI", 10);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22), BackColor = BackColor };
            Controls.Add(content);
            Label title = Line("百业道碑 · 万般世事皆可证道", 42); title.Font = new Font(Font.FontFamily, 18, FontStyle.Bold); title.ForeColor = Color.FromArgb(73, 49, 132); content.Controls.Add(title);
            content.Controls.Add(Line("现实中的工作、学习、创作与娱乐，都会化作游戏里的后天道途。百业与灵根、境界、功法、挂机修炼相互独立，并行成长。", 44));

            content.Controls.Add(Line("活动识别方式", 24));
            FlowLayoutPanel modes = Row();
            modeOff.Text = "关闭活动识别"; modeOff.Width = 150;
            modeCategory.Text = "仅识别应用类别"; modeCategory.Width = 170;
            modeManual.Text = "应用类别 + 手动活动标记"; modeManual.Width = 240;
            modeOff.CheckedChanged += delegate { if (!updating && modeOff.Checked) ApplyMode(0); };
            modeCategory.CheckedChanged += delegate { if (!updating && modeCategory.Checked) ApplyMode(1); };
            modeManual.CheckedChanged += delegate { if (!updating && modeManual.Checked) ApplyMode(2); };
            modes.Controls.AddRange(new Control[] { modeOff, modeCategory, modeManual }); content.Controls.Add(modes);
            FlowLayoutPanel explainRow = Row();
            Configure(explain, "功能说明与隐私", 170, delegate { ShowExplanation(true); });
            explainRow.Controls.Add(explain); content.Controls.Add(explainRow);
            content.Controls.Add(Line("识别只看前台应用的类别（如「演示软件」「浏览器」），永不读取文档正文、代码、聊天内容、网页历史与屏幕画面；所有分类与结算数据只保存在本机。关闭识别后立即停止新的活动采样，普通挂机修炼不受影响。", 60));

            activity.Width = 640; activity.Height = 34; activity.BackColor = Color.White; activity.Padding = new Padding(10, 6, 10, 0); activity.BorderStyle = BorderStyle.FixedSingle; content.Controls.Add(activity);
            FlowLayoutPanel markRow = Row();
            Label markLabel = new Label { Text = "手动标记：", Width = 92, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 6, 0, 0) };
            manualMarks.DropDownStyle = ComboBoxStyle.DropDownList; manualMarks.Width = 320; manualMarks.Font = Font;
            manualMarks.Items.Add("（无手动标记 · 跟随自动识别）");
            foreach (BaiyeManualMark mark in BaiyeCatalog.ManualMarks)
                manualMarks.Items.Add(new MarkEntry { Id = mark.Id, Text = BaiyeCatalog.FindPath(mark.PathId).Name + " · " + mark.Label });
            manualMarks.SelectedIndexChanged += delegate
            {
                if (updating) return;
                MarkEntry entry = manualMarks.SelectedItem as MarkEntry;
                controller.SetBaiyeManualMark(entry == null ? null : entry.Id);
                RefreshData();
            };
            hints.SetToolTip(manualMarks, "识别不可靠或想指明具体活动时使用。手动标记同样只在你操作电脑时结算，锁屏与长时间无操作不产生道韵。");
            Configure(cancelMark, "取消标记", 100, delegate { controller.SetBaiyeManualMark(null); RefreshData(); });
            hints.SetToolTip(cancelMark, "立即取消手动标记，回到自动识别。长时间挂着手动标记会导致道途误分类。");
            markRow.Controls.Add(markLabel); markRow.Controls.Add(manualMarks); markRow.Controls.Add(cancelMark); content.Controls.Add(markRow);

            content.Controls.Add(Line("道途熟练度", 24));
            grid.Width = 640; grid.Height = 356; grid.Margin = new Padding(0, 2, 0, 7); content.Controls.Add(grid);
            notice.Width = 640; notice.Height = 30; notice.ForeColor = Color.FromArgb(173, 113, 37); content.Controls.Add(notice);

            content.Controls.Add(Line("机缘与传承", 24));
            encounters.Width = 640; encounters.Height = 96; encounters.IntegralHeight = false; encounters.Font = Font; encounters.BackColor = Color.FromArgb(250, 249, 253); content.Controls.Add(encounters);
            content.Controls.Add(Line("道韵获取记录", 24));
            gains.Width = 640; gains.Height = 150; gains.IntegralHeight = false; gains.Font = Font; gains.BackColor = Color.FromArgb(250, 249, 253); content.Controls.Add(gains);
            FlowLayoutPanel tail = Row();
            Configure(closeStele, "收起道碑", 140, delegate { Hide(); });
            tail.Controls.Add(closeStele); content.Controls.Add(tail);

            refresh.Interval = 500; refresh.Tick += delegate { if (Visible) RefreshData(); }; refresh.Start();
            Shown += delegate { RefreshData(); };
            if (controller.IsolatedDesktop) DesktopFeatureStyle.Apply(this);
        }

        private Label Line(string text, int height) { return new Label { Text = text, Width = 640, Height = height, AutoSize = true, MaximumSize = new Size(640, 0), MinimumSize = new Size(0, height), Margin = new Padding(0, 4, 0, 4) }; }
        private FlowLayoutPanel Row() { return new FlowLayoutPanel { Width = 650, Height = 40, AutoSize = true, MinimumSize = new Size(0, 40), WrapContents = true, Margin = new Padding(0, 3, 0, 3) }; }
        private void Configure(Button button, string text, int width, Action action) { button.Text = text; button.Width = width; button.Height = 34; button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(115, 78, 191); button.ForeColor = Color.White; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand; button.Click += delegate { action(); }; }

        private void ApplyMode(int mode)
        {
            if (mode > 0 && !controller.BaiyeExplained && !ShowExplanation(false)) { RefreshData(); return; }
            controller.SetBaiyeMode(mode);
            RefreshData();
        }
        // 首次启用前的功能说明；返回玩家是否确认开启。
        private bool ShowExplanation(bool passive)
        {
            DialogResult answer = MessageBox.Show(this,
                "百业证道把你现实中的活动化为游戏道途：\n\n" +
                "· 只识别前台窗口所属应用的类别（演示、写作、浏览器、编程、AI、建模、剪辑、游戏）\n" +
                "· 永不读取文档正文、代码内容、聊天内容、网页历史或屏幕画面\n" +
                "· 同一时间只结算一种活动；电脑锁屏、长时间无人操作不产生道韵\n" +
                "· 所有分类与结算数据只保存在本机，可随时改回「关闭活动识别」\n" +
                "· 百业与基础挂机修炼相互独立，关闭百业不影响修炼\n\n" +
                (passive ? "" : "确认了解并开启活动识别吗？"),
                "百业证道 · 功能说明与隐私",
                passive ? MessageBoxButtons.OK : MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);
            if (passive || answer != DialogResult.OK) return false;
            controller.AcknowledgeBaiye();
            return true;
        }

        internal void RefreshData()
        {
            if (IsDisposed) return; updating = true;
            try
            {
                BaiyeProgress progress = controller.Baiye;
                modeOff.Checked = progress.Mode == 0; modeCategory.Checked = progress.Mode == 1; modeManual.Checked = progress.Mode == 2;
                manualMarks.Enabled = progress.Mode == 2;
                int markIndex = 0;
                if (progress.ManualMarkId.Length > 0)
                    for (int i = 1; i < manualMarks.Items.Count; i++)
                        if (((MarkEntry)manualMarks.Items[i]).Id == progress.ManualMarkId) { markIndex = i; break; }
                if (manualMarks.SelectedIndex != markIndex) manualMarks.SelectedIndex = markIndex;
                cancelMark.Enabled = progress.Mode == 2 && progress.ManualMarkId.Length > 0;
                activity.Text = "当前活动：" + controller.BaiyeActivityText +
                    (progress.Mode == 2 && progress.ManualMarkId.Length > 0 ? "　【手动标记生效中】" : "");
                grid.SetProgress(progress, controller.BaiyeCurrentPathId);
                notice.Text = controller.BaiyeNotice ?? "";

                encounters.Items.Clear();
                foreach (BaiyeEncounterDef def in BaiyeEncounters.All)
                {
                    long ticks;
                    if (progress.EncounterUnlockedUtc.TryGetValue(def.Id, out ticks))
                        encounters.Items.Add("✓ " + def.Name + " — " + def.RewardText + " · " + new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("MM-dd HH:mm", CultureInfo.InvariantCulture));
                    else
                        encounters.Items.Add("○ ？？？ · " + def.Hint);
                }
                if (progress.Heritages.Count == 0) encounters.Items.Add("传承：（尚未获得传承器物）");
                else foreach (string heritage in progress.Heritages)
                    encounters.Items.Add("传承：" + HeritageName(heritage));
                if (controller.BaiyeResonanceHint.Length > 0) encounters.Items.Add(controller.BaiyeResonanceHint);

                gains.Items.Clear();
                for (int i = progress.Log.Count - 1; i >= 0; i--) gains.Items.Add(progress.Log[i].Format());
                if (gains.Items.Count == 0) gains.Items.Add("（尚无道韵记录：开启识别或手动标记后，在有效活动时段内积累）");
            }
            finally { updating = false; }
        }
        private static string HeritageName(string id)
        {
            foreach (BaiyeEncounterDef def in BaiyeEncounters.All) if (def.HeritageId == id) return def.HeritageName + "（" + def.Name + "机缘所赠）";
            return id;
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { refresh.Stop(); refresh.Dispose(); hints.Dispose(); } base.Dispose(disposing); }
        private sealed class MarkEntry { internal string Id, Text; public override string ToString() { return Text; } }
    }

    // 八道途熟练度碑面：已发现的道途显示名称、境界与进度；
    // 未发现的只留一方「？？？」空碑，等待机缘。
    internal sealed class BaiyePathGrid : Control
    {
        private BaiyeProgress progress;
        private string activePathId;
        internal BaiyePathGrid()
        {
            DoubleBuffered = true; BackColor = Color.White; Font = new Font("Microsoft YaHei UI", 9);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "百业道途熟练度"; AccessibleRole = AccessibleRole.List; TabStop = false;
        }
        internal void SetProgress(BaiyeProgress value, string active)
        {
            progress = value; activePathId = active; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.Clear(DesktopFeatureStyle.Tone(this, Color.FromArgb(245, 244, 251))); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int columns = 2, rows = 4;
            int cellWidth = (Width - 8 * (columns + 1)) / columns, cellHeight = (Height - 8 * (rows + 1)) / rows;
            for (int i = 0; i < BaiyeCatalog.Paths.Length; i++)
            {
                BaiyePathDef path = BaiyeCatalog.Paths[i];
                int cx = 8 + (i % columns) * (cellWidth + 8), cy = 8 + (i / columns) * (cellHeight + 8);
                Rectangle cell = new Rectangle(cx, cy, cellWidth, cellHeight);
                bool discovered = progress != null && progress.Discovered(path.Id);
                bool active = discovered && path.Id == activePathId;
                Art.Fill(g, cell, 12, active ? DesktopFeatureStyle.Tone(this, Color.FromArgb(239, 232, 252)) : DesktopFeatureStyle.Tone(this, Color.White));
                using (Pen border = new Pen(active ? DesktopFeatureStyle.Tone(this, Color.FromArgb(115, 78, 191)) : DesktopFeatureStyle.Tone(this, Color.FromArgb(224, 220, 236)), active ? 2 : 1))
                    g.DrawRectangle(border, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
                Rectangle seal = new Rectangle(cell.X + 12, cell.Y + 12, 40, 40);
                Art.Fill(g, seal, 9, discovered ? DesktopFeatureStyle.Tone(this, Color.FromArgb(115, 78, 191)) : DesktopFeatureStyle.Tone(this, Color.FromArgb(214, 210, 226)));
                using (Font sealFont = new Font(Font.FontFamily, 15, FontStyle.Bold))
                    TextRenderer.DrawText(g, discovered ? path.Name.Substring(0, 1) : "？", sealFont, seal, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                Rectangle nameBounds = new Rectangle(cell.X + 62, cell.Y + 10, cell.Width - 74, 22);
                using (Font nameFont = new Font(Font.FontFamily, 10.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, discovered ? path.Name : "？？？", nameFont, nameBounds, discovered ? DesktopFeatureStyle.Tone(this, Color.FromArgb(73, 49, 132)) : DesktopFeatureStyle.Tone(this, Color.FromArgb(150, 146, 168)), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                Rectangle detailBounds = new Rectangle(cell.X + 62, cell.Y + 32, cell.Width - 74, 30);
                if (discovered)
                {
                    int level = progress.LevelOf(path.Id);
                    double yun = progress.YunOf(path.Id);
                    double baseYun = BaiyeCatalog.YunForLevel(level), nextYun = BaiyeCatalog.YunForLevel(level + 1);
                    string line = BaiyeCatalog.LevelName(level) + " · 道韵 " + ((int)yun).ToString(CultureInfo.InvariantCulture) + " / " + ((int)nextYun).ToString(CultureInfo.InvariantCulture);
                    TextRenderer.DrawText(g, line, Font, detailBounds, DesktopFeatureStyle.Tone(this, Color.FromArgb(91, 87, 107)), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    Rectangle track = new Rectangle(cell.X + 62, cell.Bottom - 14, cell.Width - 74, 5);
                    Art.Fill(g, track, 2, DesktopFeatureStyle.Tone(this, Color.FromArgb(235, 230, 244)));
                    double ratio = nextYun > baseYun ? Math.Min(1, Math.Max(0, (yun - baseYun) / (nextYun - baseYun))) : 0;
                    if (ratio > 0)
                    {
                        int fill = Math.Max(2, (int)Math.Round(track.Width * ratio));
                        using (Brush brush = new SolidBrush(DesktopFeatureStyle.Tone(this, Color.FromArgb(115, 78, 191)))) g.FillRectangle(brush, track.X, track.Y, fill, track.Height);
                    }
                }
                else
                    TextRenderer.DrawText(g, "尚未识得的道途 · " + path.Description, Font, detailBounds, DesktopFeatureStyle.Tone(this, Color.FromArgb(150, 146, 168)), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
        }
    }
}
