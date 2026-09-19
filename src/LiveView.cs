using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Pulse {
    // Hit publication never performs network I/O and works with the WPF window hidden.
    public sealed class LiveViewState {
        readonly object gate=new object();
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly long[] sequence=new long[8],at=new long[8];
        readonly int[] velocity=new int[8];
        long total; int last=-1; bool preview;
        string theme="Green",status="Waiting for drums";
        bool ready,pedals,closed,learning; double kick,hat,masterGain;
        public readonly string Session=Guid.NewGuid().ToString("N");
        public readonly string ControlToken=Guid.NewGuid().ToString("N");
        public Func<string,string> Controls;
        public void Hit(int part,int value,bool audition) {
            if (part < 0 || part > 7) return;
            lock (gate) { sequence[part]++; at[part]=clock.ElapsedMilliseconds; velocity[part]=Math.Max(0,Math.Min(127,value)); last=part; preview=audition; if (!audition) total++; }
        }
        public void Update(string color,string connection,bool connected,bool pedalReady,bool hatClosed,double kickPosition,double hatPosition,bool setup,double gain=0) {
            lock (gate) { theme=color; status=connection; ready=connected; pedals=pedalReady; closed=hatClosed; kick=kickPosition; hat=hatPosition; learning=setup; masterGain=gain; }
        }
        public static string Quote(string value) {
            var b=new StringBuilder("\"");
            foreach (char c in value ?? "") { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); }
            return b.Append('"').ToString();
        }
        public string Json() {
            lock (gate) {
                var b=new StringBuilder(700); long now=clock.ElapsedMilliseconds;
                b.Append("{\"session\":").Append(Quote(Session)).Append(",\"theme\":").Append(Quote(theme)).Append(",\"status\":").Append(Quote(status));
                b.Append(",\"ready\":").Append(ready ? "true" : "false").Append(",\"pedals\":").Append(pedals ? "true" : "false").Append(",\"closed\":").Append(closed ? "true" : "false");
                b.Append(",\"learning\":").Append(learning ? "true" : "false").Append(",\"kick\":").Append(kick.ToString("0.000",CultureInfo.InvariantCulture)).Append(",\"hat\":").Append(hat.ToString("0.000",CultureInfo.InvariantCulture));
                b.Append(",\"gain\":").Append(masterGain.ToString("0.00",CultureInfo.InvariantCulture));
                b.Append(",\"total\":").Append(total).Append(",\"last\":").Append(last).Append(",\"preview\":").Append(preview ? "true" : "false").Append(",\"pads\":[");
                for (int i=0;i<8;i++) { if (i>0) b.Append(','); b.Append('[').Append(sequence[i]).Append(',').Append(velocity[i]).Append(',').Append(sequence[i] == 0 ? -1 : now-at[i]).Append(']'); }
                return b.Append("]}").ToString();
            }
        }
    }
    // Bounded local HTTP/TLS server with same-origin authenticated command requests.
    public sealed class LiveViewServer : IDisposable {
        readonly LiveViewState state;
        readonly object gate=new object(); readonly List<TcpClient> clients=new List<TcpClient>();
        readonly byte[] page;
        TcpListener listener; Thread accept; volatile bool stopped;
        X509Certificate2 certificate; byte[] setupPage,rootCertificate;
        public string Url { get; private set; }
        public LiveViewServer(LiveViewState value) {
            state=value;
            using (var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Pulse.LiveView.html")) {
                if (input == null) throw new InvalidOperationException("Browser view resource missing");
                using (var m=new MemoryStream()) { input.CopyTo(m); page=m.ToArray(); }
            }
        }
        public void Start(int preferredPort=8765) {
            StartNetwork(IPAddress.Loopback,preferredPort,null,null,null);
        }
        public void StartNetwork(IPAddress address,int preferredPort,X509Certificate2 cert,byte[] setup,byte[] root) {
            certificate=cert; setupPage=setup; rootCertificate=root;
            for (int n=0;n<10;n++) {
                var candidate=new TcpListener(address,preferredPort == 0 ? 0 : preferredPort+n);
                try { candidate.Start(); listener=candidate; break; } catch (SocketException) { candidate.Stop(); }
            }
            if (listener == null) throw new IOException("Browser preview ports 8765–8774 are busy.");
            Url=(cert==null ? "http://" : "https://")+address+":"+((IPEndPoint)listener.LocalEndpoint).Port+"/";
            accept=new Thread(Accept) { IsBackground=true,Name="Pulse browser preview" }; accept.Start();
        }
        void Accept() {
            while (!stopped) {
                try {
                    var client=listener.AcceptTcpClient(); client.NoDelay=true; client.ReceiveTimeout=1500; client.SendTimeout=1500;
                    lock (gate) { if (stopped || clients.Count >= 12) { client.Close(); continue; } clients.Add(client); }
                    new Thread(() => Serve(client)) { IsBackground=true,Name="Pulse browser client" }.Start();
                } catch (SocketException) { if (stopped) return; } catch (ObjectDisposedException) { return; }
            }
        }
        void Serve(TcpClient client) {
            try {
                Stream stream=client.GetStream();
                if(certificate!=null) { var tls=new SslStream(stream,false); stream=tls; tls.AuthenticateAsServer(certificate,false,SslProtocols.Tls12,false); }
                var request=new StringBuilder(); int value; var deadline=Stopwatch.StartNew();
                while (request.Length < 8192 && deadline.ElapsedMilliseconds < 2000 && (value=stream.ReadByte()) >= 0) {
                    request.Append((char)value); int n=request.Length;
                    if (n>=4 && request[n-4]=='\r' && request[n-3]=='\n' && request[n-2]=='\r' && request[n-1]=='\n') break;
                }
                string text=request.ToString(); if (!text.EndsWith("\r\n\r\n")) return;
                string[] lines=text.Split(new[] {"\r\n"},StringSplitOptions.None), first=lines[0].Split(' ');
                if (first.Length != 3 || (first[0] != "GET" && first[0] != "POST")) { Reply(stream,405,"text/plain",new byte[0]); return; }
                string host="",origin="",token=""; int length=0;
                foreach (string line in lines) { if (line.StartsWith("Host:",StringComparison.OrdinalIgnoreCase)) host=line.Substring(5).Trim(); if (line.StartsWith("Origin:",StringComparison.OrdinalIgnoreCase)) origin=line.Substring(7).Trim(); }
                foreach(string line in lines) { if(line.StartsWith("X-Pulse-Token:",StringComparison.OrdinalIgnoreCase)) token=line.Substring(14).Trim(); if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase) && !Int32.TryParse(line.Substring(15).Trim(),out length)) length=-1; }
                string body="";
                if(first[0]=="POST") {
                    if(length<1 || length>4096) { Reply(stream,413,"text/plain",new byte[0]); return; }
                    foreach(string line in lines) if(line.Equals("Expect: 100-continue",StringComparison.OrdinalIgnoreCase)) Send(stream,"HTTP/1.1 100 Continue\r\n\r\n");
                    var bytes=new byte[length]; int read=0;
                    while(read<length) { int count=stream.Read(bytes,read,length-read); if(count<=0)return; read+=count; }
                    body=Encoding.UTF8.GetString(bytes);
                }
                var expected=new Uri(Url);
                if (host != expected.Authority || (origin != "" && origin != Url.TrimEnd('/'))) { Reply(stream,403,"text/plain",Encoding.UTF8.GetBytes("Open the preview using its local Pulse URL.")); return; }
                string route=first[1].Split('?')[0];
                if(first[0]=="POST") {
                    if(setupPage!=null || route!="/control" || token!=state.ControlToken || origin!=Url.TrimEnd('/')) { Reply(stream,403,"text/plain",new byte[0]); return; }
                    string answer=state.Controls==null ? "{\"error\":\"Controls unavailable\"}" : state.Controls(body);
                    Reply(stream,200,"application/json",Encoding.UTF8.GetBytes(answer)); return;
                }
                if(setupPage!=null) {
                    if(route=="/Pulse-Preview.cer") Reply(stream,200,"application/x-x509-ca-cert",rootCertificate);
                    else if(route=="/" || route=="/setup") Reply(stream,200,"text/html; charset=utf-8",setupPage);
                    else Reply(stream,404,"text/plain",new byte[0]);
                    return;
                }
                if (route == "/") { Reply(stream,200,"text/html; charset=utf-8",Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(page).Replace("__PULSE_TOKEN__",state.ControlToken))); return; }
                if (route == "/controls" && state.Controls!=null) { Reply(stream,200,"application/json",Encoding.UTF8.GetBytes(state.Controls(""))); return; }
                if (route == "/state") { Reply(stream,200,"application/json",Encoding.UTF8.GetBytes(state.Json())); return; }
                if (route != "/events") { Reply(stream,404,"text/plain",new byte[0]); return; }
                Send(stream,"HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\nretry: 1000\n\n");
                while (!stopped) { Send(stream,"data: "+state.Json()+"\n\n"); Thread.Sleep(25); }
            } catch (IOException) { } catch (SocketException) { } catch (ObjectDisposedException) { } catch (AuthenticationException) { }
            finally { client.Close(); lock (gate) clients.Remove(client); }
        }
        static void Send(Stream stream,string text) { byte[] bytes=Encoding.UTF8.GetBytes(text); stream.Write(bytes,0,bytes.Length); }
        static void Reply(Stream stream,int code,string type,byte[] body) {
            Send(stream,"HTTP/1.1 "+code+" "+(code == 200 ? "OK" : "Unavailable")+"\r\nContent-Type: "+type+"\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\nContent-Security-Policy: default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'self'; base-uri 'none'\r\n\r\n");
            stream.Write(body,0,body.Length);
        }
        public void Dispose() {
            stopped=true; if (listener != null) listener.Stop();
            lock (gate) foreach (var c in clients) c.Close();
            if (accept != null) accept.Join(2000);
        }
    }
}
