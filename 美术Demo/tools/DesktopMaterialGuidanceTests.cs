using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace NetBunny
{
    // Hidden/stopped-clock real controller and panel. All fixture archives are
    // private. Navigation buttons are inspected, never clicked or shown.
    internal static class DesktopMaterialGuidanceTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        static int checks, failures;
        static readonly string[][] Entries = {
            new[] { "lingyu", "shuiyue-lingsui" }, new[] { "dongtian", "shuiyue-lingsui" },
            new[] { "lingyu", "jinghua-shuiyue" }, new[] { "dongtian", "jinghua-shuiyue" },
            new[] { "lingyu", "suijing-jing" }, new[] { "lingyu", "jinghua" },
            new[] { "scroll", "jingxin-scroll" }, new[] { "heritage", "shuiyue-mirror" },
            new[] { "dongtian", "linggu" }, new[] { "dongtian", "ningshen" }, new[] { "dongtian", "jianxin" },
            new[] { "lingyu", "future-lingyu-material" }, new[] { "dongtian", "future-cave-material" }
        };
        static T Field<T>(object value, string name) { return (T)value.GetType().GetField(name, Hidden).GetValue(value); }
        static void Set(object value, string name, object field) { value.GetType().GetField(name, Hidden).SetValue(value, field); }
        static object Call(object value, string name, params object[] args) { return value.GetType().GetMethod(name, Hidden).Invoke(value, args); }
        static void Check(bool okay, string message) { checks++; if (!okay) failures++; Console.WriteLine((okay ? "PASS " : "FAIL ") + message); }
        static Control[] Children(Control root)
        { return root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c))).ToArray(); }
        static bool OwnVisible(Control control) { return (bool)typeof(Control).GetMethod("GetState", Hidden).Invoke(control, new object[] { 2 }); }
        static Control Card(DesktopPracticePanel panel, string source, string id)
        { return Children(Field<FlowLayoutPanel>(panel, "materials")).FirstOrDefault(c => c.Name == "material-" + source + "-" + id); }
        static string TextOf(Control card)
        { return card == null ? "" : String.Join("\n", Children(card).OfType<Label>().Select(l => l.Text).ToArray()); }
        static Button ActionOf(Control card, string source, string id)
        { return card == null ? null : Children(card).OfType<Button>().FirstOrDefault(b => b.Name == "material-action-" + source + "-" + id); }
        static bool Quantity(string text, int count) { return Regex.IsMatch(text, "[×x]\\s*" + count + "(?![0-9])"); }
        static bool Any(string text, params string[] words) { return words.Any(text.Contains); }
        static bool CollectionOnly(string text)
        { return text.Contains("收藏") && Any(text, "仅", "暂", "尚", "未开放", "没有", "无已开放"); }
        static Dictionary<string, Control> Cards(DesktopPracticePanel panel)
        { return Entries.ToDictionary(e => e[0] + ":" + e[1], e => Card(panel, e[0], e[1])); }
        static bool SameCards(Dictionary<string, Control> before, Dictionary<string, Control> after)
        { return before.All(p => p.Value != null && Object.ReferenceEquals(p.Value, after[p.Key]) && !p.Value.IsDisposed); }
        static void Layout(Control root)
        { IntPtr handle = root.Handle; foreach (Control child in root.Controls) Layout(child); root.PerformLayout(); }
        static Rectangle InControl(Control child, Control parent)
        { return new Rectangle(parent.PointToClient(child.PointToScreen(Point.Empty)), child.Size); }
        static string Files(string root)
        {
            using (SHA256 sha = SHA256.Create())
                return String.Join("|", Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal)
                    .Select(p => p.Substring(root.Length) + ":" + BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(p)))).ToArray());
        }
        static string Inventory(AdventureController game)
        {
            Func<Dictionary<string, int>, string> pairs = d => String.Join("|", d.OrderBy(p => p.Key).Select(p => p.Key + ":" + p.Value).ToArray());
            return pairs(game.Lingyu.Materials) + "/" + pairs(game.Dongtian.Bag) + "/" + pairs(game.Lingyu.Scrolls) + "/"
                + String.Join("|", game.Lingyu.Heritages.OrderBy(s => s).ToArray()) + "/" + game.Dongtian.ImportSeq + "/"
                + game.Battle.Progress.Experience + "/" + game.Battle.Progress.Score + "/" + game.Battle.Progress.FreedBytes + "/" + game.Cultivation.LifetimeQi;
        }
        static string Stock(AdventureController game)
        { return Inventory(game) + "/" + game.Dongtian.Level + "/" + String.Join("|", game.Dongtian.Gears.OrderBy(s => s).ToArray()) + "/" + game.Dongtian.Furnace + "/" + game.Dongtian.Forge; }
        static void RefreshSafely(DesktopPracticePanel panel, AdventureController game, string profile, int repetitions, string label)
        {
            string files = Files(profile), stock = Stock(game);
            for (int i = 0; i < repetitions; i++) panel.UpdateState();
            Check(Stock(game) == stock && Files(profile) == files, label + ": refreshes do not consume, transfer, reward or write the private archives");
            Check(game.DesktopRealWorkIdle && !game.DesktopProcessBusy && !game.MaintenanceConsentCleanup && !game.MaintenanceConsentMemory
                && !game.MaintenanceConsentAutoMaintenance && game.Baiye.Mode == 0,
                label + ": refreshes start no observer/worker or maintenance/activity authorization");
        }
        static void Route(Control card, string source, string id, string route, string label)
        {
            Button action = ActionOf(card, source, id);
            Check(action != null && (string)action.Tag == route && OwnVisible(action), label);
        }
        static void InitialFacts(DesktopPracticePanel panel)
        {
            foreach (string[] entry in Entries)
                Check(Card(panel, entry[0], entry[1]) != null, "material card identifies its separate storage source: " + entry[0] + ":" + entry[1]);
            Control spirit = Card(panel, "lingyu", "shuiyue-lingsui"), storedSpirit = Card(panel, "dongtian", "shuiyue-lingsui");
            string text = TextOf(spirit), stored = TextOf(storedSpirit);
            Check(text.Contains("灵域") && Quantity(text, 2) && stored.Contains("洞天") && Quantity(stored, 7)
                && spirit != null && storedSpirit != null && !Object.ReferenceEquals(spirit, storedSpirit),
                "the same lingsui ID is shown as two independent source/count cards, never a combined total");
            Check(text.Contains("+15%") && text.Contains("成败皆消耗") && text.Contains("入库后不计入破境"),
                "qi-refining lingsui states the real breakthrough bonus, consumption and storage tradeoff");
            Route(spirit, "lingyu", "shuiyue-lingsui", "breakthrough", "qi-refining lingsui navigates to the existing breakthrough panel");
            Check(stored.Contains("不计入破境") && stored.Contains("凝露丹") && Any(stored, "1份", "1 份", "×1", "× 1"),
                "stored lingsui states that one stored portion makes the actual ninglu recipe and is not breakthrough stock");
            Route(storedSpirit, "dongtian", "shuiyue-lingsui", "dongtian", "stored lingsui navigates to its existing cave recipe panel");
            text = TextOf(Card(panel, "lingyu", "jinghua-shuiyue"));
            Check(text.Contains("灵域") && Quantity(text, 3) && text.Contains("筑基") && text.Contains("入库")
                && Regex.IsMatch(text.Replace("\n", " "), "(?:每次|一次).{0,10}1\\s*份"),
                "unlocked-later mirror material states its qi-refining gate and one-portion transfer instead of implying immediate use");
            text = TextOf(Card(panel, "dongtian", "jinghua-shuiyue"));
            Check(text.Contains("洞天") && Quantity(text, 8) && text.Contains("镜花凝露丹") && Any(text, "2份", "2 份", "×2", "× 2"),
                "stored mirror material explains its actual two-portion mirror-ninglu recipe");
            foreach (string[] entry in new[] { new[] { "lingyu", "suijing-jing" }, new[] { "lingyu", "jinghua" }, new[] { "scroll", "jingxin-scroll" }, new[] { "heritage", "shuiyue-mirror" } })
            {
                Control card = Card(panel, entry[0], entry[1]); text = TextOf(card); Button action = ActionOf(card, entry[0], entry[1]);
                Check(CollectionOnly(text) && !Regex.IsMatch(text, "[+＋]\\s*[0-9]+%") && (action == null || (string)action.Tag == "lingyu"),
                    "unimplemented collection item claims no learned technique, consumer or numerical buff: " + entry[1]);
            }
            Check(Quantity(TextOf(Card(panel, "scroll", "jingxin-scroll")), 2) && Quantity(TextOf(Card(panel, "heritage", "shuiyue-mirror")), 1),
                "scroll and heritage quantities come from their own actual collection rather than the material dictionary");
            foreach (string[] entry in new[] { new[] { "lingyu", "future-lingyu-material" }, new[] { "dongtian", "future-cave-material" } })
            {
                Control card = Card(panel, entry[0], entry[1]); text = TextOf(card); Button action = ActionOf(card, entry[0], entry[1]);
                Check(card != null && Any(text, "未知", "未识", "未辨", "未收录", "未配置", "暂无", "暂未", "尚无", "尚未", "未开放", "没有", "仅作收藏")
                    && !Regex.IsMatch(text, "[+＋]\\s*[0-9]+%") && (action == null || (string)action.Tag == "lingyu"),
                    "unknown material has an honest fallback without a fabricated recipe or breakthrough route: " + entry[0]);
            }
            foreach (string id in new[] { "linggu", "ningshen", "jianxin" })
            {
                text = TextOf(Card(panel, "dongtian", id));
                Check(text.Contains("洞天") && Any(text, "炼", "铸", "扩建"), "existing cave crop material explains an actual recipe use: " + id);
                Route(Card(panel, "dongtian", id), "dongtian", id, "dongtian", "existing cave crop navigates to cave operations: " + id);
            }
            Check(Entries.SelectMany(e => { Control card = Card(panel, e[0], e[1]); return card == null ? new Button[0] : Children(card).OfType<Button>(); })
                .All(b => b.Tag is string && new[] { "breakthrough", "dongtian", "lingyu" }.Contains((string)b.Tag)),
                "every material action is marked as existing-panel navigation, with no direct consumption action");
        }
        static bool Minutes(string text, int value) { return Regex.IsMatch(text, "炼制\\s*" + value + "\\s*分钟"); }
        static void RecipeFacts(DesktopPracticePanel panel, AdventureController game, string profile)
        {
            DongtianPillDef recipe = DongtianRecipes.Pill("ninglu-jinghua");
            string name = recipe.Name, effect = recipe.EffectText; long seconds = recipe.BrewSeconds; string[] costs = recipe.Materials;
            bool hadFurnace = game.Dongtian.Gears.Contains("jinglu"); Dictionary<string, Control> original = Cards(panel);
            string stock = Inventory(game);
            try
            {
                game.Dongtian.Gears.Remove("jinglu"); RefreshSafely(panel, game, profile, 1, "recipe baseline");
                Check(Minutes(TextOf(Card(panel, "dongtian", "jinghua-shuiyue")), 30), "the actual mirror recipe initially explains thirty minutes without a mirror furnace");
                game.Dongtian.Gears.Add("jinglu"); RefreshSafely(panel, game, profile, 1, "mirror-furnace-only refresh");
                Check(Inventory(game) == stock && SameCards(original, Cards(panel))
                    && Minutes(TextOf(Card(panel, "dongtian", "jinghua-shuiyue")), 27)
                    && !Minutes(TextOf(Card(panel, "dongtian", "jinghua-shuiyue")), 30),
                    "obtaining the real mirror furnace without changing quantities updates the same recipe card from thirty to twenty-seven minutes");

                // These definitions belong only to this short-lived test process.
                // Alter actual recipe inputs, not a duplicated guidance catalog.
                recipe.Name = "回归测试镜花方"; recipe.EffectText = "战斗伤害 +9% · 回归测试药效"; recipe.BrewSeconds = 2400;
                recipe.Materials = new[] { "jinghua-shuiyue", "jinghua-shuiyue", "jinghua-shuiyue", "linggu" };
                RefreshSafely(panel, game, profile, 1, "actual in-memory recipe definition refresh");
                string text = TextOf(Card(panel, "dongtian", "jinghua-shuiyue"));
                Check(text.Contains(recipe.Name) && text.Contains(recipe.EffectText) && Regex.IsMatch(text, "镜花水月\\s*×\\s*3(?![0-9])")
                    && Regex.IsMatch(text, "灵谷\\s*×\\s*1(?![0-9])") && Minutes(text, 36) && !text.Contains(name),
                    "the card derives recipe name, costs, effect and furnace-adjusted time from the current real definition rather than hardcoded prose");
                Check(TextOf(Card(panel, "dongtian", "linggu")).Contains(recipe.Name) && SameCards(original, Cards(panel)) && Inventory(game) == stock,
                    "a changed real ingredient updates its existing crop card without rebuilding any card or consuming stock");
                string[][] collectibles = { new[] { "lingyu", "suijing-jing" }, new[] { "lingyu", "jinghua" }, new[] { "scroll", "jingxin-scroll" },
                    new[] { "heritage", "shuiyue-mirror" }, new[] { "lingyu", "future-lingyu-material" }, new[] { "dongtian", "future-cave-material" } };
                Check(collectibles.All(e => Card(panel, e[0], e[1]) != null && !TextOf(Card(panel, e[0], e[1])).Contains(recipe.Name)
                    && !TextOf(Card(panel, e[0], e[1])).Contains("回归测试药效") && !Regex.IsMatch(TextOf(Card(panel, e[0], e[1])), "[+＋]\\s*[0-9]+%")),
                    "collection-only and unknown rows do not inherit a new unrelated recipe or fabricated numerical effect");
            }
            finally
            {
                recipe.Name = name; recipe.EffectText = effect; recipe.BrewSeconds = seconds; recipe.Materials = costs;
                if (hadFurnace) game.Dongtian.Gears.Add("jinglu"); else game.Dongtian.Gears.Remove("jinglu");
                RefreshSafely(panel, game, profile, 1, "restored recipe definitions");
            }
            Check(recipe.Name == name && recipe.EffectText == effect && recipe.BrewSeconds == seconds && Object.ReferenceEquals(recipe.Materials, costs)
                && game.Dongtian.Gears.Contains("jinglu") == hadFurnace && SameCards(original, Cards(panel))
                && TextOf(Card(panel, "dongtian", "jinghua-shuiyue")).Contains(name) && !TextOf(Card(panel, "dongtian", "jinghua-shuiyue")).Contains("回归测试药效"),
                "all temporary recipe and equipment changes are restored and the same cards return to the actual production description");
        }
        static void CraftedGearFacts(DesktopPracticePanel panel, AdventureController game, string profile)
        {
            int level = game.Dongtian.Level; bool hadSword = game.Dongtian.HasGear("jianwan"), hadFurnace = game.Dongtian.HasGear("jinglu");
            Dictionary<string, Control> before = Cards(panel); Control grass = Card(panel, "dongtian", "jianxin");
            Button retiringAction = ActionOf(grass, "dongtian", "jianxin"); Control actionParent = retiringAction == null ? null : retiringAction.Parent;
            bool actionRemoved = false, actionFocusClearedBeforeFrameworkFallback = false;
            ControlEventHandler actionRemoval = delegate(object sender, ControlEventArgs e)
            {
                if (Object.ReferenceEquals(e.Control, retiringAction))
                { actionRemoved = true; actionFocusClearedBeforeFrameworkFallback = panel.ActiveControl == null; }
            };
            if (actionParent != null) actionParent.ControlRemoved += actionRemoval;
            if (retiringAction != null) panel.ActiveControl = retiringAction;
            Check(retiringAction != null && Object.ReferenceEquals(panel.ActiveControl, retiringAction) && !panel.ContainsFocus && !panel.Visible,
                "hidden inactive fixture remembers the material action that is about to lose its last real consumer");
            try
            {
                game.Dongtian.Level = 3; game.Dongtian.Gears.Add("jianwan"); game.Dongtian.Gears.Add("jinglu");
                RefreshSafely(panel, game, profile, 1, "all grass consumers already crafted");
                string text = TextOf(Card(panel, "dongtian", "jianxin"));
                Check(Any(text, "已铸成", "暂无", "没有", "无可用", "未开放") && ActionOf(Card(panel, "dongtian", "jianxin"), "dongtian", "jianxin") == null
                    && ((!text.Contains("剑丸") && !text.Contains("镜炉")) || text.Contains("已铸成")),
                    "at max cave level, grass with both one-time gears already crafted offers no nonexistent recipe or expansion action");
                Check(SameCards(before, Cards(panel)) && Object.ReferenceEquals(grass, Card(panel, "dongtian", "jianxin"))
                    && Quantity(text, 11), "retiring every real grass consumer keeps the existing card and its unspent stock");
                Check(actionRemoved && actionFocusClearedBeforeFrameworkFallback && panel.ActiveControl == null,
                    "an inactive remembered action is cleared before removal can invoke Framework focus fallback when all recipes retire");

                game.Dongtian.Gears.Remove("jinglu"); RefreshSafely(panel, game, profile, 1, "only sword gear already crafted");
                text = TextOf(Card(panel, "dongtian", "jianxin"));
                Check(text.Contains("镜炉") && !text.Contains("剑丸") && SameCards(before, Cards(panel)),
                    "with only sword gear owned, the same grass card still explains the remaining mirror-furnace recipe");
                Route(Card(panel, "dongtian", "jianxin"), "dongtian", "jianxin", "dongtian", "the remaining furnace recipe restores cave navigation on the same grass card");

                game.Dongtian.Gears.Add("jinglu"); game.Dongtian.Gears.Remove("jianwan");
                RefreshSafely(panel, game, profile, 1, "only mirror furnace already crafted");
                text = TextOf(Card(panel, "dongtian", "jianxin"));
                Check(text.Contains("剑丸") && !text.Contains("镜炉") && SameCards(before, Cards(panel)),
                    "with only the mirror furnace owned, the same grass card still explains the remaining sword-gear recipe");
                Route(Card(panel, "dongtian", "jianxin"), "dongtian", "jianxin", "dongtian", "the remaining sword recipe keeps its existing cave navigation");
            }
            finally
            {
                if (actionParent != null) actionParent.ControlRemoved -= actionRemoval;
                game.Dongtian.Level = level;
                if (hadSword) game.Dongtian.Gears.Add("jianwan"); else game.Dongtian.Gears.Remove("jianwan");
                if (hadFurnace) game.Dongtian.Gears.Add("jinglu"); else game.Dongtian.Gears.Remove("jinglu");
                RefreshSafely(panel, game, profile, 1, "restored one-time gear availability");
            }
            Check(SameCards(before, Cards(panel)) && Object.ReferenceEquals(grass, Card(panel, "dongtian", "jianxin"))
                && ActionOf(grass, "dongtian", "jianxin") != null && (string)ActionOf(grass, "dongtian", "jianxin").Tag == "dongtian",
                "restoring the real cave state brings navigation back without replacing its material card");
        }
        static void RemovalFacts(DesktopPracticePanel panel, AdventureController game, string profile)
        {
            Dictionary<string, Control> before = Cards(panel); int count = game.Lingyu.Materials["shuiyue-lingsui"];
            Control retiringCard = before["lingyu:shuiyue-lingsui"], cardParent = retiringCard == null ? null : retiringCard.Parent;
            Button retiringAction = ActionOf(retiringCard, "lingyu", "shuiyue-lingsui");
            bool cardRemoved = false, cardFocusClearedBeforeFrameworkFallback = false;
            ControlEventHandler cardRemoval = delegate(object sender, ControlEventArgs e)
            {
                if (Object.ReferenceEquals(e.Control, retiringCard))
                { cardRemoved = true; cardFocusClearedBeforeFrameworkFallback = panel.ActiveControl == null; }
            };
            if (cardParent != null) cardParent.ControlRemoved += cardRemoval;
            if (retiringAction != null) panel.ActiveControl = retiringAction;
            Check(retiringAction != null && Object.ReferenceEquals(panel.ActiveControl, retiringAction) && !panel.ContainsFocus && !panel.Visible,
                "hidden inactive fixture remembers an action inside the stock card that is about to become empty");
            try
            {
                game.Lingyu.Materials.Remove("shuiyue-lingsui"); RefreshSafely(panel, game, profile, 1, "single-source removal");
                Dictionary<string, Control> after = Cards(panel); Control removed = before["lingyu:shuiyue-lingsui"];
                Check(after["lingyu:shuiyue-lingsui"] == null && removed != null && removed.IsDisposed
                    && before.Where(p => p.Key != "lingyu:shuiyue-lingsui").All(p => p.Value != null && !p.Value.IsDisposed && Object.ReferenceEquals(p.Value, after[p.Key])),
                    "removing one source disposes only its own material card and preserves every other card instance");
                Check(Object.ReferenceEquals(before["dongtian:shuiyue-lingsui"], after["dongtian:shuiyue-lingsui"])
                    && Quantity(TextOf(after["dongtian:shuiyue-lingsui"]), 6),
                    "removing lingyu lingsui does not remove or merge the independent cave stock card");
                Check(cardRemoved && cardFocusClearedBeforeFrameworkFallback && panel.ActiveControl == null,
                    "an inactive remembered descendant is cleared before disposing an exhausted source card can refocus its background form");
            }
            finally
            {
                if (cardParent != null) cardParent.ControlRemoved -= cardRemoval;
                game.Lingyu.Materials["shuiyue-lingsui"] = count; RefreshSafely(panel, game, profile, 1, "restored single-source stock");
            }
            Dictionary<string, Control> restored = Cards(panel);
            Check(restored["lingyu:shuiyue-lingsui"] != null && !Object.ReferenceEquals(before["lingyu:shuiyue-lingsui"], restored["lingyu:shuiyue-lingsui"])
                && before.Where(p => p.Key != "lingyu:shuiyue-lingsui").All(p => p.Value != null && !p.Value.IsDisposed && Object.ReferenceEquals(p.Value, restored[p.Key])),
                "restoring a removed source creates just that card while the existing inventory cards keep their identities");
        }
        static void LayoutFacts(DesktopPracticePanel panel, AdventureController game, string profile)
        {
            TabControl tabs = Field<TabControl>(panel, "tabs"); tabs.SelectedIndex = 2;
            FlowLayoutPanel bag = (FlowLayoutPanel)tabs.SelectedTab.Controls[0], materials = Field<FlowLayoutPanel>(panel, "materials");
            foreach (Size size in new[] { new Size(390, 490), new Size(470, 700) })
            {
                panel.ClientSize = size; bag.AutoScrollPosition = Point.Empty;
                for (int i = 0; i < 3; i++) { Layout(panel); Call(panel, "ResizeColumns"); }
                Layout(panel);
                Control[] cards = Entries.Select(e => Card(panel, e[0], e[1])).Where(c => c != null).ToArray();
                Check(cards.Length == Entries.Length && bag.DisplayRectangle.Width <= bag.ClientSize.Width + 1
                    && bag.Controls.Cast<Control>().All(c => c.Right + c.Margin.Right <= bag.ClientSize.Width + 1),
                    size + ": all material cards fit the inventory with vertical scrolling only");
                Check(cards.Length == Entries.Length && cards.SelectMany(Children).OfType<Label>().Where(OwnVisible)
                    .All(l => l.Width > 0 && l.GetPreferredSize(new Size(l.Width, 0)).Height <= l.Height + 2),
                    size + ": every inner material label wraps its full explanation without clipping");
                Check(cards.Length == Entries.Length && cards.SelectMany(c => new[] { c }.Concat(Children(c))).Where(c => c.HasChildren)
                    .All(row => row.Controls.Cast<Control>().Where(OwnVisible).All(c => c.Right + c.Margin.Right <= row.ClientSize.Width - row.Padding.Right + 2)),
                    size + ": inner material labels and navigation buttons do not overflow their own row");
                bag.AutoScrollPosition = new Point(0, Math.Max(0, bag.DisplayRectangle.Height));
                Point requestedScroll = bag.AutoScrollPosition; Layout(panel);
                Control last = materials.Controls.Cast<Control>().LastOrDefault(); Rectangle bottom = last == null ? Rectangle.Empty : InControl(last, bag);
                Control[] following = bag.Controls.Cast<Control>().Skip(bag.Controls.GetChildIndex(materials) + 1).ToArray();
                FieldInfo screensField = typeof(DesktopPracticePanel).GetField("screenChoices", Hidden);
                Control screenChoices = screensField == null ? null : screensField.GetValue(panel) as Control;
                Console.WriteLine("OBSERVED absolute-settings-bottom " + size + " last=" + bottom + " bagClient=" + bag.ClientRectangle
                    + " display=" + bag.DisplayRectangle + " immediateScroll=" + requestedScroll + " scroll=" + bag.AutoScrollPosition + " materialsBottom=" + materials.Bottom
                    + " followingSettingsHeight=" + (following.Length == 0 ? 0 : following.Max(c => c.Bottom + c.Margin.Bottom) - materials.Bottom)
                    + " screenChoices=" + (screenChoices == null ? "legacy" : screenChoices.Bounds + " count=" + screenChoices.Controls.Count));
                if (last != null) bag.ScrollControlIntoView(last);
                Point intoViewScroll = bag.AutoScrollPosition; bag.PerformLayout();
                bottom = last == null ? Rectangle.Empty : InControl(last, bag);
                Console.WriteLine("OBSERVED material-tail-into-view " + size + " last=" + bottom + " scrollBeforeLayout=" + intoViewScroll
                    + " scroll=" + bag.AutoScrollPosition + " verticalVisible=" + bag.VerticalScroll.Visible + " maximum=" + bag.VerticalScroll.Maximum);
                Check(last != null && bag.AutoScroll && -bag.AutoScrollPosition.Y > 0 && bottom.Bottom <= bag.ClientSize.Height + 2 && bottom.Bottom > 0,
                    size + ": the final material card can be reached by scrolling that card into view, before the later settings section");
                Button exit = Children(panel).OfType<Button>().First(b => b.Text == "保存并退出");
                Check(panel.ClientRectangle.Contains(InControl(exit, panel)), size + ": the fixed save-and-exit footer remains reachable");
                Point scroll = bag.AutoScrollPosition; Dictionary<string, Control> stable = Cards(panel);
                RefreshSafely(panel, game, profile, 12, size + " stable scrolled inventory");
                Check(SameCards(stable, Cards(panel)) && Math.Abs(bag.AutoScrollPosition.Y - scroll.Y) <= 1,
                    size + ": repeated refreshes preserve the exact card controls and current scroll position");
            }
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
                string profile = Path.Combine(output, "private-profile"); Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(args[2]));
                using (Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000) })
                using (AdventureController game = new AdventureController(owner, Path.GetFullPath(args[0]), false, null, false, profile))
                {
                    Field<Timer>(game, "timer").Stop(); game.ConfigureDesktopDemo(false); Field<Timer>(game, "timer").Stop();
                    game.Cultivation.Mortal = false; game.Cultivation.RealmIndex = 0; game.Cultivation.Layer = 12;
                    game.Lingyu.Materials["shuiyue-lingsui"] = 2; game.Lingyu.Materials["jinghua-shuiyue"] = 3;
                    game.Lingyu.Materials["suijing-jing"] = 4; game.Lingyu.Materials["jinghua"] = 5; game.Lingyu.Materials["future-lingyu-material"] = 6;
                    game.Lingyu.Scrolls["jingxin-scroll"] = 2; game.Lingyu.Heritages.Add("shuiyue-mirror");
                    game.Dongtian.Bag["shuiyue-lingsui"] = 7; game.Dongtian.Bag["jinghua-shuiyue"] = 8;
                    game.Dongtian.Bag["linggu"] = 9; game.Dongtian.Bag["ningshen"] = 10; game.Dongtian.Bag["jianxin"] = 11; game.Dongtian.Bag["future-cave-material"] = 12;
                    Check(game.DesktopSaveNow() && game.DesktopArchivePaths.All(p => p.StartsWith(profile + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(p)),
                        "the real fixture archives are established only under the supplied private profile");
                    object session = FormatterServices.GetUninitializedObject(typeof(DesktopDemoSession));
                    Set(session, "<Game>k__BackingField", game); Set(session, "<ProfileRoot>k__BackingField", profile);
                    using (DesktopPracticePanel panel = new DesktopPracticePanel((DesktopDemoSession)session))
                    {
                        Field<Timer>(panel, "refresh").Stop(); panel.OpenSection("inventory");
                        RefreshSafely(panel, game, profile, 1, "initial material display");
                        string existing = String.Join("\n", Children(Field<FlowLayoutPanel>(panel, "materials")).OfType<Label>().Select(l => l.Text).ToArray());
                        Check(existing.Contains("水月灵髓") && Quantity(existing, 2) && Quantity(existing, 7),
                            "the old-compatible real material UI is populated before the new contract is checked");
                        InitialFacts(panel);
                        Dictionary<string, Control> first = Cards(panel); RefreshSafely(panel, game, profile, 12, "stable card refresh");
                        Check(SameCards(first, Cards(panel)), "unchanged material refreshes retain all actual card controls");
                        game.Lingyu.Materials["shuiyue-lingsui"] = 3; game.Dongtian.Bag["shuiyue-lingsui"] = 6; game.Lingyu.Scrolls["jingxin-scroll"] = 4;
                        RefreshSafely(panel, game, profile, 1, "quantity-only refresh");
                        Check(SameCards(first, Cards(panel)) && Quantity(TextOf(Card(panel, "lingyu", "shuiyue-lingsui")), 3)
                            && Quantity(TextOf(Card(panel, "dongtian", "shuiyue-lingsui")), 6) && Quantity(TextOf(Card(panel, "scroll", "jingxin-scroll")), 4),
                            "quantity updates retain card instances and update only their own source counts");
                        game.Cultivation.RealmIndex = 1; game.Cultivation.Layer = 1;
                        RefreshSafely(panel, game, profile, 1, "realm-only refresh");
                        Check(SameCards(first, Cards(panel)), "realm changes update existing guidance controls instead of rebuilding the material list");
                        Route(Card(panel, "lingyu", "shuiyue-lingsui"), "lingyu", "shuiyue-lingsui", "dongtian", "foundation-establishment lingsui now navigates to the cave without changing stock");
                        Route(Card(panel, "lingyu", "jinghua-shuiyue"), "lingyu", "jinghua-shuiyue", "dongtian", "foundation-establishment mirror material offers its existing cave route");
                        Set(game, "dongtianPersistPending", true); RefreshSafely(panel, game, profile, 1, "pending-archive refresh");
                        Check(SameCards(first, Cards(panel)), "pending archive status retains every existing material card");
                        Set(game, "dongtianPersistPending", false); RefreshSafely(panel, game, profile, 1, "resolved-pending refresh");
                        Check(SameCards(first, Cards(panel)), "resolving pending status retains every existing material card");
                        RecipeFacts(panel, game, profile); CraftedGearFacts(panel, game, profile); RemovalFacts(panel, game, profile); LayoutFacts(panel, game, profile);
                        Check(!owner.Visible && !panel.Visible && !game.DesktopHeroWindow.Visible && game.DesktopEnemyWindows.Values.All(w => !w.Visible)
                            && !Field<Timer>(game, "timer").Enabled && !Field<Timer>(panel, "refresh").Enabled,
                            "the fixture keeps all panels/actors hidden and all simulation/UI timers stopped; no navigation button is clicked");
                    }
                    GC.KeepAlive(session); // The deliberately unconstructed session must not be disposed.
                }
                Console.WriteLine("Desktop material guidance: " + checks + " checks, " + failures + " failures. Hidden private fixture; no native navigation, natural-time claim or maintenance.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 2; }
        }
    }
}
