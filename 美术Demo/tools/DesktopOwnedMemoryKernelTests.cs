using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace NetBunny
{
    // Opt-in native API evidence, restricted to one hidden child created here.
    // This is neither a game/controller fixture nor proof of the boss UI flow.
    internal static class DesktopOwnedMemoryKernelTests
    {
        const int Bytes = 96 * 1024 * 1024, Page = 4096, ParentBudgetMs = 28000;
        const uint StillActive = 259;
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr process, out uint code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr process, uint code);
        [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        sealed class OwnedChild
        {
            internal Process Original; // Retained until parent exit; worker wrappers are separate.
            internal IntPtr Handle;
            internal int Id;
            internal long StartTicks;
            internal string Path, Nonce;
        }

        static readonly Stopwatch wall = Stopwatch.StartNew();
        static readonly List<Dictionary<string, object>> assertions = new List<Dictionary<string, object>>();
        static readonly Dictionary<string, object> report = new Dictionary<string, object>();
        static OwnedChild child;
        static string output;
        static int finished, actualCalls, callbacks;
        static bool normalStop, forcedStop;

        static string Hash(string path)
        { if (!File.Exists(path)) return "missing"; using (SHA256 sha = SHA256.Create()) using (Stream input = File.OpenRead(path)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", ""); }
        static string BlockHash(byte[] block)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(block)).Replace("-", ""); }
        static Dictionary<string, string> Archives()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            return new[] { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt", "maintenance-consent.txt", "maintenance-consent.txt.revoked" }
                .ToDictionary(name => name, name => Hash(Path.Combine(root, name)));
        }
        static object Memory(MemorySnapshot snapshot)
        { return snapshot == null ? null : new { available = snapshot.Available, usedPercent = snapshot.UsedPercent, totalBytes = snapshot.TotalBytes, usedBytes = snapshot.UsedBytes, availableBytes = snapshot.AvailableBytes }; }
        static object Outcome(MemoryReclaimResult result)
        {
            return result == null ? null : new { trimmed = result.Trimmed, skipped = result.Skipped, unobserved = result.Unobserved, cancelled = result.Cancelled,
                timedOut = result.TimedOut, processLimited = result.ProcessLimited, workingSetReduction = result.WorkingSetReduction, error = result.Error,
                summary = result.Summary, systemBefore = Memory(result.Before), systemAfter = Memory(result.After) };
        }
        static void Check(bool okay, string detail)
        {
            assertions.Add(new Dictionary<string, object> { { "passed", okay }, { "detail", detail }, { "elapsedSeconds", wall.Elapsed.TotalSeconds } });
            Console.WriteLine((okay ? "PASS " : "FAIL ") + detail); Console.Out.Flush();
            if (!okay) throw new InvalidOperationException(detail);
        }
        static int Remaining(int maximum)
        {
            int remaining = ParentBudgetMs - (int)wall.ElapsedMilliseconds;
            if (remaining <= 0) throw new TimeoutException("Owned-child parent deadline reached.");
            return Math.Min(maximum, remaining);
        }
        static bool Alive(OwnedChild owned)
        { uint code; return owned != null && GetExitCodeProcess(owned.Handle, out code) && code == StillActive; }
        static string ReadLine(int maximum)
        {
            Task<string> reading = child.Original.StandardOutput.ReadLineAsync();
            if (!reading.Wait(Remaining(maximum))) throw new TimeoutException("Owned-child protocol response timed out.");
            string line = reading.GetAwaiter().GetResult();
            if (line == null) throw new EndOfStreamException("Owned child exited before its protocol response.");
            return line;
        }
        static void Send(string command)
        { child.Original.StandardInput.WriteLine(command + " " + child.Nonce); child.Original.StandardInput.Flush(); }
        static Dictionary<string, object> Identity(Process candidate)
        {
            candidate.Refresh();
            int id = candidate.Id; long ticks = candidate.StartTime.ToUniversalTime().Ticks;
            string path = System.IO.Path.GetFullPath(candidate.MainModule.FileName);
            bool match = Alive(child) && id == child.Id && ticks == child.StartTicks && String.Equals(path, child.Path, StringComparison.OrdinalIgnoreCase);
            return new Dictionary<string, object> { { "matched", match }, { "pid", id }, { "startUtcTicks", ticks }, { "path", path },
                { "retainedOriginalHandle", "0x" + child.Handle.ToInt64().ToString("X") }, { "executableSHA256", Hash(path) } };
        }
        static bool SameIdentity(Dictionary<string, object> identity)
        { return (bool)identity["matched"] && (string)identity["executableSHA256"] == (string)report["executableSHA256"]; }
        static void WriteReport(string name, object value)
        { File.WriteAllText(Path.Combine(output, name), new JavaScriptSerializer().Serialize(value), new UTF8Encoding(true)); }

        static int Child(string nonce)
        {
            // Independent exit deadline also bounds an orphan if its parent dies.
            using (System.Threading.Timer expiry = new System.Threading.Timer(delegate { Environment.Exit(124); }, null, 22000, Timeout.Infinite))
            {
                byte[] block = new byte[Bytes];
                for (int page = 0; page < Bytes / Page; page++)
                    for (int offset = 0; offset < Page; offset++) block[page * Page + offset] = (byte)(page * 37 + offset * 13 + 19);
                int own; using (Process self = Process.GetCurrentProcess()) own = self.Id;
                string originalHash = BlockHash(block);
                Console.WriteLine("READY|" + nonce + "|" + own + "|" + Bytes + "|" + (Bytes / Page) + "|" + originalHash); Console.Out.Flush();
                while (true)
                {
                    string command = Console.ReadLine();
                    if (command == "STOP " + nonce) { Console.WriteLine("STOPPED|" + nonce + "|" + own); Console.Out.Flush(); GC.KeepAlive(block); return 0; }
                    if (command != "VERIFY " + nonce) { GC.KeepAlive(block); return 3; }
                    int verified = 0;
                    for (int page = 0; page < Bytes / Page; page++)
                    {
                        for (int offset = 0; offset < Page; offset++)
                            if (block[page * Page + offset] != (byte)(page * 37 + offset * 13 + 19)) { Console.WriteLine("CORRUPT|" + page + "|" + offset); Console.Out.Flush(); return 4; }
                        verified++;
                    }
                    Console.WriteLine("VERIFIED|" + nonce + "|" + own + "|" + verified + "|" + BlockHash(block)); Console.Out.Flush(); GC.KeepAlive(block);
                }
            }
        }

        static void StopOwnedChild()
        {
            if (child == null) return;
            if (Alive(child))
            {
                try
                {
                    Send("STOP");
                    string stopped = ReadLine(2500);
                    bool acknowledged = stopped == "STOPPED|" + child.Nonce + "|" + child.Id;
                    uint waited = WaitForSingleObject(child.Handle, (uint)Remaining(2500)); uint code;
                    normalStop = acknowledged && waited == 0 && GetExitCodeProcess(child.Handle, out code) && code == 0;
                }
                catch (Exception error) { report["stopError"] = error.ToString(); }
            }
            if (Alive(child))
            {
                // Never look up a process to kill. This is the original retained
                // handle obtained only from Process.Start in this invocation.
                forcedStop = TerminateProcess(child.Handle, 125);
                WaitForSingleObject(child.Handle, 1000);
            }
            uint exit; report["childExited"] = GetExitCodeProcess(child.Handle, out exit) && exit != StillActive;
            report["childExitCode"] = exit; report["normalStop"] = normalStop; report["forcedStop"] = forcedStop;
        }

        static int Run(string directory)
        {
            output = System.IO.Path.GetFullPath(directory); Directory.CreateDirectory(output);
            string executable = System.IO.Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            report["scope"] = "Opt-in real Windows working-set API; only this invocation's hidden 96 MiB child. No GUI, process enumeration, controller, boss gameplay or long-soak claim.";
            report["executableSHA256"] = Hash(executable); report["actualAPIcalled"] = false; report["passed"] = false;
            report["parentBudgetMilliseconds"] = ParentBudgetMs; report["childBudgetMilliseconds"] = 22000;
            report["archivesBefore"] = Archives(); report["systemBefore"] = Memory(CleanupService.ReadMemory());
            int exitCode = 1;
            using (System.Threading.Timer deadline = new System.Threading.Timer(delegate
            {
                if (Volatile.Read(ref finished) != 0) return;
                OwnedChild owned = child; bool terminated = owned != null && Alive(owned) && TerminateProcess(owned.Handle, 124);
                try { WriteReport("deadline-failure.json", new { passed = false, deadlineTriggered = true, actualAPIcalled = Volatile.Read(ref actualCalls) > 0,
                    callbackCount = Volatile.Read(ref callbacks), onlyRetainedOwnedChildTerminated = terminated, ownedPid = owned == null ? 0 : owned.Id, elapsedSeconds = wall.Elapsed.TotalSeconds }); }
                finally { Environment.Exit(124); }
            }, null, ParentBudgetMs, Timeout.Infinite))
            {
                try
                {
                    string nonce = Guid.NewGuid().ToString("N");
                    Process original = Process.Start(new ProcessStartInfo(executable, "--owned-memory-child " + nonce) {
                        UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardInput = true, RedirectStandardOutput = true });
                    child = new OwnedChild { Original = original, Handle = original.Handle, Id = original.Id, StartTicks = original.StartTime.ToUniversalTime().Ticks,
                        Path = System.IO.Path.GetFullPath(original.MainModule.FileName), Nonce = nonce };
                    report["calleeownidentity"] = Identity(original);
                    Check(SameIdentity((Dictionary<string, object>)report["calleeownidentity"]), "retained child PID, executable path, start time, original handle and SHA identify this invocation's child");
                    string ready = ReadLine(7000); report["ready"] = ready; string[] readyParts = ready.Split('|');
                    Check(readyParts.Length == 6 && readyParts[0] == "READY" && readyParts[1] == nonce && readyParts[2] == child.Id.ToString()
                        && readyParts[3] == Bytes.ToString() && readyParts[4] == (Bytes / Page).ToString() && readyParts[5].Length == 64,
                        "child READY follows actual allocation and touch of all 24576 pages in 96 MiB");
                    using (CancellationTokenSource stop = new CancellationTokenSource())
                    {
                        stop.Cancel(); int preCallbacks = 0;
                        MemoryReclaimResult cancelled = MemoryReclaimer.Reclaim(stop.Token, new[] { Process.GetProcessById(child.Id), Process.GetProcessById(child.Id) },
                            delegate(Process ignored, CancellationToken token) { preCallbacks++; throw new InvalidOperationException("Pre-cancel must never invoke the trim callback."); });
                        report["preCancelled"] = Outcome(cancelled); report["preCancelledCallbackCount"] = preCallbacks;
                        Check(preCallbacks == 0 && cancelled.Cancelled && cancelled.Trimmed == 0 && cancelled.WorkingSetReduction == 0,
                            "pre-cancel invokes no callback and no working-set API");
                    }
                    MemoryTrimObservation actual = null;
                    using (CancellationTokenSource stop = new CancellationTokenSource())
                    {
                        MemoryReclaimResult partial = MemoryReclaimer.Reclaim(stop.Token, new[] { Process.GetProcessById(child.Id), Process.GetProcessById(child.Id) }, delegate(Process candidate, CancellationToken token)
                        {
                            callbacks++;
                            try
                            {
                                Dictionary<string, object> identity = Identity(candidate); report["callbackIdentity"] = identity;
                                if (!SameIdentity(identity) || !Alive(child)) throw new InvalidOperationException("Owned-child identity changed before the native API call.");
                                actualCalls++; actual = MemoryReclaimer.TrimWorkingSet(candidate, token);
                                return actual;
                            }
                            finally { stop.Cancel(); } // Failed/throwing native calls must never be retried on the second wrapper.
                        });
                        report["result"] = Outcome(partial); report["actualAPIcalled"] = actualCalls > 0;
                        report["actualTrimObservation"] = actual == null ? null : new { applied = actual.Applied, observed = actual.Observed, before = actual.Before, after = actual.After };
                        report["callbackCount"] = callbacks; report["actualAPICallCount"] = actualCalls;
                        Check(actualCalls == 1 && callbacks == 1 && actual != null && actual.Applied, "the exact-owned callback calls production TrimWorkingSet once; later candidates receive no callback");
                        Check(actual.Observed && actual.Before > actual.After && actual.Before >= Bytes && partial.Unobserved == 0
                            && partial.WorkingSetReduction == actual.Before - actual.After, "actual kernel working-set samples show a positive reduction without inventing unobserved bytes");
                        Check(partial.Trimmed == 1 && partial.Cancelled && !partial.TimedOut && !partial.ProcessLimited && String.IsNullOrEmpty(partial.Error)
                            && partial.Summary.Contains("已取消") && partial.Summary.Contains("实际完成量"), "one completed native trim remains counted and explicitly partial-cancelled");
                    }
                    Check(Alive(child) && SameIdentity(Identity(original)), "child remains alive with the same exact identity after working-set trim");
                    Send("VERIFY"); string verification = ReadLine(7000); report["verify"] = verification;
                    Check(verification == "VERIFIED|" + nonce + "|" + child.Id + "|" + (Bytes / Page) + "|" + readyParts[5],
                        "child re-reads every byte of every page and retains its original content SHA after the kernel trim");
                    StopOwnedChild();
                    Check(normalStop && !forcedStop && (bool)report["childExited"] && (uint)report["childExitCode"] == 0, "the same retained child acknowledges STOP and exits normally with code zero");
                    exitCode = 0;
                }
                catch (Exception error) { report["error"] = error.ToString(); Console.Error.WriteLine(error); }
                finally
                {
                    if (child != null && Alive(child)) StopOwnedChild();
                    try
                    {
                        Dictionary<string, string> after = Archives(); var before = (Dictionary<string, string>)report["archivesBefore"];
                        report["archivesAfter"] = after; bool unchanged = before.All(pair => after[pair.Key] == pair.Value); report["archivesUnchanged"] = unchanged;
                        if (!unchanged) { exitCode = 1; report["archiveFailure"] = "Real archive hash changed during this invocation; no write is performed by this tool."; }
                        report["systemAfter"] = Memory(CleanupService.ReadMemory());
                        report["actualAPIcalled"] = actualCalls > 0; report["actualAPICallCount"] = actualCalls; report["callbackCount"] = callbacks;
                        report["passed"] = exitCode == 0; report["assertions"] = assertions; report["elapsedSeconds"] = wall.Elapsed.TotalSeconds;
                        WriteReport("owned-memory-kernel.json", report);
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); exitCode = 1; }
                    Volatile.Write(ref finished, 1);
                }
            }
            return exitCode;
        }

        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--owned-memory-child") return Child(args[1]);
            if (args.Length == 2 && args[0] == "--run-kernel") return Run(args[1]);
            Console.Error.WriteLine("Build-only by default. Opt-in parent invocation: --run-kernel <new evidence directory>."); return 2;
        }
    }
}
