using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NetBunny
{
    // An on-demand companion panel. Actors and cultivation keep their own
    // desktop lifetime; this form never owns a second game or a scene canvas.
    internal sealed class DesktopPracticePanel : Form
    {
        readonly DesktopDemoSession session;
        readonly Label objective = new Label(), stats = new Label(), hint = new Label(), notice = new Label();
        readonly Label attackState = new Label(), blockState = new Label(), cultivation = new Label(), weaponInfo = new Label(), techniqueInfo = new Label();
        readonly Button strike = new Button(), block = new Button(), awaken = new Button(), automatic = new Button(), practice = new Button();
        readonly ComboBox weapons = new ComboBox(), techniques = new ComboBox();
        readonly FlowLayoutPanel targets = new FlowLayoutPanel(), materials = new FlowLayoutPanel();
        readonly TextBox activity = new TextBox();
        readonly TabControl tabs = new TabControl();
        readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
        readonly List<FlowLayoutPanel> columns = new List<FlowLayoutPanel>();
        readonly Dictionary<string, Button> targetButtons = new Dictionary<string, Button>();
        readonly List<Image> ownedImages = new List<Image>();
        readonly DesktopProgressMeter meter = new DesktopProgressMeter();
        string inventoryKey = "", materialsKey = "";
        bool welcomeWithoutActivation;
        static readonly Color Ink = Color.FromArgb(38, 65, 58), Paper = Color.FromArgb(246, 242, 230), Jade = Color.FromArgb(41, 91, 75), Gold = Color.FromArgb(133, 91, 41);

        internal DesktopPracticePanel(DesktopDemoSession session)
        {
            this.session = session;
            Text = "桌面修仙传 · 修行手札";
            Font = new Font("Microsoft YaHei UI", 10);
            BackColor = Paper; ForeColor = Ink; AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.Manual;
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(470, work.Width - 32), Math.Min(700, work.Height - 72));
            MinimumSize = new Size(Math.Min(390, work.Width - 24), Math.Min(490, work.Height - 24));
            var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(14) };
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(shell);
            var summary = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, 0, 0, 10) };
            objective.Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold); objective.ForeColor = Jade; StyleLabel(objective);
            StyleLabel(stats); stats.Margin = new Padding(0, 5, 0, 8);
            meter.Height = 5; meter.Width = 420; meter.Margin = new Padding(0, 0, 0, 5);
            StyleLabel(notice); notice.ForeColor = Gold; notice.AccessibleName = "战斗结果";
            summary.Controls.Add(objective); summary.Controls.Add(stats); summary.Controls.Add(notice); summary.Controls.Add(meter); shell.Controls.Add(summary, 0, 0);
            tabs.Dock = DockStyle.Fill; tabs.SizeMode = TabSizeMode.Fixed; tabs.ItemSize = new Size(108, 30); tabs.Margin = Padding.Empty;
            shell.Controls.Add(tabs, 0, 1);

            FlowLayoutPanel battle = Column("桌面历练");
            StyleLabel(hint); hint.AccessibleName = "下一步操作"; battle.Controls.Add(hint);
            Add(battle, "电脑生态 · 查看来源与维护结果", delegate { session.OpenPanel("ecology"); });
            Section(battle, "桌面妖物 · 点击切换目标");
            targets.AutoSize = true; targets.FlowDirection = FlowDirection.TopDown; targets.WrapContents = false; battle.Controls.Add(targets);
            StyleLabel(attackState); StyleLabel(blockState); battle.Controls.Add(attackState); battle.Controls.Add(blockState);
            Configure(strike, "对选中妖物出招", delegate { session.Game.DesktopStrikeSelected(); }); battle.Controls.Add(strike);
            Configure(block, "格挡 · 点击修士也可施展", delegate { session.Game.TryBlock(); }); battle.Controls.Add(block);
            Configure(awaken, "灵根觉醒 · 十次有效击杀后", delegate { session.Game.AwakenSpiritRoot(); }); battle.Controls.Add(awaken);
            Configure(automatic, "自动作战", delegate { session.Game.SetAutoPlan(!session.Game.AutoPlanning); }); battle.Controls.Add(automatic);
            Section(battle, "历练记事");
            activity.ReadOnly = true; activity.Multiline = true; activity.Height = 104; activity.BorderStyle = BorderStyle.None;
            activity.BackColor = Color.FromArgb(237, 235, 221); activity.ForeColor = Ink; activity.ScrollBars = ScrollBars.Vertical; activity.TabStop = false;
            activity.AccessibleName = "最近六条历练记录"; battle.Controls.Add(activity);

            FlowLayoutPanel training = Column("修行与洞天");
            StyleLabel(cultivation); cultivation.AccessibleName = "境界与成长目标"; training.Controls.Add(cultivation);
            Configure(practice, "修炼", delegate { session.Game.SetCultivationPaused(!session.Game.CultivationPaused); }); training.Controls.Add(practice);
            Section(training, "功法 · 觉醒后可随时改修");
            techniques.DropDownStyle = ComboBoxStyle.DropDownList; techniques.AccessibleName = "修行功法";
            foreach (CultivationTechnique technique in CultivationTechnique.All) techniques.Items.Add(new Choice { Id = technique.Id, Text = technique.Name });
            techniques.SelectedIndexChanged += delegate { UpdateTechniqueDescription(); }; training.Controls.Add(techniques);
            StyleLabel(techniqueInfo); training.Controls.Add(techniqueInfo);
            Add(training, "改修所选功法", delegate { Choice choice = techniques.SelectedItem as Choice; if (choice != null && !session.Game.Cultivation.Mortal) session.Game.ChooseTechnique(choice.Id); }).Name = "change-technique";
            Section(training, "在世修行");
            Add(training, "百业道碑 · 工作与生活的后天道途", delegate { session.OpenPanel("baiye"); });
            Add(training, "灵域历练 · 探索秘境与机缘", delegate { session.OpenPanel("lingyu"); });
            Add(training, "破境 · 查看圆满与契机", delegate { session.OpenPanel("breakthrough"); });
            Add(training, "洞天 · 筑基后种植、炼丹与铸器", delegate { session.OpenPanel("dongtian"); });
            var privacy = TextLine("百业默认关闭；由你在道碑中选择是否开启。\n收起所有面板，修士仍会在桌面继续修炼。"); training.Controls.Add(privacy);

            FlowLayoutPanel bag = Column("行囊与设置");
            Section(bag, "已有兵器"); weapons.DropDownStyle = ComboBoxStyle.DropDownList; weapons.AccessibleName = "已拥有兵器";
            weapons.SelectedIndexChanged += delegate { UpdateWeaponDescription(); }; bag.Controls.Add(weapons);
            StyleLabel(weaponInfo); bag.Controls.Add(weaponInfo);
            Add(bag, "装备所选兵器", delegate { Choice choice = weapons.SelectedItem as Choice; if (choice != null) session.Game.ChooseWeapon(choice.Id); });
            Section(bag, "材料与洞天储物");
            materials.AutoSize = true; materials.FlowDirection = FlowDirection.TopDown; materials.WrapContents = false; bag.Controls.Add(materials);
            Section(bag, "桌面位置");
            foreach (Screen screen in Screen.AllScreens)
            { Screen selected = screen; Add(bag, "移至屏幕 " + (Array.IndexOf(Screen.AllScreens, screen) + 1), delegate { session.Game.ChangeScreen(selected); }); }
            Add(bag, "隐藏／恢复桌面角色", delegate { session.Game.DesktopSetActorsVisible(!session.Game.DesktopActorsVisible); });
            bag.Controls.Add(TextLine("拖动修士可以移动；右键修士可再次打开手札。\n独立进度每20秒保存，正常退出时再次保存。"));

            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            Button hide = new Button { Text = "收起 · 继续桌面修仙", Width = 245, Height = 34 }; StyleButton(hide); hide.Click += delegate { Hide(); };
            Button exit = new Button { Text = "保存并退出", Width = 160, Height = 34 }; StyleButton(exit); exit.Click += delegate { session.ExitAfterAcceptance(0); };
            footer.Controls.Add(hide); footer.Controls.Add(exit); shell.Controls.Add(footer, 0, 2);
            Resize += delegate
            {
                int width = Math.Max(280, shell.ClientSize.Width - shell.Padding.Horizontal - 4);
                objective.MaximumSize = stats.MaximumSize = new Size(width, 0); meter.Width = width;
                hide.Width = Math.Max(160, (width - 12) * 3 / 5); exit.Width = Math.Max(100, width - hide.Width - 12);
                ResizeColumns();
            };
            refresh.Interval = 250; refresh.Tick += delegate { if (Visible) UpdateState(); }; refresh.Start();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            Shown += delegate { ResizeColumns(); };
            Activated += delegate { TopMost = false; };
            VisibleChanged += delegate { if (!Visible) TopMost = false; };
        }

        protected override bool ShowWithoutActivation { get { return welcomeWithoutActivation; } }
        internal void ShowWelcome() { welcomeWithoutActivation = true; try { UpdateState(); TopMost = true; Show(); } finally { welcomeWithoutActivation = false; } }
        internal void OpenSection(string id) { tabs.SelectedIndex = id == "training" ? 1 : id == "inventory" ? 2 : 0; }
        internal void UpdateState()
        {
            AdventureController game = session.Game; if (game == null) return;
            DesktopPlayerView view = game.ReadDesktopPlayerView();
            var c = game.Cultivation; var p = game.Battle.Progress;
            objective.Text = view.Objective;
            stats.Text = "气血 " + p.Health + "/" + p.MaxHealth + (c.Mortal ? "　历练积累 " + c.DeferredQi : "　" + c.RootName + (c.RealmComplete ? " · 圆满" : ""));
            meter.Value = c.Mortal ? c.MortalKills / 10.0 : c.RealmComplete ? 1 : c.Qi / (double)Math.Max(1, c.NextLayerCost);
            hint.Text = view.Hint;
            attackState.Text = "目标：" + view.TargetName + "\n" + view.AttackText;
            blockState.Text = view.BlockText;
            strike.Enabled = view.StrikeReady; block.Enabled = view.BlockReady;
            awaken.Enabled = c.CanAwaken; awaken.Text = c.CanAwaken ? "灵根觉醒 · 十战已满，引气入体" : c.Mortal ? "灵根觉醒 · 历练 " + c.MortalKills + "/10" : "灵根已觉醒 · " + c.RootName;
            automatic.Enabled = c.AutoBattleUnlocked && !p.Dead;
            automatic.Text = c.AutoBattleUnlocked ? "自动作战：" + (game.AutoPlanning ? "开启 · 自主索敌" : "关闭 · 手动历练") : "自动作战 · 灵根觉醒后开启";
            notice.Text = view.Notice;
            string events = String.Join(Environment.NewLine, view.RecentEvents ?? new string[0]); if (activity.Text != events) activity.Text = events;
            UpdateTargets(view.Enemies ?? new DesktopEnemyChoice[0]);
            cultivation.Text = c.Mortal ? "凡躯尚不能存纳灵气。\n十次有效击杀后，主动觉醒灵根；历练积累将转为修为。"
                : c.RootName + " · " + CultivationIdentity.PathName(c.DaoPath) + "\n正在修习「" + c.Technique.Name + "」\n" + game.CultivationLine
                + "\n" + (c.RealmComplete ? "本境已圆满。打开破境查看契机、材料与成功率。" : "下层目标：" + c.NextLayerCost + " 修为；吐纳与战斗均可积累。")
                + "\n百业可积累后天道途，灵域带来机缘；筑基后开启洞天经营。";
            practice.Enabled = !c.Mortal; practice.Text = c.Mortal ? "吐纳修炼 · 觉醒后开启" : "吐纳修炼：" + (game.CultivationPaused ? "暂停，点击继续" : "持续进行，点击暂歇");
            techniques.Enabled = !c.Mortal;
            foreach (Control control in columns[1].Controls) if (control.Name == "change-technique") control.Enabled = !c.Mortal;
            if (techniques.SelectedIndex < 0) techniques.SelectedIndex = Array.FindIndex(CultivationTechnique.All, t => t.Id == c.TechniqueId);
            string key = String.Join("|", p.Inventory.ToArray()) + ":" + p.WeaponId;
            if (key != inventoryKey)
            {
                inventoryKey = key; weapons.Items.Clear();
                foreach (string id in p.Inventory) weapons.Items.Add(new Choice { Id = id, Text = WeaponName(id) + (id == p.WeaponId ? " · 已装备" : "") });
                for (int i = 0; i < weapons.Items.Count; i++) if (((Choice)weapons.Items[i]).Id == p.WeaponId) weapons.SelectedIndex = i;
                if (weapons.SelectedIndex < 0 && weapons.Items.Count > 0) weapons.SelectedIndex = 0;
            }
            UpdateMaterials(); ResizeColumns();
        }

        void UpdateTargets(DesktopEnemyChoice[] enemies)
        {
            var ids = new HashSet<string>(enemies.Select(e => e.Id));
            foreach (string id in targetButtons.Keys.Where(id => !ids.Contains(id)).ToArray()) { Button b = targetButtons[id]; targets.Controls.Remove(b); b.Dispose(); targetButtons.Remove(id); }
            foreach (DesktopEnemyChoice enemy in enemies)
            {
                Button button;
                if (!targetButtons.TryGetValue(enemy.Id, out button))
                {
                    string id = enemy.Id; button = new Button { Height = 78, Margin = new Padding(0, 0, 0, 5) }; StyleButton(button);
                    button.Click += delegate { session.Game.DesktopChooseEnemy(id, false); UpdateState(); };
                    targetButtons.Add(id, button); targets.Controls.Add(button);
                }
                button.Text = (enemy.Selected ? "当前目标 · " : "选中 · ") + enemy.Name + "　" + enemy.Health + "/" + enemy.MaxHealth + "\n" + enemy.Source + "\n" + enemy.Action;
                button.BackColor = enemy.Selected ? Color.FromArgb(221, 232, 214) : Color.FromArgb(251, 248, 239);
                button.Enabled = !session.Game.Battle.Progress.Dead;
            }
        }
        void UpdateWeaponDescription()
        {
            Choice choice = weapons.SelectedItem as Choice; if (choice == null) return;
            AdventureWeapon w = AdventureWeapon.Find(choice.Id);
            weaponInfo.Text = "基础威力 " + w.Damage + " · 出招间隔 " + w.Cooldown.ToString("0.00") + "秒\n"
                + (w.Range <= 160 ? "近身剑招" : "远程御剑") + (w.Area ? " · 伤及已选中的多个目标" : " · 单体出招") + "\n实际伤害受境界与敌人状态影响；武器外观采用三阶共用剑形。";
        }
        void UpdateTechniqueDescription()
        { Choice choice = techniques.SelectedItem as Choice; if (choice != null) techniqueInfo.Text = CultivationTechnique.Find(choice.Id).Description; }
        void UpdateMaterials()
        {
            var game = session.Game;
            string key = String.Join("|", game.Lingyu.Materials.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value)) + "/" + String.Join("|", game.Dongtian.Bag.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value));
            if (materialsKey == key && materials.Controls.Count > 0) return;
            materialsKey = key;
            foreach (Control control in materials.Controls.Cast<Control>().ToArray()) control.Dispose(); materials.Controls.Clear();
            foreach (var pair in game.Lingyu.Materials.Where(p => p.Value > 0)) MaterialRow(LingyuCatalog.MaterialName(pair.Key), pair.Value, pair.Key.Contains("lingsui") ? "item-marrow.png" : "item-material.png");
            foreach (var pair in game.Dongtian.Bag.Where(p => p.Value > 0)) MaterialRow(DongtianRecipes.MaterialName(pair.Key), pair.Value, pair.Key.Contains("dan") ? "item-pill.png" : "item-material.png");
            if (materials.Controls.Count == 0) materials.Controls.Add(TextLine("行囊暂空。灵域探索与洞天收获的材料会出现在这里；普通战斗先积累历练与修为。"));
        }
        void MaterialRow(string name, int count, string image)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 5) };
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArtDemoAssets", image);
            if (File.Exists(path))
            {
                // UI thumbnails own their bitmap; no file lock or per-refresh allocation.
                Image thumbnail = ownedImages.FirstOrDefault(i => (string)i.Tag == image);
                if (thumbnail == null) { using (Image source = Image.FromFile(path)) thumbnail = new Bitmap(source); thumbnail.Tag = image; ownedImages.Add(thumbnail); }
                row.Controls.Add(new PictureBox { Image = thumbnail, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(30, 30), Margin = new Padding(0, 0, 10, 0) });
            }
            var label = TextLine(name + " × " + count); label.Margin = new Padding(0, 4, 0, 0); row.Controls.Add(label); materials.Controls.Add(row);
        }
        FlowLayoutPanel Column(string title)
        {
            var page = new TabPage(title) { BackColor = Paper, Padding = new Padding(10) }; tabs.TabPages.Add(page);
            var column = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            page.Controls.Add(column); columns.Add(column); return column;
        }
        void ResizeColumns()
        {
            foreach (var column in columns)
            {
                int width = Math.Max(240, column.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 16);
                foreach (Control control in column.Controls) { control.Width = width; if (control is Label) control.MaximumSize = new Size(width, 0); }
                foreach (Control control in targets.Controls) control.Width = width;
                foreach (Control control in materials.Controls) { control.Width = width; if (control is Label) control.MaximumSize = new Size(width, 0); }
            }
        }
        static void StyleLabel(Label label) { label.AutoSize = true; label.Margin = new Padding(0, 0, 0, 8); label.MaximumSize = new Size(400, 0); }
        static Label TextLine(string text) { var label = new Label { Text = text }; StyleLabel(label); return label; }
        static void Section(FlowLayoutPanel parent, string text) { var label = TextLine(text); label.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold); label.Margin = new Padding(0, 12, 0, 6); parent.Controls.Add(label); }
        Button Add(FlowLayoutPanel content, string text, Action action) { var button = new Button(); Configure(button, text, action); content.Controls.Add(button); return button; }
        void Configure(Button button, string text, Action action) { button.Text = text; button.Height = 35; button.Margin = new Padding(0, 0, 0, 6); StyleButton(button); button.Click += delegate { action(); UpdateState(); }; }
        static void StyleButton(Button button) { button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderColor = Color.FromArgb(154, 166, 142); button.BackColor = Color.FromArgb(251, 248, 239); button.ForeColor = Ink; button.Cursor = Cursors.Hand; }
        internal static string WeaponName(string id)
        { switch (id) { case "spear": return "灵铁短剑"; case "greatsword": return "青钢重剑"; case "wave": return "破空剑气"; case "laser": return "流光飞剑"; case "boomerang": return "月轮飞刃"; case "blackhole": return "归墟剑阵"; case "automaton": return "御剑符"; default: return "行囊兵器"; } }
        sealed class Choice { internal string Id, Text; public override string ToString() { return Text; } }
        protected override void Dispose(bool disposing)
        { if (disposing) { refresh.Stop(); refresh.Dispose(); foreach (Image image in ownedImages) image.Dispose(); ownedImages.Clear(); } base.Dispose(disposing); }
    }
    internal sealed class DesktopProgressMeter : Control
    {
        double value;
        internal double Value { get { return value; } set { double next = Math.Max(0, Math.Min(1, value)); if (next != this.value) { this.value = next; Invalidate(); } } }
        internal DesktopProgressMeter() { DoubleBuffered = true; TabStop = false; }
        protected override void OnPaint(PaintEventArgs e)
        { e.Graphics.Clear(Color.FromArgb(216, 218, 199)); using (var brush = new SolidBrush(Color.FromArgb(56, 108, 83))) e.Graphics.FillRectangle(brush, 0, 0, (float)(Width * value), Height); }
    }
}
