using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetBunny
{
    internal enum ProcessCloseStatus { Exited, StillRunning, Denied, Cancelled, IdentityChanged, Error }

    internal sealed class ProcessCloseResult
    {
        internal ProcessCloseStatus Status;
        internal string Detail;
        internal string Summary
        {
            get
            {
                string prefix;
                switch (Status)
                {
                    case ProcessCloseStatus.Exited: prefix = "程序已实际退出"; break;
                    case ProcessCloseStatus.StillRunning: prefix = "程序仍在运行"; break;
                    case ProcessCloseStatus.Denied: prefix = "已拒绝退出请求"; break;
                    case ProcessCloseStatus.Cancelled: prefix = "已取消退出请求或等待"; break;
                    case ProcessCloseStatus.IdentityChanged: prefix = "目标进程身份已变化，未执行退出"; break;
                    default: prefix = "退出请求发生错误"; break;
                }
                return prefix + (String.IsNullOrEmpty(Detail) ? "" : "；" + Detail);
            }
        }
    }

    internal static class DesktopProcessActions
    {
        static ProcessCloseResult Result(ProcessCloseStatus status, string detail) { return new ProcessCloseResult { Status = status, Detail = detail }; }
        static bool SameIdentity(ProcessIdentity expected, ProcessIdentity actual)
        {
            return expected.Id == actual.Id && expected.StartedUtcTicks == actual.StartedUtcTicks
                && String.Equals(Path.GetFullPath(expected.ExecutablePath), actual.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }

        // Caller must consume one target-specific user confirmation in non-Virtual mode.
        // Run on a worker: normal app shutdown is observed for at most five seconds.
        // A sent WM_CLOSE cannot be recalled; cancellation never forces the app to exit.
        internal static ProcessCloseResult RequestGracefulClose(ProcessIdentity expected, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Result(ProcessCloseStatus.Cancelled, "尚未发送正常关闭请求");
            if (!DesktopProcessTargets.ValidIdentity(expected)) return Result(ProcessCloseStatus.Denied, "缺少可核实的 PID、启动时间或程序路径");
            // Copy the expected identity so an asynchronous caller cannot mutate it mid-check.
            expected = new ProcessIdentity { Id = expected.Id, StartedUtcTicks = expected.StartedUtcTicks, ExecutablePath = expected.ExecutablePath };
            IntPtr handle = IntPtr.Zero;
            Process process = null;
            Stopwatch elapsed = Stopwatch.StartNew();
            try
            {
                try { process = Process.GetProcessById(expected.Id); }
                catch (ArgumentException) { return Result(ProcessCloseStatus.Exited, "目标在请求前已经退出，未发送关闭请求"); }
                handle = DesktopProcessNative.OpenProcess(DesktopProcessNative.QueryAndWait, false, expected.Id);
                if (handle == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    return error == 87 ? Result(ProcessCloseStatus.Exited, "目标在请求前已经退出，未发送关闭请求")
                        : Result(ProcessCloseStatus.Denied, "无法取得身份核实权限（" + error + "），未发送关闭请求");
                }
                uint initialState = DesktopProcessNative.WaitForSingleObject(handle, 0);
                if (initialState == 0) return Result(ProcessCloseStatus.Exited, "目标在请求前已经退出，未发送关闭请求");
                if (initialState != 258) return Result(ProcessCloseStatus.Denied, "无法核实原进程运行状态，未发送关闭请求");
                ProcessInspection info = DesktopProcessTargets.Inspect(process, handle);
                if (!SameIdentity(expected, info.Identity)) return Result(ProcessCloseStatus.IdentityChanged, "PID、启动时间或程序路径与所选目标不符");
                string refusal = DesktopProcessTargets.CurrentRefusal(info);
                if (refusal != null) return Result(ProcessCloseStatus.Denied, refusal);
                if (token.IsCancellationRequested) return Result(ProcessCloseStatus.Cancelled, "尚未发送正常关闭请求");
                // Re-read from the pinned process handle immediately before the only mutation.
                info = DesktopProcessTargets.Inspect(process, handle);
                if (!SameIdentity(expected, info.Identity)) return Result(ProcessCloseStatus.IdentityChanged, "执行前复核发现目标身份变化");
                refusal = DesktopProcessTargets.CurrentRefusal(info);
                if (refusal != null) return Result(ProcessCloseStatus.Denied, refusal);
                if (token.IsCancellationRequested) return Result(ProcessCloseStatus.Cancelled, "尚未发送正常关闭请求");
                if (!process.CloseMainWindow()) return Result(ProcessCloseStatus.Denied, "程序未接受正常窗口关闭请求");
                while (true)
                {
                    uint state = DesktopProcessNative.WaitForSingleObject(handle, 0);
                    if (state == 0) return Result(ProcessCloseStatus.Exited, "已核实原进程退出；未使用强制结束");
                    if (state != 258) return Result(ProcessCloseStatus.Error, "无法继续核实原进程状态");
                    if (token.IsCancellationRequested) return Result(ProcessCloseStatus.Cancelled, "正常关闭请求已发出；程序可能保留保存提示或继续运行");
                    int remaining = 5000 - (int)elapsed.ElapsedMilliseconds;
                    if (remaining <= 0) return Result(ProcessCloseStatus.StillRunning, "等待正常退出已超时；可能存在保存提示或用户取消，不会强制结束");
                    token.WaitHandle.WaitOne(Math.Min(50, remaining));
                }
            }
            catch (Win32Exception e) { return Result(ProcessCloseStatus.Denied, "权限或身份信息不足（" + e.NativeErrorCode + "），未强制结束"); }
            catch (InvalidOperationException)
            {
                return handle != IntPtr.Zero && DesktopProcessNative.WaitForSingleObject(handle, 0) == 0
                    ? Result(ProcessCloseStatus.Exited, "目标已经退出") : Result(ProcessCloseStatus.Denied, "进程或窗口信息已不可核实");
            }
            catch (Exception e) { return Result(ProcessCloseStatus.Error, "无法完成正常退出核实：" + e.GetType().Name); }
            finally
            {
                if (handle != IntPtr.Zero) DesktopProcessNative.CloseHandle(handle);
                if (process != null) process.Dispose();
            }
        }
    }
}
