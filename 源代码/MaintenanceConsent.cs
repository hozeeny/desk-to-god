using System;
using System.IO;
using System.Text;

namespace NetBunny
{
    // Real system maintenance consent, split into two independent scopes:
    //   cleanup — defeating a file monster deletes the stale temp files bound to it;
    //   memory  — defeating a memory boss trims same-session background working sets.
    // Neither scope ever implies the other, and neither is ever implied by combat,
    // cultivation progress, or unattended auto-battle.
    //
    // Enabling is fail-closed: the in-memory flag only switches on after the new
    // state has been durably written, so a failed save can never leave consent
    // half-persisted. Disabling always applies in memory immediately; if the file
    // cannot be updated, the stale file is deleted so a future launch cannot
    // resurrect consent, and any remaining failure is reported to the caller.
    internal sealed class MaintenanceConsentStore
    {
        internal bool CleanupEnabled, MemoryEnabled;

        internal static MaintenanceConsentStore Load(string path)
        {
            MaintenanceConsentStore store = new MaintenanceConsentStore();
            try
            {
                if (String.IsNullOrEmpty(path) || !File.Exists(path)) return store;
                foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int equals = line.IndexOf('='); if (equals < 1) continue;
                    string key = line.Substring(0, equals).Trim(), value = line.Substring(equals + 1).Trim();
                    if (key == "cleanup" && value == "1") store.CleanupEnabled = true;
                    else if (key == "memory" && value == "1") store.MemoryEnabled = true;
                    // First-stage single-scope archives upgrade to both scopes on read.
                    else if (key == "consent" && value == "1") { store.CleanupEnabled = true; store.MemoryEnabled = true; }
                }
            }
            catch { return new MaintenanceConsentStore(); }
            return store;
        }

