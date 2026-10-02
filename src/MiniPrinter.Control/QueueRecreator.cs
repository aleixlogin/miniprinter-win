using System.Diagnostics;
using System.Text;

namespace MiniPrinter.Control;

public sealed record PowerShellResult(int ExitCode, string Output, string Error);

/// <summary>Runs a PowerShell script with the given environment variables (a fake in the tests).</summary>
public interface IPowerShellRunner
{
    Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, string> environment, CancellationToken ct);
}

/// <summary>
/// Real runner: <c>powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand</c>. The script is a
/// constant; everything the user can influence (queue names, URL) travels in environment variables (design.md D4).
/// </summary>
public sealed class SystemPowerShellRunner : IPowerShellRunner
{
    /// <summary>
    /// The script inside a try/catch that reports any error on stdout (stderr would come back as CLIXML) with a plain
    /// exit code, and without progress output.
    /// </summary>
    internal static string Wrap(string script) =>
        "$ProgressPreference='SilentlyContinue';$OutputEncoding=[Text.Encoding]::UTF8;[Console]::OutputEncoding=[Text.Encoding]::UTF8;" +
        "try {\n" + script + "\n} catch { [Console]::Out.WriteLine('ERROR: ' + $_.Exception.Message); exit 1 }";

    public async Task<PowerShellResult> RunAsync(string script, IReadOnlyDictionary<string, string> environment, CancellationToken ct)
    {
        var info = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-InputFormat", "None", "-OutputFormat", "Text", "-EncodedCommand",
                     Convert.ToBase64String(Encoding.Unicode.GetBytes(Wrap(script))) })
            info.ArgumentList.Add(arg);
        foreach (var (key, value) in environment)
            info.Environment[key] = value;

        using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo ejecutar PowerShell.");
        var output = process.StandardOutput.ReadToEndAsync(ct);
        var error = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return new PowerShellResult(process.ExitCode, (await output).Trim(), (await error).Trim());
    }
}

/// <summary>The PowerShell steps of the recreation. They are constants: the user's text only travels in the environment.</summary>
public static class QueueScripts
{
    public const string Pending = "# step:pending\n$n = @(Get-PrintJob -PrinterName $env:MP_OLD -ErrorAction SilentlyContinue).Count; Write-Output $n; exit 0";
    public const string NameFree = "# step:name-free\nif ($env:MP_QUEUE -ne $env:MP_OLD -and (Get-Printer -Name $env:MP_QUEUE -ErrorAction SilentlyContinue)) { exit 3 }; exit 0";
    public const string Cleanup = "# step:cleanup\n$t = Get-Printer -Name $env:MP_TEMP -ErrorAction SilentlyContinue; if ($t) { Remove-Printer -Name $env:MP_TEMP -ErrorAction Stop }; exit 0";
    public const string Create = "# step:create\n$ErrorActionPreference = 'Stop'; Add-Printer -Name $env:MP_TEMP -IppURL $env:MP_URL";
    public const string Verify = "# step:verify\n$ErrorActionPreference = 'Stop'; Get-Printer -Name $env:MP_TEMP | Out-Null";
    public const string RemoveOld =
        "# step:remove-old\n$ErrorActionPreference = 'Stop'; $p = Get-Printer -Name $env:MP_OLD -ErrorAction SilentlyContinue; " +
        "if ($p) { $port = $p.PortName; Remove-Printer -Name $env:MP_OLD; " +
        "if ($port -and -not (Get-Printer | Where-Object { $_.PortName -eq $port })) { Remove-PrinterPort -Name $port -ErrorAction SilentlyContinue } }; exit 0";
    public const string Rename = "# step:rename\n$ErrorActionPreference = 'Stop'; Rename-Printer -Name $env:MP_TEMP -NewName $env:MP_QUEUE";
    public const string Exists = "# step:exists\nif (Get-Printer -Name $env:MP_QUEUE -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }";

    /// <summary>
    /// What Windows itself offers for the queue: the PageMediaSize options of its print capabilities, one "WxH" (mm) per line.
    /// This is what the Printing Preferences dialog shows, and it can lag one recreation behind the printer's announcement.
    /// </summary>
    public const string Sizes =
        "# step:sizes\n$ErrorActionPreference = 'Stop'; Add-Type -AssemblyName System.Printing; " +
        "$q = New-Object System.Printing.PrintQueue((New-Object System.Printing.LocalPrintServer), $env:MP_QUEUE); " +
        "$xml = [xml](New-Object System.IO.StreamReader($q.GetPrintCapabilitiesAsXml())).ReadToEnd(); " +
        "$ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable); $ns.AddNamespace('psf', 'http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework'); " +
        "$f = $xml.SelectSingleNode(\"//psf:Feature[@name='psk:PageMediaSize']\", $ns); if (-not $f) { exit 4 }; " +
        "foreach ($o in $f.SelectNodes('psf:Option', $ns)) { " +
        "$w = $o.SelectSingleNode(\"psf:ScoredProperty[@name='psk:MediaSizeWidth']/psf:Value\", $ns).InnerText; " +
        "$h = $o.SelectSingleNode(\"psf:ScoredProperty[@name='psk:MediaSizeHeight']/psf:Value\", $ns).InnerText; " +
        "Write-Output ('{0}x{1}' -f [math]::Round([double]$w / 1000), [math]::Round([double]$h / 1000)) }; exit 0";

