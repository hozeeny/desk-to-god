using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace NetBunny
{
    // Reflection lets an unimplemented policy produce an executable RED result.
    // Controllers stay disabled, with stopped timers and hidden owners throughout.
    internal static class DesktopEnvironmentTests
    {
        const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int checks, failures;
        static string assets, fixtures;
        static readonly string[] ArchiveNames = { "adventure-v1.txt", "cultivation-v2.txt", "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt", "maintenance-consent.txt" };

        static void Check(bool okay, string message)
        {
            checks++;
            Console.WriteLine((okay ? "PASS " : "FAIL ") + message);
            if (!okay) failures++;
        }
        static void Run(string name, Action body)
        {
            try { body(); }
            catch (Exception error)
            {
                while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                Check(false, name + ": " + error.GetType().Name + " - " + error.Message);
            }
        }
        static object Read(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name, AnyInstance);
            if (property == null) throw new InvalidOperationException("missing capability " + target.GetType().Name + "." + name);
            return property.GetValue(target, null);
        }
        static object Call(object target, string name, params object[] values)
        {
            MethodInfo method = target.GetType().GetMethod(name, AnyInstance);
            if (method == null) throw new InvalidOperationException("missing capability " + target.GetType().Name + "." + name);
            return method.Invoke(target, values);
        }
        static Type RequiredType(string name)
        {
            Type type = typeof(AdventureController).Assembly.GetType("NetBunny." + name);
            if (type == null) throw new InvalidOperationException("missing environment policy type " + name);
            return type;
        }
        static string NewProfile(string name)
        {
            string path = Path.Combine(fixtures, name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
        static Form HiddenOwner()
        {
            Form owner = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-25000, -25000), Size = new Size(1, 1) };
            IntPtr handle = owner.Handle;
            return owner;
        }
        static AdventureController Controller(Form owner, string profile, bool observe)
        {
            AdventureController controller;
            if (!observe) controller = new AdventureController(owner, assets, false, null, false, profile);
            else
            {
                ConstructorInfo constructor = typeof(AdventureController).GetConstructors(AnyInstance).FirstOrDefault(c =>
                {
                    ParameterInfo[] p = c.GetParameters();
                    return p.Length == 7 && p[5].ParameterType == typeof(string) && p[6].ParameterType == typeof(bool);
                });
                if (constructor == null) throw new InvalidOperationException("missing explicit desktopObserve controller constructor");
                controller = (AdventureController)constructor.Invoke(new object[] { owner, assets, false, null, false, profile, true });
            }
            ((Timer)typeof(AdventureController).GetField("timer", AnyInstance).GetValue(controller)).Stop();
            return controller;
        }
        static void Idle(AdventureController controller, string phase)
        {
            Check(!controller.Enabled && !((Timer)typeof(AdventureController).GetField("timer", AnyInstance).GetValue(controller)).Enabled
                && controller.DesktopRealWorkIdle, phase + " does not start monitoring or maintenance workers");
        }
        static void Mode(AdventureController controller, string expected, bool observes, string phase)
        {
            Check(Read(controller, "DesktopMode").ToString() == expected && (bool)Read(controller, "DesktopObservesSystem") == observes,
                phase + " reports " + expected + " with its own observation capability");
        }
        static bool Grant(AdventureController controller, string method, bool enabled)
        { return (bool)Call(controller, method, enabled); }
        static bool Consent(AdventureController controller, string name) { return (bool)Read(controller, name); }

        static void PolicyMatrix()
        {
            Type policyType = RequiredType("DesktopEnvironmentPolicy"), scopeType = RequiredType("DesktopMaintenanceScope");
            Type modeType = RequiredType("DesktopEnvironmentMode");
            Check(modeType.IsEnum && scopeType.IsEnum, "environment modes and maintenance scopes are explicit enums");
            foreach (string name in new[] { "Virtual", "Observe", "Maintain" })
                Check(Enum.IsDefined(modeType, name), "environment mode includes " + name);
            foreach (bool virtualOnly in new[] { true, false })
            {
                object policy = Activator.CreateInstance(policyType, AnyInstance, null, new object[] { virtualOnly }, null);
                Check((bool)Read(policy, "MayObserve") == !virtualOnly, "observation is independent from profile persistence, virtualOnly=" + virtualOnly);
                foreach (bool cleanup in new[] { false, true })
                    foreach (bool memory in new[] { false, true })
                    {
                        string expectedMode = virtualOnly ? "Virtual" : (cleanup || memory ? "Maintain" : "Observe");
                        Check(Call(policy, "ReadMode", cleanup, memory).ToString() == expectedMode,
                            "mode derives from this profile's grants: virtual=" + virtualOnly + ", cleanup=" + cleanup + ", memory=" + memory);
                    }
                foreach (string scopeName in new[] { "FileCleanup", "MemoryReclaim", "ProcessExit" })
                {
                    object scope = Enum.Parse(scopeType, scopeName);
                    foreach (bool scopeConsent in new[] { false, true })
                        foreach (bool automatic in new[] { false, true })
                            foreach (bool automaticConsent in new[] { false, true })
                            {
                                bool expected = !virtualOnly && scopeConsent && (!automatic || automaticConsent);
                                if (scopeName == "ProcessExit" && automatic) expected = false;
                                Check((bool)Call(policy, "MayMaintain", scope, scopeConsent, automatic, automaticConsent) == expected,
                                    scopeName + " permission virtual=" + virtualOnly + ", scope=" + scopeConsent + ", automatic=" + automatic + ", auto-consent=" + automaticConsent);
                            }
                }
                foreach (int invalid in new[] { -1, 99, Int32.MaxValue })
                {
                    bool refused;
                    try { refused = !(bool)Call(policy, "MayMaintain", Enum.ToObject(scopeType, invalid), true, false, true); }
                    catch (TargetInvocationException error) { refused = error.InnerException is ArgumentException; }
                    Check(refused, "unknown maintenance scope " + invalid + " fails closed, virtual=" + virtualOnly);
                }
            }
        }

        static void EntryModes()
        {
            MethodInfo read = typeof(DesktopDemoSession).GetMethod("ReadDesktopObserve", AnyStatic);
            if (read == null) throw new InvalidOperationException("missing pure entry policy DesktopDemoSession.ReadDesktopObserve");
            string[][] inputs = {
                new[] { "--desktop-demo" },
                new[] { "--desktop-demo", "--profile", "verify-policy" },
                new[] { "--desktop-demo-accept", "fixture" },
                new[] { "--desktop-demo-smoke" },
                new[] { "--desktop-demo", "--seed", "mortal" },
                new[] { "--desktop-demo-accept", "fixture", "--seed", "qishi-full", "--normal-speed" },
                new[] { "--desktop-demo", "--normal-speed" }
            };
            bool[] expected = { true, true, false, false, false, false, false };
            for (int i = 0; i < inputs.Length; i++)
                Check((bool)read.Invoke(null, new object[] { inputs[i] }) == expected[i],
                    "entry observation is " + expected[i] + " for " + String.Join(" ", inputs[i]));
        }

        static void VirtualCannotInherit()
        {
            string profile = NewProfile("virtual"), consentPath = Path.Combine(profile, "maintenance-consent.txt");
            File.WriteAllText(consentPath, "cleanup=1\nmemory=1\nautomaint=1\n", new UTF8Encoding(false));
            string before = Hash(consentPath);
            using (Form owner = HiddenOwner())
            using (AdventureController controller = Controller(owner, profile, false))
            {
                Mode(controller, "Virtual", false, "legacy six-argument private controller");
                Idle(controller, "virtual startup");
                Check(!Consent(controller, "MaintenanceConsentCleanup") && !Consent(controller, "MaintenanceConsentMemory")
                    && !Consent(controller, "MaintenanceConsentAutoMaintenance"), "virtual entry ignores saved real-maintenance grants");
                Check(!Grant(controller, "SetCleanupConsent", true) && !Grant(controller, "SetMemoryConsent", true)
                    && !Grant(controller, "SetAutoMaintenanceConsent", true), "virtual entry cannot acquire any maintenance grant");
                controller.DesktopSetAcceptanceSpeed(6);
                Mode(controller, "Virtual", false, "acceptance speed");
                controller.DesktopSetAcceptanceSpeed(1);
                Mode(controller, "Virtual", false, "normal speed");
                Check(controller.DesktopSaveNow(), "virtual controller still saves its five private business archives");
            }
            Check(Hash(consentPath) == before, "virtual startup, save and shutdown cannot rewrite saved maintenance authorization");
        }

        static void ObserveConsentLifecycle()
        {
            string profile = NewProfile("observe"), consentPath = Path.Combine(profile, "maintenance-consent.txt");
            using (Form owner = HiddenOwner())
            {
                using (AdventureController controller = Controller(owner, profile, true))
                {
                    Mode(controller, "Observe", true, "new private profile"); Idle(controller, "observe startup");
                    string prefix = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    Check(controller.DesktopArchivePaths.Length == 5 && controller.DesktopArchivePaths.All(path => Path.GetFullPath(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)),
                        "all five business archives remain inside the same private profile");
                    Check(Grant(controller, "SetCleanupConsent", true) && File.Exists(consentPath)
                        && Consent(controller, "MaintenanceConsentCleanup") && !Consent(controller, "MaintenanceConsentMemory")
                        && !Consent(controller, "MaintenanceConsentAutoMaintenance"), "file cleanup grant persists only to this profile and cannot authorize memory or automation");
                    Mode(controller, "Maintain", true, "one durable scope grant");
                    Check(Grant(controller, "SetMemoryConsent", true) && Grant(controller, "SetCleanupConsent", false), "memory grant survives independent file-scope revocation");
                    Mode(controller, "Maintain", true, "remaining memory scope");
                    Check(Grant(controller, "SetAutoMaintenanceConsent", true) && Grant(controller, "SetMemoryConsent", false), "automation grant does not keep a revoked maintenance scope enabled");
                    Mode(controller, "Observe", true, "all maintenance scopes revoked");
                    Check(Grant(controller, "SetAutoMaintenanceConsent", false) && controller.DesktopSaveNow(), "revocation and private business state save successfully");
                    Idle(controller, "all authorization changes");
                }
                using (AdventureController restarted = Controller(owner, profile, true))
                {
                    Mode(restarted, "Observe", true, "restart after revocation");
                    Check(!Consent(restarted, "MaintenanceConsentCleanup") && !Consent(restarted, "MaintenanceConsentMemory")
                        && !Consent(restarted, "MaintenanceConsentAutoMaintenance"), "restart cannot resurrect any revoked scope");
                    Check(Grant(restarted, "SetCleanupConsent", true) && Grant(restarted, "SetAutoMaintenanceConsent", true), "private cleanup and automation grants can persist independently");
                    Idle(restarted, "grants before restart");
                }
                using (AdventureController restored = Controller(owner, profile, true))
                {
                    Mode(restored, "Maintain", true, "same-profile restart with grants");
                    Check(Consent(restored, "MaintenanceConsentCleanup") && !Consent(restored, "MaintenanceConsentMemory")
                        && Consent(restored, "MaintenanceConsentAutoMaintenance"), "restart restores exactly this profile's durable grants");
                    Idle(restored, "restored grants");
                }
                using (AdventureController other = Controller(owner, NewProfile("other"), true))
                {
                    Mode(other, "Observe", true, "another private profile");
                    Check(!Consent(other, "MaintenanceConsentCleanup") && !Consent(other, "MaintenanceConsentMemory")
                        && !Consent(other, "MaintenanceConsentAutoMaintenance"), "another profile cannot inherit these grants");
                    Idle(other, "other profile");
                }
                using (AdventureController virtualRestart = Controller(owner, profile, false))
                {
                    Mode(virtualRestart, "Virtual", false, "same saved profile through legacy entry");
                    Check(!Consent(virtualRestart, "MaintenanceConsentCleanup") && !Consent(virtualRestart, "MaintenanceConsentMemory")
                        && !Consent(virtualRestart, "MaintenanceConsentAutoMaintenance"), "saved grants cannot upgrade a virtual entry");
                    Idle(virtualRestart, "virtual restart");
                }
            }
        }

        static void SaveFailureCannotGrant()
        {
            string profile = NewProfile("blocked-consent");
            Directory.CreateDirectory(Path.Combine(profile, "maintenance-consent.txt.new"));
            using (Form owner = HiddenOwner())
            using (AdventureController controller = Controller(owner, profile, true))
            {
                Check(!Grant(controller, "SetCleanupConsent", true) && !Grant(controller, "SetMemoryConsent", true)
                    && !Grant(controller, "SetAutoMaintenanceConsent", true), "unwritable authorization file rejects each grant");
                Check(!Consent(controller, "MaintenanceConsentCleanup") && !Consent(controller, "MaintenanceConsentMemory")
                    && !Consent(controller, "MaintenanceConsentAutoMaintenance"), "failed durable writes leave every runtime grant disabled");
                Mode(controller, "Observe", true, "failed authorization persistence");
                Idle(controller, "failed authorization persistence");
            }
        }

        static string Hash(string path)
        { using (SHA256 hash = SHA256.Create()) using (FileStream file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)); }
        static Dictionary<string, string> FormalArchives()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KnightAssistant");
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(root)) foreach (string path in Directory.GetFiles(root))
                if (ArchiveNames.Any(name => Path.GetFileName(path).StartsWith(name, StringComparison.OrdinalIgnoreCase))) result[path] = Hash(path);
            return result;
        }
        [STAThread] static int Main(string[] args)
        {
            Dictionary<string, string> formalBefore = null;
            try
            {
                if (args.Length != 2) throw new ArgumentException("Expected assets directory and isolated evidence directory.");
                assets = Path.GetFullPath(args[0]);
                string evidence = Path.GetFullPath(args[1]);
                string allowed = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!evidence.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Evidence must remain inside the independent test build.");
                fixtures = Path.Combine(evidence, "environment-profiles"); Directory.CreateDirectory(fixtures);
                formalBefore = FormalArchives();
                Run("policy authorization matrix", PolicyMatrix);
                Run("desktop entry modes", EntryModes);
                Run("virtual isolation and restart", VirtualCannotInherit);
                Run("observe grants, revocation and restart", ObserveConsentLifecycle);
                Run("authorization persistence failure", SaveFailureCannotGrant);
            }
            catch (Exception error) { Check(false, "test harness setup: " + error.Message); }
            finally
            {
                if (formalBefore != null) Run("formal archive isolation", delegate
                {
                    Dictionary<string, string> after = FormalArchives();
                    Check(after.Count == formalBefore.Count && formalBefore.All(entry => after.ContainsKey(entry.Key) && after[entry.Key] == entry.Value),
                        "formal business archives and authorization files retain identical SHA256 hashes (" + formalBefore.Count + " files)");
                });
            }
            Console.WriteLine("Desktop environment regression: " + checks + " checks, " + failures + " failures.");
            return failures == 0 ? 0 : 1;
        }
    }
}
