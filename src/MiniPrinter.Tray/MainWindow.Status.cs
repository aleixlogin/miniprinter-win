using System.Windows;
using System.Windows.Input;
using MiniPrinter.Control;
using MiniPrinter.Gui;

namespace MiniPrinter.Tray;

/// <summary>The Status tab: the view model and what it needs from the window (navigation, files, the preview window).</summary>
public partial class MainWindow
{
    private StatusViewModel _status = null!;
    private DateTime _lastClientsRefresh = DateTime.MinValue;

    /// <summary>The view model of the Status tab (UiShots fills it to photograph the panel).</summary>
    public StatusViewModel Status => _status;

    private void InitStatus()
    {
        var app = (App)Application.Current;
        _status = new StatusViewModel(new ServiceStatusGateway(_service), Updater.CurrentVersion.ToString(), app.Preferences.StatusDetailsOpen);
        _status.Failed += ex => MessageBox.Show(this, ex.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
        _status.DetailsOpenChanged += open => app.UpdatePreferences(p => p with { StatusDetailsOpen = open });
        _status.ActionRequested += action =>
        {
            switch (action)
            {
                case StatusAction.FindPrinters:
                    Tabs.SelectedItem = SearchTab;
                    StartScan();
                    break;
                case StatusAction.Connect:
                    _status.ConnectOrDisconnect.Execute(null);
                    break;
            }
        };
        _status.Reprinted += again => _ = _status.RefreshJobsAsync();
        _status.DiagnosticsRequested += OpenDiagnostics;
        StatusRoot.DataContext = _status;
    }

    public void ShowStatus(StatusDto? status, bool available, string? error)
    {
        _status.Update(status, available, error);
        _settings.Raw.ShowStatus(status?.RawPort, status?.NetworkMode ?? NetworkMode.Local);
        // While the direct printing section is open, the list of recent clients follows what the port receives.
        if (Tabs.SelectedItem == SettingsTab && _settings.Selected == _settings.Raw && DateTime.UtcNow - _lastClientsRefresh > TimeSpan.FromSeconds(3))
        {
            _lastClientsRefresh = DateTime.UtcNow;
            _ = _settings.Raw.Tools.RefreshClientsAsync();
        }
        if (!available || status is null)
            return;
        if (!_wizard)
            Banner.Visibility = Visibility.Collapsed;
        if (_status.ShouldRefreshJobs(DateTime.UtcNow))
            _ = _status.RefreshJobsAsync();
    }

    private void ShowBanner(string text)
    {
        BannerText.Text = text;
        Banner.Visibility = Visibility.Visible;
    }

    // ---- diagnostics -------------------------------------------------------------------------------------------------

    private void OpenDiagnostics()
    {
        var model = new DiagnosticsViewModel(new TrayDiagnosticsSource(_service));
        new DiagnosticsWindow(this, model, ReportContext, PerformDiagnosticAction).Show();
    }

    private DiagnosticsReportContext ReportContext()
    {
        var status = _service.Status;
        return new DiagnosticsReportContext(Updater.CurrentVersion.ToString(), status?.Version is { Length: > 0 } version ? version : "—",
            Environment.OSVersion.VersionString, _settings.Paper.Saved, status?.Printer, status?.LastError, DateTimeOffset.Now);
    }

    /// <summary>Leads to the fix of a row of the diagnostics.</summary>
    private void PerformDiagnosticAction(DiagnosticAction action)
    {
        void Section(string key)
        {
            Tabs.SelectedItem = SettingsTab;
            _settings.Selected = _settings.Sections.First(s => s.Key == key);
        }

        switch (action)
        {
            case DiagnosticAction.FindPrinters:
                Tabs.SelectedItem = SearchTab;
                StartScan();
                break;
            case DiagnosticAction.ConnectPrinter:
                Tabs.SelectedItem = StatusTab;
                _status.ConnectOrDisconnect.Execute(null);
                break;
            case DiagnosticAction.OpenBluetoothSettings:
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
                return;
            case DiagnosticAction.OpenSettingsPaper:
                Section("Paper");
                break;
            case DiagnosticAction.OpenSettingsNetwork:
                Section("Network");
                break;
            case DiagnosticAction.OpenSettingsRaw:
                Section("Raw");
                break;
        }
        Activate();
    }

    private void OnMoreActions(object sender, RoutedEventArgs e)
    {
        if (MoreButton.ContextMenu is not { } menu)
            return;
        menu.PlacementTarget = MoreButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private async void OnExportTelemetry(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"miniprinter-bateria-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Filter = "CSV (*.csv)|*.csv",
        };
        if (dialog.ShowDialog(this) != true)
            return;
        await Run(async c =>
        {
            await System.IO.File.WriteAllTextAsync(dialog.FileName, await c.ExportTelemetryAsync());
            return true;
        });
    }

    // ---- previews ----------------------------------------------------------------------------------------------------

    private void OnPreviewLast(object sender, RoutedEventArgs e) =>
        new JobPreviewWindow(this, Strings.Get("Preview.Last"), page => _service.Client.GetLastJobPageAsync(page), Strings.Get("Preview.NoLast"), reprint: null).Show();

    private void OnPreviewSelectedJob(object sender, RoutedEventArgs e)
    {
        if (_status.SelectedJob is { } job)
            ShowPreview(job);
    }

    private void OnJobDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double click on a row opens the preview, not one on the header or the empty space.
        if (e.OriginalSource is DependencyObject source && ItemsControlFor(source) is not null && _status.SelectedJob is { } job)
            ShowPreview(job);
    }

    private static System.Windows.Controls.ListViewItem? ItemsControlFor(DependencyObject source)
    {
        for (var node = source; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.ListViewItem item)
                return item;
        }
        return null;
    }

    /// <summary>Opens the pages of a job as they were printed, or says why they are not there.</summary>
    private void ShowPreview(JobRowViewModel job)
    {
        var title = Strings.Get("Preview.Title") + " · " + job.Name;
        var unavailable = job.PreviewProblem ?? Strings.Get("Preview.NotKept");
        Func<Task>? reprint = job.CanReprint ? async () => { await _service.Client.ReprintJobAsync(job.Id); await _status.RefreshJobsAsync(); } : null;
        new JobPreviewWindow(this, title, page => _service.Client.GetJobPageAsync(job.Id, page), unavailable, reprint).Show();
    }

    private async Task<bool> Run<T>(Func<ControlClient, Task<T>> action)
    {
        try
        {
            await action(_service.Client);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "MiniPrinter", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}

/// <summary>The service as the Status tab uses it.</summary>
internal sealed class ServiceStatusGateway(ServiceConnection service) : IStatusGateway
{
    private ControlClient Client => service.Client;

    public async Task ConnectAsync() => await Client.ConnectAsync();

    public async Task DisconnectAsync() => await Client.DisconnectAsync();

    public async Task TestPrintAsync() => await Client.TestPrintAsync();

    public async Task FeedAsync() => await Client.FeedAsync();

    public async Task StartSamplingAsync(TimeSpan duration) => await Client.StartSamplingAsync(duration);

    public Task StopSamplingAsync() => Client.StopSamplingAsync();

    public Task<IReadOnlyList<JobDto>> GetJobsAsync() => Client.GetJobsAsync();

    public Task CancelJobAsync(int jobId) => Client.CancelJobAsync(jobId);

    public Task<JobDto> ReprintJobAsync(int jobId) => Client.ReprintJobAsync(jobId);
}