    public const int MaxNameLength = 60;

    /// <summary>Why a printer name cannot be used for a Windows queue, or null.</summary>
    public static string? CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "El nombre de la impresora está vacío.";
        if (name.Length > MaxNameLength)
            return $"El nombre de la impresora supera los {MaxNameLength} caracteres.";
        if (name.Any(c => char.IsControl(c) || c is '"' or '\\' or '/'))
            return "El nombre de la impresora no puede llevar comillas dobles, barras ni caracteres de control.";
        return null;
    }
}

/// <summary>What the recreation has to produce.</summary>
/// <param name="Name">Final queue name (the printer's name).</param>
/// <param name="Old">Queue to replace (the previous name after a rename; otherwise the same as <paramref name="Name"/>).</param>
/// <param name="Url">The IPP URL of the service.</param>
/// <param name="ExpectedSizes">The sizes Windows must offer, as "WxH" in millimetres.</param>
/// <param name="MaxPasses">How many times the recreation is repeated while Windows still offers a stale list.</param>
public sealed record QueueRequest(string Name, string Old, string Url, IReadOnlyList<string> ExpectedSizes, int MaxPasses = 3);

public sealed record QueueOutcome(bool Succeeded, string Message, string Step, bool NeedsElevation = false);

/// <summary>The service side of a pass: take a new printer identity and wait until the listener announces it.</summary>
public interface IQueueHost
{
    Task PrepareAsync(CancellationToken ct);
}

/// <summary>
/// Recreates the Windows print queue (design.md D4). A new queue is created next to the old one (under a temporary name), the
/// old one is removed and the new one renamed, so a failure never leaves the user without a printer. Windows refuses a second
/// queue for a printer with the same UUID, so every pass starts by taking a new identity from the host. The driver of Windows
/// builds its list of sizes from the data of the previous creation, so after the pass the list Windows offers is read back and,
/// while it differs from the announced one, the recreation is repeated (up to <see cref="QueueRequest.MaxPasses"/> times).
/// </summary>
public sealed class QueueRecreator(IPowerShellRunner runner)
{
    /// <summary>Raised with (state, message, step) as the work advances.</summary>
    public event Action<string, string, string>? Progress;

