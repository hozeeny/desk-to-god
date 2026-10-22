using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    // Short lifecycle regression only. These cases never claim long-soak
    // completion, click the desktop, seed a profile or enable maintenance.
    internal static class DesktopNaturalSoakExitTests
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        static int checks, failures;

        static void Check(bool pass, string name)
        {
            checks++; if (!pass) failures++;
            Console.WriteLine((pass ? "PASS " : "FAIL ") + name);
        }
        static Exception Unwrap(Exception error)
        { return error is TargetInvocationException && error.InnerException != null ? error.InnerException : error; }
        static object Invoke(object value, string name, params object[] args)
        { return value.GetType().GetMethod(name, Private).Invoke(value, args); }
        static Dictionary<string, object> ReadReport(string output)
        {
            string terminal = Path.Combine(output, "natural-soak-terminal-failure.json");
            return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(File.Exists(terminal) ? terminal : Path.Combine(output, "natural-soak.json"), Encoding.UTF8));
        }
        static Dictionary<string, string> RealHashes()
        { return (Dictionary<string, string>)typeof(DesktopNaturalSoak).GetMethod("HashRealArchives", Private).Invoke(null, null); }
        static bool SameHashes(Dictionary<string, string> a, Dictionary<string, string> b)
        { return a.Count == b.Count && a.All(p => b.ContainsKey(p.Key) && b[p.Key] == p.Value); }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                if (args.Length == 5 && args[0] == "--case") return RunCase(args[1], args[2], args[3], args[4]);
                if (args.Length != 3) throw new ArgumentException("Usage: DesktopNaturalSoakExitTests assetsRoot artRoot newOutputRoot");
                string output = Path.GetFullPath(args[2]);
                if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Regression output must be new/empty; existing evidence will not be overwritten.");
                Directory.CreateDirectory(output);
                var results = new List<object>();
                Stopwatch total = Stopwatch.StartNew();
                foreach (string name in new[] { "message-loop-exit", "disposed-context-exit", "locked-diagnostic-log", "locked-canonical-report" })
                {
                    string caseRoot = Path.Combine(output, name);
                    var info = new ProcessStartInfo(Application.ExecutablePath,
                        "--case " + Quote(name) + " " + Quote(Path.GetFullPath(args[0])) + " " + Quote(Path.GetFullPath(args[1])) + " " + Quote(caseRoot))
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (Process child = Process.Start(info))
                    {
                        // This handle belongs to the precise child just launched.
                        // A hung regression must never stop another game/process.
                        IntPtr ownChild = child.Handle;
                        int remaining = Math.Max(1, Math.Min(12000, 44500 - (int)total.ElapsedMilliseconds));
                        bool completed = child.WaitForExit(remaining);
                        if (!completed) { child.Kill(); child.WaitForExit(); }
                        string standard = child.StandardOutput.ReadToEnd(), error = child.StandardError.ReadToEnd();
                        File.WriteAllText(Path.Combine(output, name + ".log"), standard + error, new UTF8Encoding(true));
                        Console.Write(standard); Console.Error.Write(error);
                        Check(completed && child.ExitCode == 0, name + " short lifecycle child completes with truthful final report");
                        results.Add(new { name = name, completed = completed, exitCode = child.ExitCode, processId = child.Id });
                    }
                }
                Check(total.Elapsed.TotalSeconds < 45, "all short lifecycle regressions finish within 45 seconds (each child at most 12 seconds)");
                File.WriteAllText(Path.Combine(output, "exit-regression.json"), Json.Serialize(new { passed = failures == 0, checks = checks, failures = failures, elapsedSeconds = total.Elapsed.TotalSeconds, cases = results, scope = "short lifecycle regression only; not an eight-hour test; no mouse, seeded saves or user process actions" }), new UTF8Encoding(true));
                Console.WriteLine("Natural soak exit regression: " + checks + " checks, " + failures + " failures; " + output);
                return failures == 0 ? 0 : 1;
            }
            catch (Exception error) { Console.Error.WriteLine(Unwrap(error)); return 2; }
        }
        static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0 || value.EndsWith("\\", StringComparison.Ordinal)) throw new ArgumentException("Unsupported child argument quoting.");
            return "\"" + value + "\"";
        }
        static int RunCase(string name, string assets, string art, string root)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Environment.SetEnvironmentVariable(ArtDemoPack.EnvironmentVariable, Path.GetFullPath(art));
            Directory.CreateDirectory(root);
            string evidence = Path.Combine(root, "evidence"), profile = Path.Combine(root, "profiles", "verify-exit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence);
            Dictionary<string, string> original = RealHashes();
            DesktopDemoSession host = null; DesktopNaturalSoak soak = null;
            FileStream lockedDiagnostic = null, lockedCanonical = null;
            Exception callbackError = null;
            bool loopReturned = false, actualDisposed = false;
            double beforeElapsed = 0;
            try
            {
                host = new DesktopDemoSession(Path.GetFullPath(assets), profile, true, null, false);
                Check(host.Game.IsolatedDesktop && !host.Game.DesktopObservesSystem && host.Game.DesktopAcceptanceMaintenanceOff(), name + ": fresh private Virtual session has all maintenance disabled");
                Check(host.Game.Cultivation.Mortal && host.Game.Cultivation.MortalKills == 0, name + ": fresh normal mortal state; no cultivation/time seed or injection");
                soak = (DesktopNaturalSoak)Activator.CreateInstance(typeof(DesktopNaturalSoak), Private, null,
                    new object[] { host, evidence, 8 * 3600.0, original, Path.GetFullPath(assets), Path.GetFullPath(art) }, null);
                Invoke(soak, "Start");
                Dictionary<string, object> before = ReadReport(evidence);
                beforeElapsed = Convert.ToDouble(before["elapsedSeconds"]);
                Check(Convert.ToBoolean(before["running"]) && !Convert.ToBoolean(before["passed"]), name + ": production Start writes running=true, passed=false checkpoint");
                string canonicalBefore = File.ReadAllText(Path.Combine(evidence, "natural-soak.json"), Encoding.UTF8);
                if (name == "locked-diagnostic-log")
                {
                    lockedDiagnostic = new FileStream(Path.Combine(evidence, "natural-soak.log"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                    using (var writableReport = new FileStream(Path.Combine(evidence, "natural-soak.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                        Check(lockedDiagnostic.CanRead && writableReport.CanWrite, name + ": diagnostic log is exclusively locked while final JSON remains writable");
                }
                if (name == "locked-canonical-report")
                {
                    lockedCanonical = new FileStream(Path.Combine(evidence, "natural-soak.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
                    Check(lockedCanonical.CanRead && !lockedCanonical.CanWrite, name + ": canonical checkpoint is readable but replacement/delete sharing is denied");
                }
                if (name == "message-loop-exit")
                {
                    using (var close = new System.Windows.Forms.Timer { Interval = 300 })
                    {
                        close.Tick += delegate { close.Stop(); host.ExitAfterAcceptance(1); };
                        close.Start();
                        try { Application.Run(host); loopReturned = true; }
                        catch (Exception error) { callbackError = Unwrap(error); }
                    }
                    actualDisposed = host.Game == null;
                    Check(actualDisposed, name + ": real WinForms message-loop shutdown disposed session and cleared Game");
                    Check(loopReturned && callbackError == null, name + ": real ApplicationExit callback does not throw");
                }
                else if (name == "disposed-context-exit" || name == "locked-diagnostic-log" || name == "locked-canonical-report")
                {
                    host.Dispose(); actualDisposed = host.Game == null;
                    Check(actualDisposed, name + ": explicit context Dispose cleared Game before soak exit handler");
                    try { Invoke(soak, "OnExit", null, EventArgs.Empty); }
                    catch (Exception error) { callbackError = Unwrap(error); }
                    Check(callbackError == null, name + ": exit handler tolerates already-disposed session");
                }
                else throw new ArgumentException("Unknown case: " + name);
                Dictionary<string, object> after = ReadReport(evidence);
                Check(!Convert.ToBoolean(after["running"]), name + ": final report clears running even when Game is null");
                Check(!Convert.ToBoolean(after["passed"]), name + ": premature exit cannot claim eight-hour success");
                string reasons = Json.Serialize(after["reasons"]);
                Check(reasons.Contains("退出") || reasons.Contains("disposed") || reasons.Contains("Dispose"), name + ": final failure explains premature exit/disposed session");
                Check(Convert.ToDouble(after["elapsedSeconds"]) >= beforeElapsed && (string)after["updatedUtc"] != (string)before["updatedUtc"], name + ": final checkpoint replaces stale running snapshot");
                Check((string)after["profile"] == host.ProfileRoot, name + ": final report retains private-profile provenance");
                Check(!Convert.ToBoolean(after["loadVerified"]), name + ": unavailable controller is not reported as verified Load");
                if (name == "locked-canonical-report")
                {
                    Check(File.Exists(Path.Combine(evidence, "natural-soak-terminal-failure.json")), name + ": independent terminal failure report is persisted when canonical replacement is blocked");
                    Check(File.ReadAllText(Path.Combine(evidence, "natural-soak.json"), Encoding.UTF8) == canonicalBefore, name + ": locked canonical report preserves the exact last successful checkpoint");
                }
                if (name == "locked-diagnostic-log" || name == "locked-canonical-report")
                {
                    Exception repeatedError = null;
                    try { Invoke(soak, "OnExit", null, EventArgs.Empty); host.Dispose(); host.Dispose(); }
                    catch (Exception error) { repeatedError = Unwrap(error); }
                    Check(repeatedError == null, name + ": repeated OnExit and context Dispose do not throw");
                    Dictionary<string, object> repeated = ReadReport(evidence);
                    Check(!Convert.ToBoolean(repeated["running"]) && !Convert.ToBoolean(repeated["passed"]), name + ": repeated exit/dispose retain terminal failure while diagnostic/report lock remains held");
                }
                Check(SameHashes(original, RealHashes()), name + ": real archives and consent hashes unchanged");
                File.WriteAllText(Path.Combine(root, "case-diagnostics.json"), Json.Serialize(new { name = name, actualDisposed = actualDisposed, loopReturned = loopReturned, callbackError = callbackError == null ? null : callbackError.ToString(), checks = checks, failures = failures, profile = profile, restriction = "new private profile, Virtual, all maintenance disabled; short lifecycle only" }), new UTF8Encoding(true));
                if (callbackError != null) Console.WriteLine("Observed callback error: " + callbackError);
            }
            finally
            {
                if (lockedDiagnostic != null) lockedDiagnostic.Dispose();
                if (lockedCanonical != null) lockedCanonical.Dispose();
                if (soak != null) soak.Dispose();
                if (host != null) host.Dispose();
            }
            Console.WriteLine(name + ": " + checks + " checks, " + failures + " failures");
            return failures == 0 ? 0 : 1;
        }
    }
}
