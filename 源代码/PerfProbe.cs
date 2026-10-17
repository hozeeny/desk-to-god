using System;
using System.Diagnostics;
using System.Text;

namespace NetBunny
{
    // Opt-in CPU profiler for the desktop-pet tick pipeline (--perf-probe).
    // Zero-cost when disabled: every call site checks Enabled first, so the
    // only overhead in normal runs is one boolean test per section.
    // Single-threaded by design — all instrumented sections run on the UI thread.
    internal static class PerfProbe
    {
        internal static bool Enabled;
        private const int MaxSections = 32;
        private static readonly string[] names = new string[MaxSections];
        private static readonly double[] millis = new double[MaxSections];
        private static readonly long[] calls = new long[MaxSections];
        private static int registered;
        private static readonly Stopwatch watch = new Stopwatch();
        private static readonly Stopwatch totalWatch = new Stopwatch();

        internal static int Register(string name)
        {
            if (registered >= MaxSections) throw new InvalidOperationException("too many perf sections");
            names[registered] = name;
            return registered++;
        }
        internal static void Begin() { watch.Restart(); }
        internal static void End(int section) { millis[section] += watch.Elapsed.TotalMilliseconds; calls[section]++; }
        // 独立秒表测整拍：不被分节 Begin 重置。
        internal static void BeginTotal() { totalWatch.Restart(); }
        internal static void EndTotal(int section) { millis[section] += totalWatch.Elapsed.TotalMilliseconds; calls[section]++; }

        internal static string Report(double wallSeconds, double cpuSeconds)
        {
            StringBuilder text = new StringBuilder();
            text.AppendLine("== perf-probe 分模块 CPU 测量 ==");
            text.AppendLine("墙钟时长: " + wallSeconds.ToString("F1") + " s   进程 CPU: " + cpuSeconds.ToString("F1") +
                " s   平均占用: " + (cpuSeconds / Math.Max(1, wallSeconds) * 100).ToString("F1") + "% 单核");
            int[] order = new int[registered];
            for (int i = 0; i < registered; i++) order[i] = i;
            Array.Sort(order, delegate(int a, int b) { return millis[b].CompareTo(millis[a]); });
            double total = 0; for (int i = 0; i < registered; i++) if (names[order[i]] != "tick-total" && names[order[i]].IndexOf('.') < 0) total += millis[order[i]];
            text.AppendLine("section                total_ms    ms/min   share    calls    avg_ms");
            for (int i = 0; i < registered; i++)
            {
                int id = order[i];
                double share = total > 0 ? millis[id] / total * 100 : 0;
                text.AppendLine((names[id] ?? ("#" + id)).PadRight(22) +
                    millis[id].ToString("F1").PadLeft(10) +
                    (millis[id] / Math.Max(1, wallSeconds) * 60).ToString("F1").PadLeft(10) +
                    (share.ToString("F1") + "%").PadLeft(9) +
                    calls[id].ToString().PadLeft(9) +
                    (calls[id] > 0 ? millis[id] / calls[id] : 0).ToString("F3").PadLeft(9));
            }
            return text.ToString();
        }
        internal static void Reset()
        {
            for (int i = 0; i < MaxSections; i++) { millis[i] = 0; calls[i] = 0; }
        }
    }
}
