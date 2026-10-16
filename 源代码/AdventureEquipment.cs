using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace NetBunny
{
    internal sealed class AdventureEquipmentForm : Form
    {
        private readonly AdventureController controller;
        private readonly Label stats = new Label(), reward = new Label(), detail = new Label();
        private readonly DiskSpaceBar diskSpace = new DiskSpaceBar();
        private readonly ResourceGrowthCard resourceGrowth = new ResourceGrowthCard();
        private readonly CultivationCard cultivation = new CultivationCard();
        private readonly ComboBox techniques = new ComboBox();
        private readonly Button breakthrough = new Button(), pauseCultivation = new Button();
        private readonly CheckBox cleanupConsent = new CheckBox(), memoryConsent = new CheckBox();
        private readonly ListBox weapons = new ListBox();
        private readonly RadioButton automatic = new RadioButton(), manual = new RadioButton();
        private readonly CheckBox roam = new CheckBox();
        private readonly ComboBox themes = new ComboBox(), sizes = new ComboBox(), monitors = new ComboBox();
        private readonly Button equip = new Button(), revive = new Button(), blackhole = new Button(), rescan = new Button(), demo = new Button(), cancel = new Button();
        private readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        private readonly ToolTip hints = new ToolTip { ShowAlways = true, AutoPopDelay = 10000 };
        private bool updating;
        private string inventoryKey = "";
        private static readonly string[] ThemeCodes = { "AUTO", "OFF", "CN", "JP", "US", "TW", "HK", "OTHER" };
        internal AdventureEquipmentForm(AdventureController controller)
        {
            this.controller = controller; Text = "桌面修仙传 · 修行与装备"; Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea; Width = Math.Min(760, workArea.Width - 24); Height = Math.Min(900, workArea.Height - 40); MinimumSize = new Size(Math.Min(650, workArea.Width - 24), Math.Min(540, workArea.Height - 40));
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(245, 244, 251); Font = new Font("Microsoft YaHei UI", 10);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            FlowLayoutPanel content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22), BackColor = BackColor };
            Controls.Add(content);
            Label title = TextLine("修行面板 · 你的桌面洞天", 48); title.Font = new Font(Font.FontFamily, 19, FontStyle.Bold); title.ForeColor = Color.FromArgb(73, 49, 132); content.Controls.Add(title);
            cultivation.Width = 640; cultivation.Margin = new Padding(0, 2, 0, 7); content.Controls.Add(cultivation);
            FlowLayoutPanel practice = Row();
            techniques.DropDownStyle = ComboBoxStyle.DropDownList; techniques.Width = 210; techniques.Font = Font;
            foreach (CultivationTechnique technique in CultivationTechnique.All) techniques.Items.Add(new TechniqueEntry { Id = technique.Id, Text = technique.Name + "（" + technique.School + "）" });
            techniques.SelectedIndexChanged += delegate { if (!updating) { TechniqueEntry entry = techniques.SelectedItem as TechniqueEntry; if (entry != null) controller.ChooseTechnique(entry.Id); } };
            hints.SetToolTip(techniques, "功法可自由改修，只影响修行速度的微小加成；灵根与路线都不会被锁死。");
            Configure(breakthrough, "尝试破境", 130, delegate
            {
                CultivationProgress progress = controller.Cultivation;
                if (progress == null || controller.IsDemo) return;
                string question = "当前境界：" + progress.RealmName + " · 第" + progress.Layer + "层\n本次破境成功率：" + progress.BreakthroughPercent + "%（第" + CultivationBalance.LayersPerRealm + "层时 100%）\n消耗灵气：" + progress.BreakthroughCost + "\n失败不会损失已积累的修为与根基。\n\n现在尝试破境吗？";
                if (MessageBox.Show(this, question, "尝试破境", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                bool success = controller.TryBreakthrough();
                MessageBox.Show(this, controller.Cultivation.LastNotice, success ? "破境成功" : "破境未成", MessageBoxButtons.OK, success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                RefreshData();
            });
            Configure(pauseCultivation, "暂停修炼", 110, delegate { controller.SetCultivationPaused(!controller.CultivationPaused); RefreshData(); });
            practice.Controls.AddRange(new Control[] { techniques, breakthrough, pauseCultivation }); content.Controls.Add(practice);
            FlowLayoutPanel consentRow = Row();
            cleanupConsent.Text = "授权文件清理：击败文件怪后，删除其绑定的、超过 24 小时的本用户临时文件";
            cleanupConsent.Width = 470; cleanupConsent.Height = 30; cleanupConsent.BackColor = Color.Transparent;
            cleanupConsent.CheckedChanged += delegate
            {
                if (updating) return;
                if (cleanupConsent.Checked)
                {
                    DialogResult answer = MessageBox.Show(this,
                        "本授权只覆盖文件清理：\n· 击败文件怪后，删除其绑定的、超过 24 小时且未被占用的本用户临时文件\n\n不包含内存工作集回收（那是下方独立授权）。\n击杀怪物本身永远不会自动删除文件；授权状态会保存，可随时关闭。\n确认开启文件清理授权吗？",
                        "文件清理授权", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (answer != DialogResult.OK) { updating = true; cleanupConsent.Checked = false; updating = false; return; }
                }
                if (!controller.SetCleanupConsent(cleanupConsent.Checked)) { updating = true; cleanupConsent.Checked = controller.MaintenanceConsentCleanup; updating = false; }
                RefreshData();
            };
            consentRow.Controls.Add(cleanupConsent); content.Controls.Add(consentRow);
            FlowLayoutPanel memoryConsentRow = Row();
            memoryConsent.Text = "授权内存维护：击败内存 Boss 后，回收同会话后台进程的工作集";
            memoryConsent.Width = 470; memoryConsent.Height = 30; memoryConsent.BackColor = Color.Transparent;
            memoryConsent.CheckedChanged += delegate
            {
                if (updating) return;
                if (memoryConsent.Checked)
                {
                    DialogResult answer = MessageBox.Show(this,
                        "本授权只覆盖内存维护：\n· 击败内存 Boss 后，尝试回收同会话后台进程的工作集，并显示实测前后占用\n\n不包含任何文件删除（那是上方独立授权）。应用再次访问内存时占用可能回升。\n授权状态会保存，可随时关闭。\n确认开启内存维护授权吗？",
                        "内存维护授权", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (answer != DialogResult.OK) { updating = true; memoryConsent.Checked = false; updating = false; return; }
                }
                if (!controller.SetMemoryConsent(memoryConsent.Checked)) { updating = true; memoryConsent.Checked = controller.MaintenanceConsentMemory; updating = false; }
                RefreshData();
            };
            memoryConsentRow.Controls.Add(memoryConsent); content.Controls.Add(memoryConsentRow);
            content.Controls.Add(TextLine("默认纯修仙模式：修炼、战斗、修为与境界成长都不依赖真实文件；未开启授权时，击败任何妖物只结算虚拟奖励，不会删除文件或回收内存。", 50));
            stats.Width = 640; stats.Height = 108; stats.BackColor = Color.White; stats.Padding = new Padding(12); stats.BorderStyle = BorderStyle.FixedSingle; stats.Font = new Font(Font.FontFamily, 12, FontStyle.Bold); content.Controls.Add(stats);
            diskSpace.Width = 640; diskSpace.Height = 95; diskSpace.BackColor = BackColor; diskSpace.Margin = new Padding(0, 7, 0, 7); content.Controls.Add(diskSpace);
            resourceGrowth.Width = 640; resourceGrowth.Margin = new Padding(0, 0, 0, 7); content.Controls.Add(resourceGrowth);
            reward.Width = 640; reward.Height = 52; reward.ForeColor = Color.FromArgb(104, 64, 181); content.Controls.Add(reward);
            content.Controls.Add(TextLine("武器背包（升级必得新武器，也有 28% 清理掉落机会）", 26));
            weapons.Width = 640; weapons.Height = 139; weapons.IntegralHeight = false; weapons.HorizontalScrollbar = true; weapons.Font = Font; content.Controls.Add(weapons);
            FlowLayoutPanel actions = Row(); Configure(equip, "装备选中武器", 150, delegate { WeaponEntry item = weapons.SelectedItem as WeaponEntry; if (item != null) controller.ChooseWeapon(item.Id); RefreshData(); });
            Configure(revive, "从墓碑复活", 130, delegate { controller.Resurrect(); RefreshData(); });
            actions.Controls.AddRange(new Control[] { equip, revive }); content.Controls.Add(actions);
            content.Controls.Add(TextLine("作战方式 · 白骨妖兵 / 碧渊水灵 / 独眼山魈 / 夜行蝠妖 / 幻菌菇妖随机现身，洞天的灵气也会持续吸引虚拟妖物来访", 30));
            FlowLayoutPanel modes = Row(); manual.Text = "手动点选作战"; manual.Width = 180; manual.Height = 34;
            automatic.Text = "自动作战（默认 · 挂机自主修炼）"; automatic.Width = 250; automatic.Height = 34;
            manual.CheckedChanged += delegate { if (!updating && manual.Checked) controller.SetAutoPlan(false); };
            automatic.CheckedChanged += delegate { if (!updating && automatic.Checked) controller.SetAutoPlan(true); };
            modes.Controls.AddRange(new Control[] { manual, automatic }); content.Controls.Add(modes);
            content.Controls.Add(TextLine("自动模式会利用攻击冷却退避、侧移，并根据武器射程反击。自动作战是独立于两项真实维护授权的偏好；力竭后修士入定调息约一分钟自行恢复，无需定时上线。", 44));
            content.Controls.Add(TextLine("每击杀一只怪物随机获得 2～3 经验，只结算一次。怪物空血后立即碎裂消失；临时文件在后台清理，占用或失败的文件记为待重扫，不会让死怪复活。", 56));
            FlowLayoutPanel clear = Row(); Configure(blackhole, "一键清场并清理本批临时文件", 300, delegate { controller.ClearField(); RefreshData(); }); Configure(cancel, "取消待办清理", 155, delegate { controller.CancelPending(); RefreshData(); }); clear.Controls.AddRange(new Control[] { blackhole, cancel }); content.Controls.Add(clear);
            content.Controls.Add(TextLine("一键清场处理眼前的文件怪，邪恶分身 Boss 需正常战斗。每只击杀仍得 2～3 经验；文件占用或近期文件会跳过，只有实际删除计入磁盘释放。", 56));
            FlowLayoutPanel queue = Row(); Configure(rescan, "重新扫描临时文件", 175, delegate { controller.RefreshWorld(); RefreshData(); }); Configure(demo, "播放安全演示", 155, delegate { if (controller.IsDemo) controller.StopDemo(); else controller.PlayDemo(); RefreshData(); }); queue.Controls.AddRange(new Control[] { rescan, demo }); content.Controls.Add(queue);
            content.Controls.Add(TextLine("敌人总数最多 11 个（含 Boss），普通文件怪最多 9 个。新增临时文件立即触发复核，有空位就补充；普通怪血量随主角等级提高，保持已损失的血量比例。", 56));
            content.Controls.Add(TextLine("文件变化约 0.35 秒合并后立即扫描，另每 2 秒复核；不再等待资源阈值或休整。城堡显示实测可处理项数/大小。近期文件也会产生怪物，击败后仍须满足超过 24 小时、未占用等检查才会删除。", 76));
            content.Controls.Add(TextLine("地区皮肤与地标", 26));
            themes.DropDownStyle = ComboBoxStyle.DropDownList; themes.Width = 640; themes.Items.AddRange(new object[] { "自动 · 按 VPN / 代理出口地区", "关闭自动识别 · 保留当前造型", "中国 · 剑修与妖塔", "日本 · 武士与鸟居神龛", "美国 · 牛仔与自由女神像", "台湾 · 青蛙与旧屋", "香港 · 街头小弟与九龙城寨", "其他地区 · 骑士与恶魔古堡" });
            themes.SelectedIndexChanged += delegate { if (!updating && themes.SelectedIndex >= 0) controller.ChooseTheme(ThemeCodes[themes.SelectedIndex]); }; content.Controls.Add(themes);
            Button refreshRegion = new Button(); Configure(refreshRegion, "刷新出口地区", 165, delegate { controller.RefreshRegion(); }); content.Controls.Add(refreshRegion);
            content.Controls.Add(TextLine(RegionThemeService.PrivacyNote + " 所有皮肤共用武器和等级。", 64));
            FlowLayoutPanel display = Row(); roam.Text = "骑士满屏漫游"; roam.Width = 150; roam.Height = 30; roam.Checked = controller.Roaming; roam.CheckedChanged += delegate { if (!updating) controller.Roaming = roam.Checked; }; hints.SetToolTip(roam, "只控制骑士空闲漫游。怪物主动追击和攻击；修士力竭后入定调息并自行恢复，断网时本地修炼与战斗照常；警示Boss不攻击。"); display.Controls.Add(roam);
            sizes.DropDownStyle = ComboBoxStyle.DropDownList; sizes.Width = 130; sizes.Items.AddRange(new object[] { "小 · 80%", "中 · 100%", "大 · 120%" }); sizes.SelectedIndex = 1; sizes.SelectedIndexChanged += delegate { if (!updating && sizes.SelectedIndex >= 0) controller.ChangeScale(new[] { .8f, 1f, 1.2f }[sizes.SelectedIndex]); }; display.Controls.Add(sizes);
            monitors.DropDownStyle = ComboBoxStyle.DropDownList; monitors.Width = 190; int n = 0; foreach (Screen screen in Screen.AllScreens) monitors.Items.Add("屏幕 " + (++n) + (screen.Primary ? "（主屏）" : "")); monitors.SelectedIndex = Math.Max(0, Array.FindIndex(Screen.AllScreens, screen => screen.WorkingArea == controller.WorkingArea)); monitors.SelectedIndexChanged += delegate { if (!updating && monitors.SelectedIndex >= 0) controller.ChangeScreen(Screen.AllScreens[monitors.SelectedIndex]); }; display.Controls.Add(monitors);
            Button hide = new Button(); Configure(hide, "隐藏桌面角色", 160, delegate { controller.Enabled = false; }); display.Controls.Add(hide); content.Controls.Add(display);
            content.Controls.Add(TextLine("C 盘剩余不足 1 GiB 时出现磁盘危机 Boss，只清理它绑定的已扫描临时文件，释放量按实际结果计算；当前没有安全候选时只提示，不扩大清理范围。", 56));
            content.Controls.Add(TextLine("RAM 每秒采样，超过 70% 出现随机邪恶皮肤/武器的 Boss，普通战斗按约 2 分钟调校。击败后回收同会话后台应用的工作集，显示实测前后占用；应用再次访问时占用可能回升。", 50));
            detail.Width = 640; detail.Height = 100; detail.AutoSize = true; detail.MaximumSize = new Size(640, 0); detail.ForeColor = Color.FromArgb(100, 99, 121); content.Controls.Add(detail);
                        Action fit = delegate {
                int width = Math.Max(460, content.ClientSize.Width - content.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 8);
                foreach (Control control in content.Controls) {
                    if (control is Button) continue;
                    control.Width = width;
                    Label label = control as Label; if (label != null && label != stats && label != reward) label.MaximumSize = new Size(width, 0);
                }
            };
            content.SizeChanged += delegate { fit(); }; fit();
            refresh.Interval = 350; refresh.Tick += delegate { if (Visible) RefreshData(); }; refresh.Start();
            Shown += delegate { RefreshData(); };
        }
        private Label TextLine(string text, int height) { return new Label { Text = text, Width = 640, Height = height, AutoSize = true, MaximumSize = new Size(640, 0), MinimumSize = new Size(0, height), Margin = new Padding(0, 4, 0, 4) }; }
        private FlowLayoutPanel Row() { return new FlowLayoutPanel { Width = 650, Height = 43, AutoSize = true, MinimumSize = new Size(0, 43), WrapContents = true, Margin = new Padding(0, 3, 0, 3) }; }
        private void Configure(Button button, string text, int width, Action action) { button.Text = text; button.Width = width; button.Height = 34; button.FlatStyle = FlatStyle.Flat; button.BackColor = Color.FromArgb(115, 78, 191); button.ForeColor = Color.White; button.FlatAppearance.BorderSize = 0; button.Cursor = Cursors.Hand; button.Click += delegate { action(); }; }
        internal void RefreshData()
        {
            if (IsDisposed) return; updating = true;
            try
            {
                AdventureBattle battle = controller.Battle; AdventureProgress p = battle.Progress;
                CultivationProgress practice = controller.Cultivation;
                if (practice != null)
                {
                    double ratio = practice.Layer >= CultivationBalance.LayersPerRealm ? 1 : practice.NextLayerCost <= 0 ? 0 : Math.Min(1, practice.Qi / (double)practice.NextLayerCost);
                    string heading = (String.IsNullOrEmpty(practice.DaoTitle) ? "无名修士" : practice.DaoTitle) + " · " + practice.RealmName + " · 第" + practice.Layer + "层";
                    string progressText = "灵根：" + practice.RootName + " · 路线：" + CultivationIdentity.PathName(practice.DaoPath) + " · 洞天：" + controller.CaveText +
                        "\r\n功法：" + practice.Technique.Name + "（" + practice.Technique.School + " · 速率 ×" + practice.Technique.RateFactor.ToString("0.00", CultureInfo.InvariantCulture) + "）· " + (controller.CultivationPaused ? "已暂停" : "吐纳中") +
                        "\r\n修为 " + practice.Qi + " / " + (practice.Layer >= CultivationBalance.LayersPerRealm ? "圆满" : practice.NextLayerCost.ToString(CultureInfo.InvariantCulture)) + " · 累计修为 " + practice.LifetimeQi + " · 已尝试破境 " + practice.BreakthroughAttempts + " 次" +
                        "\r\n" + (practice.BreakthroughPercent > 0 ? "当前破境机会 " + practice.BreakthroughPercent + "% · " : "第" + CultivationBalance.BreakthroughOpenLayer + "层起可尝试破境 · ") + "下一妖物约 " + (int)Math.Ceiling(controller.NextEncounterSeconds) + " 秒后来访" +
                        "\r\n" + practice.LastNotice;
                    cultivation.SetProgress(heading, progressText, ratio);
                    hints.SetToolTip(cultivation, heading + "\n" + progressText);
                    int techniqueIndex = Array.FindIndex(CultivationTechnique.All, technique => technique.Id == practice.TechniqueId);
                    if (techniqueIndex >= 0 && techniques.SelectedIndex != techniqueIndex) techniques.SelectedIndex = techniqueIndex;
                    breakthrough.Enabled = practice.CanBreakthrough && !controller.IsDemo;
                    breakthrough.Text = practice.IsFinalRealm ? "已至巅峰" : "尝试破境（" + practice.BreakthroughPercent + "%）";
                    pauseCultivation.Text = controller.CultivationPaused ? "恢复修炼" : "暂停修炼";
                    cleanupConsent.Checked = controller.MaintenanceConsentCleanup;
                    memoryConsent.Checked = controller.MaintenanceConsentMemory;
                }
                diskSpace.SetSnapshot(controller.DiskSnapshot); hints.SetToolTip(diskSpace, diskSpace.Summary);
                resourceGrowth.SetStatus(controller.ResourceGrowthText, controller.ResourceGrowthRatio); hints.SetToolTip(resourceGrowth, resourceGrowth.Summary);
                stats.Text = "战斗Lv." + p.Level + "（战斗熟练等级，与修仙境界相互独立）   经验 " + p.Experience + " / " + p.NextLevelExperience + "   生命 " + p.Health + " / " + p.MaxHealth + "   RAM " + controller.MemoryText + "\r\n战斗分 " + p.Score + "   累计实际清理 " + AdventureBattle.FormatBytes(p.FreedBytes) + "   排队 " + battle.QueueCount + "\r\n" + (controller.IsDemo ? "纯演示：本页不改变真实进度，不会清理文件" : controller.StateText);
                reward.Text = battle.LastReward;
                string key = String.Join(",", p.Inventory.ToArray()) + ":" + p.WeaponId;
                if (key != inventoryKey)
                {
                    inventoryKey = key; weapons.Items.Clear();
                    foreach (string id in p.Inventory) { AdventureWeapon w = AdventureWeapon.Find(id); weapons.Items.Add(new WeaponEntry { Id = id, Text = (id == p.WeaponId ? "✓ " : "   ") + w.Name + "  —  " + w.Description }); }
                    for (int i = 0; i < weapons.Items.Count; i++) if (((WeaponEntry)weapons.Items[i]).Id == p.WeaponId) { weapons.SelectedIndex = i; break; }
                }
                equip.Enabled = !controller.IsDemo; revive.Enabled = p.Dead && !controller.IsDemo; blackhole.Enabled = controller.CanClearField;
                automatic.Checked = battle.Automatic; manual.Checked = !battle.Automatic; automatic.Enabled = manual.Enabled = !p.Dead && !controller.IsDemo;
                themes.SelectedIndex = Array.IndexOf(ThemeCodes, controller.ThemeMode); themes.Enabled = !controller.IsDemo;
                roam.Checked = controller.Roaming; monitors.SelectedIndex = Math.Max(0, Array.FindIndex(Screen.AllScreens, screen => screen.WorkingArea == controller.WorkingArea));
                bool queueBusy = battle.QueueCount > 0 || battle.Enemies.Any(enemy => enemy.CleanupStarted && enemy.Cleaning);
                rescan.Enabled = !controller.IsScanning && !controller.IsDemo && !queueBusy;
                rescan.Text = queueBusy ? "清理中 · 稍后扫描" : controller.IsScanning ? "正在扫描临时文件" : "重新扫描临时文件";
                string scanHint = queueBusy ? "先完成或取消当前清理队列，再重新扫描；清理过程中不会替换已授权的文件批次。" : controller.IsScanning ? "正在读取临时文件清单，请等待扫描完成。" : controller.IsDemo ? "安全演示期间不扫描真实临时文件。" : "重新检查 24 小时以上的临时文件并更新怪物；扫描本身不删除文件。";
                hints.SetToolTip(rescan, scanHint); if (rescan.Parent != null) hints.SetToolTip(rescan.Parent, scanHint);
                cancel.Enabled = !controller.IsDemo;
                demo.Text = controller.IsDemo ? "结束演示" : "播放安全演示"; detail.Text = controller.DetailText;
            }
            finally { updating = false; }
        }
        protected override void OnFormClosing(FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } base.OnFormClosing(e); }
        protected override void Dispose(bool disposing) { if (disposing) { refresh.Stop(); refresh.Dispose(); hints.Dispose(); } base.Dispose(disposing); }
        private sealed class WeaponEntry { internal string Id, Text; public override string ToString() { return Text; } }
        private sealed class TechniqueEntry { internal string Id, Text; public override string ToString() { return Text; } }
    }

    // Cultivation status card: dao title, realm and layer on top, practice details
    // in the middle, and a qi progress bar that never claims more than measured.
    internal sealed class CultivationCard : Control
    {
        private string heading = "修士苏醒中", summary = "等待修炼数据";
        private double ratio = -1;
        internal CultivationCard()
        {
            DoubleBuffered = true; BackColor = Color.White; Font = new Font("Microsoft YaHei UI", 9);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "修士境界与修为进度"; AccessibleRole = AccessibleRole.ProgressBar; TabStop = false;
            Width = 640; FitText();
        }
        internal void SetProgress(string title, string text, double progress)
        {
            string nextTitle = String.IsNullOrEmpty(title) ? "修士" : title;
            string next = String.IsNullOrEmpty(text) ? "等待修炼数据" : text;
            double nextRatio = Double.IsNaN(progress) || Double.IsInfinity(progress) ? -1 : Math.Max(-1, Math.Min(1, progress));
            if (nextTitle == heading && next == summary && nextRatio == ratio) return;
            heading = nextTitle; summary = next; ratio = nextRatio; AccessibleDescription = heading + " " + summary; FitText(); Invalidate();
        }
        private void FitText()
        {
            Size measured = TextRenderer.MeasureText(summary, Font, new Size(Math.Max(1, Width - 32), Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            Height = Math.Max(128, measured.Height + 78);
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); FitText(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.Clear(Color.FromArgb(245, 244, 251)); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Art.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), 16, Color.White);
            Color accent = ratio >= 1 ? Color.FromArgb(173, 113, 37) : Color.FromArgb(91, 76, 168);
            using (Font titleFont = new Font(Font.FontFamily, 11, FontStyle.Bold))
                TextRenderer.DrawText(g, heading, titleFont, new Rectangle(16, 10, Width - 32, 24), accent, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, summary, Font, new Rectangle(16, 38, Math.Max(1, Width - 32), Math.Max(1, Height - 66)), Color.FromArgb(91, 87, 107), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            Rectangle track = new Rectangle(16, Height - 16, Math.Max(1, Width - 32), 6);
            Art.Fill(g, track, 3, Color.FromArgb(235, 230, 244));
            if (ratio > 0)
            {
                int fill = Math.Max(1, (int)Math.Round(track.Width * ratio));
                if (fill < 6) using (Brush brush = new SolidBrush(accent)) g.FillRectangle(brush, track.X, track.Y, fill, track.Height);
                else Art.Fill(g, new Rectangle(track.X, track.Y, fill, track.Height), 3, accent);
            }
        }
    }

    // Multiline status stays in its own measured card instead of overfilling the level/health label.
    internal sealed class ResourceGrowthCard : Control
    {
        private string summary = "等待资源采样";
        private double ratio = -1;
        internal string Summary { get { return summary; } }
        internal double Ratio { get { return ratio; } }
        internal Rectangle TextBounds { get { return new Rectangle(16, 35, Math.Max(1, Width - 32), Math.Max(1, Height - 58)); } }
        internal ResourceGrowthCard()
        {
            DoubleBuffered = true; BackColor = Color.White; Font = new Font("Microsoft YaHei UI", 9);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "城堡实时监测与敌人数量"; AccessibleRole = AccessibleRole.ProgressBar; TabStop = false;
            Width = 640; FitText();
        }
        internal void SetStatus(string text, double progress)
        {
            string next = String.IsNullOrEmpty(text) ? "等待资源采样" : text;
            double nextRatio = Double.IsNaN(progress) || Double.IsInfinity(progress) ? -1 : Math.Max(-1, Math.Min(1, progress));
            if (next == summary && nextRatio == ratio) return;
            summary = next; ratio = nextRatio; AccessibleDescription = summary; FitText(); Invalidate();
        }
        private void FitText()
        {
            Size measured = TextRenderer.MeasureText(summary, Font, new Size(Math.Max(1, Width - 32), Int32.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            Height = Math.Max(110, measured.Height + 63);
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); FitText(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.Clear(Color.FromArgb(245, 244, 251)); g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Art.Fill(g, new Rectangle(0, 0, Width - 1, Height - 1), 16, Color.White);
            Color accent = ratio >= 1 ? Color.FromArgb(173, 113, 37) : Color.FromArgb(115, 78, 191);
            using (Font heading = new Font(Font.FontFamily, 10, FontStyle.Bold))
                TextRenderer.DrawText(g, "城堡实时监测 · 战场容量", heading, new Rectangle(16, 10, Width - 32, 22), accent, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, summary, Font, TextBounds, Color.FromArgb(91, 87, 107), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            Rectangle track = new Rectangle(16, Height - 16, Math.Max(1, Width - 32), 6);
            Art.Fill(g, track, 3, Color.FromArgb(235, 230, 244));
            if (ratio > 0)
            {
                int fill = Math.Max(1, (int)Math.Round(track.Width * ratio));
                if (fill < 6) using (Brush brush = new SolidBrush(accent)) g.FillRectangle(brush, track.X, track.Y, fill, track.Height);
                else Art.Fill(g, new Rectangle(track.X, track.Y, fill, track.Height), 3, accent);
            }
        }
    }
}




