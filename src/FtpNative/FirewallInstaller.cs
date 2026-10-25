using System.Diagnostics;

namespace FtpNative;

internal static class FirewallInstaller
{
    private const string ControlRule = "FtpNative FTP Control";
    private const string DataRule = "FtpNative FTP Data";
    private const string WebRule = "FtpNative Web";

    public static void InstallRules(ServerConfig config)
    {
        RemoveRules();
        RunNetsh(
            "advfirewall", "firewall", "add", "rule",
            $"name={ControlRule}", "dir=in", "action=allow", "protocol=TCP",
            $"localport={config.Port}", "profile=any", "enable=yes");

        RunNetsh(
            "advfirewall", "firewall", "add", "rule",
            $"name={DataRule}", "dir=in", "action=allow", "protocol=TCP",
            $"localport={config.PasvMinPort}-{config.PasvMaxPort}", "profile=any", "enable=yes");

        if (config.WebPort > 0)
        {
            RunNetsh(
                "advfirewall", "firewall", "add", "rule",
                $"name={WebRule}", "dir=in", "action=allow", "protocol=TCP",
                $"localport={config.WebPort}", "profile=any", "enable=yes");
        }
    }

    public static void RemoveRules()
    {
        RunNetsh("advfirewall", "firewall", "delete", "rule", $"name={ControlRule}");
        RunNetsh("advfirewall", "firewall", "delete", "rule", $"name={DataRule}");
        RunNetsh("advfirewall", "firewall", "delete", "rule", $"name={WebRule}");
    }

    public static bool HasRule(string name)
    {
        var output = RunNetshCapture("advfirewall", "firewall", "show", "rule", $"name={name}");
        return output.Contains("FtpNative", StringComparison.OrdinalIgnoreCase);
    }

    private static void RunNetsh(params string[] arguments)
    {
        RunNetshCapture(arguments);
    }

    private static string RunNetshCapture(params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("netsh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            if (process is null)
            {
                return "";
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000);
            return output;
        }
        catch
        {
            return "";
        }
    }
}
