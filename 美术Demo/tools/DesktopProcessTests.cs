using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;

namespace NetBunny
{
    internal static class DesktopProcessTests
    {
        static int checks, failures;
        static readonly BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static Type identityType, targetsType, actionsType;
        static void Check(bool pass, string name) { checks++; Console.WriteLine((pass ? "PASS " : "FAIL ") + name); if (!pass) failures++; }
        static object Get(object value, string name) { return value.GetType().GetField(name, flags).GetValue(value); }
        static void Set(object value, string name, object data) { value.GetType().GetField(name, flags).SetValue(value, data); }
        static object Identity(Process process)
        {
            object id = Activator.CreateInstance(identityType, true);
            Set(id, "Id", process.Id); Set(id, "StartedUtcTicks", process.StartTime.ToUniversalTime().Ticks);
            Set(id, "ExecutablePath", process.MainModule.FileName); Set(id, "Name", process.ProcessName); Set(id, "WorkingSetBytes", process.WorkingSet64);
            return id;
        }
        static object Close(object expected, CancellationToken token) { return actionsType.GetMethod("RequestGracefulClose", flags).Invoke(null, new object[] { expected, token }); }
        static string Status(object result) { return Get(result, "Status").ToString(); }
        static string Summary(object result) { return (string)result.GetType().GetProperty("Summary", flags).GetValue(result, null); }
        static Process Start(string fixture, string root, string mode, out string marker)
        {
            marker = Path.Combine(root, mode + "-" + Guid.NewGuid().ToString("N"));
            Process child = Process.Start(new ProcessStartInfo(fixture, "\"" + marker + "\" " + mode) { UseShellExecute = false, CreateNoWindow = false });
            Stopwatch wait = Stopwatch.StartNew();
            while (!File.Exists(marker + ".ready") && !child.HasExited && wait.ElapsedMilliseconds < 5000) Thread.Sleep(30);
            if (!File.Exists(marker + ".ready")) throw new Exception("Dedicated process fixture did not become ready.");
            return child;
        }
        static void Stop(Process child, string marker)
        {
            if (!child.HasExited) { File.WriteAllText(marker + ".stop", "normal fixture cleanup"); if (!child.WaitForExit(4000)) throw new Exception("Fixture did not stop normally; PID " + child.Id); }
            child.Dispose();
        }
        static bool ContainsProcess(int pid)
        {
            Array candidates = (Array)targetsType.GetMethod("Observe", flags).Invoke(null, null);
            foreach (object candidate in candidates) if ((int)Get(candidate, "Id") == pid) return true;
            return false;
        }
        static void PolicyTests()
        {
            Type inspectionType = typeof(DesktopProcessTests).Assembly.GetType("NetBunny.ProcessInspection");
            MethodInfo refusal = targetsType.GetMethod("RefuseReason", flags);
            object inspection = Activator.CreateInstance(inspectionType, true);
            object identity = Activator.CreateInstance(identityType, true);
            Set(identity, "Id", 200); Set(identity, "StartedUtcTicks", 638000000000000000L); Set(identity, "ExecutablePath", @"C:\Apps\fixture.exe"); Set(identity, "Name", "fixture");
            Set(inspection, "Identity", identity); Set(inspection, "SessionId", 3); Set(inspection, "UserSid", "S-1-5-21-test");
            Set(inspection, "CriticalKnown", true); Set(inspection, "Critical", false); Set(inspection, "Window", new IntPtr(100)); Set(inspection, "HasCloseEntry", true);
            Func<string> reason = delegate { return (string)refusal.Invoke(null, new object[] { inspection, 100, 3, "S-1-5-21-test", (uint)300, @"C:\Windows" }); };
            Check(reason() == null, "verified same-user background window is eligible");
            Set(inspection, "UserSid", null); Check(reason() != null, "unknown owner fails closed");
            Set(inspection, "UserSid", "S-other"); Check(reason() != null, "different user is denied");
            Set(inspection, "UserSid", "S-1-5-21-test"); Set(inspection, "SessionId", 4); Check(reason() != null, "different session is denied");
            Set(inspection, "SessionId", 3); Set(inspection, "CriticalKnown", false); Check(reason() != null, "failed critical-process query is denied");
            Set(inspection, "CriticalKnown", true); Set(inspection, "Critical", true); Check(reason() != null, "critical process is denied");
            Set(inspection, "Critical", false); Set(identity, "Id", 100); Check(reason() != null, "own process is denied");
            Set(identity, "Id", 300); Check(reason() != null, "new foreground process is denied");
            Set(identity, "Id", 200); Set(identity, "ExecutablePath", @"C:\Windows\System32\app.exe"); Check(reason() != null, "Windows directory process is denied");
            Set(identity, "ExecutablePath", @"C:\Apps\fixture.exe"); Set(identity, "Name", "csrss"); Check(reason() != null, "known system process is denied even outside Windows path");
            Set(identity, "Name", "fixture"); Set(inspection, "HasCloseEntry", false); Check(reason() != null, "missing normal close entry is denied");
            Set(inspection, "HasCloseEntry", true); Set(identity, "StartedUtcTicks", 0L); Check(reason() != null, "unknown start identity is denied");
        }
        [STAThread] static int Main(string[] args)
        {
            try
            {
                identityType = typeof(DesktopProcessTests).Assembly.GetType("NetBunny.ProcessIdentity");
                targetsType = typeof(DesktopProcessTests).Assembly.GetType("NetBunny.DesktopProcessTargets");
                actionsType = typeof(DesktopProcessTests).Assembly.GetType("NetBunny.DesktopProcessActions");
                Check(identityType != null && targetsType != null && actionsType != null, "safe process observation and normal-close backend exists");
                if (failures > 0) return 1;
                PolicyTests();
                string fixture = Path.GetFullPath(args[0]), root = Path.GetFullPath(args[1]); Directory.CreateDirectory(root);
                using (Process self = Process.GetCurrentProcess())
                {
                    Check(!ContainsProcess(self.Id), "observer excludes its own process");
                    Check(Status(Close(Identity(self), CancellationToken.None)) == "Denied" && !self.HasExited, "native execution rejects its own process without closing it");
                }
                object nullResult = Close(null, CancellationToken.None); Check(Status(nullResult) == "Denied" && Summary(nullResult).Length > 0, "missing identity is refused with explanation");
                string marker;
                Process child = Start(fixture, root, "close", out marker);
                try
                {
                    object identity = Identity(child);
                    Check(ContainsProcess(child.Id), "native observer includes only our verified background fixture");
                    long ticks = (long)Get(identity, "StartedUtcTicks"); Set(identity, "StartedUtcTicks", ticks + 1);
                    Check(Status(Close(identity, CancellationToken.None)) == "IdentityChanged" && !child.HasExited, "changed start time (including PID reuse) cannot close a replacement");
                    Set(identity, "StartedUtcTicks", ticks); string path = (string)Get(identity, "ExecutablePath"); Set(identity, "ExecutablePath", path + ".changed");
                    Check(Status(Close(identity, CancellationToken.None)) == "IdentityChanged" && !child.HasExited, "changed executable path cannot close target"); Set(identity, "ExecutablePath", path);
                    using (CancellationTokenSource cancelled = new CancellationTokenSource()) { cancelled.Cancel(); Check(Status(Close(identity, cancelled.Token)) == "Cancelled" && !child.HasExited, "revoked request sends no close message"); }
                    object result = Close(identity, CancellationToken.None);
                    Check(Status(result) == "Exited" && child.WaitForExit(1000), "normal-close request reports the actual fixture exit");
                    Check(Status(Close(identity, CancellationToken.None)) == "Exited", "already exited target is handled without another request");
                }
                finally { Stop(child, marker); }
                child = Start(fixture, root, "cancel", out marker);
                try
                {
                    Stopwatch elapsed = Stopwatch.StartNew(); object result = Close(Identity(child), CancellationToken.None);
                    Check(Status(result) == "StillRunning" && !child.HasExited && File.Exists(marker + ".requested") && elapsed.ElapsedMilliseconds >= 4500 && elapsed.ElapsedMilliseconds < 6200,
                        "cancelled save stays running and bounded wait reports timeout honestly");
                    using (CancellationTokenSource cancelled = new CancellationTokenSource())
                    {
                        cancelled.CancelAfter(200); elapsed.Restart(); result = Close(Identity(child), cancelled.Token);
                        Check(Status(result) == "Cancelled" && !child.HasExited && elapsed.ElapsedMilliseconds < 1200, "waiting for a refusing fixture can be cancelled promptly");
                    }
                }
                finally { Stop(child, marker); }
                child = Start(fixture, root, "prompt", out marker);
                try
                {
                    using (CancellationTokenSource cancelled = new CancellationTokenSource())
                    {
                        cancelled.CancelAfter(450); object result = Close(Identity(child), cancelled.Token);
                        Check(Status(result) == "Cancelled" && !child.HasExited && File.Exists(marker + ".prompt"), "open save prompt is never forced closed");
                    }
                }
                finally { Stop(child, marker); }
                Console.WriteLine("Process backend: " + checks + " checks, " + failures + " failures. Only dedicated fixtures received normal-close requests.");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
