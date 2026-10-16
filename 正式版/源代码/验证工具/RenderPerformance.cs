using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace NetBunny
{
    // Read-only measurement. Renders into memory and immediately disposes each bitmap.
    // Never constructs the controller, scans temp files, queries networks, or opens desktop windows.
    internal static class RenderPerformance
    {
        private const int Samples = 120;
        private static readonly StringBuilder report = new StringBuilder();
        private static readonly CultureInfo invariant = CultureInfo.InvariantCulture;
        [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
        private static int Gdi { get { return GetGuiResources(Process.GetCurrentProcess().Handle, 0); } }
        private static void Print(string line) { report.AppendLine(line); Console.WriteLine(line); }
        private static string F(double value) { return value.ToString("0.000", invariant); }

        [STAThread] private static int Main(string[] args)
        {
            string assets = args.Length > 0 ? args[0] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string log = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "render-performance.txt");
            try
            {
                Print("Knight Assistant V5 — measured render cost, not an FPS guarantee");
                Print("Logical CPU count=" + Environment.ProcessorCount + "; 64-bit process=" + Environment.Is64BitProcess + "; Stopwatch frequency=" + Stopwatch.Frequency);
                Print("Timing: render + bitmap disposal; PNG loading/JIT warm-up excluded; no layered-window upload, DWM composition, message-pump scheduling, or scan/capture work included.");
                Print("120 measured frames per case after 20 warm-up frames. Values in milliseconds. Default actual window sizes.");
                int initialGdi = Gdi; Stopwatch loading = Stopwatch.StartNew();
                using (AdventureRenderer renderer = new AdventureRenderer(assets))
                {
                    Print("Asset initialization=" + F(loading.Elapsed.TotalMilliseconds) + " ms; source files only read.");
                    foreach (string theme in RasterActorRenderer.ThemeCodes)
                    {
                        string captured = theme;
                        Measure("Hero " + theme + " 220x240, running + Lv label", delegate(int frame) { Draw(renderer, Hero(captured, frame, false)); });
                    }
                    Measure("Hero OTHER 220x240, rainbow aura + laser attack", delegate(int frame) { Draw(renderer, Hero("OTHER", frame, true)); });
                    Measure("13 enemies only: 12 common types + 1 Boss", delegate(int frame) { Enemies(renderer, frame); });
                    Measure("Full scene: hero + 12 common + Boss + landmark (15 bitmaps)", delegate(int frame) { Scene(renderer, frame); });
                    Measure("Staggered controller: hero + 4 rotating monsters each tick, landmark every 16th", delegate(int frame) { StaggeredScene(renderer, frame); });
                    NativeTimer(renderer, false, 16);
                    NativeTimer(renderer, false, 15);
                    NativeTimer(renderer, true, 16);
                    NativeTimer(renderer, true, 15);
                    Print("Revised controller: hero every tick; at most four rotating enemies each tick; landmark about every250ms. User-forced scene refresh still draws everything immediately.");
                    Print("GDI with renderer live=" + Gdi + "; process working set=" + F(Process.GetCurrentProcess().WorkingSet64 / 1048576.0) + " MiB.");
                }
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Print("GDI lifetime: before assets=" + initialGdi + ", after renderer dispose=" + Gdi + ".");
                Print("Interpretation: p95 below 16.667 ms leaves a render budget for near-60-Hz hero updates, but actual displayed FPS also depends on window upload, Windows scheduling, capture/scan load, display scale, and other processes. No constant-60-FPS claim.");
                File.WriteAllText(log, report.ToString(), new UTF8Encoding(true)); return 0;
            }
            catch (Exception e) { Print(e.ToString()); File.WriteAllText(log, report.ToString(), new UTF8Encoding(true)); return 1; }
        }
        private static AdventureRenderState Hero(string theme, int frame, bool effects)
        {
            return new AdventureRenderState { Kind = "Hero", ThemeCode = theme, Size = new Size(220, 240), Phase = effects ? "attack" : "run", Cycle = frame / 60.0, Time = frame / 60.0, FacingRight = frame % 120 < 60, WeaponId = effects ? "laser" : "spear", Level = effects ? 7 : 1, GlowProgress = effects ? .7 : -1, AttackProgress = effects ? .25 + .5 * Math.Abs(Math.Sin(frame / 10.0)) : 0, HealthRatio = effects ? .8 : 1, Label = effects ? "Lv.7" : "Lv.1" };
        }
        private static void Draw(AdventureRenderer renderer, AdventureRenderState state) { using (Bitmap bitmap = renderer.Render(state)) { } }
        private static void Enemies(AdventureRenderer renderer, int frame)
        {
            for (int i = 0; i < 13; i++)
            {
                bool boss = i == 12;
                Draw(renderer, new AdventureRenderState { Kind = boss ? "Boss" : "Skeleton", MonsterKind = boss ? "boss" : RasterActorRenderer.MonsterKinds[i % 5], Size = boss ? new Size(190, 220) : new Size(130, 170), Phase = "run", Time = frame / 15.0 + i * .2, FacingRight = i % 2 == 0, HealthRatio = .75, QueueNumber = i + 1, Label = boss ? "内存压力 88%" : "#" + (i + 1) + " · 128 MiB", HitFlash = i < 2 ? .2 : 0 });
            }
        }
        private static void Scene(AdventureRenderer renderer, int frame)
        {
            Draw(renderer, Hero("OTHER", frame, false)); Enemies(renderer, frame);
            Draw(renderer, new AdventureRenderState { Kind = "Landmark", ThemeCode = "OTHER", Size = new Size(200, 220), Time = frame / 15.0, Label = "可清理 1.5 GiB" });
        }
        private static void StaggeredScene(AdventureRenderer renderer, int frame)
        {
            Draw(renderer, Hero("OTHER", frame, false));
            for (int n = 0; n < 4; n++) Draw(renderer, EnemyState((frame * 4 + n) % 13, frame));
            if (frame % 16 == 0) Draw(renderer, new AdventureRenderState { Kind = "Landmark", ThemeCode = "OTHER", Size = new Size(200, 220), Time = frame / 60.0, Label = "可清理 1.5 GiB" });
        }
        private static AdventureRenderState EnemyState(int index, int frame)
        {
            bool boss = index == 12;
            return new AdventureRenderState { Kind = boss ? "Boss" : "Skeleton", MonsterKind = boss ? "boss" : RasterActorRenderer.MonsterKinds[index % 5], Size = boss ? new Size(190, 220) : new Size(130, 170), Phase = "run", Time = frame / 60.0 + index * .2, HealthRatio = .75, Label = "128 MiB", FacingRight = index % 2 == 0 };
        }
        private static void Measure(string name, Action<int> render)
        {
            for (int i = 0; i < 20; i++) render(i);
            GC.Collect(); GC.WaitForPendingFinalizers();
            int gdiBefore = Gdi, gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2), late = 0;
            double[] values = new double[Samples]; Stopwatch clock = new Stopwatch();
            for (int frame = 0; frame < Samples; frame++) { clock.Restart(); render(frame); clock.Stop(); values[frame] = clock.Elapsed.TotalMilliseconds; if (values[frame] > 16.667) late++; }
            double total = 0; foreach (double sample in values) total += sample; Array.Sort(values);
            Print(name + ": avg=" + F(total / Samples) + "; median=" + F((values[59] + values[60]) / 2) + "; p95=" + F(values[(int)Math.Ceiling(Samples * .95) - 1]) + "; min=" + F(values[0]) + "; max=" + F(values[Samples - 1]) + "; >16.667ms=" + late + "/" + Samples + "; GDI=" + gdiBefore + "->" + Gdi + "; GC=" + (GC.CollectionCount(0) - gen0) + "/" + (GC.CollectionCount(1) - gen1) + "/" + (GC.CollectionCount(2) - gen2));
        }
        private static void NativeTimer(AdventureRenderer renderer, bool battlefield, int interval)
        {
            List<double> intervals = new List<double>(), durations = new List<double>();
            List<KnightLayeredWindow> windows = new List<KnightLayeredWindow>();
            Exception failure = null; int frame = 0; double previous = 0, measuredMs = 0; int baselineGdi = Gdi;
            using (ApplicationContext context = new ApplicationContext()) using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer())
            {
                try
                {
                    for (int i = 0; i < (battlefield ? 15 : 1); i++)
                    {
                        KnightLayeredWindow window = new KnightLayeredWindow(); window.Location = new Point(-16000 - i * 300, -16000); window.Size = new Size(220, 240); window.PassThrough = true; window.Show(); windows.Add(window);
                    }
                    Stopwatch elapsed = Stopwatch.StartNew(); timer.Interval = interval;
                    timer.Tick += delegate
                    {
                        try
                        {
                            double now = elapsed.Elapsed.TotalMilliseconds, delta = now - previous; previous = now;
                            Stopwatch cost = Stopwatch.StartNew(); windows[0].Present(renderer.Render(Hero("OTHER", frame, false)));
                            if (battlefield)
                            {
                                for (int n = 0; n < 4; n++)
                                {
                                    int i = (frame * 4 + n) % 13; windows[i + 1].Present(renderer.Render(EnemyState(i, frame)));
                                }
                                if (frame % 16 == 0) windows[14].Present(renderer.Render(new AdventureRenderState { Kind = "Landmark", ThemeCode = "OTHER", Size = new Size(200, 220), Time = frame / 60.0, Label = "可清理 1.5 GiB" }));
                            }
                            cost.Stop(); if (frame >= 20) { intervals.Add(delta); durations.Add(cost.Elapsed.TotalMilliseconds); measuredMs += delta; }
                            frame++; if (measuredMs >= 3000 && intervals.Count > 1) { timer.Stop(); context.ExitThread(); }
                        }
                        catch (Exception e) { failure = e; timer.Stop(); context.ExitThread(); }
                    };
                    timer.Start(); Application.Run(context);
                    if (failure != null) throw failure;
                    double seconds = 0; foreach (double observedInterval in intervals) seconds += observedInterval / 1000;
                    Print("Native off-screen WinForms Timer(" + interval + ")+Present " + (battlefield ? "hero+4 rotating enemies each tick" : "solo hero") + ": callback rate=" + F(intervals.Count / seconds) + " Hz; n=" + intervals.Count + "; intervals " + Summary(intervals) + "; callback cost " + Summary(durations) + "; GDI live=" + Gdi);
                    Print("Off-screen windows use real UpdateLayeredWindow calls and transfer/dispose actual PArgb frames; off-screen compositor work may be lower than a visible desktop. No timer resolution or system settings changed.");
                }
                finally { timer.Stop(); foreach (KnightLayeredWindow window in windows) window.Dispose(); }
            }
            Print("Native timer disposed: GDI " + baselineGdi + "->" + Gdi);
        }
        private static string Summary(List<double> samples)
        {
            double[] values = samples.ToArray(); Array.Sort(values); double total = 0; foreach (double value in values) total += value;
            return "avg=" + F(total / values.Length) + ", median=" + F((values[values.Length / 2 - 1] + values[values.Length / 2]) / 2) + ", p95=" + F(values[(int)Math.Ceiling(values.Length * .95) - 1]) + ", max=" + F(values[values.Length - 1]);
        }
    }
}
