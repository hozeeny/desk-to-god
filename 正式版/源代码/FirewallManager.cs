using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace NetBunny
{
    // No rule is written by construction, refresh, or application shutdown.
    // Rules persist in Windows Firewall until the user explicitly restores access.
    public sealed class FirewallManager
    {
        private const string Group = "NetBunny v1";
        private const string Prefix = "NetBunny v1 | ";
        private const int AllProfiles = 0x7fffffff;
        private readonly object gate = new object();
        private DateTime snapshotAt = DateTime.MinValue;
        private List<RuleInfo> snapshot = new List<RuleInfo>();
        private Health health = new Health(false, "尚未读取 Windows 防火墙状态");
        private string lastFailure = "";

        public string Status
        {
            get
            {
                lock (gate)
                {
                    try { RefreshSnapshot(false); }
                    catch (Exception ex) { lastFailure = "读取防火墙失败：" + ex.Message; }
                    return health.Text + (lastFailure.Length == 0 ? "" : "；" + lastFailure);
                }
            }
        }

        // True means this app's complete block configuration is enabled and the
        // current Windows Firewall profiles permit local policy to take effect.
        // It is not a claim that an external proxy or a different child exe is blocked.
        public bool IsBlocked(string fullExePath)
        {
            lock (gate)
            {
                try
                {
                    string path = NormalizePath(fullExePath);
                    RefreshSnapshot(false);
                    return health.Effective && HasCompleteBlock(snapshot, path);
                }
                catch (Exception ex)
                {
                    lastFailure = "无法确认断网状态：" + ex.Message;
                    return false;
                }
            }
        }

        // Includes incomplete/disabled managed rules so the UI can restore them,
        // including executable paths whose processes have exited or files were deleted.
        public List<string> BlockedPaths()
        {
            lock (gate)
            {
                try
                {
                    RefreshSnapshot(false);
                    HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (RuleInfo rule in snapshot)
                        if (IsOwned(rule)) paths.Add(NormalizePath(rule.Path));
                    List<string> result = new List<string>(paths);
                    result.Sort(StringComparer.OrdinalIgnoreCase);
                    return result;
                }
                catch (Exception ex)
                {
                    lastFailure = "读取断网列表失败：" + ex.Message;
                    throw new InvalidOperationException(lastFailure, ex);
                }
            }
        }

        public void Block(string fullExePath)
        {
            lock (gate)
            {
                string path = ValidateBlockTarget(fullExePath);
                List<string> created = new List<string>();
                using (ComContext context = new ComContext())
                {
                    try
                    {
                        Health current = ReadHealth(context);
                        if (!current.Effective) throw new InvalidOperationException(current.Text);
                        List<RuleInfo> before = ReadRules(context);
                        for (int direction = 1; direction <= 2; direction++)
                        {
                            string name = RuleName(path, direction);
                            List<RuleInfo> matches = FindByName(before, name);
                            if (matches.Count > 0)
                            {
                                if (matches.Count != 1 || !IsOwned(matches[0]) ||
                                    !IsCompleteRule(matches[0], path, direction))
                                    throw new InvalidOperationException("断网规则存在冲突或已被其他工具修改。请先恢复该程序联网，再重新断网。");
                                continue;
                            }
                            AddRule(context, path, direction);
                            created.Add(name);
                        }
                        if (!HasCompleteBlock(ReadRules(context), path) || !ReadHealth(context).Effective)
                            throw new InvalidOperationException("Windows 未确认完整断网规则已生效。");
                        lastFailure = "";
                    }
                    catch (Exception ex)
                    {
                        List<string> rollbackErrors = new List<string>();
                        foreach (string name in created)
                        {
                            try { RemoveOwnedName(context, name); }
                            catch (Exception rollback) { rollbackErrors.Add(rollback.Message); }
                        }
                        lastFailure = "断网失败：" + ex.Message;
                        if (rollbackErrors.Count > 0)
                            lastFailure += "；部分新规则未能撤销，请使用恢复联网。" + string.Join("；", rollbackErrors.ToArray());
                        throw new InvalidOperationException(lastFailure, ex);
                    }
                    finally { InvalidateSnapshot(); }
                }
            }
        }

        public void Unblock(string fullExePath)
        {
            lock (gate)
            {
                string path = NormalizePath(fullExePath);
                RemoveManagedRules(path);
            }
        }

        public void UnblockAll()
        {
            lock (gate) { RemoveManagedRules(null); }
        }

        private void RemoveManagedRules(string path)
        {
            List<string> errors = new List<string>();
            try
            {
                using (ComContext context = new ComContext())
                {
                    HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (RuleInfo rule in ReadRules(context))
                        if (IsOwned(rule) && (path == null || SamePath(rule.Path, path))) names.Add(rule.Name);
                    foreach (string name in names)
                    {
                        try { RemoveOwnedName(context, name); }
                        catch (Exception ex) { errors.Add(ex.Message); }
                    }
                    int remaining = 0;
                    foreach (RuleInfo rule in ReadRules(context))
                        if (IsOwned(rule) && (path == null || SamePath(rule.Path, path))) remaining++;
                    if (remaining > 0) errors.Add("仍有 " + remaining + " 条本工具规则未移除");
                }
                if (errors.Count > 0) throw new InvalidOperationException(string.Join("；", errors.ToArray()));
                lastFailure = "";
            }
            catch (Exception ex)
            {
                lastFailure = "恢复联网未完全完成：" + ex.Message;
                throw new InvalidOperationException(lastFailure, ex);
            }
            finally { InvalidateSnapshot(); }
        }

        private void RefreshSnapshot(bool force)
        {
            if (!force && (DateTime.UtcNow - snapshotAt).TotalMilliseconds < 1200) return;
            using (ComContext context = new ComContext())
            {
                List<RuleInfo> current = ReadRules(context);
                Health currentHealth = ReadHealth(context);
                snapshot = current;
                health = currentHealth;
                snapshotAt = DateTime.UtcNow;
            }
        }

        private void InvalidateSnapshot() { snapshotAt = DateTime.MinValue; }

        private static Health ReadHealth(ComContext context)
        {
            dynamic policy = context.Policy;
            int currentProfiles = (int)policy.CurrentProfileTypes & 7;
            if (currentProfiles == 0)
                return new Health(false, "没有可确认的活动防火墙配置，暂时不能确认断网状态");
            List<string> disabled = new List<string>();
            int[] profileBits = { 1, 2, 4 };
            string[] profileNames = { "域网络", "专用网络", "公用网络" };
            for (int i = 0; i < profileBits.Length; i++)
                if ((currentProfiles & profileBits[i]) != 0 && !(bool)policy.FirewallEnabled[profileBits[i]])
                    disabled.Add(profileNames[i]);
            if (disabled.Count > 0)
                return new Health(false, string.Join("、", disabled.ToArray()) + "的 Windows 防火墙未启用，断网规则无法保证生效");
            int modifyState = (int)policy.LocalPolicyModifyState;
            if (modifyState == 1)
                return new Health(false, "组织策略覆盖本地防火墙规则，无法确认断网生效");
            if (modifyState == 2)
                return new Health(false, "当前防火墙策略限制本地入站规则，无法确认完整断网生效");
            if (modifyState != 0)
                return new Health(false, "Windows 返回未知的防火墙策略状态：" + modifyState);
            return new Health(true, "Windows 防火墙正常；断网设置会保留，点击恢复联网后解除");
        }

        private static List<RuleInfo> ReadRules(ComContext context)
        {
            List<RuleInfo> result = new List<RuleInfo>();
            foreach (object raw in (IEnumerable)context.Rules)
            {
                try
                {
                    dynamic rule = raw;
                    string name = (string)rule.Name ?? "";
                    string group = (string)rule.Grouping ?? "";
                    if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) && group != Group) continue;
                    object interfaces = rule.Interfaces;
                    Array interfaceArray = interfaces as Array;
                    result.Add(new RuleInfo {
                        Name = name, Grouping = group, Path = (string)rule.ApplicationName ?? "",
                        Enabled = (bool)rule.Enabled, Direction = (int)rule.Direction,
                        Action = (int)rule.Action, Profiles = (int)rule.Profiles,
                        Protocol = (int)rule.Protocol, InterfaceTypes = (string)rule.InterfaceTypes ?? "",
                        LocalAddresses = (string)rule.LocalAddresses ?? "",
                        RemoteAddresses = (string)rule.RemoteAddresses ?? "",
                        ServiceName = (string)rule.ServiceName ?? "",
                        UnrestrictedInterfaces = interfaces == null || (interfaceArray != null && interfaceArray.Length == 0)
                    });
                }
                finally { ReleaseCom(raw); }
            }
            return result;
        }

        private static void AddRule(ComContext context, string path, int direction)
        {
            object raw = CreateCom("HNetCfg.FWRule");
            try
            {
                dynamic rule = raw;
                rule.Name = RuleName(path, direction);
                rule.Description = "骑士助手用户选择的程序断网规则。请在骑士助手中恢复联网以移除。";
                rule.Grouping = Group;
                rule.ApplicationName = path;
                rule.Protocol = 256; // NET_FW_IP_PROTOCOL_ANY
                rule.Direction = direction; // 1 inbound, 2 outbound
                rule.Action = 0; // NET_FW_ACTION_BLOCK
                rule.Profiles = AllProfiles;
                rule.InterfaceTypes = "All";
                rule.LocalAddresses = "*";
                rule.RemoteAddresses = "*";
                rule.Enabled = true;
                ((dynamic)context.Rules).Add(rule);
            }
            finally { ReleaseCom(raw); }
        }

        private static void RemoveOwnedName(ComContext context, string name)
        {
            // COM removes by name. Check every matching object before each removal
            // so a colliding unrelated rule cannot be removed by this tool.
            int attempts = 0;
            while (true)
            {
                List<RuleInfo> matches = FindByName(ReadRules(context), name);
                if (matches.Count == 0) return;
                foreach (RuleInfo rule in matches)
                    if (!IsOwned(rule)) throw new InvalidOperationException("规则名称与其他规则冲突，已保留：" + name);
                if (++attempts > 32) throw new InvalidOperationException("重复规则过多或被其他程序持续重建，请检查防火墙设置。");
                ((dynamic)context.Rules).Remove(name);
            }
        }

        private static List<RuleInfo> FindByName(List<RuleInfo> rules, string name)
        {
            List<RuleInfo> matches = new List<RuleInfo>();
            foreach (RuleInfo rule in rules)
                if (string.Equals(rule.Name, name, StringComparison.OrdinalIgnoreCase)) matches.Add(rule);
            return matches;
        }

        private static bool HasCompleteBlock(List<RuleInfo> rules, string path)
        {
            for (int direction = 1; direction <= 2; direction++)
            {
                List<RuleInfo> matches = FindByName(rules, RuleName(path, direction));
                if (matches.Count != 1 || !IsOwned(matches[0]) || !IsCompleteRule(matches[0], path, direction)) return false;
            }
            return true;
        }

        private static bool IsCompleteRule(RuleInfo rule, string path, int direction)
        {
            return SamePath(rule.Path, path) && rule.Enabled && rule.Action == 0 &&
                rule.Direction == direction && rule.Protocol == 256 && (rule.Profiles & 7) == 7 &&
                string.Equals(rule.InterfaceTypes, "All", StringComparison.OrdinalIgnoreCase) &&
                rule.LocalAddresses == "*" && rule.RemoteAddresses == "*" &&
                rule.ServiceName.Length == 0 && rule.UnrestrictedInterfaces;
        }

        private static bool IsOwned(RuleInfo rule)
        {
            if (rule.Grouping != Group) return false;
            try
            {
                string path = NormalizePath(rule.Path);
                return rule.Name == RuleName(path, 1) || rule.Name == RuleName(path, 2);
            }
            catch { return false; }
        }

        private static string ValidateBlockTarget(string value)
        {
            string path = NormalizePath(value);
            if (!string.Equals(System.IO.Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new ArgumentException("请选择仍然存在的 .exe 程序文件。");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("请选择程序的实际 .exe 文件，不能使用文件链接。");
            using (Process process = Process.GetCurrentProcess())
                if (SamePath(path, process.MainModule.FileName))
                    throw new InvalidOperationException("不能给骑士助手自己断网。");
            string filename = System.IO.Path.GetFileName(path).ToLowerInvariant();
            string critical = "|svchost.exe|system|ntoskrnl.exe|lsass.exe|lsaiso.exe|services.exe|wininit.exe|csrss.exe|smss.exe|winlogon.exe|fontdrvhost.exe|dwm.exe|audiodg.exe|runtimebroker.exe|dllhost.exe|sihost.exe|taskhostw.exe|conhost.exe|backgroundtaskhost.exe|applicationframehost.exe|shellexperiencehost.exe|startmenuexperiencehost.exe|explorer.exe|securityhealthservice.exe|msmpeng.exe|nissrv.exe|sense.exe|";
            if (critical.IndexOf("|" + filename + "|", StringComparison.Ordinal) >= 0)
                throw new InvalidOperationException("这是 Windows 共享宿主或关键系统程序，断网可能影响多个应用，因此不能在此操作。");
            return path;
        }

        private static string NormalizePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("程序路径为空。");
            if (value.IndexOfAny(new char[] { '*', '?', '"', '\0' }) >= 0)
                throw new ArgumentException("程序路径包含不允许的字符。");
            bool driveAbsolute = value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/');
            bool uncAbsolute = value.StartsWith("\\\\", StringComparison.Ordinal) && !value.StartsWith("\\\\.\\", StringComparison.Ordinal);
            if (!driveAbsolute && !uncAbsolute) throw new ArgumentException("必须使用程序的完整绝对路径。");
            return System.IO.Path.GetFullPath(value).Replace('/', '\\');
        }

        private static bool SamePath(string left, string right)
        {
            try { return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        private static string RuleName(string path, int direction)
        {
            byte[] hash;
            using (SHA256 algorithm = SHA256.Create())
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(NormalizePath(path).ToUpperInvariant()));
            StringBuilder hex = new StringBuilder(64);
            foreach (byte value in hash) hex.Append(value.ToString("x2"));
            return Prefix + hex.ToString() + (direction == 1 ? " | IN" : " | OUT");
        }

        public static string SelfTest()
        {
            string one = @"C:\Example\Program.exe";
            if (RuleName(one, 1) != RuleName(@"c:\example\program.EXE", 1)) throw new Exception("Rule path identity failed");
            if (RuleName(one, 1) == RuleName(one, 2)) throw new Exception("Rule direction identity failed");
            if (RuleName(one, 1) == RuleName(@"C:\Example2\Program.exe", 1)) throw new Exception("Rule path isolation failed");
            bool rejected = false;
            try { NormalizePath(@"C:relative.exe"); } catch (ArgumentException) { rejected = true; }
            if (!rejected) throw new Exception("Drive-relative path must be rejected");
            RuleInfo own = new RuleInfo { Name = RuleName(one, 1), Grouping = Group, Path = one };
            if (!IsOwned(own)) throw new Exception("Own rule recognition failed");
            own.Grouping = "Other application";
            if (IsOwned(own)) throw new Exception("Unrelated rule must be preserved");
            return "Firewall helpers: PASS (6 checks; no firewall changes)";
        }

        private static object CreateCom(string progId)
        {
            Type type = Type.GetTypeFromProgID(progId);
            if (type == null) throw new InvalidOperationException("系统缺少 Windows 防火墙组件。");
            return Activator.CreateInstance(type);
        }

        private static void ReleaseCom(object value)
        {
            if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
        }

        private sealed class ComContext : IDisposable
        {
            public object Policy;
            public object Rules;
            public ComContext()
            {
                Policy = CreateCom("HNetCfg.FwPolicy2");
                try { Rules = ((dynamic)Policy).Rules; }
                catch { ReleaseCom(Policy); Policy = null; throw; }
            }
            public void Dispose() { ReleaseCom(Rules); ReleaseCom(Policy); Rules = null; Policy = null; }
        }

        private sealed class Health
        {
            public bool Effective;
            public string Text;
            public Health(bool effective, string text) { Effective = effective; Text = text; }
        }

        private sealed class RuleInfo
        {
            public string Name, Grouping, Path, InterfaceTypes, LocalAddresses, RemoteAddresses, ServiceName;
            public bool Enabled, UnrestrictedInterfaces;
            public int Direction, Action, Profiles, Protocol;
        }
    }
}
