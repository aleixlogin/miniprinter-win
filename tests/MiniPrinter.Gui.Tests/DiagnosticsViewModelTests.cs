using MiniPrinter.Control;

namespace MiniPrinter.Gui.Tests;

/// <summary>diagnostics-panel: the rows, what each result means, the summary and the report that can be shared.</summary>
public class DiagnosticsViewModelTests
{
    private static readonly PrinterSelection Printer = new() { Name = "X5h-E07A", Address = "7A:E0:0C:1D:87:AE", ProfileKey = "d1" };

    private sealed class FakeSource : IDiagnosticsSource
    {
        public IReadOnlyList<DiagnosticCheckDto> ServiceChecks { get; set; } = Healthy();
        public Exception? ServiceFailure { get; set; }
        public Func<Task<bool?>> Bluetooth { get; set; } = () => Task.FromResult<bool?>(true);
        public Func<Task<bool?>> Paired { get; set; } = () => Task.FromResult<bool?>(true);
        public PrinterSelection? Printer { get; set; } = DiagnosticsViewModelTests.Printer;

        public Task<IReadOnlyList<DiagnosticCheckDto>> GetServiceChecksAsync() =>
            ServiceFailure is null ? Task.FromResult(ServiceChecks) : Task.FromException<IReadOnlyList<DiagnosticCheckDto>>(ServiceFailure);

        public Task<bool?> IsBluetoothOnAsync() => Bluetooth();

        public Task<bool?> IsPairedAsync(PrinterSelection printer) => Paired();
    }

    private static IReadOnlyList<DiagnosticCheckDto> Healthy() =>
    [
        new(DiagnosticIds.Link, DiagnosticStatus.Ok, null),
        new(DiagnosticIds.Paper, DiagnosticStatus.Ok, null),
        new(DiagnosticIds.Queue, DiagnosticStatus.Ok, "X5h Thermal Printer"),
        new(DiagnosticIds.Ipp, DiagnosticStatus.Ok, "8631"),
        new(DiagnosticIds.Raw, DiagnosticStatus.Ok, "Desactivado"),
        new(DiagnosticIds.Fonts, DiagnosticStatus.Ok, null),
    ];

    private static async Task<DiagnosticsViewModel> RunAsync(FakeSource source)
    {
        var model = new DiagnosticsViewModel(source);
        await model.RunAsync();
        return model;
    }

    private static DiagnosticRow Row(DiagnosticsViewModel model, string id) => model.Rows.Single(r => r.Id == id);

    [Fact]
    public async Task Everything_working_says_so_with_all_rows_in_order()
    {
        var model = await RunAsync(new FakeSource());
        Assert.Equal([DiagnosticIds.Service, DiagnosticIds.Bluetooth, DiagnosticIds.Paired, DiagnosticIds.Link, DiagnosticIds.Paper,
            DiagnosticIds.Queue, DiagnosticIds.Ipp, DiagnosticIds.Raw, DiagnosticIds.Fonts], model.Rows.Select(r => r.Id));
        Assert.All(model.Rows, r => Assert.Equal(DiagnosticStatus.Ok, r.Status));
        Assert.Equal("Todo en orden.", model.Summary);
        Assert.Equal(DiagnosticStatus.Ok, model.OverallStatus);
        Assert.All(model.Rows, r => Assert.False(r.HasAction));
    }

    [Fact]
    public async Task A_stopped_service_is_explained_and_what_depends_on_it_is_unknown_not_blamed()
    {
        var source = new FakeSource { ServiceFailure = new HttpRequestException("Connection refused") };
        var model = await RunAsync(source);
        var service = Row(model, DiagnosticIds.Service);
        Assert.Equal(DiagnosticStatus.Fail, service.Status);
        Assert.Contains("services.msc", service.Hint);
        foreach (var id in new[] { DiagnosticIds.Link, DiagnosticIds.Paper, DiagnosticIds.Queue, DiagnosticIds.Ipp, DiagnosticIds.Raw, DiagnosticIds.Fonts })
            Assert.Equal(DiagnosticStatus.Unknown, Row(model, id).Status);
        // What only the tray sees is still checked.
        Assert.Equal(DiagnosticStatus.Ok, Row(model, DiagnosticIds.Bluetooth).Status);
        Assert.Equal(DiagnosticStatus.Fail, model.OverallStatus);
        Assert.Equal("Hay un problema.", model.Summary);
    }

    [Fact]
    public async Task Bluetooth_off_fails_and_the_button_opens_the_settings_of_windows()
    {
        var source = new FakeSource { Bluetooth = () => Task.FromResult<bool?>(false) };
        var model = await RunAsync(source);
        var row = Row(model, DiagnosticIds.Bluetooth);
        Assert.Equal(DiagnosticStatus.Fail, row.Status);
        Assert.Equal(DiagnosticAction.OpenBluetoothSettings, row.Action);
        Assert.Contains("Enciende el Bluetooth", row.Hint);
    }

