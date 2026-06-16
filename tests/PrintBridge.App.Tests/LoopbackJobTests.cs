using System.Collections.Generic;
using System.Text;
using System.Threading;
using PrintBridge.App;
using PrintBridge.Protocol;
using PrintBridge.Spooler;
using Xunit;

public class LoopbackJobTests
{
    private class FakePrinter : IPrinterService
    {
        public readonly List<string> Printed = new List<string>();
        public IReadOnlyList<string> ListLocalPrinters() => new[] { "FAKE" };
        public void PrintPostScript(string printer, string path, int copies,
            double mediaWidthPoints, double mediaHeightPoints, bool fitToPage) => Printed.Add(printer);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SendOverLoopback_PrintsViaService()
    {
        var fake = new FakePrinter();
        IReadOnlyList<string> shared = new[] { "FAKE" };
        using (var server = new SharingService(fake, () => new AppConfig(),
            () => shared, "pc-test", "TEST", jobPort: 49190))
        {
            server.Start();
            Thread.Sleep(200);

            var header = new JobHeader
            {
                JobId = "j1", TargetPrinter = "FAKE", Copies = 1,
                RequestingUser = "u", RequestingPc = "PC"
            };
            var result = new JobSender().Send("127.0.0.1", 49190, header,
                Encoding.ASCII.GetBytes("%!PS fake"));

            Assert.Equal(JobStatus.Printed, result.Status);
            Assert.Single(fake.Printed);
        }
    }
}