    public async Task<QueueOutcome> RunAsync(QueueRequest request, IQueueHost host, CancellationToken ct)
    {
        var temp = request.Name + " (nueva)";
        var sizes = string.Join(",", request.ExpectedSizes);
        var old = request.Old;
        QueueOutcome? lastNote = null;

        for (var pass = 1; pass <= Math.Max(1, request.MaxPasses); pass++)
        {
            var env = Env(request.Name, old, temp, request.Url, sizes);
            var suffix = pass == 1 ? "" : $" (repaso {pass}/{request.MaxPasses})";

            Report($"Renovando la identidad de la impresora…{suffix}", "uuid");
            await host.PrepareAsync(ct);

            if (pass == 1)
            {
                Report("Comprobando los trabajos pendientes…", "pending");
                var pending = await runner.RunAsync(QueueScripts.Pending, env, ct);
                if (pending.ExitCode == 0 && int.TryParse(pending.Output.Split('\n').LastOrDefault()?.Trim(), out var jobs) && jobs > 0)
                    return Fail("pending", $"Hay {jobs} trabajo{(jobs == 1 ? "" : "s")} pendiente{(jobs == 1 ? "" : "s")} en la cola de Windows. Espera a que terminen o cancélalos y vuelve a intentarlo.");

                if (request.Name != old)
                {
                    Report("Comprobando el nombre nuevo…", "name-free");
                    if ((await runner.RunAsync(QueueScripts.NameFree, env, ct)).ExitCode == 3)
                        return Fail("name-free", $"Ya existe en Windows otra impresora llamada «{request.Name}».");
                }
            }

            Report($"Limpiando restos de un intento anterior…{suffix}", "cleanup");
            var cleanup = await runner.RunAsync(QueueScripts.Cleanup, env, ct);
            if (cleanup.ExitCode != 0)
                return Fail("cleanup", Describe("No se pudo quitar la cola temporal anterior", cleanup), cleanup);

            Report($"Creando la impresora con los tamaños nuevos…{suffix}", "create");
            var create = await runner.RunAsync(QueueScripts.Create, env, ct);
            if (create.ExitCode != 0)
                return Fail("create", Describe("No se pudo crear la impresora nueva. La anterior sigue funcionando", create), create);

            Report("Comprobando la impresora nueva…", "verify");
            var verify = await runner.RunAsync(QueueScripts.Verify, env, ct);
            if (verify.ExitCode != 0)
                return Fail("verify", Describe("La impresora nueva no aparece en Windows. La anterior sigue funcionando", verify), verify);

            Report($"Quitando la impresora antigua…{suffix}", "remove-old");
            var remove = await runner.RunAsync(QueueScripts.RemoveOld, env, ct);
            if (remove.ExitCode != 0)
                return Fail("remove-old", Describe($"No se pudo quitar la impresora antigua «{old}». La nueva quedó como «{temp}»", remove), remove);

            Report("Dejando el nombre de siempre…", "rename");
            var rename = await runner.RunAsync(QueueScripts.Rename, env, ct);
            if (rename.ExitCode != 0)
                return Fail("rename", Describe($"No se pudo renombrar la impresora nueva. Quedó como «{temp}»", rename), rename);

            // From here on the queue exists with its final name: the next pass replaces it.
            old = request.Name;

            Report("Comprobando los tamaños que ve Windows…", "sizes");
            var offered = await runner.RunAsync(QueueScripts.Sizes, env, ct);
            if (offered.ExitCode != 0)
                return new QueueOutcome(true, "La impresora de Windows se ha actualizado (no se pudo comprobar la lista de tamaños que ve Windows).", "done");

            var missing = Diff(request.ExpectedSizes, Parse(offered.Output));
            if (missing.Count == 0)
                return new QueueOutcome(true, pass == 1 ? "La impresora de Windows se ha actualizado." : $"La impresora de Windows se ha actualizado (hizo falta repasarla {pass - 1} vez{(pass == 2 ? "" : "es")} para que Windows viera los tamaños nuevos).", "done");

            lastNote = new QueueOutcome(true,
                "La impresora de Windows se ha recreado, pero Windows aún no muestra todos los tamaños" +
                $" ({string.Join(", ", missing)}); puede tardar unos minutos en actualizarse.", "done");
        }
        return lastNote!;
    }

    /// <summary>The sizes that are wrong (missing or not expected) between the announced list and the one Windows offers.</summary>
    internal static List<string> Diff(IReadOnlyList<string> expected, IReadOnlySet<string> offered)
    {
        var expectedSet = expected.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. expectedSet.Where(e => !offered.Contains(e)).Select(e => e + " falta"), .. offered.Where(o => !expectedSet.Contains(o)).Select(o => o + " sobra")];
    }

    internal static HashSet<string> Parse(string output) =>
        new(output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> Env(string name, string old, string temp, string url, string sizes) => new()
    {
        ["MP_QUEUE"] = name,
        ["MP_OLD"] = old,
        ["MP_TEMP"] = temp,
        ["MP_URL"] = url,
        ["MP_SIZES"] = sizes,
    };

    private void Report(string message, string step) => Progress?.Invoke("Running", message, step);

    private QueueOutcome Fail(string step, string message, PowerShellResult? result = null) =>
        new(false, message, step, NeedsElevation: result is not null && IsAccessDenied(result));

    /// <summary>True when PowerShell failed because the process has no administrator rights.</summary>
    public static bool IsAccessDenied(PowerShellResult result)
    {
        var text = result.Error + " " + result.Output;
        return text.Contains("denied", StringComparison.OrdinalIgnoreCase) || text.Contains("denegad", StringComparison.OrdinalIgnoreCase)
               || text.Contains("0x80070005", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A message with the reason; an access-denied error is explained as a permissions problem.</summary>
    private static string Describe(string what, PowerShellResult result)
    {
        // PowerShell may wrap stderr as CLIXML; the wrapper writes the real message to stdout as "ERROR: …".
        var text = string.IsNullOrWhiteSpace(result.Error) || result.Error.StartsWith("#< CLIXML", StringComparison.Ordinal) ? result.Output : result.Error;
        if (text.StartsWith("ERROR: ", StringComparison.Ordinal))
            text = text[7..];
        if (IsAccessDenied(result))
            return $"{what}: no hay permisos para gestionar impresoras (hace falta ejecutar con privilegios de administrador).";
        text = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        return text.Length == 0 ? $"{what}." : $"{what}: {text}";
    }
}
