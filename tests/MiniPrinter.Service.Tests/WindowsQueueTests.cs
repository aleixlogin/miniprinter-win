using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MiniPrinter.Control;

namespace MiniPrinter.Service.Tests;

/// <summary>A scripted PowerShell: each step is recognised by its "# step:" marker and answers what the test says.</summary>
public sealed class FakePowerShell : IPowerShellRunner
{
    private readonly Dictionary<string, Queue<PowerShellResult>> _sequences = [];
    private readonly Dictionary<string, PowerShellResult> _answers = [];

    public List<(string Step, IReadOnlyDictionary<string, string> Env, string Script)> Calls { get; } = [];

    public FakePowerShell On(string step, int exitCode, string output = "", string error = "")
    {
        _answers[step] = new PowerShellResult(exitCode, output, error);
        return this;
    }

    /// <summary>Answers the first calls of a step in order (then falls back to <see cref="On"/> or the default).</summary>
    public FakePowerShell OnSequence(string step, params PowerShellResult[] answers)
    {
        _sequences[step] = new Queue<PowerShellResult>(answers);
        return this;
    }

    /// <summary>The steps of the recreation (the status query "exists" is not part of it).</summary>
    public IEnumerable<string> Steps => Calls.Select(c => c.Step).Where(s => s != "exists");

    public Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, string> environment, CancellationToken ct)
    {
        var step = script.Split('\n')[0].Replace("# step:", "").Trim();
        lock (Calls)
            Calls.Add((step, environment, script));
        if (_sequences.TryGetValue(step, out var queue) && queue.Count > 0)
            return Task.FromResult(queue.Dequeue());
        if (_answers.TryGetValue(step, out var answer))
            return Task.FromResult(answer);
        return Task.FromResult(step switch
        {
            "pending" => new PowerShellResult(0, "0", ""),
            // By default Windows offers exactly what was announced.
            "sizes" => new PowerShellResult(0, string.Join("\n", environment["MP_SIZES"].Split(',', StringSplitOptions.RemoveEmptyEntries)), ""),
            _ => new PowerShellResult(0, "", ""),
        });
    }
}

