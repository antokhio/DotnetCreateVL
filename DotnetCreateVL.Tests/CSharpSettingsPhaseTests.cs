using DotnetCreateVL.Discovery;
using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class CSharpSettingsPhaseTests
{
    private static BasicsModel Basics(FakeEnvironment env, string name = "Gallery")
    {
        var b = new BasicsModel();
        b.Kind.Set(RepositoryKind.Application, SlotSource.Flag);
        b.Name.Set(name, SlotSource.Flag);
        b.Destination.Set(Path.Combine(env.WorkDir, name), SlotSource.Default);
        b.Vvvv.Set(env.Install, SlotSource.Flag);
        b.Pipeline.Set(PipelineBackend.None, SlotSource.Flag);
        return b;
    }

    private static Func<string, CancellationToken, Task<DotnetDiscoveryResult>> Probe(bool found = true, string? arch = "x64", params string[] sdks) =>
        (_, _) => Task.FromResult(new DotnetDiscoveryResult(found, arch, sdks.Select(v => new DotnetSdk(v, "x")).ToList()));

    [Fact]
    public async Task Verified_metadata_fills_the_table_and_one_confirm_accepts_it()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushKey(ConsoleKey.Enter);       // use these settings? Yes

        var phase = new CSharpSettingsPhase(console, new SpectrePrompter(console), env.Build(), Basics(env));
        var m = new CSharpModel();
        Assert.Empty(phase.ApplyFlags(m, null, null, null, null, null, null, null, null));

        var errors = await phase.RunAsync(m, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(m.Missing());
        Assert.Equal("net8.0", m.Tfm.Value);
        Assert.Equal(SlotSource.Default, m.Tfm.Source);
        Assert.Equal("2025.7.4", m.VlVersion.Value);
        Assert.Equal(new DotnetSelection("dotnet", "10.0.100", "x64"), m.Dotnet.Value);
        Assert.Equal("Debug", m.Configuration.Value);
        Assert.Equal(new SolutionChoice("Gallery", "slnx"), m.Solution.Value);
        Assert.Equal(VersionManagement.Shared, m.Versions.Value);
        Assert.Equal([NuGetSource.VvvvPublicFeed], m.Sources.Value);
        Assert.Contains("vvvv-public-feed", console.Output);
        Assert.Contains("net8.0 (vvvv host)", console.Output);
        Assert.Contains("2025.7.4 (VL.Core.nuspec)", console.Output);
        Assert.DoesNotContain("Target framework (e.g.", console.Output);
        Assert.Equal("C#: net8.0 · VL 2025.7.4 · Debug · Gallery.slnx · SDK 10.0.100 x64", phase.Summary(m));
    }

    [Fact]
    public async Task Unverifiable_values_are_asked_before_the_table_and_never_taken_from_the_folder_name()
    {
        using var env = new FakeEnvironment(hostTfm: null, vlVersion: null);
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("net8");            // rejected
        console.Input.PushTextWithEnter("net8.0-windows");
        console.Input.PushTextWithEnter("latest");          // rejected
        console.Input.PushTextWithEnter("2025.7.4");
        console.Input.PushKey(ConsoleKey.Enter);            // use these settings

        var phase = new CSharpSettingsPhase(console, new SpectrePrompter(console), env.Build(), Basics(env));
        var m = new CSharpModel();
        var errors = await phase.RunAsync(m, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal("net8.0-windows", m.Tfm.Value);
        Assert.Equal(SlotSource.Prompt, m.Tfm.Source);
        Assert.Equal("2025.7.4", m.VlVersion.Value);
        Assert.Equal(SlotSource.Prompt, m.VlVersion.Source);
        Assert.DoesNotContain("(9.9)", console.Output);     // the folder name is never offered as a default
        Assert.Contains("Target framework (e.g. net8.0):", console.Output);
        Assert.Contains("VL version (e.g. 2025.7.4):", console.Output);
    }

    [Fact]
    public async Task Noninteractive_without_metadata_reports_missing_tfm_and_vl_version()
    {
        using var env = new FakeEnvironment(hostTfm: null, vlVersion: null);
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(), Basics(env));
        var m = new CSharpModel();

        var errors = await phase.RunAsync(m, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal(["--tfm", "--vl-version"], m.Missing().Select(x => x.Flag));
    }

    [Fact]
    public async Task Customize_changes_only_the_picked_settings()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("n");          // use these settings? No
        console.Input.PushKey(ConsoleKey.DownArrow);   // Tfm
        console.Input.PushKey(ConsoleKey.DownArrow);   // VlVersion
        console.Input.PushKey(ConsoleKey.DownArrow);   // Configuration
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);   // Solution
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.DownArrow);   // Versions
        console.Input.PushKey(ConsoleKey.DownArrow);   // Sources
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);   // Configuration: Release
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("Gallery.sln"); // rejected: extension
        console.Input.PushTextWithEnter("GalleryAll");
        console.Input.PushKey(ConsoleKey.DownArrow);   // format: sln
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("local=https://user:pw@feed/x");   // rejected: credentials
        console.Input.PushTextWithEnter("local=D:\\feed, cd=https://feed.example/v3/index.json");   // replaces the baseline

        var phase = new CSharpSettingsPhase(console, new SpectrePrompter(console), env.Build(), Basics(env));
        var m = new CSharpModel();
        var errors = await phase.RunAsync(m, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal("Release", m.Configuration.Value);
        Assert.Equal(SlotSource.Prompt, m.Configuration.Source);
        Assert.Equal(new SolutionChoice("GalleryAll", "sln"), m.Solution.Value);
        Assert.Equal([new NuGetSource("local", "D:\\feed"), new NuGetSource("cd", "https://feed.example/v3/index.json")], m.Sources.Value);
        Assert.Equal(SlotSource.Prompt, m.Sources.Source);
        Assert.Equal(SlotSource.Default, m.Tfm.Source);
        Assert.Equal(SlotSource.Default, m.Versions.Source);
        Assert.Contains("Credentials in a source URL are not accepted", console.Output);
        Assert.Contains("GalleryAll.sln", console.Output);
    }

    [Fact]
    public async Task Flags_resolve_everything_and_probe_the_given_dotnet()
    {
        using var env = new FakeEnvironment();
        var exe = Path.Combine(env.Root, "dotnet.exe");
        File.WriteAllText(exe, "");
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(), Basics(env), Probe(sdks: ["8.0.400", "9.0.100-preview.1"]));
        var m = new CSharpModel();

        var flagErrors = phase.ApplyFlags(m, exe, "NET8.0-Windows", "2025.7.4", "release", "Shapes", null, "inline", ["local=D:\\feed"]);
        var errors = await phase.RunAsync(m, CancellationToken.None);

        Assert.Empty(flagErrors);
        Assert.Empty(errors);
        Assert.Empty(m.Missing());
        Assert.Equal(new DotnetSelection(exe, "8.0.400", "x64"), m.Dotnet.Value);
        Assert.Equal(SlotSource.Flag, m.Dotnet.Source);
        Assert.Equal("net8.0-windows", m.Tfm.Value);
        Assert.Equal("Release", m.Configuration.Value);
        Assert.Equal(new SolutionChoice("Shapes", "sln"), m.Solution.Value);     // SDK 8 → no slnx
        Assert.Equal(VersionManagement.Inline, m.Versions.Value);
        Assert.Equal([NuGetSource.VvvvPublicFeed, new NuGetSource("local", "D:\\feed")], m.Sources.Value);
        Assert.True(m.Missing().ToList().Count == 0);
    }

    [Theory]
    [InlineData(new[] { "none" }, new string[0])]
    [InlineData(new[] { "none", "local=D:\\feed" }, new[] { "local" })]
    [InlineData(new[] { "vvvv", "local=D:\\feed" }, new[] { "vvvv-public-feed", "local" })]
    [InlineData(new[] { "local=D:\\feed", "none", "vvvv" }, new[] { "vvvv-public-feed" })]
    public void Nuget_source_shorthands_compose_in_order(string[] flags, string[] expectedNames)
    {
        using var env = new FakeEnvironment();
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(), Basics(env));
        var m = new CSharpModel();

        Assert.Empty(phase.ApplyFlags(m, null, null, null, null, null, null, null, flags));

        Assert.Equal(expectedNames, m.Sources.Value!.Select(s => s.Name));
    }

    [Fact]
    public void Flag_errors_are_reported_per_flag()
    {
        using var env = new FakeEnvironment();
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(), Basics(env));
        var m = new CSharpModel();

        var errors = phase.ApplyFlags(m, Path.Combine(env.Root, "missing.exe"), "netcore8", "latest", "Test", "Gallery.slnx", "xml", "cpm", ["nope"]);
        var sourceErrors = new[] { new[] { "a=https://u:p@h/x" }, new[] { "a=D:\\x", "a=D:\\y" } }
            .Select(flags => phase.ApplyFlags(new CSharpModel(), null, null, null, null, null, null, null, flags).Single()).ToList();

        Assert.Equal(8, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("--dotnet") && e.Contains("does not exist"));
        Assert.Contains(errors, e => e.StartsWith("--tfm"));
        Assert.Contains(errors, e => e.StartsWith("--vl-version"));
        Assert.Contains(errors, e => e.StartsWith("--configuration"));
        Assert.Contains(errors, e => e.StartsWith("--solution ") && e.Contains("without extension"));
        Assert.Contains(errors, e => e.StartsWith("--solution-format"));
        Assert.Contains(errors, e => e.StartsWith("--versions"));
        Assert.Contains(errors, e => e.Contains("'nope'"));
        Assert.Contains(sourceErrors, e => e.Contains("Credentials"));
        Assert.Contains(sourceErrors, e => e.Contains("duplicate source name"));
    }

    [Fact]
    public async Task Slnx_requested_on_an_old_sdk_is_blocking()
    {
        using var env = new FakeEnvironment();
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(sdks: "8.0.400"), Basics(env));
        var m = new CSharpModel();

        Assert.Empty(phase.ApplyFlags(m, null, null, null, null, null, "slnx", null, null));
        var errors = await phase.RunAsync(m, CancellationToken.None);

        var error = Assert.Single(errors);
        Assert.Contains("9.0.200", error);
    }

    [Theory]
    [InlineData("x64", new[] { "6.0.428", "8.0.201", "10.0.100-rc.1", "9.0.301" }, "9.0.301", null)]
    [InlineData("x64", new[] { "6.0.428", "10.0.100-rc.1" }, "10.0.100-rc.1", null)]
    [InlineData("x64", new[] { "6.0.428" }, null, "no SDK 8.0")]
    [InlineData("arm64", new[] { "10.0.100" }, null, "arm64")]
    public async Task Sdk_on_path_is_chosen_only_when_suitable(string arch, string[] sdks, string? expected, string? warning)
    {
        using var env = new FakeEnvironment();
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(dotnetArchitecture: arch, sdks: sdks), Basics(env));
        var m = new CSharpModel();

        await phase.RunAsync(m, CancellationToken.None);

        if (expected is null)
        {
            Assert.Equal(["--dotnet"], m.Missing().Select(x => x.Flag));
            Assert.Contains(phase.Warnings, w => w.Contains(warning!));
        }
        else
        {
            Assert.Equal(expected, m.Dotnet.Value!.SdkVersion);
            Assert.Empty(phase.Warnings);
        }
    }

    [Theory]
    [InlineData(false, "x64", new[] { "10.0.100" }, "could not be run")]
    [InlineData(true, "arm64", new[] { "10.0.100" }, "is arm64")]
    [InlineData(true, "x64", new[] { "6.0.428" }, "no SDK 8.0")]
    public async Task Explicit_dotnet_that_fails_the_probe_is_blocking(bool found, string arch, string[] sdks, string expected)
    {
        using var env = new FakeEnvironment();
        var exe = Path.Combine(env.Root, "dotnet.exe");
        File.WriteAllText(exe, "");
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(), Basics(env), Probe(found, arch, sdks));
        var m = new CSharpModel();

        Assert.Empty(phase.ApplyFlags(m, exe, null, null, null, null, null, null, null));
        var errors = await phase.RunAsync(m, CancellationToken.None);

        Assert.Contains(expected, Assert.Single(errors));
    }

    [Fact]
    public async Task Existing_solution_and_version_props_are_kept()
    {
        using var env = new FakeEnvironment();
        var basics = Basics(env);
        var dest = basics.Destination.Require();
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "Old.sln"), "");
        File.WriteAllText(Path.Combine(dest, "Directory.Packages.props"), "<Project/>");
        var phase = new CSharpSettingsPhase(new TestConsole(), null, env.Build(), basics);
        var m = new CSharpModel();

        await phase.RunAsync(m, CancellationToken.None);

        Assert.Equal(new SolutionChoice("Old", "sln"), m.Solution.Value);
        Assert.Equal(VersionManagement.Keep, m.Versions.Value);
    }

    [Theory]
    [InlineData("net8.0", 8)]
    [InlineData("net8.0-windows10.0.19041", 8)]
    [InlineData("net10.0", 10)]
    [InlineData(null, null)]
    [InlineData("netstandard2.0", null)]
    public void Host_tfm_major_is_parsed(string? tfm, int? expected) =>
        Assert.Equal(expected, VvvvHostMetadata.MajorOf(tfm));
}
