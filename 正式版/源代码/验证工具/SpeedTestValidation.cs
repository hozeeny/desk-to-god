// Local-only real HTTP tests. Compile together with SpeedTest.cs and application sources,
// using /main:NetBunny.SpeedTestValidation. No Internet requests, firewall or limits.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetBunny
{
    internal static class SpeedTestValidation
    {
        private static int assertions;
        private static void Check(bool pass,string name){if(!pass)throw new Exception("FAILED: "+name);assertions++;Console.WriteLine("PASS "+name);}
        [STAThread] private static int Main(string[] args)
        {
            try
            {
                if(args.Length>0&&args[0]=="--probe")return ProbePublicNodes();
                if(args.Length>0&&args[0]=="--probe-upload")return ProbeSmallUploads();
                // Both client and fixture use synchronous sockets; give the local server
                // enough workers so pool ramp-up does not become the simulated bandwidth.
                int minWorkers,minIo;ThreadPool.GetMinThreads(out minWorkers,out minIo);ThreadPool.SetMinThreads(Math.Max(16,minWorkers),minIo);
                Console.WriteLine(SpeedTestView.SelfTest());
                using(var server=new LocalSpeedServer())
                {
                    var options=new SpeedOptions {AllowLoopback=true,DirectionSeconds=3,DirectionCap=2*1024*1024,IdleProbes=2,Streams=2};
                    var points=new List<SpeedProgress>();SpeedResult result=new SpeedRunner(server.Node,options,points.Add).Run(CancellationToken.None);
                    Console.WriteLine("cap test: download="+result.DownloadPayload+"; upload written="+result.UploadPayload+"; confirmed="+result.UploadConfirmed+"; server received="+server.Received+"; seconds="+result.ElapsedSeconds.ToString("0.000")+"; status="+result.Status+"; note="+result.Note);
                    Check(result.Status=="完成","local download/upload complete");
                    Check(result.DownloadPayload==options.DirectionCap,"download payload cap exact");
                    Check(result.UploadPayload==options.DirectionCap&&result.UploadConfirmed==options.DirectionCap,"upload counted only after server acknowledgment");
                    Check(result.DownloadBps>0&&result.UploadBps>0,"both rates positive");
                    Check(server.Received==options.DirectionCap,"server received entire generated upload");
                    Check(result.Idle.Samples.Count==2,"idle latency excludes warmup");
                    Check(points.Count>2&&result.Points.Count==points.Count,"live sample reports preserved");
                    Check(result.Note.Contains("流量上限"),"short cap-limited result marked");
                    string dir=Path.Combine(Path.GetTempPath(),"NetBunny-SpeedValidation-"+Guid.NewGuid().ToString("N"));string path=Path.Combine(dir,"history.json");
                    SpeedHistory history=new SpeedHistory(path);Check(history.Add(result),"history writes atomically");SpeedHistory loaded=new SpeedHistory(path);Check(loaded.Items.Count==1&&loaded.Items[0].UploadConfirmed==options.DirectionCap,"history reload preserves confirmed bytes");
                    Check(history.Add(new SpeedResult {Status="已停止",Time="cancelled-test"})&&new SpeedHistory(path).Items[0].Status=="已停止","cancelled history retained");
                    File.Delete(path);Directory.Delete(dir);
                }
                using(var server=new LocalSpeedServer {DownloadDelay=22})using(var cancel=new CancellationTokenSource())
                {
                    var options=new SpeedOptions {AllowLoopback=true,DirectionSeconds=4,DirectionCap=32*1024*1024,IdleProbes=1};cancel.CancelAfter(450);
                    Stopwatch watch=Stopwatch.StartNew();SpeedResult result=new SpeedRunner(server.Node,options,null).Run(cancel.Token);
                    Check(result.Status=="已停止","cancellation recorded");Check(watch.Elapsed.TotalSeconds<2.5,"active download cancellation bounded");Check(result.DownloadPayload>0&&result.DownloadPayload<options.DirectionCap,"partial consumed bytes preserved");Check(result.UploadPayload==0,"cancel never starts upload");
                }
                using(var server=new LocalSpeedServer {UploadAckDelay=2200})
                {
                    var options=new SpeedOptions {AllowLoopback=true,DirectionSeconds=.65,DirectionCap=256*1024,IdleProbes=1};
                    SpeedResult result=new SpeedRunner(server.Node,options,null).Run(CancellationToken.None);
                    Check(result.UploadPayload>0,"socket accepted generated upload bytes");Check(result.UploadConfirmed==0,"delayed server acknowledgment not mistaken for transfer completion");Check(!result.UploadBps.HasValue&&result.Status=="部分完成","unconfirmed upload has no fabricated speed");
                }
                using(var server=new LocalSpeedServer {UploadAckDelay=3000})using(var cancel=new CancellationTokenSource())
                {
                    var options=new SpeedOptions {AllowLoopback=true,DirectionSeconds=4,DirectionCap=256*1024,IdleProbes=1};cancel.CancelAfter(600);
                    Stopwatch watch=Stopwatch.StartNew();SpeedResult result=new SpeedRunner(server.Node,options,null).Run(cancel.Token);
                    Check(result.Status=="已停止"&&result.UploadPayload>0,"cancel active upload awaiting server confirmation");Check(watch.Elapsed.TotalSeconds<2.5&&result.UploadConfirmed==0,"upload cancellation bounded with no false confirmation");
                }
                using(var server=new LocalSpeedServer {RejectUpload=true})
                {
                    var options=new SpeedOptions {AllowLoopback=true,DirectionSeconds=1,DirectionCap=256*1024,IdleProbes=1};SpeedResult result=new SpeedRunner(server.Node,options,null).Run(CancellationToken.None);
                    Check(result.UploadConfirmed==0&&!result.UploadBps.HasValue,"HTTP error does not count as uploaded");Check(result.Note.Contains("失败"),"partial failure explained");
                }
                using(var server=new LocalSpeedServer {DownloadDelay=5})
                {
                    var options=new SpeedOptions {AllowLoopback=true,DirectionSeconds=.65,DirectionCap=32*1024*1024,IdleProbes=1};SpeedResult result=new SpeedRunner(server.Node,options,null).Run(CancellationToken.None);
                    Check(result.DownloadPayload>0&&result.DownloadPayload<options.DirectionCap,"time limit bounds slow transfer");Check(result.DownloadBps>0,"slow streaming data provides measured speed");Check(result.DownloadLatency.Samples.Count>0,"latency runs during download");
                }
                Console.WriteLine("SpeedTest local HTTP validation: "+assertions+" assertions passed.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
        private static int ProbePublicNodes()
        {
            Console.WriteLine(".NET runtime="+Environment.Version+"; default SecurityProtocol="+ServicePointManager.SecurityProtocol);
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            Console.WriteLine("Probe SecurityProtocol="+ServicePointManager.SecurityProtocol+"; certificate validation unchanged.");
            int reachable=0;
            foreach(SpeedNode node in SpeedNode.Defaults())
            {
                HttpWebRequest request=(HttpWebRequest)WebRequest.Create(node.PingUrl);request.Timeout=2500;request.ReadWriteTimeout=2500;request.AllowAutoRedirect=false;request.UserAgent="NetBunny/3.0 (bounded endpoint verification)";
                using(var cancel=new CancellationTokenSource(4000))using(cancel.Token.Register(delegate{request.Abort();}))
                {
                    Stopwatch watch=Stopwatch.StartNew();
                    try{using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()){Console.WriteLine(node.Name+": HTTP "+(int)response.StatusCode+"; "+watch.ElapsedMilliseconds+" ms; host="+new Uri(node.PingUrl).Host+"; explicit TLS1.2; body not downloaded");if((int)response.StatusCode>=200&&(int)response.StatusCode<300)reachable++;}}
                    catch(WebException ex){HttpWebResponse response=ex.Response as HttpWebResponse;Console.WriteLine(node.Name+": "+ex.Status+(response==null?"":"; HTTP "+(int)response.StatusCode)+(ex.InnerException==null?"":"; "+ex.InnerException.GetType().Name)+"; "+watch.ElapsedMilliseconds+" ms");if(response!=null)response.Dispose();}
                }
            }
            Console.WriteLine("Reachable "+reachable+"/3; three GET probes only, no upload or throughput test.");return 0;
        }
        private static int ProbeSmallUploads()
        {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            byte[] payload=new byte[16*1024];using(var random=System.Security.Cryptography.RandomNumberGenerator.Create())random.GetBytes(payload);
            int succeeded=0;var defaults=SpeedNode.Defaults();
            for(int i=0;i<2;i++)
            {
                SpeedNode node=defaults[i];HttpWebRequest request=(HttpWebRequest)WebRequest.Create(node.UploadUrl);request.Method="POST";request.ContentLength=payload.Length;request.ContentType="application/octet-stream";request.AllowWriteStreamBuffering=false;request.ServicePoint.Expect100Continue=false;request.Timeout=5000;request.ReadWriteTimeout=5000;request.AllowAutoRedirect=false;request.UserAgent="NetBunny/3.0 (bounded endpoint verification)";
                using(var cancel=new CancellationTokenSource(6000))using(cancel.Token.Register(delegate{request.Abort();}))
                {
                    int written=0;
                    try{using(Stream stream=request.GetRequestStream()){stream.Write(payload,0,payload.Length);written=payload.Length;}using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()){Console.WriteLine(node.Name+": HTTP "+(int)response.StatusCode+"; memory payload="+written+" bytes; TLS1.2; no local file read");if((int)response.StatusCode>=200&&(int)response.StatusCode<300)succeeded++;}}
                    catch(WebException ex){HttpWebResponse response=ex.Response as HttpWebResponse;Console.WriteLine(node.Name+": "+ex.Status+(response==null?"":"; HTTP "+(int)response.StatusCode)+"; bytes written="+written);if(response!=null)response.Dispose();}
                }
            }
            Console.WriteLine("Upload endpoints accepted "+succeeded+"/2; total payload at most 32768 bytes; this was not a throughput test.");return 0;
        }
    }
    internal sealed class LocalSpeedServer:IDisposable
    {
        private readonly TcpListener listener;private readonly CancellationTokenSource stop=new CancellationTokenSource();private readonly Task accept;
        private readonly List<TcpClient> clients=new List<TcpClient>();private long received;
        internal int DownloadDelay,UploadAckDelay;internal bool RejectUpload;
        internal long Received {get{return Interlocked.Read(ref received);}}
        internal readonly SpeedNode Node;
        internal LocalSpeedServer()
        {
            listener=new TcpListener(IPAddress.Loopback,0);listener.Start();string root="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
            Node=new SpeedNode {Name="Local validation",Location="loopback",Protocol="cloudflare",DownloadUrl=root+"/__down",UploadUrl=root+"/__up",PingUrl=root+"/__down?bytes=0"};
            accept=Task.Run(delegate{while(!stop.IsCancellationRequested){try{TcpClient c=listener.AcceptTcpClient();lock(clients)clients.Add(c);Task.Run(delegate{Serve(c);});}catch{if(stop.IsCancellationRequested)break;}}});
        }
        private void Serve(TcpClient client)
        {
            try
            {
                using(client)using(NetworkStream stream=client.GetStream())
                {
                    stream.ReadTimeout=5000;stream.WriteTimeout=3000;List<byte> header=new List<byte>();int b;
                    while((b=stream.ReadByte())>=0){header.Add((byte)b);int n=header.Count;if(n>65536)return;if(n>=4&&header[n-4]==13&&header[n-3]==10&&header[n-2]==13&&header[n-1]==10)break;}
                    string[] lines=Encoding.ASCII.GetString(header.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);if(lines.Length==0)return;string[] first=lines[0].Split(' ');if(first.Length<2)return;
                    int length=0;foreach(string line in lines)if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))Int32.TryParse(line.Substring(15).Trim(),out length);
                    byte[] buffer=new byte[16384];
                    if(first[0]=="POST")
                    {
                        int got=0;while(got<length){int read=stream.Read(buffer,0,Math.Min(buffer.Length,length-got));if(read==0)return;got+=read;Interlocked.Add(ref received,read);}
                        if(UploadAckDelay>0&&stop.Token.WaitHandle.WaitOne(UploadAckDelay))return;
                        byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 "+(RejectUpload?"500 Error":"200 OK")+"\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");stream.Write(response,0,response.Length);
                    }
                    else
                    {
                        int bytes=0;Uri uri=new Uri("http://localhost"+first[1]);foreach(string pair in uri.Query.TrimStart('?').Split('&'))if(pair.StartsWith("bytes="))Int32.TryParse(pair.Substring(6),out bytes);
                        byte[] response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nContent-Length: "+bytes+"\r\nConnection: close\r\n\r\n");stream.Write(response,0,response.Length);
                        for(int sent=0;sent<bytes;){if(DownloadDelay>0&&stop.Token.WaitHandle.WaitOne(DownloadDelay))return;int size=Math.Min(buffer.Length,bytes-sent);stream.Write(buffer,0,size);sent+=size;}
                    }
                }
            }
            catch(IOException){}catch(ObjectDisposedException){}catch(SocketException){}
            finally{lock(clients)clients.Remove(client);}
        }
        public void Dispose(){stop.Cancel();listener.Stop();lock(clients)foreach(TcpClient client in clients)client.Close();try{accept.Wait(1000);}catch{}stop.Dispose();}
    }
}