    [Fact]
    public async Task A_probe_that_cannot_tell_is_unknown_never_a_failure()
    {
        var source = new FakeSource
        {
            Bluetooth = () => Task.FromResult<bool?>(null),
            Paired = () => throw new InvalidOperationException("sin acceso"),
        };
        var model = await RunAsync(source);
        Assert.Equal(DiagnosticStatus.Unknown, Row(model, DiagnosticIds.Bluetooth).Status);
        Assert.Equal(DiagnosticStatus.Unknown, Row(model, DiagnosticIds.Paired).Status);
        Assert.Equal(DiagnosticStatus.Unknown, model.OverallStatus);
        Assert.Equal("No se pudieron comprobar 2 cosas.", model.Summary);
    }

    [Fact]
    public async Task A_printer_that_is_not_paired_leads_to_the_search()
    {
        var model = await RunAsync(new FakeSource { Paired = () => Task.FromResult<bool?>(false) });
        var row = Row(model, DiagnosticIds.Paired);
        Assert.Equal(DiagnosticStatus.Fail, row.Status);
        Assert.Equal(DiagnosticAction.FindPrinters, row.Action);
    }

    [Fact]
    public async Task Without_a_printer_the_pairing_is_unknown_and_asks_to_choose_one()
    {
        var model = await RunAsync(new FakeSource { Printer = null });
        var row = Row(model, DiagnosticIds.Paired);
        Assert.Equal(DiagnosticStatus.Unknown, row.Status);
        Assert.Equal(DiagnosticAction.FindPrinters, row.Action);
    }

    [Fact]
    public async Task What_the_service_finds_is_explained_with_what_to_do()
    {
        var source = new FakeSource
        {
            ServiceChecks =
            [
                new(DiagnosticIds.Link, DiagnosticStatus.Fail, "Sin enlace"),
                new(DiagnosticIds.Paper, DiagnosticStatus.Warn, "OutOfPaper"),
                new(DiagnosticIds.Queue, DiagnosticStatus.Fail, "X5h Thermal Printer"),
                new(DiagnosticIds.Ipp, DiagnosticStatus.Fail, "8631"),
                new(DiagnosticIds.Raw, DiagnosticStatus.Fail, "El puerto 9100 está en uso"),
                new(DiagnosticIds.Fonts, DiagnosticStatus.Warn, "japonés, chino y coreano"),
            ],
        };
        var model = await RunAsync(source);

        Assert.Equal(DiagnosticAction.ConnectPrinter, Row(model, DiagnosticIds.Link).Action);
        Assert.Contains("Sin enlace", Row(model, DiagnosticIds.Link).Message);

        Assert.Contains("sin papel", Row(model, DiagnosticIds.Paper).Message);
        Assert.Contains("esperan solos", Row(model, DiagnosticIds.Paper).Hint);

        Assert.Equal(DiagnosticAction.OpenSettingsPaper, Row(model, DiagnosticIds.Queue).Action);
        Assert.Contains("«X5h Thermal Printer»", Row(model, DiagnosticIds.Queue).Message);

        Assert.Equal(DiagnosticAction.OpenSettingsNetwork, Row(model, DiagnosticIds.Ipp).Action);
        Assert.Contains("8631", Row(model, DiagnosticIds.Ipp).Message);

        Assert.Equal(DiagnosticAction.OpenSettingsRaw, Row(model, DiagnosticIds.Raw).Action);
        Assert.Contains("en uso", Row(model, DiagnosticIds.Raw).Message);

        Assert.Contains("japonés", Row(model, DiagnosticIds.Fonts).Message);
        Assert.Equal("Hay 4 problemas.", model.Summary);
    }

    [Fact]
    public async Task No_printer_chosen_leads_to_the_search_and_not_to_a_connection()
    {
        var source = new FakeSource { ServiceChecks = [new(DiagnosticIds.Link, DiagnosticStatus.Fail, "No hay ninguna impresora elegida.")] };
        Assert.Equal(DiagnosticAction.FindPrinters, Row(await RunAsync(source), DiagnosticIds.Link).Action);
    }

    [Fact]
    public async Task The_raw_port_off_and_listening_read_differently_and_the_firewall_is_named()
    {
        var off = await RunAsync(new FakeSource());
        Assert.Contains("Desactivado", Row(off, DiagnosticIds.Raw).Message);

        var listening = await RunAsync(new FakeSource { ServiceChecks = [new(DiagnosticIds.Raw, DiagnosticStatus.Ok, "127.0.0.1:9100")] });
        Assert.Contains("127.0.0.1:9100", Row(listening, DiagnosticIds.Raw).Message);

        var firewall = await RunAsync(new FakeSource { ServiceChecks = [new(DiagnosticIds.Raw, DiagnosticStatus.Warn, "Falta la regla del cortafuegos para redes privadas.")] });
        Assert.Contains("cortafuegos", Row(firewall, DiagnosticIds.Raw).Message);
    }

