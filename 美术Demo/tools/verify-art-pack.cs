using System;
using System.IO;
using System.Reflection;
internal static class VerifyArtPack
{
    static int Main(string[] args)
    {
        try
        {
            Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            MethodInfo verify = assembly.GetType("NetBunny.AdventureRenderer", true).GetMethod("ArtDemoSelfTest", BindingFlags.Static | BindingFlags.NonPublic);
            Console.WriteLine(verify.Invoke(null, new object[] { Path.GetFullPath(args[1]) }));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
