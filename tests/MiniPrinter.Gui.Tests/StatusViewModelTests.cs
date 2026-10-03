using MiniPrinter.Control;

namespace MiniPrinter.Gui.Tests;

/// <summary>tray-status-dashboard and job-history: the single state of the card, the battery, the actions and the list of jobs.</summary>
public class StatusViewModelTests
{
    private static StatusDto Healthy() => new()
    {
        Link = "Connected",
        Printer = new PrinterSelection { Name = "X5h-E07A", Address = "AA:BB", Transport = TransportChoice.Rfcomm },
        Ready = true,
        Version = "0.9.0",
        IppUrls = ["http://127.0.0.1:8631/ipp/print"],
    };

    private static JobDto Job(int id, string state, string? message = null, string? source = "Windows", string? origin = "maria",
        bool canReprint = false, int pages = 1) =>
        new(id, $"doc {id}", "maria", state, message, new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero).AddSeconds(id), null, pages, 100)
        {
            Source = source,
            Origin = origin,
            CanReprint = canReprint,
        };

    private sealed class FakeGateway : IStatusGateway
    {
        public List<string> Calls { get; } = [];
        public IReadOnlyList<JobDto> Jobs { get; set; } = [];
        public Exception? Failure { get; set; }

        private Task Record(string call)
        {
            Calls.Add(call);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task ConnectAsync() => Record("connect");
        public Task DisconnectAsync() => Record("disconnect");
        public Task TestPrintAsync() => Record("test");
        public Task FeedAsync() => Record("feed");
        public Task StartSamplingAsync(TimeSpan duration) => Record($"sample:{duration.TotalHours}");
        public Task StopSamplingAsync() => Record("stop-sample");
        public Task<IReadOnlyList<JobDto>> GetJobsAsync() => Failure is null ? Task.FromResult(Jobs) : Task.FromException<IReadOnlyList<JobDto>>(Failure);
        public Task CancelJobAsync(int jobId) => Record($"cancel:{jobId}");

        public Task<JobDto> ReprintJobAsync(int jobId)
        {
            Calls.Add($"reprint:{jobId}");
            return Task.FromResult(Job(99, "Pending", source: "Panel"));
        }
    }

    private static StatusViewModel Create(out FakeGateway gateway)
    {
        gateway = new FakeGateway();
        return new StatusViewModel(gateway, "0.9.0");
    }

    // ---- the single state, by priority --------------------------------------------------------------------------------

    public static IEnumerable<object[]> States()
    {
        var printer = new PrinterSelection { Name = "X5h", Address = "AA" };
        yield return [null!, false, StatusKind.ServiceUnavailable, StatusSeverity.Error];
        yield return [Healthy(), false, StatusKind.ServiceUnavailable, StatusSeverity.Error]; // old data does not win over a dead service
        yield return [Healthy() with { Printer = null }, true, StatusKind.NoPrinter, StatusSeverity.Info];
        yield return [Healthy() with { Alarms = ["OutOfPaper"], Ready = false }, true, StatusKind.Alarm, StatusSeverity.Warn];
        yield return [Healthy() with { Alarms = ["Overheated"] }, true, StatusKind.Alarm, StatusSeverity.Warn];
        yield return [Healthy() with { Alarms = ["LowBattery"] }, true, StatusKind.Alarm, StatusSeverity.Warn];
        yield return [Healthy() with { Link = "Error", LastError = "Sin enlace" }, true, StatusKind.LinkError, StatusSeverity.Error];
        yield return [Healthy() with { Link = "Connecting" }, true, StatusKind.Connecting, StatusSeverity.Info];
        yield return [Healthy() with { Reconnecting = true, Link = "Error" }, true, StatusKind.Alarm == StatusKind.Alarm ? StatusKind.LinkError : StatusKind.LinkError, StatusSeverity.Error];
        yield return [Healthy() with { Printing = true }, true, StatusKind.Printing, StatusSeverity.Info];
        yield return [Healthy(), true, StatusKind.Ready, StatusSeverity.Ok];
        yield return [Healthy() with { Link = "Disconnected" }, true, StatusKind.Disconnected, StatusSeverity.Neutral];
        yield return [new StatusDto { Printer = printer, Link = "Disconnected", Alarms = ["OutOfPaper"] }, true, StatusKind.Alarm, StatusSeverity.Warn];
    }

    [Theory]
    [MemberData(nameof(States))]
    public void The_card_shows_one_state_by_priority(StatusDto? status, bool available, StatusKind kind, StatusSeverity severity)
    {
        var model = Create(out _);
        model.Update(status, available, available ? null : "No hay respuesta");
        Assert.Equal(kind, model.Summary.Kind);
        Assert.Equal(severity, model.Summary.Severity);
        Assert.NotEqual("", model.Summary.Title);
        Assert.NotEqual("", model.Summary.Message);
    }

    [Fact]
    public void Reconnecting_wins_over_printing_and_ready()
    {
        var model = Create(out _);
        model.Update(Healthy() with { Reconnecting = true, Printing = true }, true, null);
        Assert.Equal(StatusKind.Connecting, model.Summary.Kind);
    }

    [Fact]
    public void No_paper_says_jobs_wait_and_will_print_by_themselves()
    {
        var model = Create(out _);
        model.Update(Healthy() with { Alarms = ["OutOfPaper"] }, true, null);
        Assert.Equal("Sin papel", model.Summary.Title);
        Assert.Contains("esperan", model.Summary.Message);
    }

    [Fact]
    public void The_alarms_have_a_priority_and_unknown_ones_keep_their_name()
    {
        var model = Create(out _);
        model.Update(Healthy() with { Alarms = ["LowBattery", "OutOfPaper"] }, true, null);
        Assert.Equal("Sin papel", model.Summary.Title);
        model.Update(Healthy() with { Alarms = ["Jammed"] }, true, null);
        Assert.Equal("Jammed", model.Summary.Title);
    }

    [Fact]
    public void A_stopped_service_names_the_problem_it_reported()
    {
        var model = Create(out _);
        model.Update(null, false, "Connection refused");
        Assert.Equal("Servicio no disponible", model.Summary.Title);
        Assert.Equal("Connection refused", model.Summary.Message);
        model.Update(null, false, null);
        Assert.Contains("iniciado", model.Summary.Message);
    }

    [Fact]
    public void The_button_of_the_card_asks_for_what_the_state_needs()
    {
        var model = Create(out _);
        var requested = new List<StatusAction>();
        model.ActionRequested += requested.Add;

        model.Update(Healthy() with { Printer = null }, true, null);
        Assert.Equal("Buscar impresoras", model.Summary.ActionText);
        model.SummaryAction.Execute(null);

        model.Update(Healthy() with { Link = "Disconnected" }, true, null);
        model.SummaryAction.Execute(null);

        model.Update(Healthy(), true, null);
        Assert.False(model.SummaryAction.CanExecute(null));
        Assert.Equal([StatusAction.FindPrinters, StatusAction.Connect], requested);
    }

    // ---- battery --------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_known_unit_shows_a_bar_and_an_unknown_one_the_raw_value()
    {
        var model = Create(out _);
        model.Update(Healthy() with { BatteryLevel = 39, BatteryUnit = BatteryUnit.Percent, BatteryPercent = 39 }, true, null);
        Assert.True(model.ShowsBatteryBar);
        Assert.Equal(39, model.BatteryPercent);
        Assert.Equal("39 %", model.BatteryText);
        Assert.False(model.BatteryIsLow);

        model.Update(Healthy() with { BatteryLevel = 39, BatteryUnit = BatteryUnit.Unknown }, true, null);
        Assert.False(model.ShowsBatteryBar);
        Assert.Contains("valor en bruto", model.BatteryText);
    }

    [Fact]
    public void A_battery_under_the_threshold_is_marked()
    {
        var model = Create(out _);
        model.Update(Healthy() with { BatteryLevel = 12, BatteryUnit = BatteryUnit.Percent, BatteryPercent = 12, LowBattery = true }, true, null);
        Assert.True(model.BatteryIsLow);
        Assert.Contains("baja", model.BatteryText, StringComparison.OrdinalIgnoreCase);
    }

    // ---- actions --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Connect_toggles_with_the_link_and_the_secondary_actions_reach_the_service()
    {
        var model = Create(out var gateway);
        model.Update(Healthy() with { Link = "Disconnected" }, true, null);
        Assert.Equal("Conectar", model.ConnectText);
        await model.ConnectOrDisconnect.ExecuteAsync();
        model.Update(Healthy(), true, null);
        Assert.Equal("Desconectar", model.ConnectText);
        await model.ConnectOrDisconnect.ExecuteAsync();
        await model.TestPrint.ExecuteAsync();
        await model.Feed.ExecuteAsync();
        Assert.Equal(["connect", "disconnect", "test", "feed"], gateway.Calls);
    }

    [Fact]
    public void The_printer_actions_need_a_service_that_answers_and_a_printer_chosen()
    {
        var model = Create(out _);
        model.Update(null, false, null);
        Assert.False(model.ConnectOrDisconnect.CanExecute(null));
        Assert.False(model.TestPrint.CanExecute(null));
        model.Update(Healthy() with { Printer = null }, true, null);
        Assert.False(model.TestPrint.CanExecute(null));
        model.Update(Healthy(), true, null);
        Assert.True(model.ConnectOrDisconnect.CanExecute(null));
        Assert.True(model.TestPrint.CanExecute(null));
        Assert.True(model.Feed.CanExecute(null));
    }

    [Fact]
    public async Task Sampling_starts_for_eight_hours_and_stops_when_active()
    {
        var model = Create(out var gateway);
        model.Update(Healthy(), true, null);
        Assert.Equal("Muestrear batería 8 h", model.SamplingText);
        await model.ToggleSampling.ExecuteAsync();
        model.Update(Healthy() with { SamplingUntil = DateTimeOffset.UtcNow.AddHours(8) }, true, null);
        await model.ToggleSampling.ExecuteAsync();
        Assert.Equal(["sample:8", "stop-sample"], gateway.Calls);
        Assert.NotEqual("", model.SamplingInfo);
    }

    [Fact]
    public async Task A_failing_action_is_reported_and_does_not_throw()
    {
        var model = Create(out var gateway);
        gateway.Failure = new InvalidOperationException("sin servicio");
        Exception? reported = null;
        model.Failed += ex => reported = ex;
        model.Update(Healthy(), true, null);
        await model.TestPrint.ExecuteAsync();
        Assert.Equal("sin servicio", reported?.Message);
    }

    [Fact]
    public void The_details_are_remembered_through_an_event()
    {
        var model = new StatusViewModel(new FakeGateway(), "0.9.0", detailsOpen: true);
        Assert.True(model.DetailsOpen);
        bool? remembered = null;
        model.DetailsOpenChanged += open => remembered = open;
        model.DetailsOpen = false;
        Assert.False(remembered);
    }

    [Fact]
    public void The_details_hold_the_data_of_little_use()
    {
        var model = Create(out _);
        model.Update(Healthy() with { Firmware = "1.2.3" }, true, null);
        Assert.Equal("1.2.3", model.FirmwareText);
        Assert.Contains("8631", model.UrlsText);
        Assert.Contains("0.9.0", model.ServiceText);
        Assert.Contains("X5h-E07A", model.PrinterText);
    }

    // ---- jobs -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_printed_job_reads_completed_printed_and_its_origin()
    {
        var row = new JobRowViewModel(Job(1, "Completed", "Printed"));
        Assert.Equal("Completado", row.StateText);
        Assert.Equal("Impreso", row.MessageText);
        Assert.Equal("Windows · maria", row.OriginText);
        Assert.Equal(JobIcon.Done, row.Icon);
    }

    [Fact]
    public void A_job_waiting_for_paper_reads_waiting_no_paper_with_the_warning_icon()
    {
        var row = new JobRowViewModel(Job(2, "ProcessingStopped", "Printer needs attention: OutOfPaper"));
        Assert.Equal("En espera", row.StateText);
        Assert.Equal("Sin papel", row.MessageText);
        Assert.Equal(JobIcon.Held, row.Icon);
        Assert.True(row.CanCancel);
    }

    [Theory]
    [InlineData("Windows", "maria", "Windows · maria")]
    [InlineData("Panel", "maria", "Panel")]
    [InlineData("Api", "192.168.0.30", "API · 192.168.0.30")]
    [InlineData("Raw", "192.168.0.30", "Puerto 9100 · 192.168.0.30")]
    [InlineData("Raw", null, "Puerto 9100")]
    [InlineData(null, null, "")]
    public void The_origin_says_the_entrance_and_who_sent_it(string? source, string? origin, string expected) =>
        Assert.Equal(expected, new JobRowViewModel(Job(1, "Completed", source: source, origin: origin)).OriginText);

    [Theory]
    [InlineData("Pending", JobIcon.Waiting)]
    [InlineData("Processing", JobIcon.Printing)]
    [InlineData("Completed", JobIcon.Done)]
    [InlineData("Aborted", JobIcon.Failed)]
    [InlineData("Canceled", JobIcon.Canceled)]
    public void Each_state_has_its_icon(string state, JobIcon icon) =>
        Assert.Equal(icon, new JobRowViewModel(Job(1, state)).Icon);

    [Fact]
    public void Reprinting_is_offered_only_for_finished_jobs_whose_pages_are_kept()
    {
        Assert.True(new JobRowViewModel(Job(1, "Completed", canReprint: true)).CanReprint);
        Assert.True(new JobRowViewModel(Job(1, "Canceled", canReprint: true)).CanReprint);
        Assert.False(new JobRowViewModel(Job(1, "Completed", canReprint: false)).CanReprint);
        Assert.False(new JobRowViewModel(Job(1, "Processing", canReprint: true)).CanReprint);
    }

    [Fact]
    public void The_reason_for_a_missing_preview_is_given()
    {
        Assert.Null(new JobRowViewModel(Job(1, "Completed", canReprint: true)).PreviewProblem);
        Assert.Contains("ya no está guardado", new JobRowViewModel(Job(1, "Completed")).PreviewProblem);
        Assert.Contains("demasiado grande", new JobRowViewModel(Job(1, "Completed", pages: 55)).PreviewProblem);
        Assert.Contains("todavía no ha terminado", new JobRowViewModel(Job(1, "Processing")).PreviewProblem);
    }

    [Fact]
    public void Refreshing_keeps_the_rows_and_the_selection()
    {
        var model = Create(out var gateway);
        gateway.Jobs = [Job(3, "Processing"), Job(2, "Completed"), Job(1, "Completed")];
        model.MergeJobs(gateway.Jobs);
        var selected = model.Jobs.Single(j => j.Id == 2);
        model.SelectedJob = selected;

        gateway.Jobs = [Job(4, "Pending"), Job(3, "Completed", "Printed"), Job(2, "Completed")];
        model.MergeJobs(gateway.Jobs);

        Assert.Equal([4, 3, 2], model.Jobs.Select(j => j.Id));
        Assert.Same(selected, model.SelectedJob);
        Assert.Same(selected, model.Jobs.Single(j => j.Id == 2));
        Assert.Equal("Completado", model.Jobs.Single(j => j.Id == 3).StateText);
    }

    [Fact]
    public void A_row_that_changes_tells_the_view()
    {
        var model = Create(out _);
        model.MergeJobs([Job(1, "Processing")]);
        var row = model.Jobs[0];
        var changed = 0;
        row.PropertyChanged += (_, _) => changed++;
        model.MergeJobs([Job(1, "Completed", "Printed")]);
        Assert.True(changed > 0);
        Assert.Equal("Impreso", row.MessageText);
    }

    [Fact]
    public void A_selected_job_that_leaves_the_list_clears_the_selection()
    {
        var model = Create(out _);
        model.MergeJobs([Job(2, "Completed"), Job(1, "Completed")]);
        model.SelectedJob = model.Jobs.Single(j => j.Id == 1);
        model.MergeJobs([Job(2, "Completed")]);
        Assert.Null(model.SelectedJob);
    }

    [Fact]
    public async Task Cancel_and_reprint_apply_to_the_selected_job_when_they_make_sense()
    {
        var model = Create(out var gateway);
        model.MergeJobs([Job(2, "Processing"), Job(1, "Completed", canReprint: true)]);
        Assert.False(model.CancelJob.CanExecute(null));

        model.SelectedJob = model.Jobs.Single(j => j.Id == 2);
        Assert.True(model.CancelJob.CanExecute(null));
        Assert.False(model.ReprintJob.CanExecute(null));
        await model.CancelJob.ExecuteAsync();

        model.SelectedJob = model.Jobs.Single(j => j.Id == 1);
        Assert.False(model.CancelJob.CanExecute(null));
        Assert.True(model.ReprintJob.CanExecute(null));
        JobDto? again = null;
        model.Reprinted += job => again = job;
        Exception? failure = null;
        model.Failed += ex => failure = ex;
        await model.ReprintJob.ExecuteAsync();

        Assert.Null(failure);
        Assert.Equal(["cancel:2", "reprint:1"], gateway.Calls);
        Assert.Equal(99, again?.Id);
    }

    [Fact]
    public async Task A_service_that_does_not_answer_leaves_the_list_as_it_was()
    {
        var model = Create(out var gateway);
        model.MergeJobs([Job(1, "Completed")]);
        gateway.Failure = new HttpRequestException("down");
        await model.RefreshJobsAsync();
        Assert.Single(model.Jobs);
    }

    [Fact]
    public void The_list_is_asked_for_at_most_twice_a_second()
    {
        var model = Create(out _);
        var start = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(model.ShouldRefreshJobs(start));
        Assert.False(model.ShouldRefreshJobs(start.AddMilliseconds(200)));
        Assert.True(model.ShouldRefreshJobs(start.AddMilliseconds(700)));
    }

    [Fact]
    public void Old_services_that_do_not_send_the_new_fields_still_read()
    {
        // A JSON of a service before 0.9 has no source, origin or canReprint.
        var json = """{"id":5,"name":"x","user":"u","state":"Completed","message":"Printed","created":"2026-10-03T12:00:00+00:00","completed":null,"pages":1,"sizeBytes":10}""";
        var job = System.Text.Json.JsonSerializer.Deserialize<JobDto>(json, ControlDefaults.Json)!;
        var row = new JobRowViewModel(job);
        Assert.Equal("", row.OriginText);
        Assert.False(row.CanReprint);
        Assert.Equal("Impreso", row.MessageText);
    }

    [Fact]
    public void Every_state_text_exists()
    {
        foreach (var key in new[] { "Status.ServiceUnavailable.Title", "Status.NoPrinter.Action", "Preview.NotKept", "Job.Source.Raw" })
            Assert.True(Strings.Exists(key), key);
    }
}
