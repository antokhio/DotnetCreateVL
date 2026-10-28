using Spectre.Console;

namespace DotnetCreateVL.Discovery;

internal sealed record DiscoveredEnvironment(
    VvvvDiscoveryResult Vvvv,
    DotnetDiscoveryResult Dotnet,
    JustDiscoveryResult Just,
    string WorkingDirectory);

/// <summary>Phase 0: runs all probes once and prints the result table. No prompts.</summary>
internal static class DiscoveryRunner
{
    public static async Task<DiscoveredEnvironment> RunAsync(IAnsiConsole console, bool verbose, CancellationToken ct)
    {
        DiscoveredEnvironment env;
        if (console.Profile.Capabilities.Interactive)
        {
            env = await console.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Discovering toolchain…", _ => DiscoverAsync(ct));
        }
        else
        {
            env = await DiscoverAsync(ct);
        }

        Print(console, env, verbose);
        return env;
    }

    private static async Task<DiscoveredEnvironment> DiscoverAsync(CancellationToken ct)
    {
        var dotnetTask = ToolchainDiscovery.DiscoverDotnetAsync(ct);
        var justTask = ToolchainDiscovery.DiscoverJustAsync(ct);
        var vvvv = VvvvDiscovery.Discover();
        return new DiscoveredEnvironment(vvvv, await dotnetTask, await justTask, Environment.CurrentDirectory);
    }

    private static void Print(IAnsiConsole console, DiscoveredEnvironment env, bool verbose)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Discovered toolchain[/]");
        table.AddColumn("Component");
        table.AddColumn("Found");
        table.AddColumn("Details");

        if (env.Vvvv.EnvironmentHint is { } hint)
            table.AddRow("vvvv (VVVV_DIR)", Yes(), Markup.Escape(Describe(hint)));

        if (env.Vvvv.Installations.Count == 0 && env.Vvvv.EnvironmentHint is null)
            table.AddRow("vvvv", No(), "[grey]no vvvv_gamma_* installation found[/]");
        foreach (var install in env.Vvvv.Installations)
            table.AddRow($"vvvv ({Markup.Escape(install.Origin)})", Yes(), Markup.Escape(Describe(install)));

        if (!env.Dotnet.Found)
            table.AddRow(".NET SDK", No(), "[grey]dotnet not found on PATH[/]");
        else
        {
            var versions = env.Dotnet.Sdks.Select(s => s.Version).ToList();
            var detail = versions.Count == 0 ? "no SDKs listed" : string.Join(", ", versions);
            table.AddRow(".NET SDK", Yes(), Markup.Escape($"{env.Dotnet.Architecture ?? "arch ?"} · {detail}"));
        }

        table.AddRow("Just", env.Just.Found ? Yes() : No(),
            env.Just.Found ? Markup.Escape(env.Just.Version ?? "version ?") : "[grey]not on PATH; recipe validation unavailable[/]");

        console.Write(table);

        if (verbose)
        {
            foreach (var r in env.Vvvv.Rejected)
                console.MarkupLine($"[grey]discovery: {Markup.Escape(r)}[/]");
            foreach (var s in env.Dotnet.Sdks)
                console.MarkupLine($"[grey]sdk: {Markup.Escape(s.Version)} @ {Markup.Escape(s.Directory)}[/]");
        }
    }

    private static string Describe(Model.VvvvInstallation i)
    {
        var flags = new List<string>();
        if (!i.HasEditor) flags.Add("vvvv.exe missing");
        if (!i.HasCompiler) flags.Add("vvvvc.exe missing");
        var suffix = flags.Count == 0 ? "" : $" [{string.Join(", ", flags)}]";
        return $"{i.DisplayVersion} — {i.Path}{suffix}";
    }

    private static string Yes() => "[green]yes[/]";
    private static string No() => "[yellow]no[/]";
}
