using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NetBunny;

// Compiled with a generated AdventureController copy whose six archive-root
// initializers point here. No assertions or branches in the smoke are replaced.
// This is dependency-root injection, not validation of the production binary.
internal static class SmokeIsolationRegression
{
    private static string archiveRoot;
    private static readonly string[] Archives = {
        "adventure-v1.txt", "cultivation-v2.txt", "maintenance-consent.txt",
        "baiye-v1.txt", "lingyu-v1.txt", "dongtian-v1.txt"
    };

    internal static string ArchivePath(string name)
    {
        if (archiveRoot == null || !Archives.Contains(name))
            throw new InvalidOperationException("The isolated archive root was not configured.");
        return Path.Combine(archiveRoot, name);
    }

    private sealed class FileState
    {
        internal byte[] Bytes;
        internal long Created, Written;
    }

    private sealed class TreeState
    {
        internal string[] Directories;
        internal SortedDictionary<string, FileState> Files;
    }

    private static TreeState Snapshot(string root)
    {
        TreeState result = new TreeState {
            Directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(root.Length + 1)).OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            Files = new SortedDictionary<string, FileState>(StringComparer.Ordinal)
        };
        foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            result.Files.Add(path.Substring(root.Length + 1), new FileState {
                Bytes = File.ReadAllBytes(path), Created = File.GetCreationTimeUtc(path).Ticks,
                Written = File.GetLastWriteTimeUtc(path).Ticks
            });
        return result;
    }

    private static string Manifest(TreeState state)
    {
        StringBuilder text = new StringBuilder();
        foreach (string directory in state.Directories) text.AppendLine("directory " + directory);
        foreach (KeyValuePair<string, FileState> item in state.Files)
        {
            string hash;
            using (SHA256 sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(item.Value.Bytes)).Replace("-", "").ToLowerInvariant();
            text.AppendLine(item.Key + " | bytes=" + item.Value.Bytes.Length + " | sha256=" + hash
                + " | created=" + item.Value.Created + " | written=" + item.Value.Written);
        }
        return text.ToString();
    }

    private static bool Unchanged(TreeState before, TreeState after)
    {
        return before.Directories.SequenceEqual(after.Directories)
            && before.Files.Keys.SequenceEqual(after.Files.Keys)
            && before.Files.All(item => item.Value.Bytes.SequenceEqual(after.Files[item.Key].Bytes)
                && item.Value.Created == after.Files[item.Key].Created
                && item.Value.Written == after.Files[item.Key].Written);
    }

    private static bool RunCase(string assets, string outputRoot, bool existing)
    {
        string name = existing ? "existing-lingyu" : "missing-lingyu";
        string caseRoot = Path.Combine(outputRoot, name);
        archiveRoot = Path.Combine(caseRoot, "isolated-user-archives");
        Directory.CreateDirectory(archiveRoot);
        // A byte fixture, deliberately including a binary tail, catches rewrites
        // and preserves unrelated archives and backups as well as the lingyu file.
        foreach (string archive in Archives)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("pre-existing fixture: " + archive + "\r\n\0\u00ff");
            if (archive != "lingyu-v1.txt" || existing) File.WriteAllBytes(ArchivePath(archive), bytes);
            File.WriteAllBytes(ArchivePath(archive) + ".bak", bytes);
        }
        string nested = Path.Combine(archiveRoot, "unrelated");
        Directory.CreateDirectory(nested);
        File.WriteAllBytes(Path.Combine(nested, "keep.bin"), new byte[] { 0, 255, 17, 32, 0, 128 });
        TreeState before = Snapshot(archiveRoot);
        File.WriteAllText(Path.Combine(caseRoot, "before.txt"), Manifest(before), new UTF8Encoding(false));
        List<string> events = new List<string>();
        Exception failure = null;
        using (FileSystemWatcher watcher = new FileSystemWatcher(archiveRoot))
        {
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime;
            FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs e) {
                lock (events) events.Add(e.ChangeType + " " + e.Name);
            };
            watcher.Created += changed; watcher.Changed += changed; watcher.Deleted += changed;
            watcher.Renamed += delegate(object sender, RenamedEventArgs e) {
                lock (events) events.Add("Renamed " + e.OldName + " -> " + e.Name);
            };
            watcher.Error += delegate(object sender, ErrorEventArgs e) {
                lock (events) events.Add("Watcher error: " + e.GetException());
            };
            watcher.EnableRaisingEvents = true;
            try { Console.WriteLine(AdventureController.NativeSmoke(assets)); }
            catch (Exception error) { failure = error; }
            // Allow asynchronous filesystem notifications to drain before the
            // final snapshot; no watcher event is an allowed smoke side effect.
            Thread.Sleep(150);
            watcher.EnableRaisingEvents = false;
        }
        TreeState after = Snapshot(archiveRoot);
        File.WriteAllText(Path.Combine(caseRoot, "after.txt"), Manifest(after), new UTF8Encoding(false));
        string[] observed;
        lock (events) observed = events.ToArray();
        File.WriteAllLines(Path.Combine(caseRoot, "write-events.txt"), observed, new UTF8Encoding(false));
        bool unchanged = Unchanged(before, after), noWrites = observed.Length == 0;
        Console.WriteLine(name + ": " + before.Files.Count + " -> " + after.Files.Count
            + " files; all bytes/timestamps/directories unchanged=" + unchanged + "; write events=" + observed.Length);
        if (failure != null) Console.Error.WriteLine(name + ": " + failure);
        if (!unchanged) Console.Error.WriteLine(name + ": an isolated user archive changed.");
        if (!noWrites) Console.Error.WriteLine(name + ": " + String.Join("; ", observed));
        return failure == null && unchanged && noWrites;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Expected asset directory and an unused evidence directory.");
            string outputRoot = Path.GetFullPath(args[1]);
            if (Directory.Exists(outputRoot)) throw new InvalidOperationException("Evidence directory must be new: " + outputRoot);
            Directory.CreateDirectory(outputRoot);
            Application.EnableVisualStyles();
            bool existing = RunCase(args[0], outputRoot, true);
            bool missing = RunCase(args[0], outputRoot, false);
            Console.WriteLine("Smoke isolation regression: " + (existing && missing ? "PASS" : "FAIL")
                + " (dependency-root injection; both pre-existing and missing lingyu archives).");
            return existing && missing ? 0 : 1;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
