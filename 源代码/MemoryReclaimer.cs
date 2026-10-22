using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetBunny
{
    internal sealed class MemoryReclaimResult
    {
        internal MemorySnapshot Before, After;
        internal long WorkingSetReduction;
        internal int Trimmed, Skipped, Unobserved;
        internal bool Cancelled, TimedOut, ProcessLimited;
        internal string Error;
        internal string Summary
        {
            get
            {
                string usage = Before != null && Before.Available && After != null && After.Available ? Before.UsedPercent.ToString("0.0") + "% → " + After.UsedPercent.ToString("0.0") + "%" : "系统内存采样暂不可用";
                string measured = Trimmed > 0 && Unobserved >= Trimmed ? "工作集减少量未能核实"
                    : "已观测工作集减少 " + AdventureBattle.FormatBytes(WorkingSetReduction);
                string stop = Cancelled ? "；已取消（以上为实际完成量）"
                    : TimedOut ? "；已达本轮5秒时限，未继续处理其余候选"
                    : ProcessLimited ? "；已达本轮24个进程上限，未继续处理其余候选" : "";
                return "Boss 已击败 · 工作集整理：" + usage + "；已核实整理 " + Trimmed + " 个，跳过 " + Skipped + " 个；" + measured
                    + (Unobserved > 0 ? "；其中 " + Unobserved + " 个整理后采样不可用，未知变化不计入减少量" : "")
                    + stop + (String.IsNullOrEmpty(Error) ? "" : "；" + Error);
            }
        }
    }
    internal sealed class MemoryTrimObservation
    {
        internal bool Applied, Observed;
        internal long Before, After;
    }
    internal static class MemoryReclaimer
    {
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool IsProcessCritical(IntPtr handle, out bool critical);
        [DllImport("psapi.dll", SetLastError=true)] private static extern bool EmptyWorkingSet(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        internal static MemoryReclaimResult Reclaim(CancellationToken token)
        { return Reclaim(token, Process.GetProcesses(), TrimWorkingSet); }

        // The worker owns/disposes this snapshot. A caller supplying a restricted
        // set must retain separate Process objects for its own lifetime handling.
        internal static MemoryReclaimResult Reclaim(CancellationToken token, Process[] processes, Func<Process, CancellationToken, MemoryTrimObservation> trim)
        {
            if (processes == null) throw new ArgumentNullException("processes");
            if (trim == null) throw new ArgumentNullException("trim");
            MemoryReclaimResult result = new MemoryReclaimResult { Before = CleanupService.ReadMemory() };
            Stopwatch elapsed = Stopwatch.StartNew();
            int own, session;
            using (Process self = Process.GetCurrentProcess()) { own = self.Id; session = self.SessionId; }
            uint foreground; GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            try
            {
                foreach (Process process in processes)
                {
                    if (token.IsCancellationRequested) { result.Cancelled = true; break; }
                    if (elapsed.Elapsed.TotalSeconds > 5) { result.TimedOut = true; break; }
                    if (result.Trimmed >= 24) { result.ProcessLimited = true; break; }
                    try
                    {
                        GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                        if (process.Id == own || process.Id == foreground || process.SessionId != session || process.HasExited || process.WorkingSet64 < 32L*1024*1024) { result.Skipped++; continue; }
                        string path = process.MainModule.FileName;
                        if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase)) { result.Skipped++; continue; }
                        MemoryTrimObservation observed = trim(process, token);
                        if (observed != null && observed.Applied)
                        {
                            result.Trimmed++;
                            if (observed.Observed) result.WorkingSetReduction += Math.Max(0, observed.Before - observed.After);
                            else result.Unobserved++;
                        }
                        else result.Skipped++;
                    }
                    catch { result.Skipped++; }
                }
            }
            catch (Exception e) { result.Error = e.Message; }
            finally { foreach (Process process in processes) process.Dispose(); }
            result.After = CleanupService.ReadMemory();
            // The last candidate can observe cancellation without a subsequent
            // loop iteration. Preserve it even when some native work succeeded.
            result.Cancelled |= token.IsCancellationRequested;
            if (result.Trimmed == 0 && result.Error == null && !result.Cancelled && !result.TimedOut && !result.ProcessLimited)
                result.Error = "本次未整理后台工作集";
            return result;
        }
        internal static bool TryTrim(Process process, CancellationToken token, out long before, out long after)
        {
            MemoryTrimObservation result = TrimWorkingSet(process, token);
            before = result.Before; after = result.After;
            return result.Applied && result.Observed;
        }
        internal static MemoryTrimObservation TrimWorkingSet(Process process, CancellationToken token)
        {
            MemoryTrimObservation result = new MemoryTrimObservation();
            if (token.IsCancellationRequested) return result;
            IntPtr handle = OpenProcess(0x100 | 0x1000, false, process.Id);
            try
            {
                bool critical;
                if (handle == IntPtr.Zero || !IsProcessCritical(handle,out critical) || critical) return result;
                process.Refresh(); result.Before = process.WorkingSet64;
                if (token.IsCancellationRequested || !EmptyWorkingSet(handle)) return result;
                result.Applied = true;
                // A process may exit or deny inspection after a successful API
                // call. Keep that known action; do not invent its byte change.
                try { process.Refresh(); result.After = process.WorkingSet64; result.Observed = true; }
                catch { }
                return result;
            }
            finally { if(handle!=IntPtr.Zero)CloseHandle(handle); }
        }
        internal static string SelfTest()
        {
            MemoryReclaimResult r = new MemoryReclaimResult { Before = new MemorySnapshot {Available=true,UsedPercent=75}, After = new MemorySnapshot {Available=true,UsedPercent=73}, WorkingSetReduction=1024*1024, Trimmed=2 };
            if (!r.Summary.Contains("75.0% → 73.0%") || !r.Summary.Contains("1.0 MiB")) throw new Exception("Memory result must report observed changes");
            r.After.UsedPercent=76;
            if (!r.Summary.Contains("76.0%")) throw new Exception("Concurrent memory rise must not be reported as a release");
            r.After.Available=false;
            if (!r.Summary.Contains("暂不可用")) throw new Exception("Unknown memory cannot report invented bytes");
            return "Memory reclaim feedback: 3 checks passed (no process was trimmed).";
        }
    }
}
