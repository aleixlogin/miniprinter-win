using System.Text.Json;
using MiniPrinter.Control;

namespace MiniPrinter.Cli;

/// <summary>
/// <c>miniprinter queue-recreate</c>: recreates the Windows print queue from an elevated process. The tray launches it with a UAC
/// prompt when the service has no rights to manage printers. It asks the service for a new printer identity before each queue it
/// creates (through the control API) and does the PowerShell work itself, with the administrator rights it was started with.
/// </summary>
internal static class QueueRecreateCommand
{
    public static async Task<int> RunAsync(CliArgs cli, CancellationToken ct)
    {
        var old = cli.Value("--old");
        var resultPath = cli.Value("--result");
        QueueOutcome outcome;
        try
        {
            using var client = ControlClient.FromTokenFile(int.TryParse(cli.Value("--control-port"), out var port) ? port : ControlDefaults.Port);
            var settings = await client.GetSettingsAsync(ct);
            if (QueueScripts.CheckName(settings.PrinterName) is { } problem)
                throw new CliException(problem);
            if (old is not null && QueueScripts.CheckName(old) is { } oldProblem)
                throw new CliException(oldProblem);

            var request = new QueueRequest(settings.PrinterName, string.IsNullOrWhiteSpace(old) ? settings.PrinterName : old.Trim(),
                $"http://127.0.0.1:{settings.IppPort}/ipp/print",
                [.. PaperCatalog.Effective(settings).Select(p => $"{p.WidthMm}x{p.LengthMm}")]);

            var recreator = new QueueRecreator(new SystemPowerShellRunner());
            recreator.Progress += (_, message, _) => Console.WriteLine(message);
            outcome = await recreator.RunAsync(request, new RemoteHost(client), ct);
        }
        catch (Exception ex) when (ex is CliException or ControlApiException or HttpRequestException or IOException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception or TimeoutException)
        {
            outcome = new QueueOutcome(false, ex.Message, "error");
        }

        Console.WriteLine(outcome.Message);
        if (resultPath is not null)
        {
            try
            {
                await File.WriteAllTextAsync(resultPath, JsonSerializer.Serialize(outcome, ControlDefaults.Json), ct);
            }
            catch (IOException)
            {
                // The exit code still tells the tray whether it worked.
            }
        }
        return outcome.Succeeded ? 0 : 1;
    }

    /// <summary>The service side of a pass, reached through the control API.</summary>
    private sealed class RemoteHost(ControlClient client) : IQueueHost
    {
        public Task PrepareAsync(CancellationToken ct) => client.PrepareWindowsQueueAsync(ct);
    }
}
