using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using MiniPrinter.Control;

namespace MiniPrinter.Service.Tests;

public class IppRetryTests
{
    [Fact]
    public async Task A_port_that_is_busy_for_a_moment_is_taken_when_it_frees_up()
    {
        var dataDir = TestEnv.TempDirectory();
        var port = TestEnv.FreePort();
        File.WriteAllText(Path.Combine(dataDir, "settings.json"), JsonSerializer.Serialize(
            new ServiceSettings { IppPort = port, Printer = TestEnv.Simulated }, ControlDefaults.Json));

        var occupier = new TcpListener(IPAddress.Loopback, port);
        occupier.Start();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DataDirectory", dataDir);
            b.UseEnvironment("Development");
        });
        _ = factory.Server;
        await Task.Delay(1500);   // the service has tried and failed by now
        occupier.Stop();

        await TestEnv.WaitForPortAsync(port, seconds: 20);   // it keeps trying: the printer is not deaf for good
    }
}