        private bool Persist(string path, out string error)
        {
            error = null;
            if (path == null) return true; // demonstration runs never touch the disk
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!String.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string temporary = path + ".new";
                File.WriteAllText(temporary,
                    "cleanup=" + (CleanupEnabled ? "1" : "0") + "\nmemory=" + (MemoryEnabled ? "1" : "0") + "\n",
                    new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private bool Enable(string path, bool cleanupScope, out string error)
        {
            if (cleanupScope) { if (CleanupEnabled) { error = null; return true; } CleanupEnabled = true; }
            else { if (MemoryEnabled) { error = null; return true; } MemoryEnabled = true; }
            if (Persist(path, out error)) return true;
            // Fail-closed: roll the in-memory flag back so consent stays off.
            if (cleanupScope) CleanupEnabled = false; else MemoryEnabled = false;
            return false;
        }

        private bool Disable(string path, bool cleanupScope, out string error)
        {
            if (cleanupScope) CleanupEnabled = false; else MemoryEnabled = false;
            if (Persist(path, out error)) return true;
            // Never leave a stale file that would resurrect consent on next launch.
            try
            {
                if (File.Exists(path)) File.Delete(path);
                error = null;
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        internal bool SetCleanup(string path, bool enabled, out string error)
        {
            return enabled ? Enable(path, true, out error) : Disable(path, true, out error);
        }
        internal bool SetMemory(string path, bool enabled, out string error)
        {
            return enabled ? Enable(path, false, out error) : Disable(path, false, out error);
        }

        internal static string SelfTest()
        {
            int n = 0;
            Action<bool, string> check = delegate(bool okay, string label)
            {
                if (!okay) throw new InvalidOperationException("Maintenance consent: " + label);
                n++;
            };
            string fixture = Path.Combine(Path.GetTempPath(), "consent-selftest-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(fixture, "maintenance-consent.txt");
            try
            {
                MaintenanceConsentStore fresh = MaintenanceConsentStore.Load(file);
                check(!fresh.CleanupEnabled && !fresh.MemoryEnabled, "missing file means both scopes off");
                Directory.CreateDirectory(fixture);
                File.WriteAllText(file, "consent=1\n", new UTF8Encoding(false));
                MaintenanceConsentStore legacy = MaintenanceConsentStore.Load(file);
                check(legacy.CleanupEnabled && legacy.MemoryEnabled, "first-stage single consent upgrades to both scopes");
                File.WriteAllText(file, "cleanup=1\nmemory=0\n", new UTF8Encoding(false));
                MaintenanceConsentStore split = MaintenanceConsentStore.Load(file);
                check(split.CleanupEnabled && !split.MemoryEnabled, "scopes load independently");
                File.WriteAllText(file, "not-a-consent-file", new UTF8Encoding(false));
                MaintenanceConsentStore corrupt = MaintenanceConsentStore.Load(file);
                check(!corrupt.CleanupEnabled && !corrupt.MemoryEnabled, "a corrupt file fails closed");
                if (File.Exists(file)) File.Delete(file);

                string error;
                MaintenanceConsentStore store = new MaintenanceConsentStore();
                check(store.SetCleanup(file, true, out error) && store.CleanupEnabled && !store.MemoryEnabled, "cleanup scope enables on its own");
                check(MaintenanceConsentStore.Load(file).CleanupEnabled && !MaintenanceConsentStore.Load(file).MemoryEnabled, "cleanup consent persists without implying memory consent");
                check(store.SetCleanup(file, true, out error), "re-enabling an active scope is idempotent");
                check(store.SetMemory(file, true, out error) && store.MemoryEnabled, "memory scope enables on its own");
                MaintenanceConsentStore both = MaintenanceConsentStore.Load(file);
                check(both.CleanupEnabled && both.MemoryEnabled, "both scopes persist side by side");
                check(store.SetCleanup(file, false, out error) && !store.CleanupEnabled && store.MemoryEnabled, "disabling cleanup keeps memory consent");
                check(!MaintenanceConsentStore.Load(file).CleanupEnabled && MaintenanceConsentStore.Load(file).MemoryEnabled, "partial disable persists");

                // A path that names an existing directory cannot be written or replaced.
                string blocked = Path.Combine(fixture, "blocked-dir");
                Directory.CreateDirectory(blocked);
                MaintenanceConsentStore failing = new MaintenanceConsentStore();
                check(!failing.SetCleanup(blocked, true, out error) && error != null && !failing.CleanupEnabled, "a failed save never enables cleanup consent, not even in memory");
                check(!failing.SetMemory(blocked, true, out error) && error != null && !failing.MemoryEnabled, "a failed save never enables memory consent, not even in memory");
                check(failing.SetCleanup(blocked, false, out error) && !failing.CleanupEnabled, "disable against an unwritable path succeeds because no stale grant can exist there");

                // A file locked by another handle can neither be replaced nor deleted.
                File.WriteAllText(file, "cleanup=1\nmemory=1\n", new UTF8Encoding(false));
                MaintenanceConsentStore live = MaintenanceConsentStore.Load(file);
                check(live.CleanupEnabled && live.MemoryEnabled, "both scopes loaded before the lock test");
                using (FileStream lockHandle = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    check(!live.SetCleanup(file, false, out error) && error != null && !live.CleanupEnabled && live.MemoryEnabled,
                        "disable applies immediately in memory and reports when the stale grant cannot be removed");
                }
                check(MaintenanceConsentStore.Load(file).CleanupEnabled, "the stale file indeed survived the failed disable, so reporting it mattered");
                check(live.SetCleanup(file, false, out error) && !MaintenanceConsentStore.Load(file).CleanupEnabled, "once the handle is gone, disabling persists");

                MaintenanceConsentStore demo = new MaintenanceConsentStore();
                check(demo.SetCleanup(null, true, out error) && demo.CleanupEnabled, "demonstration mode applies consent in memory without touching the disk");
            }
            finally
            {
                try { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); } catch { }
            }
            return "Maintenance consent: " + n + " checks passed (isolated file fixture; fail-closed writes verified).";
        }
    }
}