/// <summary>Recreation of the Windows print queue (spec windows-queue), without touching the real spooler.</summary>
public sealed class WindowsQueueTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private string _dataDir = null!;
    private int _ippPort;
    private FakePowerShell _ps = null!;

    public Task InitializeAsync()
    {
        _dataDir = TestEnv.TempDirectory();
        _ippPort = TestEnv.FreePort();
        _ps = new FakePowerShell();
        File.WriteAllText(Path.Combine(_dataDir, "settings.json"),
            JsonSerializer.Serialize(new ServiceSettings { IppPort = _ippPort }, ControlDefaults.Json));
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("DataDirectory", _dataDir);
            b.UseEnvironment("Development");
            b.ConfigureServices(s => s.AddSingleton<IPowerShellRunner>(_ps));
        });
        _ = _factory.Server; // start
        return TestEnv.WaitForPortAsync(_ippPort);
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private ControlClient Client() =>
        new(File.ReadAllText(Path.Combine(_dataDir, "control.token")), handler: _factory.Server.CreateHandler());

    private WindowsQueue Queue => _factory.Services.GetRequiredService<WindowsQueue>();

    private async Task<WindowsQueueDto> Recreate(string? previousName = null)
    {
        using var client = Client();
        await client.RecreateWindowsQueueAsync(previousName);
        await Queue.WhenIdle();
        return await client.GetWindowsQueueAsync();
    }

    // ---- the sequence ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_successful_recreation_runs_every_step_in_order()
    {
        var result = await Recreate();
        Assert.Equal("Succeeded", result.State);
        Assert.Equal(["pending", "cleanup", "create", "verify", "remove-old", "rename", "sizes"], _ps.Steps);
        Assert.Contains("actualizado", result.Message);
    }

    [Fact]
    public async Task The_new_queue_is_created_with_a_temporary_name_pointing_at_the_ipp_endpoint()
    {
        await Recreate();
        var create = _ps.Calls.Single(c => c.Step == "create");
        Assert.Equal("X5h Thermal Printer (nueva)", create.Env["MP_TEMP"]);
        Assert.Equal($"http://127.0.0.1:{_ippPort}/ipp/print", create.Env["MP_URL"]);
        var rename = _ps.Calls.Single(c => c.Step == "rename");
        Assert.Equal("X5h Thermal Printer", rename.Env["MP_QUEUE"]);
    }

    [Fact]
    public async Task If_creating_the_new_queue_fails_the_old_one_is_not_touched()
    {
        _ps.On("create", 1, error: "Add-Printer: La impresora no responde");
        var result = await Recreate();
        Assert.Equal("Failed", result.State);
        Assert.Equal("create", result.Step);
        Assert.Contains("anterior sigue funcionando", result.Message);
        Assert.Contains("no responde", result.Message);
        Assert.DoesNotContain("remove-old", _ps.Steps);
        Assert.DoesNotContain("rename", _ps.Steps);
    }

    [Fact]
    public async Task If_the_new_queue_is_not_found_afterwards_nothing_is_removed()
    {
        _ps.On("verify", 1);
        var result = await Recreate();
        Assert.Equal("verify", result.Step);
        Assert.DoesNotContain("remove-old", _ps.Steps);
    }

    [Fact]
    public async Task Pending_windows_jobs_abort_without_touching_anything()
    {
        _ps.On("pending", 0, output: "2");
        var result = await Recreate();
        Assert.Equal("Failed", result.State);
        Assert.Contains("2 trabajos pendientes", result.Message);
        Assert.Equal(["pending"], _ps.Steps);
    }

    [Fact]
    public async Task One_pending_job_is_written_in_the_singular()
    {
        _ps.On("pending", 0, output: "1");
        Assert.Contains("1 trabajo pendiente", (await Recreate()).Message);
    }

    [Fact]
    public async Task A_leftover_temporary_queue_is_cleaned_before_creating()
    {
        await Recreate();
        Assert.True(_ps.Steps.ToList().IndexOf("cleanup") < _ps.Steps.ToList().IndexOf("create"));
        Assert.Contains("Remove-Printer -Name $env:MP_TEMP", _ps.Calls.Single(c => c.Step == "cleanup").Script);
    }

    [Fact]
    public async Task The_old_queue_port_is_only_removed_when_nobody_else_uses_it()
    {
        await Recreate();
        var script = _ps.Calls.Single(c => c.Step == "remove-old").Script;
        Assert.Contains("Where-Object { $_.PortName -eq $port }", script);
        Assert.Contains("-not (Get-Printer", script);
    }

    [Fact]
    public async Task Access_denied_is_explained_as_a_permissions_problem_and_the_old_queue_survives()
    {
        _ps.On("create", 1, error: "Add-Printer : Access is denied.");
        var result = await Recreate();
        Assert.Equal("Failed", result.State);
        Assert.Contains("permisos", result.Message);
        Assert.Contains("administrador", result.Message);
        Assert.DoesNotContain("remove-old", _ps.Steps);
    }

    [Fact]
    public async Task If_removing_the_old_queue_fails_the_user_is_told_where_the_new_one_is()
    {
        _ps.On("remove-old", 1, error: "Remove-Printer: está en uso");
        var result = await Recreate();
        Assert.Equal("remove-old", result.Step);
        Assert.Contains("X5h Thermal Printer (nueva)", result.Message);
    }

    // ---- renaming ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task After_a_rename_the_old_queue_is_removed_by_its_previous_name()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { PrinterName = "Etiquetas" });
        var result = await Recreate("X5h Thermal Printer");
        Assert.Equal("Succeeded", result.State);
        Assert.Contains("name-free", _ps.Steps);
        var remove = _ps.Calls.Single(c => c.Step == "remove-old");
        Assert.Equal("X5h Thermal Printer", remove.Env["MP_OLD"]);
        Assert.Equal("Etiquetas", remove.Env["MP_QUEUE"]);
        Assert.Equal("Etiquetas", _ps.Calls.Single(c => c.Step == "rename").Env["MP_QUEUE"]);
    }

    [Fact]
    public async Task A_new_name_that_already_exists_in_windows_is_refused()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { PrinterName = "Otra" });
        _ps.On("name-free", 3);
        var result = await Recreate("X5h Thermal Printer");
        Assert.Equal("name-free", result.Step);
        Assert.Contains("«Otra»", result.Message);
        Assert.DoesNotContain("create", _ps.Steps);
    }

    // ---- input safety -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Etiquetas \"Ana\"")]
    [InlineData("Linea1\nLinea2")]
    [InlineData("a\\b")]
    public async Task Names_with_quotes_slashes_or_control_characters_are_refused_before_running_anything(string name)
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { PrinterName = name });
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.RecreateWindowsQueueAsync());
        Assert.Equal(400, error.StatusCode);
        Assert.Empty(_ps.Calls);
    }

    [Fact]
    public async Task Overlong_names_are_refused()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { PrinterName = new string('x', 61) });
        await Assert.ThrowsAsync<ControlApiException>(() => client.RecreateWindowsQueueAsync());
    }

    [Fact]
    public async Task A_name_with_an_apostrophe_travels_in_the_environment_not_in_the_script()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { PrinterName = "Etiquetas d'Ana" });
        var result = await Recreate("Etiquetas d'Ana");
        Assert.Equal("Succeeded", result.State);
        Assert.All(_ps.Calls, c => Assert.DoesNotContain("d'Ana", c.Script));   // scripts are constants
        Assert.Equal("Etiquetas d'Ana", _ps.Calls.Single(c => c.Step == "rename").Env["MP_QUEUE"]);
    }

    [Fact]
    public void The_scripts_are_constants_that_only_read_the_environment()
    {
        foreach (var script in new[] { QueueScripts.Pending, QueueScripts.NameFree, QueueScripts.Cleanup, QueueScripts.Create,
                     QueueScripts.Verify, QueueScripts.RemoveOld, QueueScripts.Rename, QueueScripts.Exists })
            Assert.Contains("$env:MP_", script);
    }


    // ---- verification of what Windows offers, and repeated passes ---------------------------------------------------------

    private static PowerShellResult Offered(params string[] sizes) => new(0, string.Join("\n", sizes), "");

    [Fact]
    public async Task The_sizes_windows_offers_are_read_back_after_creating_the_queue()
    {
        var result = await Recreate();
        Assert.Equal("Succeeded", result.State);
        Assert.Equal("sizes", _ps.Steps.Last());
        var sizes = _ps.Calls.Last(c => c.Step == "sizes").Env["MP_SIZES"];
        Assert.Equal("48x210,48x100,48x50,48x297,48x1000,80x297,80x100", sizes);   // the announced sizes, "WxH" in mm
        Assert.Contains("PageMediaSize", QueueScripts.Sizes);
    }

    [Fact]
    public async Task A_stale_list_repeats_the_recreation_until_windows_offers_the_announced_sizes()
    {
        // Windows still shows the old list after the first pass, and the right one after the second.
        _ps.OnSequence("sizes", Offered("48x210", "48x100"), new PowerShellResult(0, string.Join("\n", PaperSizeNames()), ""));
        var result = await Recreate();

        Assert.Equal("Succeeded", result.State);
        Assert.Equal(2, _ps.Steps.Count(s => s == "create"));          // two passes
        Assert.Equal(2, _ps.Steps.Count(s => s == "rename"));
        Assert.Contains("repasarla 1 vez", result.Message);
        Assert.Equal(1, _ps.Steps.Count(s => s == "pending"));         // pending jobs are only checked once
    }

    [Fact]
    public async Task The_second_pass_replaces_the_queue_the_first_one_just_created()
    {
        _ps.OnSequence("sizes", Offered("48x210"));
        await Recreate();
        var removes = _ps.Calls.Where(c => c.Step == "remove-old").ToList();
        Assert.Equal(2, removes.Count);
        Assert.Equal("X5h Thermal Printer", removes[1].Env["MP_OLD"]);   // pass 2 removes the queue made by pass 1
        Assert.Equal("X5h Thermal Printer", removes[1].Env["MP_QUEUE"]);
    }

    [Fact]
    public async Task Every_pass_takes_a_new_printer_identity()
    {
        using var client = Client();
        var uuids = new List<string?>();
        Queue.WaitForListener = _ =>
        {
            uuids.Add(_factory.Services.GetRequiredService<SettingsStore>().Current.PrinterUuid);
            return Task.CompletedTask;
        };
        _ps.OnSequence("sizes", Offered("48x210"), Offered("48x210"));   // two stale passes, the third is right
        var result = await Recreate();
        Assert.Equal("Succeeded", result.State);
        Assert.Equal(3, uuids.Count);
        Assert.All(uuids, u => Assert.True(Guid.TryParse(u, out _)));
        Assert.Equal(3, uuids.Distinct().Count());                       // a different UUID each time
    }

    [Fact]
    public async Task If_windows_never_catches_up_it_stops_after_three_passes_and_says_so()
    {
        _ps.On("sizes", 0, output: "48x210\n48x100");
        var result = await Recreate();
        Assert.Equal("Succeeded", result.State);
        Assert.Equal(3, _ps.Steps.Count(s => s == "create"));
        Assert.Contains("aún no muestra todos los tamaños", result.Message);
        Assert.Contains("48x50 falta", result.Message);
    }

    [Fact]
    public async Task A_size_windows_offers_that_was_not_announced_also_counts_as_stale()
    {
        _ps.OnSequence("sizes", new PowerShellResult(0, string.Join("\n", PaperSizeNames().Append("33x44")), ""));
        var result = await Recreate();
        Assert.Equal(2, _ps.Steps.Count(s => s == "create"));
        Assert.Equal("Succeeded", result.State);
    }

    [Fact]
    public async Task If_the_capabilities_cannot_be_read_the_recreation_is_still_successful_and_not_repeated()
    {
        _ps.On("sizes", 1, error: "no se pueden leer las capacidades");
        var result = await Recreate();
        Assert.Equal("Succeeded", result.State);
        Assert.Equal(1, _ps.Steps.Count(s => s == "create"));
        Assert.Contains("no se pudo comprobar", result.Message);
    }

    [Fact]
    public async Task Order_and_case_do_not_matter_when_comparing_sizes()
    {
        _ps.On("sizes", 0, output: string.Join("\r\n", PaperSizeNames().Reverse()));
        await Recreate();
        Assert.Equal(1, _ps.Steps.Count(s => s == "create"));
    }

    private static IEnumerable<string> PaperSizeNames() => PaperCatalog.Presets.Select(p => $"{p.WidthMm}x{p.LengthMm}");

    // ---- elevation --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Missing_rights_are_reported_so_the_tray_can_repeat_the_work_elevated()
    {
        _ps.On("create", 1, error: "Add-Printer : Access is denied.");
        var result = await Recreate();
        Assert.Equal("Failed", result.State);
        Assert.True(result.NeedsElevation);
    }

    [Fact]
    public async Task Other_failures_do_not_ask_for_elevation()
    {
        _ps.On("create", 1, error: "Add-Printer: la impresora no responde");
        Assert.False((await Recreate()).NeedsElevation);
        Assert.False(Queue.Current.NeedsElevation && Queue.Current.State == "Succeeded");
    }

    [Fact]
    public async Task The_prepare_endpoint_gives_the_printer_a_new_identity_for_the_elevated_helper()
    {
        using var client = Client();
        var before = _factory.Services.GetRequiredService<SettingsStore>().Current.PrinterUuid;
        Queue.WaitForListener = _ => Task.CompletedTask;
        await client.PrepareWindowsQueueAsync();
        var after = _factory.Services.GetRequiredService<SettingsStore>().Current.PrinterUuid;
        Assert.NotEqual(before, after);
        Assert.True(Guid.TryParse(after, out _));
    }

    // ---- listener, state and endpoints ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_queue_is_created_only_after_the_listener_serves_the_current_sizes()
    {
        using var client = Client();
        var settings = await client.GetSettingsAsync();
        var list = PaperCatalog.Presets.Select(p => p with { Enabled = p.Id == "48x100" }).ToList();
        await client.SaveSettingsAsync(settings with { PaperSizes = list });

        await client.RecreateWindowsQueueAsync();
        await Queue.WhenIdle();

        Assert.Equal("Succeeded", (await client.GetWindowsQueueAsync()).State);
        // By the time the first PowerShell step ran, the listener already announced the new list.
        var host = _factory.Services.GetRequiredService<IppHost>();
        await host.WaitForCurrentAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }

    [Fact]
    public async Task The_wait_for_the_listener_times_out_with_a_clear_message()
    {
        var host = _factory.Services.GetRequiredService<IppHost>();
        using var client = Client();
        // The new port is taken by someone else, so the listener can never come up on it however fast the restart is:
        // the wait must give up with a timeout (not depend on how quickly the machine restarts Kestrel).
        var busy = TestEnv.FreePort();
        using var occupant = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, busy);
        occupant.Start();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { IppPort = busy });
        var error = await Assert.ThrowsAsync<TimeoutException>(() => host.WaitForCurrentAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None));
        Assert.Contains("IPP", error.Message);
    }

    [Fact]
    public async Task A_second_request_while_one_is_running_gets_409()
    {
        var gate = new TaskCompletionSource();
        Queue.WaitForListener = _ => gate.Task;
        using var client = Client();
        await client.RecreateWindowsQueueAsync();
        Assert.Equal("Running", (await client.GetWindowsQueueAsync()).State);
        var error = await Assert.ThrowsAsync<ControlApiException>(() => client.RecreateWindowsQueueAsync());
        Assert.Equal(409, error.StatusCode);
        gate.SetResult();
        await Queue.WhenIdle();
        Assert.Equal("Succeeded", (await client.GetWindowsQueueAsync()).State);
    }

    [Fact]
    public async Task Status_reports_the_step_while_running_and_the_result_in_the_general_status()
    {
        var gate = new TaskCompletionSource();
        Queue.WaitForListener = _ => gate.Task;
        using var client = Client();
        await client.RecreateWindowsQueueAsync();
        var running = await client.GetWindowsQueueAsync();
        Assert.Equal(("Running", "uuid"), (running.State, running.Step));   // blocked in the identity/listener wait
        Assert.Equal("Running", (await client.GetStatusAsync()).WindowsQueue?.State);
        gate.SetResult();
        await Queue.WhenIdle();
        Assert.Equal("Succeeded", (await client.GetStatusAsync()).WindowsQueue?.State);
    }

    [Fact]
    public async Task Exists_reflects_what_windows_answers()
    {
        using var client = Client();
        _ps.On("exists", 0);
        Assert.True((await client.GetWindowsQueueAsync()).Exists);
        _ps.On("exists", 1);
        var missing = await client.GetWindowsQueueAsync();
        Assert.False(missing.Exists);
        Assert.Equal("X5h Thermal Printer", missing.Name);
        Assert.Equal("Idle", missing.State);
    }

    [Fact]
    public async Task The_endpoints_exist_only_in_the_control_api()
    {
        using var client = Client();
        await client.SaveSettingsAsync((await client.GetSettingsAsync()) with { AutomationApiEnabled = true });
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_ippPort}") };
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", (await client.GetAutomationAsync()).Token);
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync("/api/v1/windows-queue/recreate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync("/api/v1/windows-queue")).StatusCode);

        using var wrong = new ControlClient("wrong", handler: _factory.Server.CreateHandler());
        Assert.Equal(401, (await Assert.ThrowsAsync<ControlApiException>(() => wrong.RecreateWindowsQueueAsync())).StatusCode);
    }
}
