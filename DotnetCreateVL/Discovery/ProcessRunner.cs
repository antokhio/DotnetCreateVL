using System.Diagnostics;
using System.Text;

namespace DotnetCreateVL.Discovery;

internal static class ProcessRunner
{
    /// <summary>Runs an executable and captures stdout. Returns <c>null</c> when it cannot be started.</summary>
    public static async Task<ProcessResult?> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }

        using (process)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token);
                return new ProcessResult(process.ExitCode, await stdoutTask, await stderrTask);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return null;
            }
        }
    }
}

internal sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
