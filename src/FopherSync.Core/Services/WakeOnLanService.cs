using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FopherSync.Core.Services;

public static class WakeOnLanService
{
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(int destIp, int srcIp, byte[] macAddr, ref uint physicalAddrLen);

    /// <summary>
    /// Sends a Wake-on-LAN Magic Packet via UDP broadcast to wake a remote PC.
    /// </summary>
    public static bool SendWakeOnLan(string macAddress, int port = 9)
    {
        try
        {
            var macBytes = ParseMacAddress(macAddress);
            if (macBytes == null || macBytes.Length != 6) return false;

            // 102-byte Magic Packet: 6 bytes 0xFF followed by MAC repeated 16 times
            var packet = new byte[102];
            for (int i = 0; i < 6; i++) packet[i] = 0xFF;
            for (int i = 1; i <= 16; i++)
            {
                Buffer.BlockCopy(macBytes, 0, packet, i * 6, 6);
            }

            using var client = new UdpClient();
            client.EnableBroadcast = true;

            // Broadcast to port 9 and port 7
            client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, port));
            if (port != 7)
            {
                client.Send(packet, packet.Length, new IPEndPoint(IPAddress.Broadcast, 7));
            }

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Wake-on-LAN failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sends Wake-on-LAN and polls the server until it responds to pings or max wait time expires.
    /// </summary>
    public static async Task<bool> WakeAndAwaitAsync(
        string macAddress,
        string hostOrIp,
        int maxWaitSeconds = 60,
        Action<string>? logCallback = null)
    {
        logCallback?.Invoke($"[WoL] Sending Wake-on-LAN packet to MAC: {macAddress}...");
        var sent = SendWakeOnLan(macAddress);
        if (!sent)
        {
            logCallback?.Invoke("[WoL] Failed to construct or send Wake-on-LAN packet (check MAC address format).");
            return false;
        }

        if (string.IsNullOrWhiteSpace(hostOrIp))
        {
            logCallback?.Invoke($"[WoL] Packet broadcasted. Waiting {maxWaitSeconds}s for server initialization...");
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, maxWaitSeconds)));
            return true;
        }

        logCallback?.Invoke($"[WoL] Wake packet sent. Awaiting '{hostOrIp}' to respond (max {maxWaitSeconds}s)...");

        var sw = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(Math.Max(5, maxWaitSeconds));

        while (sw.Elapsed < timeout)
        {
            await Task.Delay(2500);

            var isOnline = await PingHostAsync(hostOrIp, 1500);
            if (isOnline)
            {
                logCallback?.Invoke($"[WoL] Server '{hostOrIp}' responded online in {sw.Elapsed.TotalSeconds:F1}s!");
                logCallback?.Invoke($"[WoL] Allowing 10s stabilization buffer for mechanical drives to spin up and SMB shares to mount...");
                await Task.Delay(10000);
                return true;
            }
        }

        logCallback?.Invoke($"[WoL] Server '{hostOrIp}' did not respond within {maxWaitSeconds}s. Proceeding with backup attempt...");
        return false;
    }

    /// <summary>
    /// Pings a host name or IP address.
    /// </summary>
    public static async Task<bool> PingHostAsync(string hostOrIp, int timeoutMs = 1500)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp)) return false;

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(hostOrIp.Trim(), timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to auto-detect the MAC address of a local hostname or IP using SendARP and ARP table.
    /// </summary>
    public static async Task<string?> AutoDetectMacAddressAsync(string hostOrIp)
    {
        if (string.IsNullOrWhiteSpace(hostOrIp)) return null;

        try
        {
            // First ping to ensure ARP table has the entry
            await PingHostAsync(hostOrIp, 1000);

            // Resolve to IPv4
            var addresses = await Dns.GetHostAddressesAsync(hostOrIp.Trim());
            var ip = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ip != null)
            {
                var macBytes = new byte[6];
                uint len = (uint)macBytes.Length;
                int ipInt = BitConverter.ToInt32(ip.GetAddressBytes(), 0);

                if (SendARP(ipInt, 0, macBytes, ref len) == 0 && len == 6)
                {
                    return string.Join(":", macBytes.Select(b => b.ToString("X2")));
                }
            }

            // Fallback: parse 'arp -a' output
            var psi = new ProcessStartInfo
            {
                FileName = "arp",
                Arguments = "-a",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();

                var targetPattern = ip?.ToString() ?? Regex.Escape(hostOrIp);
                var match = Regex.Match(output, $@"{targetPattern}\s+([0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}}[:-][0-9a-fA-F]{{2}})");
                if (match.Success)
                {
                    return match.Groups[1].Value.Replace("-", ":").ToUpperInvariant();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Auto-detect MAC error: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Sends a remote sleep command to a Windows 10 Pro server.
    /// </summary>
    public static async Task<(bool Success, string Message)> ExecuteRemoteSleepAsync(
        string hostName,
        string customCommand = "",
        string username = "",
        string password = "",
        Action<string>? logCallback = null)
    {
        logCallback?.Invoke($"[REMOTE SLEEP] Initiating remote sleep for '{hostName}'...");

        try
        {
            // 1. If user supplied a custom command (e.g. psshutdown.exe \\FMC-SERVER -d -t 0)
            if (!string.IsNullOrWhiteSpace(customCommand))
            {
                var parts = customCommand.Trim().Split(' ', 2);
                var psi = new ProcessStartInfo
                {
                    FileName = parts[0],
                    Arguments = parts.Length > 1 ? parts[1] : "",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var stdout = await proc.StandardOutput.ReadToEndAsync();
                    var stderr = await proc.StandardError.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    var msg = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                    logCallback?.Invoke($"[REMOTE SLEEP] Custom command executed: {msg}");
                    return (proc.ExitCode == 0, msg);
                }
            }

            var target = string.IsNullOrWhiteSpace(hostName) ? "FMC-SERVER" : hostName.Trim();

            // 2. Try Microsoft Sysinternals psshutdown.exe (uses standard Windows SMB/RPC - works in home Workgroups without WinRM configuration)
            try
            {
                var psshutdownPath = await GetOrDownloadPsShutdownAsync();
                if (!string.IsNullOrEmpty(psshutdownPath) && File.Exists(psshutdownPath))
                {
                    logCallback?.Invoke($"[REMOTE SLEEP] Sending sleep command via Microsoft Sysinternals psshutdown to '\\\\{target}'...");
                    var credArgs = (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                        ? $" -u \"{username}\" -p \"{password}\""
                        : "";
                    var psi = new ProcessStartInfo
                    {
                        FileName = psshutdownPath,
                        Arguments = $"\\\\{target}{credArgs} -d -t 0 -accepteula",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var stdout = await proc.StandardOutput.ReadToEndAsync();
                        var stderr = await proc.StandardError.ReadToEndAsync();
                        await proc.WaitForExitAsync();

                        if (proc.ExitCode == 0 || stdout.Contains("suspended", StringComparison.OrdinalIgnoreCase) || stdout.Contains("hibernated", StringComparison.OrdinalIgnoreCase))
                        {
                            var successMsg = $"Successfully delivered sleep signal to '\\\\{target}' via Sysinternals!";
                            logCallback?.Invoke($"[REMOTE SLEEP] {successMsg}");
                            return (true, successMsg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"psshutdown attempt error: {ex.Message}");
            }

            // 3. Fallback: Windows 10 Pro remote sleep via PowerShell Remoting (WinRM)
            var credPart = !string.IsNullOrWhiteSpace(username)
                ? $"$sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force; $cred = New-Object System.Management.Automation.PSCredential ('{username.Replace("'", "''")}', $sec); "
                : "";
            var credArg = !string.IsNullOrWhiteSpace(username) ? "-Credential $cred " : "";
            var psCmd = $"{credPart}Invoke-Command -ComputerName '{target}' {credArg}-ScriptBlock {{ (Add-Type -MemberDefinition '[DllImport(\"powrprof.dll\")] public static extern bool SetSuspendState(bool h, bool f, bool d);' -Name 'P' -Namespace 'W' -PassThru)::SetSuspendState($false, $true, $false) }}";

            var defaultPsi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCmd}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var defaultProc = Process.Start(defaultPsi);
            if (defaultProc != null)
            {
                var stdout = await defaultProc.StandardOutput.ReadToEndAsync();
                var stderr = await defaultProc.StandardError.ReadToEndAsync();
                await defaultProc.WaitForExitAsync();

                if (defaultProc.ExitCode == 0)
                {
                    logCallback?.Invoke($"[REMOTE SLEEP] Remote sleep signal delivered successfully to '{target}'.");
                    return (true, $"Sleep signal delivered to '{target}'.");
                }

                var errMsg = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
                if (errMsg.Contains("TrustedHosts", StringComparison.OrdinalIgnoreCase) || errMsg.Contains("ServerNotTrusted", StringComparison.OrdinalIgnoreCase))
                {
                    errMsg += "\n\n💡 WinRM Workgroup Tip: On a home network, run this once in PowerShell as Admin:\nSet-Item WSMan:\\localhost\\Client\\TrustedHosts -Value '" + target + "' -Force\nAnd on the server: Enable-PSRemoting -Force";
                }
                logCallback?.Invoke($"[REMOTE SLEEP] Remote command notice: {errMsg}");
                return (false, errMsg);
            }

            return (false, "Could not start sleep process.");
        }
        catch (Exception ex)
        {
            logCallback?.Invoke($"[REMOTE SLEEP] Error: {ex.Message}");
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Overload for backwards compatibility.
    /// </summary>
    public static Task<(bool Success, string Message)> ExecuteRemoteSleepAsync(
        string hostName,
        string customCommand,
        Action<string>? logCallback)
        => ExecuteRemoteSleepAsync(hostName, customCommand, "", "", logCallback);

    /// <summary>
    /// Configures the local Windows machine's WinRM TrustedHosts setting to include the remote server.
    /// Works silently if running elevated, or requests elevation if needed.
    /// </summary>
    public static async Task<(bool Success, string Message)> ConfigureTrustedHostsAsync(string hostName)
    {
        var target = string.IsNullOrWhiteSpace(hostName) ? "FMC-SERVER" : hostName.Trim();
        var script = $"$h='{target}'; Start-Service WinRM -ErrorAction SilentlyContinue; $cur = (Get-Item WSMan:\\localhost\\Client\\TrustedHosts -ErrorAction SilentlyContinue).Value; if ([string]::IsNullOrWhiteSpace($cur)) {{ Set-Item WSMan:\\localhost\\Client\\TrustedHosts -Value $h -Force }} elseif ($cur -notmatch [regex]::Escape($h)) {{ Set-Item WSMan:\\localhost\\Client\\TrustedHosts -Value \"$cur,$h\" -Force }}";

        try
        {
            // 1. First check if already trusted
            var checkPsi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"(Get-Item WSMan:\\localhost\\Client\\TrustedHosts -ErrorAction SilentlyContinue).Value\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var checkProc = Process.Start(checkPsi))
            {
                if (checkProc != null)
                {
                    var curVal = (await checkProc.StandardOutput.ReadToEndAsync()).Trim();
                    await checkProc.WaitForExitAsync();
                    if (curVal.Contains(target, StringComparison.OrdinalIgnoreCase) || curVal == "*")
                    {
                        return (true, $"'{target}' is already in TrustedHosts.");
                    }
                }
            }

            // 2. Attempt direct configuration (works if FopherSync is running elevated)
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                if (proc.ExitCode == 0) return (true, "TrustedHosts configured.");
            }

            // 3. If direct configuration didn't work (requires elevation), launch with Verb = "runas" (UAC prompt)
            var elevatedPsi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var elevProc = Process.Start(elevatedPsi);
            if (elevProc != null)
            {
                await elevProc.WaitForExitAsync();
                return (true, "TrustedHosts configured via administrator elevation.");
            }

            return (false, "Could not authorize TrustedHosts.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Performs a full handshake with the remote Windows server:
    /// 1. Verifies network connectivity via ping.
    /// 2. Configures local machine's TrustedHosts for Workgroup communication.
    /// 3. Tests authenticated WinRM connection with credentials.
    /// </summary>
    public static async Task<(bool Success, string Message)> PairAndTestServerAsync(
        string hostName,
        string username = "",
        string password = "",
        Action<string>? logCallback = null)
    {
        var target = string.IsNullOrWhiteSpace(hostName) ? "FMC-SERVER" : hostName.Trim();
        logCallback?.Invoke($"[PAIR] Testing network ping to '{target}'...");

        // 1. Ping check
        var isOnline = await PingHostAsync(target, 2000);
        if (!isOnline)
        {
            return (false, $"Could not ping '{target}'. Please ensure the server is powered on and connected to your local network.");
        }

        // 2. Configure local TrustedHosts
        logCallback?.Invoke($"[PAIR] Configuring local TrustedHosts for '{target}'...");
        var (trustOk, trustMsg) = await ConfigureTrustedHostsAsync(target);
        if (!trustOk)
        {
            logCallback?.Invoke($"[PAIR] TrustedHosts notice: {trustMsg}");
        }

        // 3. Test authenticated command
        logCallback?.Invoke($"[PAIR] Testing WinRM remoting to '{target}'...");
        var credPart = !string.IsNullOrWhiteSpace(username)
            ? $"$sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force; $cred = New-Object System.Management.Automation.PSCredential ('{username.Replace("'", "''")}', $sec); "
            : "";
        var credArg = !string.IsNullOrWhiteSpace(username) ? "-Credential $cred " : "";
        var testCmd = $"{credPart}Invoke-Command -ComputerName '{target}' {credArg}-ScriptBlock {{ $env:COMPUTERNAME }}";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{testCmd}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var stdout = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                var stderr = (await proc.StandardError.ReadToEndAsync()).Trim();
                await proc.WaitForExitAsync();

                if (proc.ExitCode == 0 && stdout.Contains(target, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, $"Verified communication with '{stdout}'. Trust and credentials confirmed!");
                }

                var errMsg = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                if (errMsg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) || errMsg.Contains("LogonFailure", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Authentication failed. Please verify the server username and password.");
                }
                if (errMsg.Contains("ServerNotTrusted", StringComparison.OrdinalIgnoreCase) || errMsg.Contains("TrustedHosts", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Local machine has not yet trusted '{target}'. Please run Register-Scheduled-Task.bat as Administrator once to authorize TrustedHosts.");
                }
                if (errMsg.Contains("Connecting to remote server", StringComparison.OrdinalIgnoreCase) || errMsg.Contains("WinRM", StringComparison.OrdinalIgnoreCase))
                {
                    return (false, $"Network ping succeeded, but PowerShell Remoting is not enabled on '{target}'.\n\nTo enable it on '{target}':\nOpen PowerShell as Admin on the server and run:\nEnable-PSRemoting -Force");
                }

                return (false, errMsg);
            }
            return (false, "Could not launch PowerShell test process.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Gets the path to Microsoft Sysinternals psshutdown.exe, downloading it from Microsoft if needed.
    /// </summary>
    public static async Task<string?> GetOrDownloadPsShutdownAsync()
    {
        var localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "psshutdown.exe");
        if (File.Exists(localPath)) return localPath;

        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            var bytes = await http.GetByteArrayAsync("https://live.sysinternals.com/psshutdown.exe");
            await File.WriteAllBytesAsync(localPath, bytes);
            return localPath;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? ParseMacAddress(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return null;

        var clean = Regex.Replace(mac, @"[^0-9A-Fa-f]", "");
        if (clean.Length != 12) return null;

        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            bytes[i] = Convert.ToByte(clean.Substring(i * 2, 2), 16);
        }
        return bytes;
    }
}
