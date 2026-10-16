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
        internal int Trimmed, Skipped;
        internal string Error;
        internal string Summary
        {
            get
            {
                string usage = Before != null && Before.Available && After != null && After.Available ? Before.UsedPercent.ToString("0.0") + "% → " + After.UsedPercent.ToString("0.0") + "%" : "系统内存采样暂不可用";
                return "Boss 已击败 · 内存回收：" + usage + "；" + Trimmed + " 个后台进程工作集减少 " + AdventureBattle.FormatBytes(WorkingSetReduction) + (String.IsNullOrEmpty(Error) ? "" : "；" + Error);
            }
        }
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
        {
            MemoryReclaimResult result = new MemoryReclaimResult { Before = CleanupService.ReadMemory() };
            Process[] processes = Process.GetProcesses();
            Stopwatch elapsed = Stopwatch.StartNew();
            int own, session;
            using (Process self = Process.GetCurrentProcess()) { own = self.Id; session = self.SessionId; }
            uint foreground; GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
            try
            {
                foreach (Process process in processes)
                {
                    if (token.IsCancellationRequested || elapsed.Elapsed.TotalSeconds > 5 || result.Trimmed >= 24) break;
                    try
                    {
                        GetWindowThreadProcessId(GetForegroundWindow(), out foreground);
                        if (process.Id == own || process.Id == foreground || process.SessionId != session || process.HasExited || process.WorkingSet64 < 32L*1024*1024) { result.Skipped++; continue; }
                        string path = process.MainModule.FileName;
                        if (path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\", StringComparison.OrdinalIgnoreCase)) { result.Skipped++; continue; }
                        long before, after;
                        if (TryTrim(process,token,out before,out after))
                        {
                            result.Trimmed++;
                            result.WorkingSetReduction += Math.Max(0, before - after);
                        }
                        else result.Skipped++;
                    }
                    catch { result.Skipped++; }
                }
            }
            catch (Exception e) { result.Error = e.Message; }
            finally { foreach (Process process in processes) process.Dispose(); }
            result.After = CleanupService.ReadMemory();
            if (result.Trimmed == 0 && result.Error == null) result.Error = token.IsCancellationRequested ? "已取消" : "当前没有可回收的后台工作集";
            return result;
        }
        internal static bool TryTrim(Process process, CancellationToken token, out long before, out long after)
        {
            before = after = 0;
            if (token.IsCancellationRequested) return false;
            IntPtr handle = OpenProcess(0x100 | 0x1000, false, process.Id);
            try
            {
                bool critical;
                if (handle == IntPtr.Zero || !IsProcessCritical(handle,out critical) || critical) return false;
                process.Refresh(); before=process.WorkingSet64;
                if (token.IsCancellationRequested || !EmptyWorkingSet(handle)) return false;
                process.Refresh(); after=process.WorkingSet64; return true;
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
