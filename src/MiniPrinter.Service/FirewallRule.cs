using System.Diagnostics;

namespace MiniPrinter.Service;

/// <summary>Adds/removes the inbound rules for the IPP and RAW ports on private networks (needs elevation).</summary>
public static class FirewallRule
{
    public const string Name = "MiniPrinter IPP";
    public const string RawName = "MiniPrinter RAW";

    public static bool Add(int port, ILogger logger, string name = Name) =>
        Run($"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol=TCP localport={port} profile=private", logger);

    public static bool Remove(ILogger logger, string name = Name) =>
        Run($"advfirewall firewall delete rule name=\"{name}\"", logger);

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
