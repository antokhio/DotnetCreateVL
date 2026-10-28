using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class EntriesPhaseTests
{
    private static BasicsModel Basics(FakeEnvironment env, RepositoryKind kind = RepositoryKind.Application, string name = "Gallery")
    {
        var b = new BasicsModel();
        b.Kind.Set(kind, SlotSource.Flag);
        b.Name.Set(name, SlotSource.Flag);
        b.Destination.Set(Path.Combine(env.WorkDir, name), SlotSource.Default);
        b.Vvvv.Set(env.Install, SlotSource.Flag);
        b.Pipeline.Set(PipelineBackend.None, SlotSource.Flag);
        return b;
    }

    private static EntriesPhase Create(TestConsole console, BasicsModel basics, IPrompter? prompter) =>
        new(console, prompter, basics);

    [Fact]
    public async Task Interactive_defaults_resolve_entry_with_two_prompts_only()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);       // app name: Gallery
        console.Input.PushKey(ConsoleKey.Enter);       // entry: Gallery.vl

        var basics = Basics(env);
        var phase = Create(console, basics, new SpectrePrompter(console));
        var model = new ApplicationModel();

        await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(model.Missing());
        Assert.Equal("Gallery", model.Name.Value);
        Assert.Equal("Gallery.vl", model.EntryPath.Value);
        Assert.Equal("Entry: Gallery.vl", phase.Summary(model));
        Assert.DoesNotContain("another", console.Output);
    }

    [Fact]
    public async Task Interactive_custom_name_and_path_are_recorded_as_prompted()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive();
        console.Input.PushTextWithEnter("Gallery2");
        console.Input.PushTextWithEnter("C:\\abs\\Main.vl");          // rejected: not repository-relative
        console.Input.PushTextWithEnter("./patches/Main.vl");

        var basics = Basics(env);
        var phase = Create(console, basics, new SpectrePrompter(console));
        var model = new ApplicationModel();

        await phase.RunAsync(model, CancellationToken.None);

        Assert.Equal(SlotSource.Prompt, model.Name.Source);
        Assert.Equal(SlotSource.Prompt, model.EntryPath.Source);
        Assert.Equal("patches\\Main.vl", model.EntryPath.Value);
        Assert.Contains("relative", console.Output);
    }

    [Fact]
    public async Task Noninteractive_resolves_safe_defaults_without_prompts()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var basics = Basics(env, name: "Shapes");
        var phase = Create(console, basics, prompter: null);
        var model = new ApplicationModel();

        await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(model.Missing());
        Assert.Equal(SlotSource.Default, model.Name.Source);
        Assert.Equal("Shapes.vl", model.EntryPath.Value);
        Assert.Equal(SlotSource.Default, model.EntryPath.Source);
    }

    [Fact]
    public async Task Flags_set_name_and_entry()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var basics = Basics(env);
        var phase = Create(console, basics, prompter: null);
        var model = new ApplicationModel();

        var flagErrors = phase.ApplyFlags(model, "Probe", "patches/Probe.vl");
        await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(flagErrors);
        Assert.Empty(model.Missing());
        Assert.Equal(SlotSource.Flag, model.Name.Source);
        Assert.Equal("patches\\Probe.vl", model.EntryPath.Value);
        Assert.Equal(SlotSource.Flag, model.EntryPath.Source);
    }

    [Fact]
    public void Flag_errors_cover_bad_name_and_path()
    {
        using var env = new FakeEnvironment();
        var phase = Create(new TestConsole(), Basics(env), prompter: null);
        var model = new ApplicationModel();

        var flagErrors = phase.ApplyFlags(model, "Gallery.vl", "C:\\abs\\Gallery.vl");

        Assert.Equal(2, flagErrors.Count);
        Assert.Contains(flagErrors, e => e.StartsWith("--app-name"));
        Assert.Contains(flagErrors, e => e.StartsWith("--entry") && e.Contains("relative"));
        Assert.False(model.Name.IsResolved);
        Assert.False(model.EntryPath.IsResolved);
    }

    [Fact]
    public async Task Package_repository_skips_phase()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var basics = Basics(env, RepositoryKind.Package, "Shapes");
        var phase = Create(console, basics, prompter: null);
        var model = new ApplicationModel();

        await phase.RunAsync(model, CancellationToken.None);

        Assert.False(model.Name.IsResolved);
        Assert.Contains("skipped", console.Output);
    }

    [Fact]
    public void NormalizePath_strips_leading_dots_and_uses_backslashes()
    {
        Assert.Equal("c\\D.vl", RepositoryPaths.Normalize("./c/D.vl"));
        Assert.Equal("A.vl", RepositoryPaths.Normalize(" A.vl "));
    }
}
