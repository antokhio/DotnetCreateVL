namespace DotnetCreateVL.Model;

public enum VersionManagement
{
    /// <summary>Versions in a shared Directory.Packages.props as MSBuild properties (no Central Package Management).</summary>
    Shared,
    /// <summary>Versions inline in each project file.</summary>
    Inline,
    /// <summary>An existing arrangement is left untouched.</summary>
    Keep,
}

/// <summary>The dotnet executable used for C# builds, plus what probing it reported.</summary>
public sealed record DotnetSelection(string Executable, string? SdkVersion, string? Architecture)
{
    public bool IsOnPath => Executable == "dotnet";

    public string Describe()
    {
        var origin = IsOnPath ? "dotnet on PATH" : Executable;
        return $"{SdkVersion ?? "unknown SDK"} {Architecture ?? "?"} ({origin})";
    }

    /// <summary>The .slnx format is understood by SDK 9.0.200 and later.</summary>
    public bool SupportsSlnx =>
        ParseVersion(SdkVersion) is { } v && (v.Major > 9 || (v.Major == 9 && v.Build >= 200));

    public static Version? ParseVersion(string? sdkVersion)
    {
        if (sdkVersion is null) return null;
        var dash = sdkVersion.IndexOf('-');
        return Version.TryParse(dash < 0 ? sdkVersion : sdkVersion[..dash], out var v) ? v : null;
    }
}

public sealed record SolutionChoice(string? Name, string Format)
{
    public static SolutionChoice None { get; } = new(null, "slnx");
    public bool IsNone => Name is null;
    public string FileName => IsNone ? "none" : $"{Name}.{Format}";
}

public sealed record NuGetSource(string Name, string Location)
{
    /// <summary>vvvv's public feed; the baseline entry of every generated NuGet.config.</summary>
    public static NuGetSource VvvvPublicFeed { get; } = new("vvvv-public-feed", "https://teamcity.vvvv.org/guestAuth/app/nuget/v1/FeedService.svc/");
}

/// <summary>Phase 5 slots: settings shared by every C# project in the repository.</summary>
public sealed class CSharpModel
{
    public Slot<DotnetSelection> Dotnet { get; } = new("csharp.sdk", "--dotnet", "path to dotnet.exe (x64, SDK ≥ host .NET version)");
    public Slot<string> Tfm { get; } = new("csharp.tfm", "--tfm", "target framework moniker, for example net8.0");
    public Slot<string> VlVersion { get; } = new("csharp.vlVersion", "--vl-version", "exact VL package version, for example 2025.7.4");
    public Slot<string> Configuration { get; } = new("csharp.configuration", "--configuration", "Debug | Release");
    public Slot<SolutionChoice> Solution { get; } = new("csharp.solution", "--solution", "solution name | none");
    public Slot<VersionManagement> Versions { get; } = new("csharp.versions", "--versions", "shared | inline | keep");
    public Slot<IReadOnlyList<NuGetSource>> Sources { get; } = new("csharp.sources", "--nuget-source", "name=url-or-path, comma-separated; vvvv | none");

    public IEnumerable<MissingSlot> Missing()
    {
        if (!Dotnet.IsResolved) yield return Dotnet.AsMissing();
        if (!Tfm.IsResolved) yield return Tfm.AsMissing();
        if (!VlVersion.IsResolved) yield return VlVersion.AsMissing();
        if (!Configuration.IsResolved) yield return Configuration.AsMissing();
        if (!Solution.IsResolved) yield return Solution.AsMissing();
        if (!Versions.IsResolved) yield return Versions.AsMissing();
        if (!Sources.IsResolved) yield return Sources.AsMissing();
    }
}
