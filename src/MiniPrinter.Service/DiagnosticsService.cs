using System.Net.Sockets;
using MiniPrinter.Control;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;

namespace MiniPrinter.Service;

/// <summary>What the service can look at by itself. Each probe is a delegate so that the tests give it any state they like.</summary>
public sealed record DiagnosticProbes(
    Func<StatusDto> Status,
    Func<CancellationToken, Task<WindowsQueueDto>> Queue,
    Func<CancellationToken, Task<bool>> IppListening,
    Func<ServiceSettings> Settings,
    Func<RawPortDto> RawPort,
    Func<bool?> FirewallRuleExists,
    Func<FontSupport> Fonts);

/// <summary>Whether the fonts the ESC/POS interpreter draws with are on this PC.</summary>
public readonly record struct FontSupport(bool Cjk, bool Complex);

/// <summary>
/// The checks of the Diagnostics window that only the service can make: the link with the printer and its alarms, the printer in
/// Windows, the IPP and RAW listeners and the fonts. A check that cannot be completed in time is <c>unknown</c>, never a failure:
/// the window must not accuse something it could not look at.
/// </summary>
public sealed class DiagnosticsService
{
    private readonly DiagnosticProbes _probes;
    private readonly TimeSpan _timeout;

    public DiagnosticsService(DiagnosticProbes probes, TimeSpan? timeout = null)
    {
        _probes = probes;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task<IReadOnlyList<DiagnosticCheckDto>> RunAsync(CancellationToken ct)
    {
        var checks = new (string Id, Func<CancellationToken, Task<DiagnosticCheckDto>> Run)[]
        {
            (DiagnosticIds.Link, _ => Task.FromResult(Link())),
            (DiagnosticIds.Paper, _ => Task.FromResult(Paper())),
            (DiagnosticIds.Queue, QueueAsync),
            (DiagnosticIds.Ipp, IppAsync),
            (DiagnosticIds.Raw, _ => Task.FromResult(Raw())),
            (DiagnosticIds.Fonts, _ => Task.FromResult(Fonts())),
        };
        var results = await Task.WhenAll(checks.Select(c => Guarded(c.Id, c.Run, ct)));
        return results;
    }

    /// <summary>Runs one check with a time limit; a failure of the check itself is also <c>unknown</c>.</summary>
    private async Task<DiagnosticCheckDto> Guarded(string id, Func<CancellationToken, Task<DiagnosticCheckDto>> run, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_timeout);
        try
        {
            var task = run(limit.Token);
            var finished = await Task.WhenAny(task, Task.Delay(_timeout, ct));
            if (finished != task)
                return new DiagnosticCheckDto(id, DiagnosticStatus.Unknown, "No terminó de comprobarse a tiempo.");
            return await task;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DiagnosticCheckDto(id, DiagnosticStatus.Unknown, "No terminó de comprobarse a tiempo.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheckDto(id, DiagnosticStatus.Unknown, ex.Message);
        }
    }

    private DiagnosticCheckDto Link()
    {
        var status = _probes.Status();
        if (status.Printer is null)
            return new(DiagnosticIds.Link, DiagnosticStatus.Fail, "No hay ninguna impresora elegida.");
        return status.Link switch
        {
            "Connected" => new(DiagnosticIds.Link, DiagnosticStatus.Ok, null),
            "Connecting" => new(DiagnosticIds.Link, DiagnosticStatus.Warn, "Conectando con la impresora."),
            "Error" => new(DiagnosticIds.Link, DiagnosticStatus.Fail, status.LastError),
            // It connects by itself when something is printed: not a problem.
            _ => new(DiagnosticIds.Link, DiagnosticStatus.Ok, "Desconectada: se conecta al imprimir."),
        };
    }

    private DiagnosticCheckDto Paper()
    {
        var status = _probes.Status();
        if (status.Printer is null || (status.Link != "Connected" && status.LastSeen is null))
            return new(DiagnosticIds.Paper, DiagnosticStatus.Unknown, "Hasta que la impresora se conecta no se sabe.");
        if (status.Alarms.Count == 0)
            return new(DiagnosticIds.Paper, DiagnosticStatus.Ok, null);
        return new(DiagnosticIds.Paper, DiagnosticStatus.Warn, string.Join(", ", status.Alarms));
    }

    private async Task<DiagnosticCheckDto> QueueAsync(CancellationToken ct)
    {
        var queue = await _probes.Queue(ct);
        if (queue.Exists)
            return new(DiagnosticIds.Queue, DiagnosticStatus.Ok, queue.Name);
        return queue.State == "Running"
            ? new(DiagnosticIds.Queue, DiagnosticStatus.Warn, "La impresora de Windows se está recreando.")
            : new(DiagnosticIds.Queue, DiagnosticStatus.Fail, queue.Name);
    }

    private async Task<DiagnosticCheckDto> IppAsync(CancellationToken ct) =>
        await _probes.IppListening(ct)
            ? new(DiagnosticIds.Ipp, DiagnosticStatus.Ok, $"{_probes.Settings().IppPort}")
            : new(DiagnosticIds.Ipp, DiagnosticStatus.Fail, $"{_probes.Settings().IppPort}");

    private DiagnosticCheckDto Raw()
    {
        var settings = _probes.Settings();
        var raw = _probes.RawPort();
        if (!settings.RawPortEnabled)
            return new(DiagnosticIds.Raw, DiagnosticStatus.Ok, "Desactivado");
        if (raw.Error is { } error)
            return new(DiagnosticIds.Raw, DiagnosticStatus.Fail, error);
        if (!raw.Listening)
            return new(DiagnosticIds.Raw, DiagnosticStatus.Warn, "Todavía no escucha.");
        if (settings.NetworkMode == NetworkMode.Lan)
        {
            // On the local network the port also needs its rule of the firewall (for private networks).
            return _probes.FirewallRuleExists() switch
            {
                true => new(DiagnosticIds.Raw, DiagnosticStatus.Ok, string.Join(", ", raw.Addresses)),
                false => new(DiagnosticIds.Raw, DiagnosticStatus.Warn, "Falta la regla del cortafuegos para redes privadas."),
                null => new(DiagnosticIds.Raw, DiagnosticStatus.Unknown, "No se pudo consultar el cortafuegos."),
            };
        }
        return new(DiagnosticIds.Raw, DiagnosticStatus.Ok, string.Join(", ", raw.Addresses));
    }

    private DiagnosticCheckDto Fonts()
    {
        var fonts = _probes.Fonts();
        var missing = new List<string>();
        if (!fonts.Cjk)
            missing.Add("japonés, chino y coreano");
        if (!fonts.Complex)
            missing.Add("árabe y tailandés");
        return missing.Count == 0
            ? new(DiagnosticIds.Fonts, DiagnosticStatus.Ok, null)
            : new(DiagnosticIds.Fonts, DiagnosticStatus.Warn, string.Join("; ", missing));
    }

    // ---- the real probes ---------------------------------------------------------------------------------------------

    public static DiagnosticProbes RealProbes(StatusBuilder status, WindowsQueue queue, SettingsStore settings, RawPortHost raw, ILogger logger) => new(
        status.Build,
        queue.GetAsync,
        async ct => await CanConnectAsync(settings.Current.IppPort, ct),
        () => settings.Current,
        () => raw.Current,
        () => FirewallRule.Exists(FirewallRule.RawName, logger),
        DetectFonts);

    private static async Task<bool> CanConnectAsync(int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(System.Net.IPAddress.Loopback, port, ct);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            return false;
        }
    }

    private static bool Covers(string name, params int[] codePoints)
    {
        if (!SystemFonts.TryGet(name, out var family))
            return false;
        var font = family.CreateFont(12);
        return codePoints.All(cp => font.TryGetGlyphs(new CodePoint(cp), out var glyphs) && glyphs.Count > 0
                                                                                      && glyphs.All(g => g.GlyphMetrics.GlyphType != GlyphType.Fallback));
    }

    public static FontSupport DetectFonts() => new(
        Covers("MS Gothic", 0x65E5, 0x30C6, 0xFF71) && Covers("Microsoft YaHei", 0x4F60, 0x7B80) && Covers("Malgun Gothic", 0xC548),
        Covers("Tahoma", 0x0645, 0x0644) && Covers("Leelawadee UI", 0x0E01, 0x0E35, 0x0E48));
}
