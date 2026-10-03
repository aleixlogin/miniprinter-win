using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniPrinter.Control;
using MiniPrinter.Ipp;
using MiniPrinter.Service;

namespace MiniPrinter.Service.Tests;

/// <summary>job-history: every entrance of the service records where a job came from.</summary>
public sealed class JobOriginTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dataDir = null!;
    private int _ippPort;
    private int _rawPort;

    public async Task InitializeAsync()
    {
        _dataDir = TestEnv.TempDirectory();
        _ippPort = TestEnv.FreePort();
        _rawPort = TestEnv.FreePort();
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"), JsonSerializer.Serialize(
            new ServiceSettings { IppPort = _ippPort, RawPort = _rawPort, RawPortEnabled = true, AutomationApiEnabled = true, Printer = TestEnv.Simulated }, ControlDefaults.Json));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DataDirectory", _dataDir);
            b.UseSetting("RawPort:SilenceSeconds", "0.4");
            b.UseSetting("RawPort:IdleSeconds", "1.5");
            b.UseEnvironment("Development");
        });
        _ = _factory.Server;
        await _factory.Services.GetRequiredService<RawPortHost>().WaitForCurrentAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
        await TestEnv.WaitForPortAsync(_ippPort);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client() =>
        new(File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    private async Task<JobDto> WaitForAsync(ControlClient client, int id)
    {
        JobDto? job = null;
        await TestEnv.WaitUntilAsync(async () =>
        {
            job = (await client.GetJobsAsync()).SingleOrDefault(j => j.Id == id);
            return job?.State == "Completed";
        });
        return job!;
    }

    [Fact]
    public async Task A_job_from_the_panel_says_so()
    {
        using var client = Client();
        var job = await WaitForAsync(client, (await client.PrintTextAsync("Hola desde el panel")).Id);
        Assert.Equal("Panel", job.Source);
        Assert.False(string.IsNullOrEmpty(job.Origin));
    }

    [Fact]
    public async Task A_job_from_the_automation_api_carries_the_address_of_the_client()
    {
        using var client = Client();
        var token = (await client.GetAutomationAsync()).Token;
        using var api = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_ippPort}/api/v1/") };
        api.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var response = await api.PostAsync("print/text", new StringContent("{\"text\":\"Hola\"}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("jobId").GetInt32();

        var job = await WaitForAsync(client, id);
        Assert.Equal("Api", job.Source);
        Assert.Equal("127.0.0.1", job.Origin);
    }

    [Fact]
    public async Task A_job_from_the_raw_port_carries_the_address_of_the_client()
    {
        using var client = Client();
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, _rawPort);
            var stream = tcp.GetStream();
            await stream.WriteAsync(new byte[] { 0x1B, (byte)'@', (byte)'H', (byte)'i', 0x0A, 0x1D, (byte)'V', 66, 0 });
            tcp.Client.Shutdown(SocketShutdown.Send);
            await stream.ReadAsync(new byte[16]);
        }

        JobDto? job = null;
        await TestEnv.WaitUntilAsync(async () =>
        {
            job = (await client.GetJobsAsync()).FirstOrDefault(j => j.Source == "Raw");
            return job?.State == "Completed";
        });
        Assert.Equal("127.0.0.1", job!.Origin);
    }

    [Fact]
    public void A_job_from_the_windows_printer_is_marked_as_windows_with_the_ipp_user()
    {
        var queue = _factory.Services.GetRequiredService<JobQueue>();
        IPrintBackend backend = queue;
        var job = backend.CreateJob("documento", "maria");
        Assert.Equal(JobSource.Windows, job.Source);
        Assert.Equal("maria", job.Origin);
        queue.CancelJob(job.Id);
    }

    [Fact]
    public void The_user_name_does_not_decide_the_entrance()
    {
        // A client that calls itself "raw@10.0.0.9" over IPP is still a Windows job.
        var job = ((IPrintBackend)_factory.Services.GetRequiredService<JobQueue>()).CreateJob("x", "raw@10.0.0.9");
        Assert.Equal(JobSource.Windows, job.Source);
        _factory.Services.GetRequiredService<JobQueue>().CancelJob(job.Id);
    }
}
