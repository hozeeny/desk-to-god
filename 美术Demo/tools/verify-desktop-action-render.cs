using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;

// Offscreen pixel checks only. Never starts a game, creates windows, or reads saves.
internal static class VerifyDesktopActionRender
{
    static int checks;
    static Type stateType;
    static MethodInfo render;
    static FieldInfo hintField;
    static void Check(bool ok, string name) { checks++; if (!ok) throw new InvalidOperationException("FAIL " + name); }
    static void Set(object state, string name, object value) { stateType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(state, value); }
    static Bitmap Frame(object renderer, string stage, bool facing, string hint, string kind, int queue, Size? size = null)
    {
        object state = Activator.CreateInstance(stateType, true);
        Set(state, "Kind", kind); Set(state, "HeroStage", stage); Set(state, "Phase", "idle");
        Set(state, "Time", .7); Set(state, "MoveSpeed", 0.0); Set(state, "FacingRight", facing);
        Set(state, "Size", size ?? new Size(220, 240)); Set(state, "Label", kind == "Hero" ? "凡人 · 红尘历练" : "山野妖物");
        Set(state, "QueueNumber", queue); Set(state, "MonsterKind", "slime");
        if (hintField != null) hintField.SetValue(state, hint);
        return (Bitmap)render.Invoke(renderer, new[] { state });
    }
    static int Different(Bitmap a, Bitmap b, Rectangle area)
    { int count = 0; for (int y = area.Top; y < area.Bottom; y++) for (int x = area.Left; x < area.Right; x++) if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) count++; return count; }
    static void ClearOuterBorder(Bitmap frame)
    {
        bool clear = true;
        for (int x = 0; x < frame.Width; x++) clear &= frame.GetPixel(x, 0).A == 0 && frame.GetPixel(x, frame.Height - 1).A == 0;
        for (int y = 0; y < frame.Height; y++) clear &= frame.GetPixel(0, y).A == 0 && frame.GetPixel(frame.Width - 1, y).A == 0;
        Check(clear, "outer alpha border remains transparent");
    }
    static int Main(string[] args)
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            stateType = assembly.GetType("NetBunny.AdventureRenderState", true);
            Type rendererType = assembly.GetType("NetBunny.AdventureRenderer", true);
            render = rendererType.GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic);
            hintField = stateType.GetField("ActionHint", BindingFlags.Instance | BindingFlags.NonPublic);
            Directory.CreateDirectory(args[3]);
            string longHint = "点击妖物选敌接近后再次点击出招格挡十战觉醒继续修炼额外文字";
            string[] stages = { "mortal", "qishi", "zhuji" };
            ConstructorInfo ctor = rendererType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string), typeof(string) }, null);
            using (IDisposable renderer = (IDisposable)ctor.Invoke(new object[] { args[1], args[2] }))
            using (Bitmap sheet = new Bitmap(220 * 4, 240 * 6, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                for (int row = 0; row < 6; row++)
                {
                    string stage = stages[row / 2]; bool facing = row % 2 == 0;
                    using (Bitmap missing = Frame(renderer, stage, facing, null, "Hero", 0))
                    using (Bitmap empty = Frame(renderer, stage, facing, "", "Hero", 0))
                    using (Bitmap white = Frame(renderer, stage, facing, " \r\n\t ", "Hero", 0))
                    using (Bitmap shortFrame = Frame(renderer, stage, facing, "点击妖物 · 选敌出招", "Hero", 0))
                    using (Bitmap longFrame = Frame(renderer, stage, facing, longHint, "Hero", 0))
                    {
                        Rectangle all = new Rectangle(0, 0, 220, 240);
                        Check(Different(missing, empty, all) == 0 && Different(missing, white, all) == 0, "missing and whitespace hints do not add a blank bar");
                        Check(Different(missing, shortFrame, new Rectangle(5, 5, 210, 28)) > 400, "action hint is visibly rendered above the hero");
                        Check(Different(missing, longFrame, new Rectangle(5, 5, 210, 28)) > 400, "long action hint is rendered");
                        Check(Different(missing, longFrame, new Rectangle(0, 34, 220, 206)) == 0, "hint does not cover face, body, or stage label");
                        ClearOuterBorder(shortFrame); ClearOuterBorder(longFrame);
                        Bitmap[] frames = { missing, shortFrame, longFrame, longFrame };
                        for (int col = 0; col < 4; col++)
                        {
                            g.FillRectangle(col % 2 == 0 ? Brushes.Beige : Brushes.DarkSlateGray, col * 220, row * 240, 220, 240);
                            g.DrawImageUnscaled(frames[col], col * 220, row * 240);
                        }
                        shortFrame.Save(Path.Combine(args[3], stage + (facing ? "-right" : "-left") + "-hint.png"), ImageFormat.Png);
                        longFrame.Save(Path.Combine(args[3], stage + (facing ? "-right" : "-left") + "-long-hint.png"), ImageFormat.Png);
                    }
                }
                using (Bitmap target = Frame(renderer, "mortal", true, null, "Skeleton", 1))
                using (Bitmap queued = Frame(renderer, "mortal", true, null, "Skeleton", 2))
                {
                    Check(Different(target, queued, new Rectangle(155, 50, 60, 28)) > 80, "current target has a clear label rather than an indistinguishable queue digit");
                    ClearOuterBorder(target); target.Save(Path.Combine(args[3], "monster-current-target.png"), ImageFormat.Png);
                }
                using (Bitmap control = Frame(renderer, "mortal", true, "点击妖物\r\n\t出招", "Hero", 0))
                using (Bitmap single = Frame(renderer, "mortal", true, "点击妖物 出招", "Hero", 0))
                    Check(Different(control, single, new Rectangle(0, 0, 220, 240)) == 0, "multiline messages stay on a single safe line");
                using (Bitmap unicode = Frame(renderer, "mortal", false, "\U0001F33F\U0001F33F 点击妖物选敌接近后再次点击出招格挡十战觉醒", "Hero", 0))
                {
                    ClearOuterBorder(unicode);
                    unicode.Save(Path.Combine(args[3], "unicode-long-hint.png"), ImageFormat.Png);
                }
                using (Bitmap smallTarget = Frame(renderer, "mortal", true, null, "Skeleton", 1, new Size(130, 170)))
                {
                    ClearOuterBorder(smallTarget);
                    smallTarget.Save(Path.Combine(args[3], "monster-current-target-130x170.png"), ImageFormat.Png);
                }
                sheet.Save(Path.Combine(args[3], "action-hints-220x240-light-dark.png"), ImageFormat.Png);
            }
            using (IDisposable legacy = (IDisposable)ctor.Invoke(new object[] { args[1], null }))
            using (Bitmap plain = Frame(legacy, "mortal", true, null, "Hero", 0))
            using (Bitmap hinted = Frame(legacy, "mortal", true, longHint, "Hero", 0))
                Check(Different(plain, hinted, new Rectangle(0, 0, 220, 240)) == 0, "legacy renderer ignores new art action hints");
            Console.WriteLine("Desktop action rendering: " + checks + " checks passed."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
