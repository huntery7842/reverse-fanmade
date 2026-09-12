using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ReVerse.Relay.Desktop;

public static partial class ZeroTierHost
{
    public static async Task<string?> DetectAddressAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        var executable = Path.Combine(Environment.SystemDirectory, "ipconfig.exe");
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        if (!process.Start())
            throw new InvalidOperationException("Could not start ipconfig.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "ipconfig failed." : error.Trim());
        return ParseAddress(output);
    }

    public static string? ParseAddress(string ipconfigOutput)
    {
        var zeroTierAdapter = false;
        foreach (var rawLine in ipconfigOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.EndsWith(':'))
            {
                zeroTierAdapter = line.Contains("ZeroTier", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!zeroTierAdapter || !line.Contains("IPv4", StringComparison.OrdinalIgnoreCase))
                continue;
            var match = Ipv4Pattern().Match(line);
            if (match.Success && IsUsableIpv4(match.Value))
                return match.Value;
        }
        return null;
    }

    public static string BuildCommand(string backendFolder, string address)
    {
        var folder = Path.GetFullPath(backendFolder.Trim().Trim('"'));
        var launcher = Path.Combine(folder, "Start-Backend.cmd");
        var ip = ValidateAddress(address);
        return $"\"{launcher}\" -PublicHost {ip} -HttpUrl {BuildBackendUrl(ip)} -Players 2";
    }

    public static string BuildBackendUrl(string address) => $"http://{ValidateAddress(address)}:6080";

    private static string ValidateAddress(string address)
    {
        var value = address.Trim();
        if (!IsUsableIpv4(value))
            throw new ArgumentException("Enter a valid ZeroTier IPv4 address.");
        return value;
    }

    private static bool IsUsableIpv4(string value)
    {
        return IPAddress.TryParse(value, out var address) &&
               address.AddressFamily == AddressFamily.InterNetwork &&
               !IPAddress.IsLoopback(address) &&
               !value.StartsWith("169.254.", StringComparison.Ordinal) &&
               value != "0.0.0.0";
    }

    [GeneratedRegex(@"(?<![\d.])(?:\d{1,3}\.){3}\d{1,3}(?![\d.])")]
    private static partial Regex Ipv4Pattern();
}

internal sealed class BackendHostController : IAsyncDisposable
{
    private readonly object gate = new();
    private Process? process;

    public event Action<bool>? RunningChanged;

    public bool IsRunning
    {
        get
        {
            lock (gate)
                return process is { HasExited: false };
        }
    }

    public void Start(string backendFolder, string command)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Starting the backend from this app is available on Windows only.");
        var folder = Path.GetFullPath(backendFolder.Trim().Trim('"'));
        var launcher = Path.Combine(folder, "Start-Backend.cmd");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException("The selected backend folder does not exist.");
        if (!File.Exists(launcher))
            throw new FileNotFoundException("Start-Backend.cmd was not found in the selected backend folder.", launcher);
        command = command.Trim();
        if (command.Length == 0)
            throw new ArgumentException("Enter a backend command.");
        lock (gate)
        {
            if (process is { HasExited: false })
                throw new InvalidOperationException("The backend is already running.");
            process?.Dispose();
            var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(commandInterpreter))
                commandInterpreter = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            process = Process.Start(new ProcessStartInfo
            {
                FileName = commandInterpreter,
                Arguments = $"/k \"{command}\"",
                WorkingDirectory = folder,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            }) ?? throw new InvalidOperationException("The backend command window could not be started.");
            process.EnableRaisingEvents = true;
            process.Exited += OnExited;
        }
        RunningChanged?.Invoke(true);
    }

    public async Task StopAsync()
    {
        Process? old;
        lock (gate)
        {
            old = process;
            process = null;
            if (old is not null)
                old.Exited -= OnExited;
        }
        if (old is null)
            return;
        try
        {
            if (!old.HasExited)
            {
                old.Kill(true);
                await old.WaitForExitAsync();
            }
        }
        finally
        {
            old.Dispose();
            RunningChanged?.Invoke(false);
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        lock (gate)
        {
            if (!ReferenceEquals(process, sender))
                return;
            process?.Dispose();
            process = null;
        }
        RunningChanged?.Invoke(false);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
