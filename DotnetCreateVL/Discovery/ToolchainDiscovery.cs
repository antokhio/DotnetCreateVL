using System.Text.RegularExpressions;

namespace DotnetCreateVL.Discovery;

internal sealed record DotnetSdk(string Version, string Directory);

internal sealed record DotnetDiscoveryResult(
    bool Found,
    string? Architecture,
    IReadOnlyList<DotnetSdk> Sdks);

internal sealed record JustDiscoveryResult(bool Found, string? Version);

internal static partial class ToolchainDiscovery
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [GeneratedRegex(@"^\s*Architecture:\s*(\S+)", RegexOptions.Multiline)]
    private static partial Regex ArchitectureRegex();

    [GeneratedRegex(@"^\s*(\d+\.\d+\.\d+\S*)\s+\[(.+)\]\s*$", RegexOptions.Multiline)]
    private static partial Regex SdkLineRegex();

    public static Task<DotnetDiscoveryResult> DiscoverDotnetAsync(CancellationToken ct) => DiscoverDotnetAsync("dotnet", ct);

    public static async Task<DotnetDiscoveryResult> DiscoverDotnetAsync(string executable, CancellationToken ct)
    {
        var info = await ProcessRunner.RunAsync(executable, "--info", Timeout, ct);
        if (info is null || info.ExitCode != 0)
            return new DotnetDiscoveryResult(false, null, []);

        var arch = ArchitectureRegex().Match(info.StdOut) is { Success: true } m ? m.Groups[1].Value : null;

        // "dotnet --info" lists SDKs in a ".NET SDKs installed:" block; parse that block only.
        var sdks = new List<DotnetSdk>();
        var marker = info.StdOut.IndexOf("SDKs installed:", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var block = info.StdOut[marker..];
            var end = block.IndexOf("\n\n", StringComparison.Ordinal);
            if (end < 0) end = block.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (end > 0) block = block[..end];

            foreach (Match sdk in SdkLineRegex().Matches(block))
                sdks.Add(new DotnetSdk(sdk.Groups[1].Value, sdk.Groups[2].Value));
        }

        return new DotnetDiscoveryResult(true, arch, sdks);
    }

    public static async Task<JustDiscoveryResult> DiscoverJustAsync(CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("just", "--version", Timeout, ct);
        if (result is null || result.ExitCode != 0)
            return new JustDiscoveryResult(false, null);

        var text = result.StdOut.Trim();
        var version = text.StartsWith("just ", StringComparison.OrdinalIgnoreCase) ? text[5..].Trim() : text;
        return new JustDiscoveryResult(true, string.IsNullOrEmpty(version) ? null : version);
    }
}
