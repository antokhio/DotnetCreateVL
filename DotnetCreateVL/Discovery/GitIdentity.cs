namespace DotnetCreateVL.Discovery;

internal static class GitIdentity
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <summary>The configured git user name, or null when git is missing or nothing is configured.</summary>
    public static async Task<string?> UserNameAsync(CancellationToken ct)
    {
        var result = await ProcessRunner.RunAsync("git", "config --get user.name", Timeout, ct);
        if (result is null || result.ExitCode != 0) return null;
        var name = result.StdOut.Trim();
        return name.Length == 0 ? null : name;
    }
}
