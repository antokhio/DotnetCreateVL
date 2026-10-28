using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class BasicsPhaseTests
{
    [Fact]
    public async Task Interactive_prompts_fill_every_slot_and_accept_destination_default()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive();

        console.Input.PushKey(ConsoleKey.Enter);           // kind: Application (first item)
        console.Input.PushTextWithEnter("Gallery");        // name
        console.Input.PushKey(ConsoleKey.Enter);           // destination: accept default
        console.Input.PushKey(ConsoleKey.Enter);           // vvvv: first discovered installation
        console.Input.PushKey(ConsoleKey.DownArrow);       // pipeline: None -> Just
        console.Input.PushKey(ConsoleKey.DownArrow);       //           Just -> CMD
        console.Input.PushKey(ConsoleKey.Enter);

        var model = new BasicsModel();
        var phase = new BasicsPhase(console, new SpectrePrompter(console), env.Build());

        await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(model.Missing());
        Assert.Equal(RepositoryKind.Application, model.Kind.Value);
        Assert.Equal("Gallery", model.Name.Value);
        Assert.Equal(Path.Combine(env.WorkDir, "Gallery"), model.Destination.Value);
        Assert.Equal(SlotSource.Default, model.Destination.Source);
        Assert.Equal(env.Install.Path, model.Vvvv.Value!.Path);
        Assert.Equal(PipelineBackend.Cmd, model.Pipeline.Value);
        Assert.Empty(phase.Warnings);
    }

    [Fact]
    public async Task Interactive_manual_vvvv_path_is_validated_and_rejected_until_it_exists()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive();

        console.Input.PushKey(ConsoleKey.DownArrow);       // kind: Package repository
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("Shapes");
        console.Input.PushKey(ConsoleKey.Enter);           // destination default
        console.Input.PushKey(ConsoleKey.DownArrow);       // vvvv: "Enter installation path…" (after the single discovered item)
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter(Path.Combine(env.Root, "does-not-exist"));
        console.Input.PushTextWithEnter(env.VvvvDir);
        console.Input.PushKey(ConsoleKey.Enter);           // pipeline: None

        var model = new BasicsModel();
        var phase = new BasicsPhase(console, new SpectrePrompter(console), env.Build());

        await phase.RunAsync(model, CancellationToken.None);

        Assert.Equal(RepositoryKind.Package, model.Kind.Value);
        Assert.Equal(env.Install.Path, model.Vvvv.Value!.Path);
        Assert.Equal("manual", model.Vvvv.Value.Origin);
        Assert.Equal(PipelineBackend.None, model.Pipeline.Value);
        Assert.Contains("does not exist", console.Output);
    }

    [Fact]
    public async Task Noninteractive_without_flags_reports_all_slots_missing()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var model = new BasicsModel();
        var phase = new BasicsPhase(console, prompter: null, env.Build());

        await phase.RunAsync(model, CancellationToken.None);

        var missing = model.Missing().Select(m => m.Flag).ToArray();
        Assert.Equal(["--kind", "--name", "--output", "--vvvv-dir", "--pipeline"], missing);
    }

    [Fact]
    public async Task Flags_resolve_slots_and_destination_defaults_from_name()
    {
        using var env = new FakeEnvironment(withCompiler: false);
        var console = new TestConsole();
        var model = new BasicsModel();
        var phase = new BasicsPhase(console, prompter: null, env.Build(justFound: false));

        var errors = phase.ApplyFlags(model, "package", "Shapes", null, env.VvvvDir, "just");
        await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(model.Missing());
        Assert.Equal(Path.Combine(env.WorkDir, "Shapes"), model.Destination.Value);
        Assert.Equal(SlotSource.Default, model.Destination.Source);
        Assert.Contains(phase.Warnings, w => w.Contains("vvvvc.exe"));
        Assert.Contains(phase.Warnings, w => w.Contains("just was not found"));
    }

    [Fact]
    public async Task Nonempty_destination_is_a_blocking_error()
    {
        using var env = new FakeEnvironment();
        var dest = Path.Combine(env.WorkDir, "Shapes");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "notes.txt"), "x");
        var phase = new BasicsPhase(new TestConsole(), prompter: null, env.Build());
        var model = new BasicsModel();

        Assert.Empty(phase.ApplyFlags(model, "package", "Shapes", null, env.VvvvDir, "none"));
        var errors = await phase.RunAsync(model, CancellationToken.None);

        var error = Assert.Single(errors);
        Assert.StartsWith(dest, error);
        Assert.Contains("already contains files", error);
        Assert.NotNull(Validators.DestinationPath(dest));
        Assert.Null(Validators.DestinationPath(Path.Combine(env.WorkDir, "Fresh")));
    }

    [Fact]
    public async Task Interactive_nonempty_destination_is_re_asked()
    {
        using var env = new FakeEnvironment();
        var taken = Path.Combine(env.WorkDir, "Taken");
        Directory.CreateDirectory(taken);
        File.WriteAllText(Path.Combine(taken, "notes.txt"), "x");
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);           // kind: Application
        console.Input.PushTextWithEnter("Gallery");
        console.Input.PushTextWithEnter(taken);            // rejected
        console.Input.PushKey(ConsoleKey.Enter);           // destination default
        console.Input.PushKey(ConsoleKey.Enter);           // vvvv
        console.Input.PushKey(ConsoleKey.Enter);           // pipeline: None
        var phase = new BasicsPhase(console, new SpectrePrompter(console), env.Build());
        var model = new BasicsModel();

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Contains("already contains files", console.Output);
        Assert.Equal(Path.Combine(env.WorkDir, "Gallery"), model.Destination.Value);
    }

    [Fact]
    public void Invalid_flags_are_reported_and_leave_slots_unresolved()
    {
        using var env = new FakeEnvironment();
        var phase = new BasicsPhase(new TestConsole(), prompter: null, env.Build());
        var model = new BasicsModel();

        var errors = phase.ApplyFlags(model, "lib", "bad:name", null, Path.Combine(env.Root, "nope"), "make");

        Assert.Equal(4, errors.Count);
        Assert.False(model.Kind.IsResolved);
        Assert.False(model.Name.IsResolved);
        Assert.False(model.Vvvv.IsResolved);
        Assert.False(model.Pipeline.IsResolved);
    }
}
