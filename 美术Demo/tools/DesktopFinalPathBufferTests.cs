using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace NetBunny
{
    internal static class DesktopFinalPathBufferTests
    {
        const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int checks, failures;
        static MethodInfo read;
        static readonly List<string> facts = new List<string>();
        static void Check(bool okay, string text) { checks++; Console.WriteLine((okay ? "PASS " : "FAIL ") + text); if (!okay) failures++; }
        static string Read(Func<StringBuilder, uint> callback)
        {
            try { return (string)read.Invoke(null, new object[] { callback }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        static uint Fill(StringBuilder buffer, string value) { buffer.Clear(); buffer.Append(value); return (uint)value.Length; }
        static string PathOfLength(int count) { return @"\\?\C:\" + new string('a', count - 7); }
        static void Refused(string label, Func<StringBuilder, uint> callback)
        {
            bool rejected = false; try { Read(callback); } catch (IOException) { rejected = true; }
            Check(rejected, label + " fails closed with IOException");
        }
        static void Boundaries()
        {
            int calls = 0; var capacities = new List<int>(); string small = @"\\?\C:\owned\short.bin";
            string actual = Read(buffer => { calls++; capacities.Add(buffer.Capacity); return Fill(buffer, small); });
            Check(actual == small && calls == 1 && capacities[0] <= 512, "ordinary short path uses one small buffer call, preserving raw native text");
            foreach (int length in new[] { 259, 260, 511, 4096, 32767 })
            {
                calls = 0; capacities.Clear(); string path = PathOfLength(length);
                actual = Read(buffer => { calls++; capacities.Add(buffer.Capacity); return calls == 1 && length >= buffer.Capacity ? (uint)length + 1 : Fill(buffer, path); });
                Check(actual == path && calls == (length < 260 ? 1 : 2), "native success length " + length + " excludes its terminating null");
                Check(capacities[0] == 260 && (calls == 1 || capacities[1] == length + 1), "required length including null is used exactly, without off-by-one, for " + length);
            }
            calls = 0;
            actual = Read(buffer => { calls++; return calls == 1 ? 700U : Fill(buffer, PathOfLength(400)); });
            Check(calls == 2 && actual.Length == 400, "a shorter valid result on retry is accepted only from the actual second buffer");
            Refused("zero initial result", buffer => 0);
            calls = 0; Refused("zero retry result", buffer => ++calls == 1 ? 500U : 0U); Check(calls == 2, "zero retry never causes a third read");
            calls = 0; Refused("required capacity above original 32768 limit", buffer => { calls++; return 32769; }); Check(calls == 1, "oversized first response cannot allocate a larger buffer");
            calls = 0; Refused("required capacity uint overflow", buffer => { calls++; return UInt32.MaxValue; }); Check(calls == 1, "uint size is bounded before conversion to int");
            calls = 0; Refused("path grows beyond retry capacity", buffer => ++calls == 1 ? 500U : 600U); Check(calls == 2, "changing oversized result is bounded to two native calls");
            calls = 0; Refused("retry result exactly at 32768 capacity", buffer => ++calls == 1 ? 32768U : 32768U); Check(calls == 2, "32768 actual characters cannot hide a missing terminator");
            calls = 0; Refused("retry result greater than 32768", buffer => ++calls == 1 ? 32768U : 32769U);
            Refused("reported success disagrees with actual string", buffer => { Fill(buffer, small); return 8; });
            calls = 0; Refused("truncated second string", buffer => { if (++calls == 1) return 500U; Fill(buffer, "short"); return 400; });
            calls = 0; actual = Read(buffer => { calls++; return calls == 1 ? 260U : Fill(buffer, PathOfLength(259)); });
            Check(calls == 2 && actual.Length == 259, "equal-to-initial-capacity required response remains a bounded safe retry");
            facts.Add("Win32 W API success length excludes null; required capacity includes null. Initial capacity 260, maximum capacity 32768, at most two reads.");
        }
        static void ActualShortPath(string output)
        {
            string path = Path.Combine(output, "owned-allocation.bin"); File.WriteAllBytes(path, new byte[4096]);
            MethodInfo finalPath = typeof(CleanupService).GetMethod("FinalPath", S);
            AppDomain.MonitoringIsEnabled = true;
            using (FileStream file = File.OpenRead(path))
            {
                object[] args = { file.SafeFileHandle }; string resolved = null;
                for (int i = 0; i < 5; i++) resolved = (string)finalPath.Invoke(null, args);
                Check(String.Equals(resolved, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase), "real owned handle still resolves and normalizes to the exact expected file");
                long before = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
                for (int i = 0; i < 100; i++) finalPath.Invoke(null, args);
                long allocated = AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize - before;
                facts.Add("100 actual owned short-path resolutions allocated " + allocated + " managed bytes (AppDomain monotonic allocation counter).");
                Console.WriteLine("OBSERVED " + facts[facts.Count - 1]);
                Check(allocated < 2 * 1024 * 1024, "100 ordinary owned path reads do not allocate the old multi-megabyte maximum-sized buffers");
            }
        }
        static int Main(string[] args)
        {
            string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
            try
            {
                ActualShortPath(output);
                read = typeof(CleanupService).GetMethod("ReadFinalPath", S, null, new[] { typeof(Func<StringBuilder, uint>) }, null);
                Check(read != null, "production exposes the narrow final-path read seam for required-size fault injection");
                if (read == null) Console.WriteLine("NOT EXECUTED: required-size callback boundary cases need the new production seam; the actual old allocation check above was executed.");
                else Boundaries();
                string existing = CleanupService.SelfTest(); facts.Add(existing); Console.WriteLine(existing);
                Check(existing.Contains("passed"), "existing owned-file identity, reparse, locking and deletion regression suite passes");
            }
            catch (Exception error) { Check(false, "unexpected fixture error: " + error); }
            File.WriteAllText(Path.Combine(output, "final-path-buffer.json"), new JavaScriptSerializer().Serialize(new { checks, failures, facts, scope = "Pure required-size fault injection plus actual fixture-owned handles and the existing owned-file cleanup self-test. No GUI, user Temp scan or maintenance of user files." }), new UTF8Encoding(true));
            Console.WriteLine("Final path buffer: " + checks + " checks, " + failures + " failures."); return failures == 0 ? 0 : 1;
        }
    }
}
