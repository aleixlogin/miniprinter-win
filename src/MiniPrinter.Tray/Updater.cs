using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using MiniPrinter.Gui;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MiniPrinter.Updates;

namespace MiniPrinter.Tray;

/// <summary>Per-user update state (%LocalAppData%\MiniPrinter\updates.json).</summary>
public sealed record UpdateState
{
    public bool AutoCheck { get; init; } = true;
    public string? SkippedVersion { get; init; }
    public DateTimeOffset? LastCheck { get; init; }

    private static readonly string PathOnDisk = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPrinter", "updates.json");

    public static UpdateState Load()
    {
        try
        {
            return File.Exists(PathOnDisk) ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(PathOnDisk)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk)!);
            File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(this));
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>
/// Checks GitHub releases (1 minute after start, then every 24 h), offers updates and installs them
/// by running the verified installer, which asks for elevation itself (design.md D1–D3).
/// </summary>
public sealed class Updater : IDisposable
{
    /// <summary>ECDSA P-256 public key that verifies SHA256SUMS.sig of every release.</summary>
    public const string ReleasePublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAET+35Xc74j167TAl945THl6vTB49s2AWwEpstARSiU45GHZFvtzBWkfTe7ZxcuP/B8s8RV3U2YWBe79NqG70Kmg==";

    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    private readonly ServiceConnection _service;
    private readonly Action<string, string, bool, Action?> _notify;
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private UpdateWindow? _window;

    public Updater(ServiceConnection service, Action<string, string, bool, Action?> notify)
    {
        _service = service;
        _notify = notify;
        State = UpdateState.Load();
        _timer = new System.Windows.Threading.DispatcherTimer { Interval = FirstCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (State.AutoCheck)
                await CheckAsync(manual: false);
        };
        _timer.Start();
    }

    public UpdateState State { get; private set; }

    public static Version CurrentVersion =>
        UpdateSelector.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    public void SetAutoCheck(bool enabled)
    {
        State = State with { AutoCheck = enabled };
        State.Save();
    }

    /// <summary>Checks now. Manual checks report "up to date" and errors; automatic checks stay quiet.</summary>
    public async Task CheckAsync(bool manual)
    {
        UpdateOffer? offer;
        try
        {
            using var client = new UpdateClient(ReleasePublicKey, CurrentVersion);
            var release = await client.GetLatestReleaseAsync();
            var skipped = manual ? null : UpdateSelector.ParseTag(State.SkippedVersion);
            offer = UpdateSelector.Select(release, CurrentVersion, skipped);
            State = State with { LastCheck = DateTimeOffset.UtcNow };
            State.Save();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            if (manual)
                _notify("MiniPrinter", Strings.Get("Update.CheckFailed", ex.Message), true, null);
            return;
        }

        if (offer is null)
        {
            if (manual)
                _notify("MiniPrinter", Strings.Get("Update.UpToDate", CurrentVersion), false, null);
            return;
        }
        if (manual)
            Show(offer);
        else
            _notify(Strings.Get("Update.AvailableTitle"), Strings.Get("Update.AvailableText", offer.Version), false, () => Show(offer));
    }

    public void Show(UpdateOffer offer)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }
        _window = new UpdateWindow(offer, this, _service);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        _window.Activate();
    }

    public void Skip(Version version)
    {
        State = State with { SkippedVersion = version.ToString() };
        State.Save();
    }

    /// <summary>Downloads and verifies the installer, then starts it (the installer requests UAC).</summary>
    public async Task<string?> InstallAsync(UpdateOffer offer, IProgress<double> progress, CancellationToken ct)
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MiniPrinter-update", offer.Version.ToString());
        string installer;
        try
        {
            using var client = new UpdateClient(ReleasePublicKey, CurrentVersion);
            installer = await client.DownloadAsync(offer, folder, progress, ct);
        }
        catch (UpdateVerificationException ex)
        {
            return Strings.Get("Update.VerifyFailed", ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return Strings.Get("Update.DownloadFailed", ex.Message);
        }

        try
        {
            Process.Start(new ProcessStartInfo(installer, "/SILENT /SUPPRESSMSGBOXES /NORESTART")
            {
                UseShellExecute = true, // lets the installer's manifest trigger UAC
                WorkingDirectory = folder,
            });
            return null; // the installer closes this app, updates and reopens it
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return Strings.Get("Update.Canceled");
        }
        catch (Win32Exception ex)
        {
            return Strings.Get("Update.StartFailed", ex.Message);
        }
    }

    public void Dispose() => _timer.Stop();
}

/// <summary>Release notes and the Update / Later / Skip choice, with download progress.</summary>
public sealed class UpdateWindow : ThemedWindow
{
    private readonly UpdateOffer _offer;
    private readonly Updater _updater;
    private readonly ProgressBar _progress = new() { Height = 8, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _status = Themed.Brush(new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }, Themed.Muted);
    private readonly StackPanel _buttons = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
    private CancellationTokenSource? _download;

    public UpdateWindow(UpdateOffer offer, Updater updater, ServiceConnection service)
    {
        _offer = offer;
        _updater = updater;
        Title = Strings.Get("Update.DialogTitle", offer.Version);
        Width = 560;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/miniprinter.ico"));

        var header = new TextBlock
        {
            Text = Strings.Get("Update.NewVersion", offer.Version, Updater.CurrentVersion),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        };
        var notes = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(offer.Notes) ? Strings.Get("Update.NoNotes") : offer.Notes.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 8, 0, 0),
        };
        if (service.Status is { QueuedJobs: > 0 } status)
            _status.Text = Strings.Get("Update.JobsInQueue", status.QueuedJobs);

        var update = new Button { Content = Strings.Get("Update.Install"), FontWeight = FontWeights.SemiBold, IsDefault = true };
        update.Click += async (_, _) => await UpdateAsync();
        var later = new Button { Content = Strings.Get("Update.Later"), IsCancel = true };
        later.Click += (_, _) => Close();
        var skip = new Button { Content = Strings.Get("Update.Skip") };
        skip.Click += (_, _) => { _updater.Skip(offer.Version); Close(); };
        _buttons.Children.Add(update);
        _buttons.Children.Add(later);
        _buttons.Children.Add(skip);

        var layout = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var bottom = new StackPanel();
        bottom.Children.Add(_progress);
        bottom.Children.Add(_status);
        bottom.Children.Add(_buttons);
        DockPanel.SetDock(bottom, Dock.Bottom);
        layout.Children.Add(bottom);
        layout.Children.Add(notes);
        Content = layout;

        Closed += (_, _) => _download?.Cancel();
    }

    private async Task UpdateAsync()
    {
        _buttons.IsEnabled = false;
        _progress.Visibility = Visibility.Visible;
        _status.Text = Strings.Get("Update.Downloading");
        _download = new CancellationTokenSource();
        var error = await _updater.InstallAsync(_offer, new Progress<double>(p => _progress.Value = p * 100), _download.Token);
        if (error is null)
        {
            _status.Text = Strings.Get("Update.Installing");
            return;
        }
        _status.Text = error;
        _progress.Visibility = Visibility.Collapsed;
        _buttons.IsEnabled = true;
    }
}
