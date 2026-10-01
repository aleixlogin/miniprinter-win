using System.Diagnostics;

namespace MiniPrinter.Service;

/// <summary>Adds/removes the inbound rule for the IPP port on private networks (needs elevation).</summary>
public static class FirewallRule
{
    public const string Name = "MiniPrinter IPP";

    public static bool Add(int port, ILogger logger) =>
        Run($"advfirewall firewall add rule name=\"{Name}\" dir=in action=allow protocol=TCP localport={port} profile=private", logger);

    public static bool Remove(ILogger logger) =>
        Run($"advfirewall firewall delete rule name=\"{Name}\"", logger);

    private static bool Run(string arguments, ILogger logger)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("netsh", arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            process.WaitForExit(10_000);
            if (process.ExitCode != 0)
            {
                logger.LogWarning("netsh {Arguments} failed ({Code}): {Output}", arguments, process.ExitCode,
                    process.StandardOutput.ReadToEnd().Trim());
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not run netsh");
            return false;
        }
    }
}
