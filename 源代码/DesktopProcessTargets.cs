using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace NetBunny
{
    internal sealed class ProcessIdentity
    {
        internal int Id;
        internal long StartedUtcTicks;
        internal string ExecutablePath;
        internal string Name;
        internal long WorkingSetBytes;
    }

    internal sealed class ProcessInspection
    {
        internal ProcessIdentity Identity;
        internal int SessionId;
        internal string UserSid;
        internal bool CriticalKnown, Critical, HasCloseEntry;
        internal IntPtr Window;
    }

    // Read-only candidates are ordinary applications, never a judgement that an app is waste.
    internal static class DesktopProcessTargets
    {
        internal static ProcessIdentity[] Observe()
        {
            List<ProcessIdentity> result = new List<ProcessIdentity>();
            Process[] processes = Process.GetProcesses();
            try
            {
                foreach (Process process in processes)
                {
                    IntPtr handle = IntPtr.Zero;
                    try
                    {
                        handle = DesktopProcessNative.OpenProcess(DesktopProcessNative.QueryAndWait, false, process.Id);
                        if (handle == IntPtr.Zero) continue;
                        ProcessInspection info = Inspect(process, handle);
                        if (CurrentRefusal(info) == null && DesktopProcessNative.WaitForSingleObject(handle, 0) == 258)
                            result.Add(info.Identity);
                    }
                    catch { /* Unknown or inaccessible observations are excluded. */ }
                    finally { if (handle != IntPtr.Zero) DesktopProcessNative.CloseHandle(handle); }
                }
            }
            finally { foreach (Process process in processes) process.Dispose(); }
            result.Sort(delegate(ProcessIdentity a, ProcessIdentity b) { return b.WorkingSetBytes.CompareTo(a.WorkingSetBytes); });
            return result.ToArray();
        }

        internal static ProcessInspection Inspect(Process process, IntPtr handle)
        {
            uint pid = DesktopProcessNative.GetProcessId(handle), session;
            if (pid == 0 || pid != process.Id || !DesktopProcessNative.ProcessIdToSessionId(pid, out session))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            long created, exited, kernel, user;
            if (!DesktopProcessNative.GetProcessTimes(handle, out created, out exited, out kernel, out user))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            StringBuilder path = new StringBuilder(32768); int length = path.Capacity;
            if (!DesktopProcessNative.QueryFullProcessImageName(handle, 0, path, ref length))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            bool critical;
            bool criticalKnown = DesktopProcessNative.IsProcessCritical(handle, out critical);
            string sid = OwnerSid(handle);
            process.Refresh();
            IntPtr window = process.MainWindowHandle;
            uint windowPid = 0;
            if (window != IntPtr.Zero) DesktopProcessNative.GetWindowThreadProcessId(window, out windowPid);
            IntPtr menu = window == IntPtr.Zero ? IntPtr.Zero : DesktopProcessNative.GetSystemMenu(window, false);
            uint closeState = menu == IntPtr.Zero ? UInt32.MaxValue : DesktopProcessNative.GetMenuState(menu, 0xF060, 0);
            bool hasClose = windowPid == pid && DesktopProcessNative.IsWindow(window) && DesktopProcessNative.IsWindowVisible(window)
                && DesktopProcessNative.IsWindowEnabled(window) && closeState != UInt32.MaxValue && (closeState & 3) == 0;
            long workingSet = 0;
            try { workingSet = Math.Max(0, process.WorkingSet64); } catch { }
            string executable = Path.GetFullPath(path.ToString());
            return new ProcessInspection {
                Identity = new ProcessIdentity { Id = (int)pid, StartedUtcTicks = DateTime.FromFileTimeUtc(created).Ticks,
                    ExecutablePath = executable, Name = Path.GetFileNameWithoutExtension(executable), WorkingSetBytes = workingSet },
                SessionId = (int)session, UserSid = sid, CriticalKnown = criticalKnown, Critical = critical,
                Window = window, HasCloseEntry = hasClose
            };
        }

        static string OwnerSid(IntPtr process)
        {
            IntPtr token;
            if (!DesktopProcessNative.OpenProcessToken(process, 8, out token)) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr buffer = IntPtr.Zero;
            try
            {
                int length;
                DesktopProcessNative.GetTokenInformation(token, 1, IntPtr.Zero, 0, out length);
                if (length <= 0 || length > 1024 * 1024) throw new Win32Exception("无法核实进程所有者");
                buffer = Marshal.AllocHGlobal(length);
                if (!DesktopProcessNative.GetTokenInformation(token, 1, buffer, length, out length)) throw new Win32Exception(Marshal.GetLastWin32Error());
                return new SecurityIdentifier(Marshal.ReadIntPtr(buffer)).Value;
            }
            finally { if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer); DesktopProcessNative.CloseHandle(token); }
        }

        internal static string CurrentRefusal(ProcessInspection info)
        {
            uint foreground;
            IntPtr window = DesktopProcessNative.GetForegroundWindow();
            if (window == IntPtr.Zero || DesktopProcessNative.GetWindowThreadProcessId(window, out foreground) == 0 || foreground == 0)
                return "无法核实当前前台程序";
            using (Process self = Process.GetCurrentProcess())
            using (WindowsIdentity user = WindowsIdentity.GetCurrent())
                return RefuseReason(info, self.Id, self.SessionId, user.User == null ? null : user.User.Value, foreground,
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }

        internal static bool ValidIdentity(ProcessIdentity identity)
        {
            if (identity == null || identity.Id <= 0 || identity.StartedUtcTicks <= 0 || String.IsNullOrWhiteSpace(identity.ExecutablePath)) return false;
            try { return Path.IsPathRooted(identity.ExecutablePath) && Path.GetFullPath(identity.ExecutablePath) == identity.ExecutablePath; }
            catch { return false; }
        }

        internal static string RefuseReason(ProcessInspection info, int ownId, int ownSession, string userSid, uint foreground, string windowsDirectory)
        {
            if (info == null || !ValidIdentity(info.Identity)) return "进程身份无法核实";
            ProcessIdentity identity = info.Identity;
            if (identity.Id == ownId) return "不能关闭修仙程序自身";
            if (foreground == 0) return "无法核实当前前台程序";
            if (identity.Id == foreground) return "目标已处于前台，请保留当前使用中的程序";
            if (info.SessionId != ownSession || ownSession < 0) return "不属于当前会话";
            if (String.IsNullOrEmpty(userSid) || String.IsNullOrEmpty(info.UserSid) || !String.Equals(info.UserSid, userSid, StringComparison.Ordinal)) return "进程所有者未知或不属于当前用户";
            if (!info.CriticalKnown || info.Critical) return "关键进程状态未知或属于系统关键进程";
            if (String.IsNullOrWhiteSpace(windowsDirectory)) return "无法核实 Windows 目录";
            string windowsRoot = Path.GetFullPath(windowsDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (String.Equals(identity.ExecutablePath, windowsRoot, StringComparison.OrdinalIgnoreCase)
                || identity.ExecutablePath.StartsWith(windowsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return "不处理 Windows 系统目录中的程序";
            string name = Path.GetFileNameWithoutExtension(identity.ExecutablePath);
            string declared = identity.Name;
            foreach (string system in new string[] { "System", "Idle", "Registry", "smss", "csrss", "wininit", "services", "lsass", "winlogon", "svchost", "dwm", "fontdrvhost", "sihost", "conhost", "explorer", "SecurityHealthService", "MsMpEng" })
                if (String.Equals(name, system, StringComparison.OrdinalIgnoreCase) || String.Equals(declared, system, StringComparison.OrdinalIgnoreCase)) return "不处理系统或桌面关键程序";
            if (info.Window == IntPtr.Zero || !info.HasCloseEntry) return "没有可核实的正常退出窗口";
            return null;
        }
    }

    internal static class DesktopProcessNative
    {
        internal const uint QueryAndWait = 0x1000 | 0x100000;
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint GetProcessId(IntPtr process);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ProcessIdToSessionId(uint process, out uint session);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int length);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool IsProcessCritical(IntPtr process, out bool critical);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] internal static extern bool GetTokenInformation(IntPtr token, int type, IntPtr information, int capacity, out int required);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] internal static extern IntPtr GetSystemMenu(IntPtr window, bool revert);
        [DllImport("user32.dll")] internal static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    }
}
