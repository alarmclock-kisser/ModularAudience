using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ModularAudience.CoOp.Windows;

public sealed record CoOpFirewallResult(bool Success, bool ElevationRequested, string Message);

public sealed record CoOpFirewallRule(string Name, string Protocol, int StartPort, int EndPort)
{
    public string LocalPorts => this.StartPort == this.EndPort
        ? this.StartPort.ToString()
        : $"{this.StartPort}-{this.EndPort}";
}

/// <summary>
/// Adds the inbound firewall rules required for co-op hosting. Windows only
/// prompts for UAC elevation when the current process cannot add the rule.
/// </summary>
public static class WindowsFirewall
{
    public const string TcpRuleName = "ModularAudience Co-Op (TCP)";
    public const string UdpRuleName = "ModularAudience Co-Op Discovery (UDP)";

    public static CoOpFirewallRule CreateTcpRule(
        int startPort = CoOpDefaults.DefaultPort,
        int count = CoOpDefaults.PortSearchCount)
    {
        return new CoOpFirewallRule(TcpRuleName, "TCP", startPort, startPort + Math.Max(1, count) - 1);
    }

    public static CoOpFirewallRule CreateUdpRule(
        int startPort = CoOpDefaults.DefaultDiscoveryPort,
        int count = CoOpDefaults.DiscoveryPortCount)
    {
        return new CoOpFirewallRule(UdpRuleName, "UDP", startPort, startPort + Math.Max(1, count) - 1);
    }

    public static string BuildShowRuleArguments(string ruleName)
    {
        return $"advfirewall firewall show rule name=\"{EscapeRuleName(ruleName)}\"";
    }

    public static string BuildAddRuleArguments(CoOpFirewallRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return "advfirewall firewall add rule " +
            $"name=\"{EscapeRuleName(rule.Name)}\" " +
            "dir=in action=allow " +
            $"protocol={rule.Protocol} " +
            $"localport={rule.LocalPorts} " +
            "profile=any enable=yes";
    }

    public static string BuildDeleteRuleArguments(string ruleName)
    {
        return $"advfirewall firewall delete rule name=\"{EscapeRuleName(ruleName)}\"";
    }

    public static async Task<CoOpFirewallResult> EnsureCoOpRulesAsync(
        int tcpStartPort = CoOpDefaults.DefaultPort,
        int tcpPortCount = CoOpDefaults.PortSearchCount,
        int udpStartPort = CoOpDefaults.DefaultDiscoveryPort,
        int udpPortCount = CoOpDefaults.DiscoveryPortCount,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new CoOpFirewallResult(true, false, "Firewall rules are not required on this operating system.");
        }

        CoOpFirewallRule[] rules =
        [
            CreateTcpRule(tcpStartPort, tcpPortCount),
            CreateUdpRule(udpStartPort, udpPortCount)
        ];

        var missing = new List<CoOpFirewallRule>();
        foreach (CoOpFirewallRule rule in rules)
        {
            if (!await RuleExistsAsync(rule.Name, cancellationToken).ConfigureAwait(false))
            {
                missing.Add(rule);
            }
        }

        if (missing.Count == 0)
        {
            return new CoOpFirewallResult(true, false, "The co-op firewall rules already exist.");
        }

        var failed = new List<CoOpFirewallRule>();
        foreach (CoOpFirewallRule rule in missing)
        {
            if (!await TryAddRuleAsync(rule, elevated: false, cancellationToken).ConfigureAwait(false))
            {
                failed.Add(rule);
            }
        }

        if (failed.Count == 0)
        {
            return new CoOpFirewallResult(true, false, "The co-op firewall rules were added.");
        }

        bool elevated = await TryAddRulesElevatedAsync(failed, cancellationToken).ConfigureAwait(false);
        if (!elevated)
        {
            return new CoOpFirewallResult(
                false,
                true,
                "The firewall rules could not be added. Hosting will continue, but Windows may block incoming connections. " +
                "Run ModularAudience as administrator or allow the ports manually.");
        }

        foreach (CoOpFirewallRule rule in failed)
        {
            if (!await RuleExistsAsync(rule.Name, cancellationToken).ConfigureAwait(false))
            {
                return new CoOpFirewallResult(
                    false,
                    true,
                    $"The firewall rule '{rule.Name}' is still missing after elevation.");
            }
        }

        return new CoOpFirewallResult(true, true, "The co-op firewall rules were added with administrator rights.");
    }

    private static async Task<bool> RuleExistsAsync(string ruleName, CancellationToken cancellationToken)
    {
        (int exitCode, _, _) = await RunNetshAsync(
            BuildShowRuleArguments(ruleName),
            elevated: false,
            cancellationToken).ConfigureAwait(false);
        return exitCode == 0;
    }

    private static async Task<bool> TryAddRuleAsync(
        CoOpFirewallRule rule,
        bool elevated,
        CancellationToken cancellationToken)
    {
        if (elevated)
        {
            return await TryAddRulesElevatedAsync([rule], cancellationToken).ConfigureAwait(false);
        }

        (int exitCode, _, _) = await RunNetshAsync(
            BuildAddRuleArguments(rule),
            elevated: false,
            cancellationToken).ConfigureAwait(false);
        return exitCode == 0;
    }

    private static async Task<bool> TryAddRulesElevatedAsync(
        IReadOnlyList<CoOpFirewallRule> rules,
        CancellationToken cancellationToken)
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            $"ModularAudience-CoOp-Firewall-{Guid.NewGuid():N}.cmd");

        var script = new StringBuilder();
        script.AppendLine("@echo off");
        foreach (CoOpFirewallRule rule in rules)
        {
            script.AppendLine($"\"{GetNetshPath()}\" {BuildDeleteRuleArguments(rule.Name)} >nul 2>&1");
            script.AppendLine($"\"{GetNetshPath()}\" {BuildAddRuleArguments(rule)}");
        }

        try
        {
            await File.WriteAllTextAsync(scriptPath, script.ToString(), cancellationToken).ConfigureAwait(false);

            var startInfo = new ProcessStartInfo(scriptPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using Process? process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The user rejected the UAC prompt.
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(scriptPath); } catch { }
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunNetshAsync(
        string arguments,
        bool elevated,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(GetNetshPath(), arguments)
        {
            UseShellExecute = elevated,
            CreateNoWindow = !elevated,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !elevated,
            RedirectStandardError = !elevated
        };

        if (elevated)
        {
            startInfo.Verb = "runas";
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("netsh could not be started.");
        Task<string> outputTask = elevated ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = elevated ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }

    private static string GetNetshPath()
    {
        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(systemDirectory, "netsh.exe");
    }

    private static string EscapeRuleName(string ruleName)
    {
        return ruleName.Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
