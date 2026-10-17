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
        // Unattended real maintenance is a THIRD, separately confirmed permission:
        // without it, automatic battle only ever settles virtual rewards, even when
        // the cleanup/memory scopes above are on. Legacy consent files never grant
        // it — migration must not silently widen real maintenance into automation.
        internal bool AutoMaintenanceEnabled;

        // Revocation marker: when a disable cannot rewrite OR delete the grant
        // file (locked, ACL, transient AV lock), the revoked scope names are
        // pinned into "<path>.revoked". Load treats pinned scopes as off even
        // if a stale grant file survived, then rewrites the corrected state.
        // The marker only ever closes scopes, never opens them (fail-closed).
        private static string RevokedPath(string path) { return path == null ? null : path + ".revoked"; }
        private static string ScopeName(int scope) { return scope == 0 ? "cleanup" : scope == 1 ? "memory" : "automaint"; }

        private bool Get(int scope)
        {
            return scope == 0 ? CleanupEnabled : scope == 1 ? MemoryEnabled : AutoMaintenanceEnabled;
        }
        private void Set(int scope, bool value)
        {
            if (scope == 0) CleanupEnabled = value;
            else if (scope == 1) MemoryEnabled = value;
            else AutoMaintenanceEnabled = value;
        }

        internal static MaintenanceConsentStore Load(string path)
        {
            MaintenanceConsentStore store = new MaintenanceConsentStore();
            try
            {
                if (String.IsNullOrEmpty(path)) return store;
                string revoked = RevokedPath(path);
                System.Collections.Generic.List<string> pinned = ReadRevoked(revoked);
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
                    {
                        int equals = line.IndexOf('='); if (equals < 1) continue;
                        string key = line.Substring(0, equals).Trim(), value = line.Substring(equals + 1).Trim();
                        if (key == "cleanup" && value == "1") store.CleanupEnabled = true;
                        else if (key == "memory" && value == "1") store.MemoryEnabled = true;
                        else if (key == "automaint" && value == "1") store.AutoMaintenanceEnabled = true;
                        // First-stage single-scope archives upgrade to the two attended
                        // scopes only; unattended auto maintenance stays off (conservative).
                        else if (key == "consent" && value == "1") { store.CleanupEnabled = true; store.MemoryEnabled = true; }
                    }
                }
                if (pinned.Count > 0)
                {
                    // A pinned revocation outranks any stale grant: a previous
                    // disable pinned the scope off, so a force-quit right after
                    // revoking cannot resurrect consent on this launch.
                    foreach (string name in pinned)
                    {
                        if (name == "cleanup") store.CleanupEnabled = false;
                        else if (name == "memory") store.MemoryEnabled = false;
                        else if (name == "automaint") store.AutoMaintenanceEnabled = false;
                    }
                    // Best effort: persist the corrected state and retire the
                    // marker. If the grant file is still locked the marker stays
                    // and keeps protecting the next launch as well.
                    string ignored;
                    if (store.Persist(path, out ignored)) { try { File.Delete(revoked); } catch { } }
                }
            }
            catch { return new MaintenanceConsentStore(); }
            return store;
        }

        private static System.Collections.Generic.List<string> ReadRevoked(string revoked)
        {
            System.Collections.Generic.List<string> names = new System.Collections.Generic.List<string>();
            if (revoked == null || !File.Exists(revoked)) return names;
            try
            {
                foreach (string line in File.ReadAllLines(revoked, Encoding.UTF8))
                {
                    string name = line.Trim();
                    if ((name == "cleanup" || name == "memory" || name == "automaint") && !names.Contains(name)) names.Add(name);
                }
            }
            catch { names.Clear(); }
            // The marker exists but is unreadable or holds no recognizable scope:
            // the revocation state is uncertain, so fail closed on EVERY scope.
            // Real maintenance never resurrects from a marker we cannot read.
            if (names.Count == 0) { names.Add("cleanup"); names.Add("memory"); names.Add("automaint"); }
            return names;
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
                    "cleanup=" + (CleanupEnabled ? "1" : "0") +
                    "\nmemory=" + (MemoryEnabled ? "1" : "0") +
                    "\nautomaint=" + (AutoMaintenanceEnabled ? "1" : "0") + "\n",
                    new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
                // A successful full-state write supersedes any revocation marker.
                try { string revoked = RevokedPath(path); if (File.Exists(revoked)) File.Delete(revoked); } catch { }
                return true;
            }
            catch (Exception e) { error = e.Message; return false; }
        }

        private bool Enable(string path, int scope, out string error)
        {
            if (Get(scope)) { error = null; return true; }
            Set(scope, true);
            if (Persist(path, out error)) return true;
            // Fail-closed: roll the in-memory flag back so consent stays off.
            Set(scope, false);
            return false;
        }

        private bool Disable(string path, int scope, out string error)
        {
            Set(scope, false);
            if (Persist(path, out error)) return true;
            // Never leave a stale file that would resurrect consent on next launch.
            try
            {
                if (File.Exists(path)) File.Delete(path);
                error = null;
                return true;
            }
            catch { }
            // Rewrite and delete both failed (locked grant file): pin the revoked
            // scope so a force-quit right after this disable cannot resurrect the
            // stale grant on next launch. The marker only closes scopes.
            try
            {
                string revoked = RevokedPath(path);
                if (revoked != null)
                {
                    System.Collections.Generic.List<string> names = ReadRevoked(revoked);
                    string name = ScopeName(scope);
                    if (!names.Contains(name)) names.Add(name);
                    System.Text.StringBuilder body = new System.Text.StringBuilder();
                    foreach (string n in names) body.Append(n).Append('\n');
                    File.WriteAllText(revoked, body.ToString(), new UTF8Encoding(false));
                    error = null;
                    return true;
                }
            }
            catch (Exception pin) { error = pin.Message; return false; }
            error = "consent path unavailable";
            return false;
        }

        internal bool SetCleanup(string path, bool enabled, out string error)
        {
            return enabled ? Enable(path, 0, out error) : Disable(path, 0, out error);
        }
        internal bool SetMemory(string path, bool enabled, out string error)
        {
            return enabled ? Enable(path, 1, out error) : Disable(path, 1, out error);
        }
        internal bool SetAutoMaintenance(string path, bool enabled, out string error)
        {
            return enabled ? Enable(path, 2, out error) : Disable(path, 2, out error);
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
                check(legacy.CleanupEnabled && legacy.MemoryEnabled && !legacy.AutoMaintenanceEnabled, "first-stage single consent upgrades to both attended scopes but never to unattended maintenance");
                File.WriteAllText(file, "cleanup=1\nmemory=0\n", new UTF8Encoding(false));
                MaintenanceConsentStore split = MaintenanceConsentStore.Load(file);
                check(split.CleanupEnabled && !split.MemoryEnabled && !split.AutoMaintenanceEnabled, "scopes load independently and unattended maintenance defaults off");
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
                check(store.SetAutoMaintenance(file, true, out error) && store.AutoMaintenanceEnabled, "unattended maintenance enables only on its own explicit action");
                MaintenanceConsentStore withAuto = MaintenanceConsentStore.Load(file);
                check(!withAuto.CleanupEnabled && withAuto.MemoryEnabled && withAuto.AutoMaintenanceEnabled, "automaint persists alongside the attended scopes");
                check(store.SetAutoMaintenance(file, false, out error) && !store.AutoMaintenanceEnabled, "automaint disables independently");
                check(!MaintenanceConsentStore.Load(file).AutoMaintenanceEnabled && MaintenanceConsentStore.Load(file).MemoryEnabled, "automaint disable persists without touching memory scope");

                // A path that names an existing directory cannot be written or replaced.
                string blocked = Path.Combine(fixture, "blocked-dir");
                Directory.CreateDirectory(blocked);
                MaintenanceConsentStore failing = new MaintenanceConsentStore();
                check(!failing.SetCleanup(blocked, true, out error) && error != null && !failing.CleanupEnabled, "a failed save never enables cleanup consent, not even in memory");
                check(!failing.SetMemory(blocked, true, out error) && error != null && !failing.MemoryEnabled, "a failed save never enables memory consent, not even in memory");
                check(!failing.SetAutoMaintenance(blocked, true, out error) && error != null && !failing.AutoMaintenanceEnabled, "a failed save never enables unattended maintenance, not even in memory");
                check(failing.SetCleanup(blocked, false, out error) && !failing.CleanupEnabled, "disable against an unwritable path succeeds because no stale grant can exist there");

                // A file locked by another handle can neither be replaced nor deleted,
                // but the revocation marker still pins the scope off for next launch.
                File.WriteAllText(file, "cleanup=1\nmemory=1\n", new UTF8Encoding(false));
                MaintenanceConsentStore live = MaintenanceConsentStore.Load(file);
                check(live.CleanupEnabled && live.MemoryEnabled, "both scopes loaded before the lock test");
                string revokedFile = file + ".revoked";
                using (FileStream lockHandle = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    check(live.SetCleanup(file, false, out error) && !live.CleanupEnabled && live.MemoryEnabled,
                        "disable against a locked grant applies immediately in memory and succeeds via the revocation marker");
                    check(File.Exists(revokedFile), "the revocation marker was pinned while the grant file stayed locked");
                    // Simulate a force-quit right here: the stale grant file still
                    // says cleanup=1, but the marker must win on the next launch.
                    // While the lock also makes the grant unreadable, the load
                    // fails fully closed — every scope off is the only safe answer.
                    MaintenanceConsentStore relaunch = MaintenanceConsentStore.Load(file);
                    check(!relaunch.CleanupEnabled && !relaunch.MemoryEnabled,
                        "a force-quit after revoking fails fully closed on relaunch while the grant file is unreadable");
                    check(File.Exists(revokedFile), "marker survives while the corrected state cannot yet be persisted");
                }
                // After the lock clears, loading rewrites the corrected grant file
                // and retires the marker.
                MaintenanceConsentStore healed = MaintenanceConsentStore.Load(file);
                check(!healed.CleanupEnabled && healed.MemoryEnabled, "post-lock load keeps the revoked scope off and the other scope on");
                check(!File.Exists(revokedFile) && !MaintenanceConsentStore.Load(file).CleanupEnabled && MaintenanceConsentStore.Load(file).MemoryEnabled,
                    "the corrected state is persisted and the marker retired once the lock clears");
                // Re-enabling after a revoked disable is an explicit fresh grant.
                check(healed.SetCleanup(file, true, out error) && MaintenanceConsentStore.Load(file).CleanupEnabled && !File.Exists(revokedFile),
                    "re-consenting after a revocation is a clean new grant with no marker left");
                check(live.SetCleanup(file, false, out error) && !MaintenanceConsentStore.Load(file).CleanupEnabled, "ordinary disable still persists once handles are gone");

                // Fault injection: a marker that exists but cannot be read must
                // never be treated as absent — uncertain state fails fully closed.
                File.WriteAllText(file, "cleanup=1\nmemory=1\nautomaint=1\n", new UTF8Encoding(false));
                File.WriteAllText(revokedFile, "garbage-not-a-scope\r\n???\n", new UTF8Encoding(false));
                MaintenanceConsentStore unreadable = MaintenanceConsentStore.Load(file);
                check(!unreadable.CleanupEnabled && !unreadable.MemoryEnabled && !unreadable.AutoMaintenanceEnabled,
                    "an unreadable revocation marker fails every scope closed instead of restoring the old grant");
                check(!File.Exists(revokedFile) && !MaintenanceConsentStore.Load(file).CleanupEnabled,
                    "the corrected all-closed state persists and the unreadable marker is retired");
                File.WriteAllText(file, "cleanup=1\nmemory=1\n", new UTF8Encoding(false));
                File.WriteAllText(revokedFile, "cleanup\n", new UTF8Encoding(false));
                using (FileStream markerLock = new FileStream(revokedFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    MaintenanceConsentStore lockedMarker = MaintenanceConsentStore.Load(file);
                    check(!lockedMarker.CleanupEnabled && !lockedMarker.MemoryEnabled && !lockedMarker.AutoMaintenanceEnabled,
                        "a locked marker also fails closed on every scope rather than vanishing");
                }
                MaintenanceConsentStore afterLock = MaintenanceConsentStore.Load(file); // heals and retires the marker
                check(!File.Exists(revokedFile) && !afterLock.CleanupEnabled && !afterLock.MemoryEnabled,
                    "the healed all-closed state persists after the marker lock clears (uncertain state never re-opens any scope)");

                // If the revocation itself cannot be persisted at all, the disable
                // must report failure — never claim a reliable save.
                File.WriteAllText(file, "cleanup=1\n", new UTF8Encoding(false));
                Directory.CreateDirectory(revokedFile); // a directory blocks marker creation
                MaintenanceConsentStore unpinnable = MaintenanceConsentStore.Load(file); // read before locking
                check(unpinnable.CleanupEnabled, "grant loaded before the unpinnable-revocation test");
                using (FileStream grantLock2 = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    string pinError;
                    check(!unpinnable.SetCleanup(file, false, out pinError) && pinError != null && !unpinnable.CleanupEnabled,
                        "a revocation that cannot be persisted anywhere reports failure instead of claiming a reliable save");
                }
                Directory.Delete(revokedFile);
                check(unpinnable.SetCleanup(file, false, out error) && !MaintenanceConsentStore.Load(file).CleanupEnabled,
                    "once the block clears, the same disable persists normally");

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
