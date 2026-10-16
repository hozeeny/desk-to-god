// HTTP throughput testing, C# 5 / .NET Framework 4.8. No external SDK or telemetry.
// Protocol references (checked 2026-10-13):
// https://github.com/cloudflare/speedtest/blob/main/README.md
// https://github.com/librespeed/speedtest/blob/master/backend/garbage.php
// https://github.com/librespeed/speedtest-cli/blob/master/README.md
// Nodes: https://librespeed.org/backend-servers/servers.php
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace NetBunny
{
    internal sealed class SpeedNode
    {
        public string Name, Location, DownloadUrl, UploadUrl, PingUrl, Protocol;
        public override string ToString() { return Name; }
        internal static SpeedNode Cloudflare()
        {
            return new SpeedNode { Name="Cloudflare · 自动边缘节点", Location="Anycast 自动路由；连接后显示服务返回的节点代码", Protocol="cloudflare", DownloadUrl="https://speed.cloudflare.com/__down", UploadUrl="https://speed.cloudflare.com/__up", PingUrl="https://speed.cloudflare.com/__down?bytes=0" };
        }
        internal static SpeedNode Libre(string name,string baseUrl,string down="garbage.php",string up="empty.php",string ping="empty.php")
        {
            Uri root=new Uri(baseUrl.TrimEnd('/')+"/");
            return new SpeedNode {Name=name,Location=name+"（服务方声明）",Protocol="librespeed",DownloadUrl=new Uri(root,down).AbsoluteUri,UploadUrl=new Uri(root,up).AbsoluteUri,PingUrl=new Uri(root,ping).AbsoluteUri};
        }
        internal static List<SpeedNode> Defaults()
        {
            return new List<SpeedNode>{Cloudflare(),Libre("Tokyo, Japan · A573","https://librespeed.a573.net/","backend/garbage.php","backend/empty.php","backend/empty.php"),Libre("Los Angeles, USA · Clouvider","https://la.speedtest.clouvider.net/backend/")};
        }
        internal static bool ValidUrl(string value,bool allowLoopback)
        {
            Uri uri;return Uri.TryCreate(value,UriKind.Absolute,out uri)&&String.IsNullOrEmpty(uri.UserInfo)&&String.IsNullOrEmpty(uri.Fragment)&&(uri.Scheme=="https"||(allowLoopback&&uri.Scheme=="http"&&uri.IsLoopback));
        }
        internal void Validate(bool allowLoopback=false)
        {
            if(!ValidUrl(DownloadUrl,allowLoopback)||!ValidUrl(UploadUrl,allowLoopback)||!ValidUrl(PingUrl,allowLoopback))throw new InvalidOperationException("测速节点需要有效的 HTTPS 地址。");
            if(Protocol!="cloudflare"&&Protocol!="librespeed")throw new InvalidOperationException("不支持的测速协议。");
        }
    }

    internal sealed class SpeedPoint { public double Seconds, DownloadBps, UploadBps; }
    internal sealed class SpeedLatency
    {
        public List<double> Samples=new List<double>(); public int Failures;
        public double? Median { get { return Samples.Count==0?(double?)null:SpeedMath.Median(Samples); } }
        public double? Jitter { get { if(Samples.Count<2)return null;double sum=0;for(int i=1;i<Samples.Count;i++)sum+=Math.Abs(Samples[i]-Samples[i-1]);return sum/(Samples.Count-1); } }
    }
    internal sealed class SpeedResult
    {
        public string Time, Node, Location, Endpoint, Mode, Status, Error, Preflight, Note;
        public double? DownloadBps,UploadBps;
        public long DownloadPayload, UploadPayload, UploadConfirmed;
        public double ElapsedSeconds;
        public SpeedLatency Idle=new SpeedLatency(), DownloadLatency=new SpeedLatency(), UploadLatency=new SpeedLatency();
        public List<SpeedPoint> Points=new List<SpeedPoint>();
    }
    internal sealed class SpeedProgress
    {
        public string Phase,Location,LatencyText;public int Percent;
        public long DownloadPayload,UploadPayload,UploadConfirmed;
        public double Seconds,DownloadBps,UploadBps;
    }
    internal sealed class SpeedOptions
    {
        internal double DirectionSeconds=6; internal long DirectionCap=64L*1024*1024;
        internal int IdleProbes=5,Streams=2; internal bool AllowLoopback=false;
        internal string Mode="快速";
    }
    internal static class SpeedMath
    {
        internal static double Median(IEnumerable<double> values) { double[] a=values.OrderBy(x=>x).ToArray();if(a.Length==0)return 0;return a.Length%2==0?(a[a.Length/2-1]+a[a.Length/2])/2:a[a.Length/2]; }
        internal static string Rate(double? bps)
        { return !bps.HasValue?"—":(bps.Value*8/1000000).ToString("0.00",CultureInfo.InvariantCulture)+" Mbps  /  "+(bps.Value/1000000).ToString("0.00",CultureInfo.InvariantCulture)+" MB/s"; }
        internal static string Bytes(long b) {return (b/1048576.0).ToString("0.00",CultureInfo.InvariantCulture)+" MiB";}
        internal static string Latency(SpeedLatency l) {return l.Median.HasValue?l.Median.Value.ToString("0.0")+" ms  ·  抖动 "+(l.Jitter.HasValue?l.Jitter.Value.ToString("0.0")+" ms":"样本不足")+"  ("+l.Samples.Count+" 次成功 / "+l.Failures+" 次失败)":"无有效样本"+(l.Failures>0?"（"+l.Failures+" 次失败）":"");}
    }

    internal sealed class SpeedRunner
    {
        private readonly SpeedNode node;private readonly SpeedOptions options;private readonly Action<SpeedProgress> report;
        private readonly byte[] uploadBuffer=new byte[64*1024];
        private long downPayload,upPayload,upConfirmed;private string edge="";
        private readonly string group="NetBunnySpeed-"+Guid.NewGuid().ToString("N");
        private readonly Stopwatch total=new Stopwatch();
        internal SpeedRunner(SpeedNode node,SpeedOptions options,Action<SpeedProgress> report)
        {
            ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
            this.node=node;this.options=options;this.report=report;
            using(RandomNumberGenerator rng=RandomNumberGenerator.Create())rng.GetBytes(uploadBuffer);
        }
        private HttpWebRequest Request(string url,string method,CancellationToken token,int timeout=3500,bool latency=false)
        {
            token.ThrowIfCancellationRequested();
            HttpWebRequest request=(HttpWebRequest)WebRequest.Create(url);
            request.Method=method;request.Timeout=timeout;request.ReadWriteTimeout=timeout;request.AllowAutoRedirect=false;
            request.AllowWriteStreamBuffering=false;request.AutomaticDecompression=DecompressionMethods.None;
            request.UserAgent="KnightAssistant/4.0 (user-initiated HTTP speed test)";request.Accept="*/*";
            request.Headers[HttpRequestHeader.AcceptEncoding]="identity";
            request.CachePolicy=new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            request.ConnectionGroupName=group+(latency?"-latency":"-transfer");
            request.KeepAlive=true;request.UseDefaultCredentials=false;
            request.ServicePoint.ConnectionLimit=Math.Max(8,request.ServicePoint.ConnectionLimit);request.ServicePoint.Expect100Continue=false;
            return request;
        }
        private static string Bust(string url) {return url+(url.IndexOf('?')<0?"?":"&")+"nb="+Guid.NewGuid().ToString("N");}
        private void CheckResponse(HttpWebResponse response)
        {
            if((int)response.StatusCode<200||(int)response.StatusCode>=300)throw new IOException("节点返回 HTTP "+(int)response.StatusCode);
            string ray=response.Headers["CF-Ray"];
            if(!String.IsNullOrEmpty(ray)) {int dash=ray.LastIndexOf('-');if(dash>=0&&ray.Length-dash<=8)edge="Cloudflare edge "+ray.Substring(dash+1)+"（服务返回代码）";}
        }
        internal double Ping(CancellationToken token)
        {
            HttpWebRequest request=Request(Bust(node.PingUrl),"GET",token,2000,true);
            using(token.Register(delegate {request.Abort();}))
            {
                Stopwatch watch=Stopwatch.StartNew();
                using(HttpWebResponse response=(HttpWebResponse)request.GetResponse()) {CheckResponse(response);return watch.Elapsed.TotalMilliseconds;}
            }
        }
        internal static string Probe(SpeedNode node,CancellationToken token)
        {
            node.Validate();SpeedRunner r=new SpeedRunner(node,new SpeedOptions(),null);SpeedLatency l=new SpeedLatency();
            try {for(int i=0;i<3;i++){token.ThrowIfCancellationRequested();try{l.Samples.Add(r.Ping(token));}catch(WebException){token.ThrowIfCancellationRequested();l.Failures++;}if(i<2&&token.WaitHandle.WaitOne(120))token.ThrowIfCancellationRequested();}
                return (l.Samples.Count>0?"节点可达 · ":"节点未响应 · ")+SpeedMath.Latency(l)+"\r\n"+(String.IsNullOrEmpty(r.edge)?node.Location:r.edge);}
            finally {r.CloseConnections();}
        }
        private void CloseConnections()
        {
            foreach(string url in new[]{node.PingUrl,node.DownloadUrl,node.UploadUrl})try {ServicePoint p=ServicePointManager.FindServicePoint(new Uri(url));p.CloseConnectionGroup(group+"-transfer");p.CloseConnectionGroup(group+"-latency");}catch{}
        }
        private void Emit(string phase,int percent,double down,double up,SpeedResult result)
        {
            var point=new SpeedPoint{Seconds=total.Elapsed.TotalSeconds,DownloadBps=Math.Max(0,down),UploadBps=Math.Max(0,up)};
            result.Points.Add(point);
            string latencyText;lock(result.DownloadLatency)lock(result.UploadLatency)latencyText="HTTP 空闲："+SpeedMath.Latency(result.Idle)+"\r\n下载时："+SpeedMath.Latency(result.DownloadLatency)+"\r\n上传时："+SpeedMath.Latency(result.UploadLatency);
            if(report!=null)report(new SpeedProgress{Phase=phase,Percent=Math.Max(0,Math.Min(100,percent)),Seconds=point.Seconds,DownloadBps=point.DownloadBps,UploadBps=point.UploadBps,DownloadPayload=Interlocked.Read(ref downPayload),UploadPayload=Interlocked.Read(ref upPayload),UploadConfirmed=Interlocked.Read(ref upConfirmed),Location=String.IsNullOrEmpty(edge)?node.Location:edge,LatencyText=latencyText});
        }
        internal SpeedResult Run(CancellationToken token)
        {
            node.Validate(options.AllowLoopback);
            SpeedResult result=new SpeedResult {Time=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),Node=node.Name,Location=node.Location,Endpoint=node.DownloadUrl,Mode=options.Mode,Status="进行中",Note="HTTP 吞吐参考值；双连接；1 MB = 1,000,000 字节。流量为应用读写负载，不含协议开销。"};
            total.Start();
            try
            {
                Emit("预热节点连接",0,0,0,result);
                try {Ping(token);}catch(WebException){token.ThrowIfCancellationRequested();}
                for(int i=0;i<options.IdleProbes;i++)
                {
                    token.ThrowIfCancellationRequested();Emit("空闲 HTTP 延迟",i*12/options.IdleProbes,0,0,result);
                    try {result.Idle.Samples.Add(Ping(token));}catch(WebException){token.ThrowIfCancellationRequested();result.Idle.Failures++;}
                    if(i<options.IdleProbes-1&&token.WaitHandle.WaitOne(120))token.ThrowIfCancellationRequested();
                }
                if(result.Idle.Samples.Count==0)throw new IOException("测速节点没有响应。请切换节点，或检查代理、VPN 和本工具的断网规则。");
                result.DownloadBps=Direction(false,result.DownloadLatency,result,token);
                token.ThrowIfCancellationRequested();
                result.UploadBps=Direction(true,result.UploadLatency,result,token);
                token.ThrowIfCancellationRequested();
                result.Status=result.DownloadBps.HasValue&&result.UploadBps.HasValue?"完成":"部分完成";
            }
            catch(OperationCanceledException) {result.Status="已停止";result.Error="用户停止；只保留已经完成的样本。";}
            catch(Exception ex) {if(token.IsCancellationRequested){result.Status="已停止";result.Error="用户停止；只保留已经完成的样本。";}else{result.Status="失败";result.Error=ex.Message;}}
            finally
            {
                result.DownloadPayload=Interlocked.Read(ref downPayload);result.UploadPayload=Interlocked.Read(ref upPayload);result.UploadConfirmed=Interlocked.Read(ref upConfirmed);
                result.ElapsedSeconds=total.Elapsed.TotalSeconds;result.Location=String.IsNullOrEmpty(edge)?node.Location:edge;
                Emit(result.Status,result.Status=="完成"?100:Math.Min(99,(int)(total.Elapsed.TotalSeconds/(options.DirectionSeconds*2+2)*100)),0,0,result);
                CloseConnections();total.Stop();
            }
            return result;
        }
        private double? Direction(bool upload,SpeedLatency latencies,SpeedResult result,CancellationToken userToken)
        {
            int phaseStart=upload?56:12;string phase=upload?"上传测速 · 服务器确认":"下载测速";
            long reserved=0,completed=0;int successes=0,failures=0;string lastError="";
            object reserveGate=new object();long phaseBaseline=upload?Interlocked.Read(ref upConfirmed):Interlocked.Read(ref downPayload);Stopwatch watch=Stopwatch.StartNew();
            using(CancellationTokenSource phaseCancel=CancellationTokenSource.CreateLinkedTokenSource(userToken))
            {
                phaseCancel.CancelAfter(TimeSpan.FromSeconds(options.DirectionSeconds));CancellationToken token=phaseCancel.Token;
                Task[] workers=new Task[options.Streams];
                for(int i=0;i<workers.Length;i++)workers[i]=Task.Factory.StartNew(delegate
                {
                    int nextSize=upload?64*1024:1024*1024;
                    while(!token.IsCancellationRequested)
                    {
                        int size;lock(reserveGate){long remaining=options.DirectionCap-reserved;if(remaining<=0)break;size=(int)Math.Min(nextSize,remaining);reserved+=size;}
                        Stopwatch transfer=Stopwatch.StartNew();
                        try
                        {
                            Transfer(upload,size,token);Interlocked.Add(ref completed,size);Interlocked.Increment(ref successes);
                            double rate=size/Math.Max(0.02,transfer.Elapsed.TotalSeconds);
                            nextSize=(int)Math.Max(64*1024,Math.Min(8*1024*1024,rate*0.75));
                            if(!upload&&node.Protocol=="librespeed")nextSize=Math.Max(1024*1024,(nextSize/(1024*1024))*(1024*1024));
                        }
                        catch(Exception ex)
                        {
                            if(token.IsCancellationRequested)break;
                            lastError=ex.Message;Interlocked.Increment(ref failures);break;
                        }
                    }
                },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                Task latency=Task.Factory.StartNew(delegate
                {
                    while(!token.IsCancellationRequested)
                    {
                        try {double ms=Ping(token);lock(latencies)latencies.Samples.Add(ms);}
                        catch {if(token.IsCancellationRequested)break;lock(latencies)latencies.Failures++;}
                        if(token.WaitHandle.WaitOne(350))break;
                    }
                },CancellationToken.None,TaskCreationOptions.LongRunning,TaskScheduler.Default);
                long previous=phaseBaseline;
                double baseTime=0;long baseBytes=previous;bool warmed=false;
                Queue<KeyValuePair<double,long>> rolling=new Queue<KeyValuePair<double,long>>();rolling.Enqueue(new KeyValuePair<double,long>(0,previous));
                while(!workers.All(t=>t.IsCompleted))
                {
                    Thread.Sleep(200);
                    double now=watch.Elapsed.TotalSeconds;long bytes=upload?Interlocked.Read(ref upConfirmed):Interlocked.Read(ref downPayload);
                    while(rolling.Count>1&&now-rolling.Peek().Key>1.5)rolling.Dequeue();
                    var from=rolling.Peek();double rate=(bytes-from.Value)/Math.Max(.05,now-from.Key);rolling.Enqueue(new KeyValuePair<double,long>(now,bytes));
                    if(!warmed&&now>=1&&now<options.DirectionSeconds/2&&bytes>previous) {baseTime=now;baseBytes=bytes;warmed=true;}
                    previous=bytes;
                    int progress=phaseStart+(int)(44*Math.Max(Math.Min(1,now/options.DirectionSeconds),Math.Min(1,Interlocked.Read(ref completed)/(double)options.DirectionCap)));
                    Emit(phase,progress,upload?0:rate,upload?rate:0,result);
                    if(token.IsCancellationRequested&&watch.Elapsed.TotalSeconds>options.DirectionSeconds+5)throw new TimeoutException("节点没有及时停止响应。");
                    if(userToken.IsCancellationRequested)phaseCancel.Cancel();
                }
                Task.WaitAll(workers);double elapsed=watch.Elapsed.TotalSeconds;phaseCancel.Cancel();
                try {latency.Wait(3000);}catch(AggregateException){}
                userToken.ThrowIfCancellationRequested();
                long payload=upload?Interlocked.Read(ref upConfirmed):Interlocked.Read(ref downPayload);
                // An acknowledgment may cover bytes sent before a moving-window boundary.
                // Upload therefore uses the entire phase, never an ACK-window extrapolation.
                double stable=upload?(payload-phaseBaseline)/Math.Max(.05,elapsed):(payload-baseBytes)/Math.Max(.05,elapsed-baseTime);
                if(!upload&&warmed&&(payload<=baseBytes||elapsed-baseTime<.4))stable=(payload-phaseBaseline)/Math.Max(.05,elapsed);
                string direction=upload?"上传":"下载";
                if(reserved>=options.DirectionCap)result.Note+=" "+direction+"达到流量上限"+(elapsed<2?"，样本较短，仅供参考。":"。");
                if(failures>0)result.Note+=" "+direction+"部分请求失败："+lastError+"。";
                if(successes==0)
                {
                    if(!upload&&payload>0){result.Note+=" 下载在时间上限内未完成整块，仅按已接收数据估算。";return stable;}
                    result.Note+=" "+direction+"没有完整传输样本，结果不可用。";return null;
                }
                return Math.Max(0,stable);
            }
        }
        private void Transfer(bool upload,int bytes,CancellationToken token)
        {
            string url;
            if(upload)url=node.UploadUrl;
            else url=node.DownloadUrl+(node.DownloadUrl.IndexOf('?')<0?"?":"&")+(node.Protocol=="cloudflare"?"bytes="+bytes:"ckSize="+Math.Max(1,(int)Math.Ceiling(bytes/1048576.0)));
            HttpWebRequest request=Request(Bust(url),upload?"POST":"GET",token,5000);
            if(upload){request.ContentType="application/octet-stream";request.ContentLength=bytes;request.SendChunked=false;}
            using(token.Register(delegate {request.Abort();}))
            {
                if(upload)
                {
                    using(Stream output=request.GetRequestStream())
                    {int written=0;while(written<bytes){token.ThrowIfCancellationRequested();int n=Math.Min(uploadBuffer.Length,bytes-written);output.Write(uploadBuffer,0,n);written+=n;Interlocked.Add(ref upPayload,n);}}
                    using(HttpWebResponse response=(HttpWebResponse)request.GetResponse())
                    {
                        CheckResponse(response);
                        using(Stream input=response.GetResponseStream())
                        {byte[] tiny=new byte[1024];int read,totalRead=0;while((read=input.Read(tiny,0,tiny.Length))>0){totalRead+=read;if(totalRead>65536)throw new IOException("上传响应过大，停止测试。");token.ThrowIfCancellationRequested();}}
                    }
                    Interlocked.Add(ref upConfirmed,bytes);
                }
                else
                {
                    using(HttpWebResponse response=(HttpWebResponse)request.GetResponse())
                    {
                        CheckResponse(response);
                        if(!String.IsNullOrEmpty(response.ContentEncoding)&&!response.ContentEncoding.Equals("identity",StringComparison.OrdinalIgnoreCase))throw new IOException("节点压缩了测试数据，无法可靠测速。");
                        if(response.ContentType.IndexOf("text/html",StringComparison.OrdinalIgnoreCase)>=0)throw new IOException("节点返回网页而非测试数据，请切换节点。");
                        using(Stream input=response.GetResponseStream())
                        {byte[] buffer=new byte[64*1024];int read=0;while(read<bytes){token.ThrowIfCancellationRequested();int n=input.Read(buffer,0,Math.Min(buffer.Length,bytes-read));if(n<=0)throw new IOException("节点提前结束下载。");read+=n;Interlocked.Add(ref downPayload,n);}}
                    }
                }
            }
        }
    }

    internal sealed class SpeedHistory
    {
        internal static readonly string DirectoryPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"NetBunny","NetworkCenter");
        internal readonly List<SpeedResult> Items=new List<SpeedResult>(); internal string Error="";private readonly string path;
        internal SpeedHistory(string path=null)
        {
            this.path=path??Path.Combine(DirectoryPath,"speed-history.json");
            try {if(File.Exists(this.path)){if(new FileInfo(this.path).Length>8000000)throw new IOException("文件超过 8 MB 限制");var saved=new JavaScriptSerializer {MaxJsonLength=8000000}.Deserialize<List<SpeedResult>>(File.ReadAllText(this.path,Encoding.UTF8));if(saved!=null)Items.AddRange(saved.Where(x=>x!=null).Take(200).Select(Clean));}}
            catch(Exception ex){Error="历史记录读取失败，原文件保留："+ex.Message;}
        }
        internal static SpeedResult Clean(SpeedResult r)
        {
            r.Time=r.Time??"时间未知";r.Node=r.Node??"节点未知";r.Location=r.Location??"位置未知";r.Endpoint=r.Endpoint??"";r.Status=r.Status??"记录不完整";r.Mode=r.Mode??"";r.Note=r.Note??"";
            if(r.DownloadBps.HasValue&&(!Finite(r.DownloadBps.Value)||r.DownloadBps<0))r.DownloadBps=null;
            if(r.UploadBps.HasValue&&(!Finite(r.UploadBps.Value)||r.UploadBps<0))r.UploadBps=null;
            r.Idle=CleanLatency(r.Idle);r.DownloadLatency=CleanLatency(r.DownloadLatency);r.UploadLatency=CleanLatency(r.UploadLatency);
            r.Points=(r.Points??new List<SpeedPoint>()).Where(p=>p!=null&&Finite(p.Seconds)&&Finite(p.DownloadBps)&&Finite(p.UploadBps)&&p.Seconds>=0&&p.DownloadBps>=0&&p.UploadBps>=0).Take(1200).OrderBy(p=>p.Seconds).ToList();
            if(!Finite(r.ElapsedSeconds)||r.ElapsedSeconds<0)r.ElapsedSeconds=0;return r;
        }
        private static bool Finite(double value){return !Double.IsNaN(value)&&!Double.IsInfinity(value);}
        private static SpeedLatency CleanLatency(SpeedLatency l){l=l??new SpeedLatency();l.Samples=(l.Samples??new List<double>()).Where(v=>Finite(v)&&v>=0).Take(200).ToList();l.Failures=Math.Max(0,l.Failures);return l;}
        internal bool Add(SpeedResult item)
        {
            Items.Insert(0,item);if(Items.Count>200)Items.RemoveRange(200,Items.Count-200);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if(!String.IsNullOrEmpty(Error)&&File.Exists(path))File.Copy(path,path+".unreadable-"+DateTime.Now.ToString("yyyyMMdd-HHmmss"),false);
                string temp=path+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllText(temp,new JavaScriptSerializer {MaxJsonLength=8000000}.Serialize(Items),new UTF8Encoding(false));
                if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);Error="";return true;
            }
            catch(Exception ex){Error="测速已完成，但历史保存失败："+ex.Message;return false;}
        }
    }

    internal sealed class SpeedCurve : Control
    {
        internal List<SpeedPoint> Points=new List<SpeedPoint>();internal bool Megabytes;
        internal SpeedCurve(){DoubleBuffered=true;BackColor=Color.White;}
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);Graphics g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;Rectangle plot=new Rectangle(66,31,Math.Max(1,Width-88),Math.Max(1,Height-62));
            double end=Math.Max(15,Points.Count>0?Points.Max(p=>p.Seconds):0);double peak=Math.Max(Megabytes?1:8,Points.Count>0?Points.Max(p=>Math.Max(p.DownloadBps,p.UploadBps))*(Megabytes?1:8)/1000000:0);
            peak=Math.Ceiling(peak*1.15);using(Font small=Art.Font(8))using(Brush muted=new SolidBrush(Art.Muted))using(Pen line=new Pen(Art.Line))
            {
                g.DrawString("实时吞吐 · "+(Megabytes?"MB/s":"Mbps"),small,muted,12,8);
                g.DrawString("↓ 下载",small,Brushes.SeaGreen,Math.Max(180,Width-180),8);g.DrawString("↑ 上传确认",small,Brushes.MediumPurple,Math.Max(245,Width-100),8);
                for(int i=0;i<=4;i++){float y=plot.Bottom-i*plot.Height/4f;g.DrawLine(line,plot.Left,y,plot.Right,y);g.DrawString((peak*i/4).ToString("0.#"),small,muted,4,y-7);}
                for(int i=0;i<=4;i++){float x=plot.Left+i*plot.Width/4f;g.DrawString((end*i/4).ToString("0")+"s",small,muted,x-8,plot.Bottom+6);}
            }
            for(int direction=0;direction<2;direction++)
            {
                if(Points.Count<2)continue;PointF[] points=Points.Select(p=>new PointF(plot.Left+(float)(p.Seconds/end*plot.Width),plot.Bottom-(float)((direction==0?p.DownloadBps:p.UploadBps)*(Megabytes?1:8)/1000000/peak*plot.Height))).ToArray();
                using(Pen pen=new Pen(direction==0?Art.Mint:Art.Purple,2.5f))g.DrawLines(pen,points);
            }
        }
    }

    internal sealed class SpeedComboBox : ComboBox
    {
        internal SpeedComboBox(){DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=22;DropDownStyle=ComboBoxStyle.DropDownList;}
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            e.DrawBackground();object value=e.Index>=0&&e.Index<Items.Count?Items[e.Index]:SelectedItem;
            if(value!=null)TextRenderer.DrawText(e.Graphics,value.ToString(),Font,new Rectangle(e.Bounds.X+4,e.Bounds.Y,e.Bounds.Width-5,e.Bounds.Height),(e.State&DrawItemState.Selected)!=0?SystemColors.HighlightText:Art.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            // Native ComboBox printing omits its edit/selection child in DrawToBitmap.
            // Paint only the selected text for print messages; interactive behavior is native.
            if((message.Msg==0x317||message.Msg==0x318)&&message.WParam!=IntPtr.Zero&&SelectedItem!=null)
            {
                using(Graphics g=Graphics.FromHdc(message.WParam))
                {
                    Rectangle box=new Rectangle(3,3,Math.Max(1,ClientSize.Width-SystemInformation.VerticalScrollBarWidth-7),Math.Max(1,ClientSize.Height-6));
                    using(Brush brush=new SolidBrush(Color.White))g.FillRectangle(brush,box);
                    TextRenderer.DrawText(g,SelectedItem.ToString(),Font,box,Art.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
                }
            }
        }
    }
    internal sealed class SpeedTestView : UserControl
    {
        private readonly bool preview;private readonly Func<List<AppInfo>> getApps;private readonly Func<string> getRuleSummary;private readonly Action<bool> onTestActivity;
        private readonly ComboBox nodes=new SpeedComboBox(),mode=new SpeedComboBox(),units=new SpeedComboBox();
        private readonly NumericUpDown cap=new NumericUpDown();
        private readonly SoftButton start=new SoftButton(),cancel=new SoftButton(),probe=new SoftButton(),refreshNodes=new SoftButton(),addNode=new SoftButton(),refreshPreflight=new SoftButton();
        private readonly Label title=new Label(),nodeInfo=new Label(),optionsHint=new Label(),status=new Label(),down=new Label(),up=new Label(),latency=new Label();
        private readonly TextBox preflight=new TextBox(),details=new TextBox();
        private readonly ProgressBar progress=new ProgressBar();private readonly SpeedCurve curve=new SpeedCurve();private readonly ListView historyList=new ListView();
        private readonly Panel content=new Panel();private readonly RoundPanel configCard=new RoundPanel(),resultCard=new RoundPanel(),historyCard=new RoundPanel();
        private readonly SpeedHistory history;private CancellationTokenSource cts;private bool closed,busy;private SpeedResult current;
        private readonly System.Windows.Forms.Timer preflightTimer=new System.Windows.Forms.Timer();
        public bool IsRunning {get{return busy;}}
        internal SpeedTestView(bool preview,Func<List<AppInfo>> getApps,Func<string> getRuleSummary,Action<bool> onTestActivity)
        {
            this.preview=preview;this.getApps=getApps;this.getRuleSummary=getRuleSummary;this.onTestActivity=onTestActivity;
            BackColor=Art.Bg;Font=Art.Font(9);ForeColor=Art.Ink;AutoScroll=true;content.BackColor=Art.Bg;Controls.Add(content);
            history=preview?new SpeedHistory(Path.Combine(Path.GetTempPath(),"NetBunny-preview-no-history-"+Guid.NewGuid().ToString("N"))):new SpeedHistory();
            title.Text="跑一圈，看看网络的真本领";title.Font=Art.Font(18,FontStyle.Bold);title.AutoSize=false;
            content.Controls.AddRange(new Control[]{title,configCard,resultCard,historyCard});
            foreach(ComboBox c in new[]{nodes,mode,units}){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Font=Art.Font(9);}
            nodes.Items.AddRange(SpeedNode.Defaults().ToArray());nodes.SelectedIndex=0;nodes.DropDownWidth=620;
            mode.Items.AddRange(new object[]{"快速 · 每向 6 秒","完整 · 每向 15 秒"});mode.SelectedIndex=0;
            units.Items.AddRange(new object[]{"曲线 · Mbps","曲线 · MB/s"});units.SelectedIndex=0;
            cap.Minimum=8;cap.Maximum=512;cap.Value=64;cap.Increment=8;cap.TextAlign=HorizontalAlignment.Right;
            SetupButton(start,"开始测速",Art.Purple);SetupButton(cancel,"停止",Art.Coral);cancel.Enabled=false;
            SetupButton(probe,"检查节点",Art.Mint);SetupButton(refreshNodes,"刷新节点",Art.Purple);SetupButton(addNode,"添加自建",Art.Purple);SetupButton(refreshPreflight,"刷新影响因素",Art.Purple);
            foreach(SoftButton b in new[]{refreshNodes,addNode,refreshPreflight}){b.FillColor=Color.FromArgb(235,230,251);b.ForeColor=Art.Purple;}
            foreach(Label label in new[]{nodeInfo,optionsHint,status}){label.ForeColor=Art.Muted;label.AutoEllipsis=true;label.BackColor=Color.Transparent;}
            foreach(TextBox box in new[]{preflight,details}){box.Multiline=true;box.ReadOnly=true;box.ScrollBars=ScrollBars.Vertical;box.BorderStyle=BorderStyle.None;box.BackColor=Color.White;box.ForeColor=Art.Muted;}
            down.Font=up.Font=Art.Font(13,FontStyle.Bold);down.ForeColor=Art.Mint;up.ForeColor=Art.Purple;latency.Font=Art.Font(8.8f);down.BackColor=up.BackColor=latency.BackColor=Color.Transparent;
            down.Text="↓ 下载   —";up.Text="↑ 上传   —";latency.Text="HTTP 延迟：空闲 —  /  下载 —  /  上传 —";
            status.Text="准备好后开始；测试会使用所选节点收发临时数据。";
            optionsHint.Text="每个方向流量上限（MiB）   ·   达到时间或流量上限即停止。上传数据在内存生成。";
            configCard.Controls.AddRange(new Control[]{nodes,nodeInfo,probe,refreshNodes,addNode,mode,cap,optionsHint,start,cancel,preflight,refreshPreflight});
            resultCard.Controls.AddRange(new Control[]{down,up,units,status,progress,latency,curve});
            historyCard.Controls.AddRange(new Control[]{historyList,details});
            historyList.View=View.Details;historyList.FullRowSelect=true;historyList.MultiSelect=false;historyList.HideSelection=false;historyList.BorderStyle=BorderStyle.None;
            historyList.Columns.Add("时间",145);historyList.Columns.Add("节点",240);historyList.Columns.Add("下载 Mbps",105);historyList.Columns.Add("上传 Mbps",105);historyList.Columns.Add("状态",95);
            nodes.SelectedIndexChanged+=delegate{if(!busy)ShowNode();};mode.SelectedIndexChanged+=delegate{cap.Value=mode.SelectedIndex==1?256:64;};units.SelectedIndexChanged+=delegate{curve.Megabytes=units.SelectedIndex==1;curve.Invalidate();};
            start.Click+=async delegate{await StartTest();};cancel.Click+=delegate{Cancel();};probe.Click+=async delegate{await ProbeNode();};refreshNodes.Click+=async delegate{await RefreshNodes();};addNode.Click+=delegate{AddNode();};refreshPreflight.Click+=delegate{RefreshPreflight();};
            historyList.SelectedIndexChanged+=delegate{if(!busy&&historyList.SelectedItems.Count>0){SpeedResult selected=historyList.SelectedItems[0].Tag as SpeedResult;if(selected!=null)ShowResult(selected);}};
            Resize+=delegate{LayoutView();};preflightTimer.Interval=5000;preflightTimer.Tick+=delegate{if(!busy&&Visible)RefreshPreflight();};
            ShowNode();RefreshPreflight();FillHistory();LayoutView();if(!preview)preflightTimer.Start();
            Disposed+=delegate{Shutdown();};
        }
        private static void SetupButton(SoftButton button,string text,Color color){button.Text=text;button.FillColor=color;button.Font=Art.Font(8.5f,FontStyle.Bold);}
        private void LayoutView()
        {
            int w=Math.Max(760,ClientSize.Width-25);content.Size=new Size(w,1070);AutoScrollMinSize=new Size(760,1070);
            title.SetBounds(20,12,w-40,38);configCard.SetBounds(15,57,w-30,276);resultCard.SetBounds(15,345,w-30,396);historyCard.SetBounds(15,753,w-30,303);
            int cw=configCard.Width;nodes.SetBounds(18,17,cw-356,29);probe.SetBounds(cw-326,14,96,33);refreshNodes.SetBounds(cw-222,14,96,33);addNode.SetBounds(cw-118,14,100,33);
            nodeInfo.SetBounds(20,53,cw-40,42);mode.SetBounds(20,103,168,28);cap.SetBounds(198,104,78,27);optionsHint.SetBounds(288,100,cw-490,44);start.SetBounds(cw-194,99,94,36);cancel.SetBounds(cw-94,99,76,36);
            preflight.SetBounds(20,154,cw-180,105);refreshPreflight.SetBounds(cw-150,160,132,34);
            down.SetBounds(20,12,cw/2-35,49);up.SetBounds(cw/2,12,cw/2-20,49);units.SetBounds(cw-149,64,130,27);status.SetBounds(20,64,cw-185,29);progress.SetBounds(20,99,cw-40,8);
            latency.SetBounds(20,118,cw-40,63);curve.SetBounds(15,184,cw-30,198);
            historyList.SetBounds(18,18,cw-36,142);historyList.Columns[1].Width=Math.Max(160,cw-515);details.SetBounds(20,169,cw-40,119);
        }
        private void ShowNode()
        {
            SpeedNode n=nodes.SelectedItem as SpeedNode;if(n!=null)nodeInfo.Text=n.Location+"\r\n"+new Uri(n.DownloadUrl).Host+" · 结果仅代表到此节点的表现，可先检查节点再选择。";
        }
        private string PreflightText()
        {
            StringBuilder sb=new StringBuilder();sb.Append("测速前影响因素：");
            try{sb.Append(getRuleSummary!=null?getRuleSummary():"规则状态未知");}catch{sb.Append("规则状态读取失败");}
            try
            {
                var apps=getApps!=null?getApps():null;if(apps!=null){var active=apps.Where(a=>a.DownloadBps+a.UploadBps>16384&&!String.Equals(a.Path,Application.ExecutablePath,StringComparison.OrdinalIgnoreCase)).OrderByDescending(a=>a.DownloadBps+a.UploadBps).Take(3).ToList();sb.Append("\r\n后台传输：");sb.Append(active.Count==0?"未发现明显传输（仅限当前监测范围）":String.Join("；",active.Select(a=>a.Name+" "+SpeedMath.Rate(a.DownloadBps+a.UploadBps))));}
                var vpns=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up&&(n.NetworkInterfaceType==NetworkInterfaceType.Ppp||n.NetworkInterfaceType==NetworkInterfaceType.Tunnel||System.Text.RegularExpressions.Regex.IsMatch(n.Name+" "+n.Description,"vpn|wireguard|wintun|openvpn|tailscale|zerotier",System.Text.RegularExpressions.RegexOptions.IgnoreCase))).Select(n=>n.Name).ToArray();
                sb.Append("\r\nVPN / 虚拟网卡："+(vpns.Length>0?String.Join("、",vpns):"未识别到（不能保证未使用 VPN）"));
                Microsoft.Win32.RegistryKey key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings");
                using(key){bool proxy=key!=null&&Convert.ToInt32(key.GetValue("ProxyEnable",0))!=0;string pac=key==null?"":Convert.ToString(key.GetValue("AutoConfigURL",""));sb.Append("；系统代理："+(proxy||!String.IsNullOrEmpty(pac)?"已配置":"未发现手动配置"));}
            }
            catch{sb.Append("\r\n部分网络配置无法读取。");}
            sb.Append("\r\n可先自行调整再开始；测速不会改动限速、VPN 或断网规则。HTTP 使用当前系统代理设置。");return sb.ToString();
        }
        private void RefreshPreflight(){preflight.Text=preview?"测速前影响因素：示例 · 无断网 / 限速规则\r\n后台传输：浏览器 1.80 Mbps / 0.23 MB/s\r\nVPN / 系统代理：示例状态\r\n可先自行调整再开始；测速不会改动网络设置。":PreflightText();}
        private void SetBusy(bool value,bool transfers)
        {
            busy=value;start.Enabled=probe.Enabled=refreshNodes.Enabled=addNode.Enabled=nodes.Enabled=mode.Enabled=cap.Enabled=!value;cancel.Enabled=value;historyList.Enabled=!value;
            if(transfers&&onTestActivity!=null)onTestActivity(value);
        }
        private async Task StartTest()
        {
            if(busy||closed||preview)return;SpeedNode node=nodes.SelectedItem as SpeedNode;if(node==null)return;
            RefreshPreflight();string factors=preflight.Text;SpeedOptions options=new SpeedOptions{DirectionSeconds=mode.SelectedIndex==1?15:6,DirectionCap=(long)cap.Value*1024*1024,IdleProbes=mode.SelectedIndex==1?10:5,Mode=mode.SelectedIndex==1?"完整":"快速"};
            cts=new CancellationTokenSource();CancellationToken token=cts.Token;SetBusy(true,true);curve.Points=new List<SpeedPoint>();progress.Value=0;down.Text="↓ 下载   准备中";up.Text="↑ 上传   准备中";latency.Text="正在测试空闲 HTTP 延迟…";details.Text="本次上限：下载 "+cap.Value+" MiB + 上传 "+cap.Value+" MiB。协议开销另计。";
            try
            {
                var runner=new SpeedRunner(node,options,delegate(SpeedProgress p){Post(delegate{UpdateProgress(p);});});
                SpeedResult result=await Task.Run(delegate{return runner.Run(token);});result.Preflight=factors;
                bool saved=history.Add(result);if(closed)return;ShowResult(result);FillHistory();if(!saved)details.Text+="\r\n"+history.Error;
            }
            catch(Exception ex){if(!closed)status.Text="测速未能开始："+ex.Message;}
            finally{if(cts!=null){cts.Dispose();cts=null;}if(!closed)SetBusy(false,true);else if(onTestActivity!=null)onTestActivity(false);}
        }
        private void Post(Action action){if(closed||IsDisposed||!IsHandleCreated)return;try{BeginInvoke((MethodInvoker)delegate{if(!closed&&!IsDisposed)action();});}catch(InvalidOperationException){}}
        private void UpdateProgress(SpeedProgress p)
        {
            progress.Value=p.Percent;status.Text=p.Phase+" · "+p.Seconds.ToString("0.0")+" 秒 · 负载 ↓ "+SpeedMath.Bytes(p.DownloadPayload)+" ↑ "+SpeedMath.Bytes(p.UploadPayload);
            if(p.Phase.StartsWith("下载"))down.Text=MetricText("↓ 下载",p.DownloadBps);if(p.Phase.StartsWith("上传"))up.Text=MetricText("↑ 上传",p.UploadBps);latency.Text=p.LatencyText;
            nodeInfo.Text=p.Location+"\r\n"+((SpeedNode)nodes.SelectedItem).DownloadUrl;
            curve.Points.Add(new SpeedPoint{Seconds=p.Seconds,DownloadBps=p.DownloadBps,UploadBps=p.UploadBps});curve.Invalidate();
        }
        private void ShowResult(SpeedResult result)
        {
            current=result;down.Text=MetricText("↓ 下载",result.DownloadBps);up.Text=MetricText("↑ 上传",result.UploadBps);
            status.Text=result.Status+" · "+result.ElapsedSeconds.ToString("0.0")+" 秒 · 负载 ↓ "+SpeedMath.Bytes(result.DownloadPayload)+" ↑ "+SpeedMath.Bytes(result.UploadPayload);
            latency.Text="HTTP 空闲："+SpeedMath.Latency(result.Idle)+"\r\n下载时："+SpeedMath.Latency(result.DownloadLatency)+"\r\n上传时："+SpeedMath.Latency(result.UploadLatency);
            curve.Points=result.Points??new List<SpeedPoint>();curve.Invalidate();nodeInfo.Text=result.Location+"\r\n"+result.Endpoint;
            details.Text=result.Time+" · "+result.Node+" · "+result.Mode+"\r\n"+result.Note+"\r\n上传确认 "+SpeedMath.Bytes(result.UploadConfirmed)+"；写入中的数据不作为已确认上传速度。HTTP 延迟已剔除首次预热，仍包含服务器与连接处理，不是 ICMP ping。"+(String.IsNullOrEmpty(result.Error)?"":"\r\n"+result.Error)+(String.IsNullOrEmpty(result.Preflight)?"":"\r\n—— 当时的影响因素 ——\r\n"+result.Preflight);
        }
        private static string MetricText(string title,double? bytes){return title+"   "+(!bytes.HasValue?"—":(bytes.Value*8/1000000).ToString("0.00")+" Mbps\r\n           "+(bytes.Value/1000000).ToString("0.00")+" MB/s");}
        private void FillHistory()
        {
            historyList.BeginUpdate();historyList.Items.Clear();foreach(SpeedResult r in history.Items){ListViewItem item=new ListViewItem(new[]{r.Time,r.Node,r.DownloadBps.HasValue?(r.DownloadBps.Value*8/1000000).ToString("0.00"):"—",r.UploadBps.HasValue?(r.UploadBps.Value*8/1000000).ToString("0.00"):"—",r.Status});item.Tag=r;historyList.Items.Add(item);}historyList.EndUpdate();
            if(history.Items.Count==0)details.Text=String.IsNullOrEmpty(history.Error)?"测速历史 · 最近 200 次保存在本机。完成、失败和停止的测试均会标记状态。\r\n先检查节点的响应，再选择稳定、就近的服务。HTTP 吞吐并非运营商线路容量保证。":history.Error;
        }
        private async Task ProbeNode()
        {
            if(busy||closed||preview)return;SpeedNode n=nodes.SelectedItem as SpeedNode;if(n==null)return;cts=new CancellationTokenSource();CancellationToken token=cts.Token;SetBusy(true,false);status.Text="正在检查所选节点，最多 3 次小请求…";
            try {string text=await Task.Run(delegate{return SpeedRunner.Probe(n,token);});if(!closed){nodeInfo.Text=text;status.Text="节点检查完成，可开始测速或切换节点。";}}
            catch(Exception ex){if(!closed)status.Text=token.IsCancellationRequested?"节点检查已停止":"节点检查失败："+ex.Message;}
            finally{if(cts!=null){cts.Dispose();cts=null;}if(!closed)SetBusy(false,false);}
        }
        private async Task RefreshNodes()
        {
            if(busy||closed||preview)return;cts=new CancellationTokenSource();CancellationToken token=cts.Token;SetBusy(true,false);status.Text="正在读取 LibreSpeed 官方节点目录…";
            try
            {
                List<SpeedNode> fetched=await Task.Run(delegate
                {
                    ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
                    HttpWebRequest request=(HttpWebRequest)WebRequest.Create("https://librespeed.org/backend-servers/servers.php");request.Timeout=6000;request.ReadWriteTimeout=6000;request.AllowAutoRedirect=false;request.UserAgent="KnightAssistant/4.0";
                    using(token.Register(delegate{request.Abort();}))using(WebResponse response=request.GetResponse())using(Stream stream=response.GetResponseStream())using(MemoryStream memory=new MemoryStream())
                    {byte[] buffer=new byte[4096];int n;while((n=stream.Read(buffer,0,buffer.Length))>0){token.ThrowIfCancellationRequested();if(memory.Length+n>256*1024)throw new IOException("节点目录过大。");memory.Write(buffer,0,n);}var data=new JavaScriptSerializer().Deserialize<List<Dictionary<string,object>>>(Encoding.UTF8.GetString(memory.ToArray()));List<SpeedNode> list=new List<SpeedNode>();
                        foreach(var row in data){try{string server=Convert.ToString(row["server"]);if(server.StartsWith("//"))server="https:"+server;SpeedNode item=SpeedNode.Libre(Convert.ToString(row["name"]),server,Convert.ToString(row["dlURL"]),Convert.ToString(row["ulURL"]),Convert.ToString(row["pingURL"]));item.Validate();list.Add(item);}catch{}}
                        return list;
                    }
                });
                if(!closed){if(fetched.Count==0)throw new IOException("目录没有可用的 HTTPS 节点。");nodes.Items.Clear();nodes.Items.Add(SpeedNode.Cloudflare());nodes.Items.AddRange(fetched.ToArray());nodes.SelectedIndex=0;status.Text="已载入 "+fetched.Count+" 个服务声明的节点；选择后可检查响应。";ShowNode();}
            }
            catch(Exception ex){if(!closed)status.Text=token.IsCancellationRequested?"节点刷新已停止":"刷新失败，保留现有节点："+ex.Message;}
            finally{if(cts!=null){cts.Dispose();cts=null;}if(!closed)SetBusy(false,false);}
        }
        private void AddNode()
        {
            if(preview)return;using(Form dialog=new Form {Text="添加自己的测速节点",ClientSize=new Size(530,290),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Art.Font(9),BackColor=Art.Bg})
            {
                Label hint=new Label{Text="支持兼容 Cloudflare 或 LibreSpeed 的 HTTPS 服务。\r\n地址填写后端目录；位置按你的填写显示。",Bounds=new Rectangle(18,15,492,46)};
                ComboBox protocol=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(18,72,492,28)};protocol.Items.AddRange(new object[]{"LibreSpeed · garbage.php / empty.php","Cloudflare · __down / __up"});protocol.SelectedIndex=0;
                TextBox name=new TextBox{Text="我的节点（位置未填写）",Bounds=new Rectangle(18,113,492,28)};TextBox url=new TextBox{Text="https://",Bounds=new Rectangle(18,154,492,28)};
                Label error=new Label{ForeColor=Art.Coral,Bounds=new Rectangle(18,188,492,36)};SoftButton save=new SoftButton{Text="添加",Bounds=new Rectangle(398,238,112,35)};
                dialog.Controls.AddRange(new Control[]{hint,protocol,name,url,error,save});save.Click+=delegate
                {
                    try{if(String.IsNullOrWhiteSpace(name.Text))throw new InvalidOperationException("请填写节点名称 / 位置。");SpeedNode node=SpeedNode.Libre(name.Text.Trim(),url.Text.Trim());node.Location=name.Text.Trim()+"（用户填写，未验证位置）";if(protocol.SelectedIndex==1){Uri root=new Uri(url.Text.Trim().TrimEnd('/')+"/");node.Protocol="cloudflare";node.DownloadUrl=new Uri(root,"__down").AbsoluteUri;node.UploadUrl=new Uri(root,"__up").AbsoluteUri;node.PingUrl=new Uri(root,"__down?bytes=0").AbsoluteUri;}node.Validate();nodes.Items.Add(node);nodes.SelectedItem=node;dialog.DialogResult=DialogResult.OK;dialog.Close();}catch(Exception ex){error.Text=ex.Message;}
                };dialog.ShowDialog(FindForm());
            }
        }
        public void Cancel(){if(cts!=null){try{cts.Cancel();}catch(ObjectDisposedException){}}if(!closed&&busy)status.Text="正在停止请求，保留已完成的样本…";}
        public void Shutdown(){if(closed)return;closed=true;Cancel();preflightTimer.Stop();preflightTimer.Dispose();}
        public void LoadDemo()
        {
            SpeedResult demo=new SpeedResult{Time="示例 · 2026-10-13 10:32",Node="Cloudflare · 示例节点",Location="演示数据 · 不代表当前网络",Endpoint="https://speed.cloudflare.com/__down",Mode="完整",Status="演示结果",DownloadBps=12450000,UploadBps=4260000,DownloadPayload=167772160,UploadPayload=63963136,UploadConfirmed=63963136,ElapsedSeconds=33.6,Note="这是界面示例，没有进行网络测速。真实测速会保存时间、节点和结果，可随时停止。"};
            demo.Idle.Samples.AddRange(new double[]{22,24,23,25,22});demo.DownloadLatency.Samples.AddRange(new double[]{44,51,46,57,49});demo.UploadLatency.Samples.AddRange(new double[]{67,80,72,91,76});
            for(int i=0;i<100;i++){double t=i*.34;demo.Points.Add(new SpeedPoint{Seconds=t,DownloadBps=t>2&&t<17?(1-Math.Exp(-(t-2)))*12450000*(1+.08*Math.Sin(t*2)):0,UploadBps=t>18&&t<33?(1-Math.Exp(-(t-18)))*4260000*(1+.1*Math.Sin(t*2)):0});}
            history.Items.Clear();history.Items.Add(demo);FillHistory();ShowResult(demo);progress.Value=100;RefreshPreflight();
        }
        public static string SelfTest()
        {
            int count=0;Action<bool,string> check=delegate(bool condition,string name){if(!condition)throw new Exception("SpeedTest: "+name);count++;};
            check(Math.Abs(SpeedMath.Median(new double[]{3,1,2,4})-2.5)<.001,"even median");check(SpeedMath.Median(new double[]{6,2,4})==4,"odd median");
            var latency=new SpeedLatency();check(!latency.Median.HasValue&&!latency.Jitter.HasValue,"empty latency");latency.Samples.AddRange(new double[]{10,20,15});check(latency.Median==15&&latency.Jitter==7.5,"median and adjacent jitter");
            check(SpeedMath.Rate(1000000)=="8.00 Mbps  /  1.00 MB/s","decimal unit conversion");
            foreach(SpeedNode n in SpeedNode.Defaults()){n.Validate();check(new Uri(n.DownloadUrl).Scheme=="https","node https");}
            check(!SpeedNode.ValidUrl("http://example.com",false),"reject cleartext node");check(!SpeedNode.ValidUrl("https://user:secret@example.com/",false),"reject URL credentials");check(SpeedNode.ValidUrl("http://127.0.0.1:1234/",true),"local validation harness");
            SpeedNode libre=SpeedNode.Libre("test","https://example.com/backend");check(libre.DownloadUrl=="https://example.com/backend/garbage.php","directory resolution");
            var result=new SpeedResult{Time="test",Status="已停止",DownloadBps=1250000};result.Idle.Samples.Add(4);string encoded=new JavaScriptSerializer().Serialize(result);SpeedResult decoded=new JavaScriptSerializer().Deserialize<SpeedResult>(encoded);check(decoded.Status=="已停止"&&decoded.DownloadBps==1250000&&decoded.Idle.Samples[0]==4,"history JSON roundtrip");
            SpeedResult dirty=new SpeedResult {Idle=null,DownloadLatency=new SpeedLatency {Samples=null},Points=new List<SpeedPoint>{null,new SpeedPoint {Seconds=Double.NaN},new SpeedPoint {Seconds=2,DownloadBps=1}},DownloadBps=Double.PositiveInfinity};SpeedHistory.Clean(dirty);check(dirty.Idle.Samples.Count==0&&dirty.DownloadLatency.Samples.Count==0&&dirty.Points.Count==1&&!dirty.DownloadBps.HasValue,"malformed history normalized");
            return "SpeedTest: "+count+" assertions passed (pure logic; no external traffic)";
        }
    }
}