    [Theory]
    [InlineData(0, 1, 0, DiagnosticStatus.Warn, "Hay un aviso.")]
    [InlineData(0, 3, 0, DiagnosticStatus.Warn, "Hay 3 avisos.")]
    [InlineData(2, 3, 1, DiagnosticStatus.Fail, "Hay 2 problemas.")]
    [InlineData(1, 0, 0, DiagnosticStatus.Fail, "Hay un problema.")]
    [InlineData(0, 0, 1, DiagnosticStatus.Unknown, "No se pudo comprobar una cosa.")]
    public async Task The_summary_counts_the_worst_first(int failures, int warnings, int unknowns, string overall, string expected)
    {
        // One check per id, so that each counts once.
        string[] ids = [DiagnosticIds.Link, DiagnosticIds.Paper, DiagnosticIds.Queue, DiagnosticIds.Ipp, DiagnosticIds.Raw, DiagnosticIds.Fonts];
        var checks = new List<DiagnosticCheckDto>();
        foreach (var (status, count) in new[] { (DiagnosticStatus.Fail, failures), (DiagnosticStatus.Warn, warnings), (DiagnosticStatus.Unknown, unknowns) })
            for (var i = 0; i < count; i++)
                checks.Add(new DiagnosticCheckDto(ids[checks.Count], status, null));
        var model = await RunAsync(new FakeSource { ServiceChecks = checks });
        Assert.Equal(expected, model.Summary);
        Assert.Equal(overall, model.OverallStatus);
    }

    [Theory]
    [InlineData(DiagnosticStatus.Ok)]
    [InlineData(DiagnosticStatus.Warn)]
    [InlineData(DiagnosticStatus.Fail)]
    [InlineData(DiagnosticStatus.Unknown)]
    public void Every_check_has_texts_for_every_result_it_can_give(string status)
    {
        foreach (var id in new[] { DiagnosticIds.Service, DiagnosticIds.Bluetooth, DiagnosticIds.Paired, DiagnosticIds.Link, DiagnosticIds.Paper,
                     DiagnosticIds.Queue, DiagnosticIds.Ipp, DiagnosticIds.Raw, DiagnosticIds.Fonts })
        {
            var row = DiagnosticsViewModel.Describe(new DiagnosticCheckDto(id, status, "dato"));
            Assert.DoesNotContain("[Diag.", row.Title + row.Message + row.Hint + row.ActionText);
            Assert.NotEqual("", row.Title);
            Assert.NotEqual("", row.Message);
            if (status != DiagnosticStatus.Ok)
                Assert.NotEqual("", row.Hint);
            if (row.HasAction)
                Assert.NotEqual("", row.ActionText);
        }
    }

    [Fact]
    public void An_unknown_status_from_a_newer_service_reads_as_unknown()
    {
        var row = DiagnosticsViewModel.Describe(new DiagnosticCheckDto(DiagnosticIds.Link, "mystery", null));
        Assert.Equal(DiagnosticStatus.Unknown, row.Status);
    }

    // ---- the report --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_report_has_versions_settings_and_results_but_no_secrets()
    {
        var source = new FakeSource
        {
            ServiceChecks =
            [
                new(DiagnosticIds.Link, DiagnosticStatus.Ok, null),
                new(DiagnosticIds.Raw, DiagnosticStatus.Ok, "192.168.1.5:9100, 127.0.0.1:9100"),
                new(DiagnosticIds.Queue, DiagnosticStatus.Fail, "X5h Thermal Printer"),
            ],
        };
        var model = await RunAsync(source);
        var settings = new ServiceSettings
        {
            Printer = Printer,
            NetworkMode = NetworkMode.Lan,
            RawPortEnabled = true,
            AutomationApiEnabled = true,
            JobHistoryKeep = 7,
        };
        var report = DiagnosticsReport.Build(new DiagnosticsReportContext("0.9.0", "0.9.0", "Windows 11", settings, Printer, "Sin enlace con la impresora",
            new DateTimeOffset(2026, 10, 3, 12, 30, 0, TimeSpan.Zero)), model.Rows);

        Assert.Contains("0.9.0", report);
        Assert.Contains("Windows 11", report);
        Assert.Contains("red local", report);
        Assert.Contains("historial 7", report);
        Assert.Contains("Sin enlace con la impresora", report);
        Assert.Contains("[fallo]", report);
        Assert.Contains("[ok]", report);

        // Nothing that identifies the printer, the clients or the network, and no token.
        Assert.DoesNotContain("7A:E0:0C:1D:87:AE", report);
        Assert.DoesNotContain("X5h-E07A", report);
        Assert.DoesNotContain("192.168.1.5", report);
        Assert.DoesNotContain("token", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_report_works_without_settings_or_printer()
    {
        var model = await RunAsync(new FakeSource { ServiceFailure = new InvalidOperationException("sin servicio"), Printer = null });
        var report = DiagnosticsReport.Build(new DiagnosticsReportContext("0.9.0", "—", "Windows 11", null, null, null, DateTimeOffset.UtcNow), model.Rows);
        Assert.Contains("ninguna elegida", report);
        Assert.Contains("[fallo]", report);
        Assert.DoesNotContain("Último error", report);
    }
}
