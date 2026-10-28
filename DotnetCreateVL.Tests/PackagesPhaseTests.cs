using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class PackagesPhaseTests
{
    private static (BasicsModel Basics, ApplicationModel App) Models(FakeEnvironment env, RepositoryKind kind = RepositoryKind.Application, string name = "Gallery")
    {
        var b = new BasicsModel();
        b.Kind.Set(kind, SlotSource.Flag);
        b.Name.Set(name, SlotSource.Flag);
        b.Destination.Set(Path.Combine(env.WorkDir, name), SlotSource.Default);
        b.Vvvv.Set(env.Install, SlotSource.Flag);
        b.Pipeline.Set(PipelineBackend.None, SlotSource.Flag);

        var a = new ApplicationModel();
        if (kind == RepositoryKind.Application)
        {
            a.Name.Set(name, SlotSource.Default);
            a.EntryPath.Set($"{name}.vl", SlotSource.Default);
        }
        return (b, a);
    }

    private static PackagesPhase Create(TestConsole console, (BasicsModel Basics, ApplicationModel App) m, IPrompter? prompter) =>
        new(console, prompter, m.Basics, m.App);

    [Fact]
    public async Task Application_declines_packages_with_one_prompt()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);       // add packages? No

        var models = Models(env);
        var phase = Create(console, models, new SpectrePrompter(console));
        var model = new PackagesModel();

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(model.Packages);
        Assert.Empty(model.Missing(RepositoryKind.Application));
        Assert.Equal("Packages: none", phase.Summary(model));
    }

    [Fact]
    public async Task Interactive_loop_defaults_later_roots_to_the_first_container()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("y");                  // add packages
        console.Input.PushTextWithEnter("VL.Shapes");          // id
        console.Input.PushTextWithEnter("repos/VL.Shapes");    // root (custom)
        console.Input.PushTextWithEnter("y");                  // wrapper
        console.Input.PushTextWithEnter("y");                  // another
        console.Input.PushTextWithEnter("vl.shapes");          // duplicate id (case-insensitive) rejected
        console.Input.PushTextWithEnter("VL.Colors");
        console.Input.PushKey(ConsoleKey.Enter);               // root default: repos\VL.Colors
        console.Input.PushKey(ConsoleKey.Enter);               // wrapper: No
        console.Input.PushKey(ConsoleKey.Enter);               // another: No

        var models = Models(env);
        var phase = Create(console, models, new SpectrePrompter(console));
        var model = new PackagesModel();

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal(["VL.Shapes", "VL.Colors"], model.Packages.Select(p => p.Id));
        Assert.Equal("repos\\VL.Shapes", model.Packages[0].Root.Value);
        Assert.Equal(SlotSource.Prompt, model.Packages[0].Root.Source);
        Assert.True(model.Packages[0].HasWrapper);
        Assert.Equal("repos\\VL.Colors", model.Packages[1].Root.Value);
        Assert.Equal(SlotSource.Default, model.Packages[1].Root.Source);
        Assert.False(model.Packages[1].HasWrapper);
        Assert.Equal("repos", model.Container.Value);
        Assert.Contains("already exists", console.Output);
        Assert.Contains("repos\\VL.Shapes\\src\\VL.Shapes.csproj", console.Output);
        Assert.Equal("Packages: VL.Shapes (C#), VL.Colors", phase.Summary(model));
    }

    [Fact]
    public async Task Package_repository_starts_loop_without_gate()
    {
        using var env = new FakeEnvironment();
        var models = Models(env, RepositoryKind.Package, "Shapes");

        var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("VL.Shapes");
        console.Input.PushKey(ConsoleKey.Enter);               // root: VL.Shapes
        console.Input.PushKey(ConsoleKey.Enter);               // wrapper: No
        console.Input.PushKey(ConsoleKey.Enter);               // another: No

        var phase = Create(console, models, new SpectrePrompter(console));
        var model = new PackagesModel();

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        Assert.DoesNotContain("Does this application require", console.Output);
        var p = Assert.Single(model.Packages);
        Assert.Equal("VL.Shapes\\VL.Shapes.vl", p.DocumentPath);
        Assert.Equal("", model.Container.Value);
    }

    [Fact]
    public async Task Noninteractive_package_repository_without_packages_reports_missing()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var phase = Create(console, Models(env, RepositoryKind.Package, "Shapes"), prompter: null);
        var model = new PackagesModel();

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        var missing = Assert.Single(model.Missing(RepositoryKind.Package));
        Assert.Equal("--package", missing.Flag);
        Assert.Empty(model.Missing(RepositoryKind.Application));
    }

    [Fact]
    public async Task Flags_define_packages_container_and_wrappers()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var phase = Create(console, Models(env), prompter: null);
        var model = new PackagesModel();

        var flagErrors = phase.ApplyFlags(model, ["VL.Shapes", "VL.Colors:custom/Colors"], "repos", ["vl.shapes"]);
        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(flagErrors);
        Assert.Empty(errors);
        Assert.Empty(model.Missing(RepositoryKind.Application));
        Assert.Equal("repos\\VL.Shapes", model.Packages[0].Root.Value);
        Assert.Equal(SlotSource.Default, model.Packages[0].Root.Source);
        Assert.True(model.Packages[0].HasWrapper);
        Assert.Equal(SlotSource.Flag, model.Packages[0].Wrapper.Source);
        Assert.Equal("custom\\Colors", model.Packages[1].Root.Value);
        Assert.False(model.Packages[1].HasWrapper);
        Assert.Equal(SlotSource.Flag, model.Packages[1].Wrapper.Source);
        Assert.Equal("repos\\VL.Shapes\\VL.Shapes.vl", model.Packages[0].DocumentPath);
    }

    [Fact]
    public void Flag_errors_cover_ids_roots_and_unknown_csharp()
    {
        using var env = new FakeEnvironment();
        var phase = Create(new TestConsole(), Models(env), prompter: null);
        var model = new PackagesModel();

        var flagErrors = phase.ApplyFlags(model,
            ["1Bad", "VL.Shapes", "VL.Shapes:x", "VL.Ok:C:\\abs", "VL.Up:../out", "VL.Colors"],
            "C:\\abs", ["VL.Nope"]);

        Assert.Equal(["VL.Shapes", "VL.Colors"], model.Packages.Select(p => p.Id));
        Assert.Contains(flagErrors, e => e.Contains("'1Bad'") && e.Contains("start with a letter"));
        Assert.Contains(flagErrors, e => e.Contains("'VL.Shapes:x'") && e.Contains("already exists"));
        Assert.Contains(flagErrors, e => e.Contains("'VL.Ok:C:\\abs'") && e.Contains("relative"));
        Assert.Contains(flagErrors, e => e.Contains("'VL.Up:../out'") && e.Contains("leave the repository"));
        Assert.Contains(flagErrors, e => e.StartsWith("--package-container"));
        Assert.Contains(flagErrors, e => e.Contains("'VL.Nope'") && e.Contains("no such --package"));
        Assert.True(model.Packages.All(p => p.Wrapper.IsResolved && !p.HasWrapper));
    }

    [Fact]
    public async Task Conflicts_report_shared_roots_and_entry_collision()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var models = Models(env);
        var phase = Create(console, models, prompter: null);
        var model = new PackagesModel();

        // The application entry sits inside a package folder under that package's name: a genuine clash.
        models.App.EntryPath.Set("VL.A\\VL.A.vl", SlotSource.Flag);
        Assert.Empty(phase.ApplyFlags(model, ["VL.A", "VL.B:shared", "VL.C:shared"], null, null));
        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("VL.B, VL.C") && e.Contains("'shared'"));
        Assert.Contains(errors, e => e.Contains("VL.A\\VL.A.vl") && e.Contains("application entry"));
    }

    [Fact]
    public void Repository_root_is_not_a_package_root()
    {
        using var env = new FakeEnvironment();
        var phase = Create(new TestConsole(), Models(env), prompter: null);
        var model = new PackagesModel();

        var flagErrors = phase.ApplyFlags(model, ["VL.A:.", "VL.B:./"], null, null);

        Assert.Equal(2, flagErrors.Count);
        Assert.All(flagErrors, e => Assert.Contains("below the repository root", e));
        Assert.Empty(model.Packages);
    }

    [Theory]
    [InlineData("VL.Shapes", null)]
    [InlineData("my-pkg_2", null)]
    [InlineData("VL Shapes", "letters, digits")]
    [InlineData(".Hidden", "start with a letter")]
    [InlineData("Trailing.", "end with '.'")]
    [InlineData("", "required")]
    public void PackageId_validation(string value, string? expectedFragment)
    {
        var result = Validators.PackageId(value, []);
        if (expectedFragment is null) Assert.Null(result);
        else Assert.Contains(expectedFragment, result);
    }
}
