using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using NetBunny;

internal static class MemoryFixtureValidation
{
    static int Main(string[] args)
    {
        if(args.Length>0 && args[0]=="--child")
        {
            byte[] block=new byte[96*1024*1024];for(int i=0;i<block.Length;i+=4096)block[i]=1;
            Console.WriteLine("READY");Console.Out.Flush();Console.ReadLine();GC.KeepAlive(block);return 0;
        }
        try
        {
            ProcessStartInfo info=new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--child") {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardInput=true};
            using(Process child=Process.Start(info))
            {
                try
                {
                    if(child.StandardOutput.ReadLine()!="READY")throw new Exception("fixture not ready");
                    long before,after;
                    using(CancellationTokenSource stop=new CancellationTokenSource())
                    { stop.Cancel();if(MemoryReclaimer.TryTrim(child,stop.Token,out before,out after))throw new Exception("cancel ignored"); }
                    if(!MemoryReclaimer.TryTrim(child,CancellationToken.None,out before,out after))throw new Exception("working-set API failed");
                    if(before<=after)throw new Exception("fixture working set did not shrink");
                    Console.WriteLine("Memory native fixture: 3 checks passed; only newly-created test child trimmed. Working set "+before+" -> "+after+" bytes.");
                }
                finally { child.StandardInput.WriteLine();child.StandardInput.Flush();child.WaitForExit(5000); }
            }
            return 0;
        }
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
