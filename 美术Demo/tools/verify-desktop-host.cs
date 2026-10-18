using System;
using System.IO;
using System.Reflection;
internal static class VerifyDesktopHost
{
    static int Main(string[] args)
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type session = assembly.GetType("NetBunny.DesktopDemoSession", true);
            BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            MethodInfo profile = session.GetMethod("ProfilePath", flags);
            bool refused = false;
            try { profile.Invoke(null, new object[] { "../KnightAssistant" }); }
            catch (TargetInvocationException e) { refused = e.InnerException is ArgumentException; }
            if (!refused) throw new Exception("Profile traversal was accepted");
            string path = (string)profile.Invoke(null, new object[] { "player" });
            if (!path.EndsWith(Path.Combine("profiles", "player"))) throw new Exception("Default profile is not private");
            MethodInfo log = session.GetMethod("TryWriteDiagnostic", flags);
            if (log == null) throw new Exception("Save failure logging is unguarded; a diagnostic write can abort safe shutdown handling");
            string output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            string blocker = Path.Combine(output, "blocked-parent"); File.WriteAllText(blocker, "fixture");
            if ((bool)log.Invoke(null, new object[] { Path.Combine(blocker, "error.log"), "fixture" })) throw new Exception("Blocked diagnostic unexpectedly succeeded");
            string normal = Path.Combine(output, "working.log");
            if (!(bool)log.Invoke(null, new object[] { normal, "fixture" }) || File.ReadAllText(normal) != "fixture") throw new Exception("Valid diagnostic did not save");
            Console.WriteLine("Desktop host: 4 checks passed; private profile, traversal refusal, nonthrowing diagnostic failure and valid diagnostic.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
