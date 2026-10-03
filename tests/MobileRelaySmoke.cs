using System;
using System.IO;
using System.Threading;
using Pulse;
// Optional manual emulator integration harness. Never use this public test code in production.
class MobileSmoke {
    static int Main() {
        string folder=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","artifacts"));
        Directory.CreateDirectory(folder);
        using(var receiver=new MobileReceiver("12345678")) {
            receiver.Received+=p=>File.AppendAllText(Path.Combine(folder,"mobile-relay-smoke.txt"),p+Environment.NewLine);
            Console.WriteLine("READY: emulator host 10.0.2.2, test code 12345678");
            Thread.Sleep(55000);
            Console.WriteLine(receiver.Connected?"PHONE CONNECTED":"NO PHONE");
            return receiver.Connected?0:1;
        }
    }
}
