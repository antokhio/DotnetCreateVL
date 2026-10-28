using DotnetCreateVL.Discovery;
using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>
/// Phase 5: settings shared by every C# project, shown as one table and confirmed once.
/// Values come from verified install metadata (runtimeconfig TFM, VL.Core.nuspec version) or flags;
/// only values with no trustworthy source are asked before the table.
/// </summary>
internal sealed class CSharpSettingsPhase(
    IAnsiConsole console,
    IPrompter? prompter,
    DiscoveredEnvironment env,
    BasicsModel basics,
    Func<string, CancellationToken, Task<DotnetDiscoveryResult>>? dotnetProbe = null)
{
    private enum Setting { Sdk, Tfm, VlVersion, Configuration, Solution, Versions, Sources }

    private const string RequiredArchitecture = "x64";

    private readonly Func<string, CancellationToken, Task<DotnetDiscoveryResult>> probe = dotnetProbe ?? ToolchainDiscovery.DiscoverDotnetAsync;
    private VvvvHostMetadata host = new(null, null);

    public List<string> Warnings { get; } = [];

    public IReadOnlyList<string> ApplyFlags(CSharpModel m, string? dotnet, string? tfm, string? vlVersion, string? configuration,
        string? solution, string? solutionFormat, string? versions, IEnumerable<string>? nugetSources)
    {
        var errors = new List<string>();

        if (dotnet is not null)
        {
            if (Validators.DotnetExecutable(dotnet) is { } e) errors.Add($"--dotnet '{dotnet}': {e}");
            else m.Dotnet.Set(new DotnetSelection(Path.GetFullPath(dotnet.Trim()), null, null), SlotSource.Flag);
        }
        if (tfm is not null)
        {
            if (Validators.Tfm(tfm) is { } e) errors.Add($"--tfm '{tfm}': {e}");
            else m.Tfm.Set(tfm.Trim().ToLowerInvariant(), SlotSource.Flag);
        }
        if (vlVersion is not null)
        {
            if (Validators.PackageVersion(vlVersion) is { } e) errors.Add($"--vl-version '{vlVersion}': {e}");
            else m.VlVersion.Set(vlVersion.Trim(), SlotSource.Flag);
        }
        if (configuration is not null)
        {
            if (Validators.Configuration(configuration) is { } e) errors.Add($"--configuration '{configuration}': {e}");
            else m.Configuration.Set(NormalizeConfiguration(configuration), SlotSource.Flag);
        }
        if (solutionFormat is not null && Validators.SolutionFormat(solutionFormat) is { } fe)
            errors.Add($"--solution-format '{solutionFormat}': {fe}");
        if (solution is not null)
        {
            if (Validators.SolutionName(solution) is { } e) errors.Add($"--solution '{solution}': {e}");
            else if (solution.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) m.Solution.Set(SolutionChoice.None, SlotSource.Flag);
            else m.Solution.Set(new SolutionChoice(solution.Trim(), solutionFormat?.Trim().ToLowerInvariant() ?? "?"), SlotSource.Flag);
        }
        else if (solutionFormat is not null && Validators.SolutionFormat(solutionFormat) is null)
        {
            m.Solution.Set(new SolutionChoice(basics.Name.Require(), solutionFormat.Trim().ToLowerInvariant()), SlotSource.Flag);
        }
        if (versions is not null)
        {
            if (Validators.ParseVersions(versions) is { } v) m.Versions.Set(v, SlotSource.Flag);
            else errors.Add($"--versions '{versions}': expected shared, inline or keep.");
        }
        if (nugetSources is not null)
        {
            if (Validators.ParseNuGetSources(nugetSources, [NuGetSource.VvvvPublicFeed], out var list) is { } e) errors.Add($"--nuget-source {e}");
            else m.Sources.Set(list, SlotSource.Flag);
        }

        return errors;
    }

    public async Task<IReadOnlyList<string>> RunAsync(CSharpModel m, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[5/9]] C# settings[/]").LeftJustified());
        host = VvvvHostMetadata.Read(basics.Vvvv.Require());

        // Verified defaults first, so the pre-prompts below only cover what nothing could tell us.
        if (!m.Tfm.IsResolved && host.Tfm is not null)
            m.Tfm.Set(host.Tfm, SlotSource.Default);
        if (!m.VlVersion.IsResolved && host.VlVersion is not null)
            m.VlVersion.Set(host.VlVersion, SlotSource.Default);
        if (!m.Dotnet.IsResolved && PickSdk(env.Dotnet) is { } sdk)
            m.Dotnet.Set(sdk, SlotSource.Default);

        if (prompter is not null)
        {
            if (!m.Tfm.IsResolved)
                m.Tfm.Set((await prompter.TextAsync("Target framework (e.g. net8.0):", null, Validators.Tfm, ct)).Trim().ToLowerInvariant(), SlotSource.Prompt);
            if (!m.VlVersion.IsResolved)
                m.VlVersion.Set((await prompter.TextAsync("VL version (e.g. 2025.7.4):", null, Validators.PackageVersion, ct)).Trim(), SlotSource.Prompt);
            if (!m.Dotnet.IsResolved)
                m.Dotnet.Set(new DotnetSelection(Path.GetFullPath((await prompter.TextAsync($"dotnet executable ({RequiredArchitecture}, SDK {MinimumSdk()}+):", null, Validators.DotnetExecutable, ct)).Trim()), null, null), SlotSource.Prompt);
        }

        if (m.Dotnet.IsResolved && m.Dotnet.Require().SdkVersion is null && await ProbeAsync(m, ct) is { } probeError)
            return [probeError];

        ApplyRemainingDefaults(m);
        if (m.Missing().Any())
            return [];
        if (m.Solution.Require() is { IsNone: false, Format: "slnx" } && !m.Dotnet.Require().SupportsSlnx)
            return [$"The .slnx format needs SDK 9.0.200 or later; {m.Dotnet.Require().Describe()} cannot build it. Use --solution-format sln."];

        console.Write(SettingsTable(m));

        if (prompter is not null && !await prompter.ConfirmAsync("Use these settings?", true, ct))
        {
            var picked = await prompter.MultiSelectAsync("Change which settings?", Enum.GetValues<Setting>(), Label, ct);
            foreach (var setting in picked)
                if (await EditAsync(m, setting, ct) is { } error)
                    return [error];
            if (picked.Count > 0)
                console.Write(SettingsTable(m));
        }

        return [];
    }

    private async Task<string?> EditAsync(CSharpModel m, Setting setting, CancellationToken ct)
    {
        switch (setting)
        {
            case Setting.Sdk:
                var exe = (await prompter!.TextAsync("dotnet executable:", m.Dotnet.Require().Executable, Validators.DotnetExecutable, ct)).Trim();
                if (exe != m.Dotnet.Require().Executable)
                {
                    m.Dotnet.Set(new DotnetSelection(Path.GetFullPath(exe), null, null), SlotSource.Prompt);
                    if (await ProbeAsync(m, ct) is { } error) return error;
                    if (!m.Dotnet.Require().SupportsSlnx && m.Solution.Require() is { IsNone: false, Format: "slnx" } s && m.Solution.Source == SlotSource.Default)
                        m.Solution.Set(s with { Format = "sln" }, SlotSource.Default);
                }
                break;
            case Setting.Tfm:
                SetText(m.Tfm, (await prompter!.TextAsync("Target framework:", m.Tfm.Require(), Validators.Tfm, ct)).Trim().ToLowerInvariant());
                break;
            case Setting.VlVersion:
                SetText(m.VlVersion, (await prompter!.TextAsync("VL version:", m.VlVersion.Require(), Validators.PackageVersion, ct)).Trim());
                break;
            case Setting.Configuration:
                SetText(m.Configuration, await prompter!.SelectAsync("Configuration:", ["Debug", "Release"], c => c, ct));
                break;
            case Setting.Solution:
                var current = m.Solution.Require();
                var name = (await prompter!.TextAsync("Solution (name or none):", current.IsNone ? "none" : current.Name, Validators.SolutionName, ct)).Trim();
                if (name.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    if (!current.IsNone) m.Solution.Set(SolutionChoice.None, SlotSource.Prompt);
                    break;
                }
                var format = current.Format;
                if (m.Dotnet.Require().SupportsSlnx)
                    format = await prompter.SelectAsync("Solution format:", ["slnx", "sln"], f => f, ct);
                var chosen = new SolutionChoice(name, format);
                if (chosen != current) m.Solution.Set(chosen, SlotSource.Prompt);
                break;
            case Setting.Versions:
                IReadOnlyList<VersionManagement> options = HasExistingVersionProps() ? [VersionManagement.Keep, VersionManagement.Shared, VersionManagement.Inline] : [VersionManagement.Shared, VersionManagement.Inline];
                var picked = await prompter!.SelectAsync("Version management:", options, VersionsLabel, ct);
                if (picked != m.Versions.Require()) m.Versions.Set(picked, SlotSource.Prompt);
                break;
            case Setting.Sources:
                var existing = string.Join(", ", m.Sources.Require().Select(s => s == NuGetSource.VvvvPublicFeed ? "vvvv" : $"{s.Name}={s.Location}"));
                var text = await prompter!.TextAsync("NuGet sources (e.g. vvvv, local=D:\\feed, or none):", existing.Length == 0 ? "none" : existing, Validators.NuGetSourceList, ct);
                Validators.ParseNuGetSources(text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), [], out var list);
                if (!list.SequenceEqual(m.Sources.Require())) m.Sources.Set(list, SlotSource.Prompt);
                break;
        }
        return null;
    }

    private static void SetText(Slot<string> slot, string value)
    {
        if (value != slot.Require()) slot.Set(value, SlotSource.Prompt);
    }

    private async Task<string?> ProbeAsync(CSharpModel m, CancellationToken ct)
    {
        var selection = m.Dotnet.Require();
        var result = await probe(selection.Executable, ct);
        if (!result.Found)
            return $"'{selection.Executable}' could not be run as dotnet.";
        if (result.Architecture is not null && !result.Architecture.Equals(RequiredArchitecture, StringComparison.OrdinalIgnoreCase))
            return $"'{selection.Executable}' is {result.Architecture}; vvvv needs an {RequiredArchitecture} .NET SDK.";
        var sdk = PickSdkVersion(result.Sdks);
        if (sdk is null)
            return $"'{selection.Executable}' has no SDK {MinimumSdk()} or later (found: {(result.Sdks.Count == 0 ? "none" : string.Join(", ", result.Sdks.Select(s => s.Version)))}).";
        m.Dotnet.Set(selection with { SdkVersion = sdk, Architecture = result.Architecture ?? RequiredArchitecture }, m.Dotnet.Source);
        return null;
    }

    private void ApplyRemainingDefaults(CSharpModel m)
    {
        if (!m.Configuration.IsResolved) m.Configuration.Set("Debug", SlotSource.Default);
        if (!m.Sources.IsResolved) m.Sources.Set([NuGetSource.VvvvPublicFeed], SlotSource.Default);
        if (!m.Versions.IsResolved) m.Versions.Set(HasExistingVersionProps() ? VersionManagement.Keep : VersionManagement.Shared, SlotSource.Default);

        if (m.Solution.IsResolved && m.Solution.Require() is { IsNone: false, Format: "?" } flagged)
            m.Solution.Set(flagged with { Format = ExistingSolutionFormat(flagged.Name!) ?? DefaultSolutionFormat(m) }, SlotSource.Flag);
        if (!m.Solution.IsResolved)
        {
            var existing = ExistingSolution();
            m.Solution.Set(existing ?? new SolutionChoice(basics.Name.Require(), DefaultSolutionFormat(m)), SlotSource.Default);
        }
    }

    private string DefaultSolutionFormat(CSharpModel m) =>
        m.Dotnet.IsResolved && m.Dotnet.Require().SupportsSlnx ? "slnx" : "sln";

    private string? ExistingSolutionFormat(string name)
    {
        var dest = basics.Destination.Require();
        if (File.Exists(Path.Combine(dest, name + ".slnx"))) return "slnx";
        if (File.Exists(Path.Combine(dest, name + ".sln"))) return "sln";
        return null;
    }

    private SolutionChoice? ExistingSolution()
    {
        var dest = basics.Destination.Require();
        if (!Directory.Exists(dest)) return null;
        var file = Directory.EnumerateFiles(dest, "*.slnx").Concat(Directory.EnumerateFiles(dest, "*.sln")).FirstOrDefault();
        return file is null ? null : new SolutionChoice(Path.GetFileNameWithoutExtension(file), Path.GetExtension(file).TrimStart('.').ToLowerInvariant());
    }

    private bool HasExistingVersionProps()
    {
        var dest = basics.Destination.Require();
        return File.Exists(Path.Combine(dest, "Directory.Packages.props")) || File.Exists(Path.Combine(dest, "Directory.Build.props"));
    }

    private string MinimumSdk() => $"{VvvvHostMetadata.MajorOf(host.Tfm) ?? 8}.0";

    /// <summary>The dotnet on PATH, when its architecture matches and it carries a usable SDK.</summary>
    private DotnetSelection? PickSdk(DotnetDiscoveryResult dotnet)
    {
        if (!dotnet.Found) return null;
        if (dotnet.Architecture is not null && !dotnet.Architecture.Equals(RequiredArchitecture, StringComparison.OrdinalIgnoreCase))
        {
            Warnings.Add($"dotnet on PATH is {dotnet.Architecture}; vvvv needs an {RequiredArchitecture} SDK, so the executable must be given explicitly.");
            return null;
        }
        var sdk = PickSdkVersion(dotnet.Sdks);
        if (sdk is null)
        {
            Warnings.Add($"dotnet on PATH has no SDK {MinimumSdk()} or later.");
            return null;
        }
        return new DotnetSelection("dotnet", sdk, dotnet.Architecture ?? RequiredArchitecture);
    }

    /// <summary>Newest SDK that can target the host's .NET version; stable releases win over previews.</summary>
    private string? PickSdkVersion(IReadOnlyList<DotnetSdk> sdks)
    {
        var minimum = VvvvHostMetadata.MajorOf(host.Tfm) ?? 8;
        var candidates = sdks
            .Select(s => (s.Version, Parsed: DotnetSelection.ParseVersion(s.Version), Preview: s.Version.Contains('-')))
            .Where(s => s.Parsed is not null && s.Parsed.Major >= minimum)
            .OrderBy(s => s.Preview)
            .ThenByDescending(s => s.Parsed)
            .ToList();
        return candidates.FirstOrDefault().Version;
    }

    private Table SettingsTable(CSharpModel m)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Setting");
        table.AddColumn("Value");
        table.AddRow(Label(Setting.Sdk), Markup.Escape(m.Dotnet.Require().Describe()));
        table.AddRow(Label(Setting.Tfm), Markup.Escape(m.Tfm.Require() + (m.Tfm.Source == SlotSource.Default ? " (vvvv host)" : "")));
        table.AddRow(Label(Setting.VlVersion), Markup.Escape(m.VlVersion.Require() + (m.VlVersion.Source == SlotSource.Default ? " (VL.Core.nuspec)" : "")));
        table.AddRow(Label(Setting.Configuration), Markup.Escape(m.Configuration.Require()));
        table.AddRow(Label(Setting.Solution), Markup.Escape(m.Solution.Require().FileName));
        table.AddRow(Label(Setting.Versions), Markup.Escape(VersionsLabel(m.Versions.Require())));
        table.AddRow(Label(Setting.Sources), Markup.Escape(m.Sources.Require().Count == 0 ? "none" : string.Join(", ", m.Sources.Require().Select(s => s == NuGetSource.VvvvPublicFeed ? s.Name : $"{s.Name}={s.Location}"))));
        return table;
    }

    private static string Label(Setting s) => s switch
    {
        Setting.Sdk => ".NET SDK",
        Setting.Tfm => "Target framework",
        Setting.VlVersion => "VL version",
        Setting.Configuration => "Configuration",
        Setting.Solution => "Solution",
        Setting.Versions => "Version management",
        _ => "NuGet sources",
    };

    private static string VersionsLabel(VersionManagement v) => v switch
    {
        VersionManagement.Shared => "shared (Directory.Packages.props)",
        VersionManagement.Inline => "inline (per project)",
        _ => "keep existing",
    };

    private static string NormalizeConfiguration(string value) =>
        value.Trim().Equals("Release", StringComparison.OrdinalIgnoreCase) ? "Release" : "Debug";

    public string Summary(CSharpModel m)
    {
        var d = m.Dotnet.Require();
        return $"C#: {m.Tfm.Require()} · VL {m.VlVersion.Require()} · {m.Configuration.Require()} · {m.Solution.Require().FileName} · SDK {d.SdkVersion} {d.Architecture}";
    }
}
