using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class LibrariesPhaseTests
{
    private sealed record Context(BasicsModel Basics, ApplicationModel App, PackagesModel Packages);

    private static Context Models(FakeEnvironment env, RepositoryKind kind = RepositoryKind.Application, string name = "Gallery", params (string Id, string Root, bool Wrapper)[] packages)
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

        var p = new PackagesModel();
        foreach (var (id, root, wrapper) in packages)
        {
            var pkg = new PackageModel(id);
            pkg.Root.Set(root, SlotSource.Flag);
            pkg.Wrapper.Set(wrapper, SlotSource.Flag);
            p.Packages.Add(pkg);
        }
        return new Context(b, a, p);
    }

    private static LibrariesPhase Create(TestConsole console, Context c, IPrompter? prompter) =>
        new(console, prompter, c.Basics, c.App, c.Packages);

    [Fact]
    public async Task No_projects_when_declined_and_no_wrappers()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive();
        console.Input.PushKey(ConsoleKey.Enter);       // library? No

        var phase = Create(console, Models(env), new SpectrePrompter(console));
        var model = new ProjectsModel();
        Assert.Empty(phase.ApplyFlags(model, null, null, null));

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(model.Projects);
        Assert.Contains("Gallery.Core", console.Output);           // example in the question
        Assert.Contains("will be skipped", console.Output);
        Assert.Equal("C# projects: none", phase.Summary(model));
    }

    [Fact]
    public async Task Single_application_library_uses_root_src_and_lib_and_layout_is_confirmed()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("y");          // library? yes
        console.Input.PushKey(ConsoleKey.Enter);       // name: Gallery.Core (default)
        console.Input.PushKey(ConsoleKey.Enter);       // another? No
        console.Input.PushKey(ConsoleKey.Enter);       // use this layout? Yes

        var phase = Create(console, Models(env), new SpectrePrompter(console));
        var model = new ProjectsModel();
        Assert.Empty(phase.ApplyFlags(model, null, null, null));

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        var lib = Assert.Single(model.Projects);
        Assert.Equal("Gallery.Core", lib.Name);
        Assert.Equal(ProjectRole.ApplicationLibrary, lib.Role);
        Assert.Equal("src\\Gallery.Core.csproj", lib.ProjectPath.Value);
        Assert.Equal("lib", lib.OutputBase.Value);
        Assert.Equal(SlotSource.Default, lib.ProjectPath.Source);
        Assert.Contains("application library", console.Output);
    }

    [Fact]
    public async Task Library_name_must_differ_from_documents_and_customized_layout_is_recorded()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("y");
        console.Input.PushTextWithEnter("Gallery");            // rejected: application document name
        console.Input.PushTextWithEnter("VL.Shapes");          // rejected: package document name
        console.Input.PushTextWithEnter("Gallery.Core");
        console.Input.PushTextWithEnter("y");                  // another
        console.Input.PushTextWithEnter("gallery.core");       // rejected: duplicate
        console.Input.PushTextWithEnter("Gallery.Tools");
        console.Input.PushTextWithEnter("n");
        console.Input.PushTextWithEnter("n");                  // use this layout? No
        console.Input.PushKey(ConsoleKey.Enter);                    // VL.Shapes project default
        console.Input.PushKey(ConsoleKey.Enter);                    // output default
        console.Input.PushTextWithEnter("libs/Core/Core.csproj");   // Gallery.Core project file
        console.Input.PushKey(ConsoleKey.Enter);                    // output default
        console.Input.PushKey(ConsoleKey.Enter);                    // Gallery.Tools project default
        console.Input.PushTextWithEnter("out/tools");               // output

        var ctx = Models(env, packages: ("VL.Shapes", "VL.Shapes", true));
        var phase = Create(console, ctx, new SpectrePrompter(console));
        var model = new ProjectsModel();
        Assert.Empty(phase.ApplyFlags(model, null, null, null));

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal(["VL.Shapes", "Gallery.Core", "Gallery.Tools"], model.Projects.Select(p => p.Name));
        Assert.Contains("already the name of a VL document", console.Output);
        Assert.Contains("already exists", console.Output);

        var wrapper = model.Projects[0];
        Assert.Equal(ProjectRole.PackageWrapper, wrapper.Role);
        Assert.Equal("VL.Shapes\\src\\VL.Shapes.csproj", wrapper.ProjectPath.Value);
        Assert.Equal("VL.Shapes\\lib", wrapper.OutputBase.Value);

        var core = model.Projects[1];
        Assert.Equal("libs\\Core\\Core.csproj", core.ProjectPath.Value);
        Assert.Equal(SlotSource.Prompt, core.ProjectPath.Source);
        Assert.Equal("Gallery.Core\\lib", core.OutputBase.Value);     // multi-library default
        Assert.Equal(SlotSource.Default, core.OutputBase.Source);

        var tools = model.Projects[2];
        Assert.Equal("Gallery.Tools\\src\\Gallery.Tools.csproj", tools.ProjectPath.Value);
        Assert.Equal("out\\tools", tools.OutputBase.Value);
        Assert.Equal(SlotSource.Prompt, tools.OutputBase.Source);
    }

    [Fact]
    public async Task Package_repository_registers_wrappers_without_prompting_for_them()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushKey(ConsoleKey.Enter);       // shared library? No
        console.Input.PushKey(ConsoleKey.Enter);       // use layout? Yes

        var ctx = Models(env, RepositoryKind.Package, "Shapes", ("VL.Shapes", "VL.Shapes", true), ("VL.Colors", "repos\\Colors", false));
        var phase = Create(console, ctx, new SpectrePrompter(console));
        var model = new ProjectsModel();
        Assert.Empty(phase.ApplyFlags(model, null, null, null));

        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(errors);
        var wrapper = Assert.Single(model.Projects);
        Assert.Equal("VL.Shapes", wrapper.Name);
        Assert.Contains("Shapes.Common", console.Output);
        Assert.Equal("C# projects: VL.Shapes → VL.Shapes\\src\\VL.Shapes.csproj", phase.Summary(model));
    }

    [Fact]
    public async Task Noninteractive_flags_define_libraries_and_overrides()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole();
        var ctx = Models(env, RepositoryKind.Package, "Shapes", ("VL.Shapes", "VL.Shapes", true));
        var phase = Create(console, ctx, prompter: null);
        var model = new ProjectsModel();

        var flagErrors = phase.ApplyFlags(model, ["Shapes.Common"], ["vl.shapes=VL.Shapes/csharp/VL.Shapes.csproj"], ["Shapes.Common=build/common"]);
        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Empty(flagErrors);
        Assert.Empty(errors);
        Assert.Empty(model.Missing());
        var wrapper = model.Projects[0];
        Assert.Equal("VL.Shapes\\csharp\\VL.Shapes.csproj", wrapper.ProjectPath.Value);
        Assert.Equal(SlotSource.Flag, wrapper.ProjectPath.Source);
        Assert.Equal("VL.Shapes\\lib", wrapper.OutputBase.Value);
        var common = model.Projects[1];
        Assert.Equal(ProjectRole.HelperLibrary, common.Role);
        Assert.Equal("Shapes.Common\\src\\Shapes.Common.csproj", common.ProjectPath.Value);
        Assert.Equal("build\\common", common.OutputBase.Value);
    }

    [Fact]
    public void Flag_errors_cover_names_unknown_projects_and_bad_paths()
    {
        using var env = new FakeEnvironment();
        var phase = Create(new TestConsole(), Models(env), prompter: null);
        var model = new ProjectsModel();

        var flagErrors = phase.ApplyFlags(model,
            ["Gallery", "1Core", "Gallery.Core", "Gallery-Core"],
            ["Gallery.Core=src/Core.txt", "Nope=x.csproj", "noequals"],
            ["Gallery.Core=C:\\abs"]);

        Assert.Equal(["Gallery.Core"], model.Projects.Select(p => p.Name));
        Assert.Contains(flagErrors, e => e.Contains("'Gallery'") && e.Contains("VL document"));
        Assert.Contains(flagErrors, e => e.Contains("'1Core'") && e.Contains("dot-separated"));
        Assert.Contains(flagErrors, e => e.Contains("'Gallery-Core'") && e.Contains("dot-separated"));
        Assert.Contains(flagErrors, e => e.Contains("end with .csproj"));
        Assert.Contains(flagErrors, e => e.Contains("'Nope=x.csproj'") && e.Contains("no such project"));
        Assert.Contains(flagErrors, e => e.Contains("'noequals'") && e.Contains("form"));
        Assert.Contains(flagErrors, e => e.StartsWith("--output-base") && e.Contains("relative"));
    }

    [Fact]
    public async Task Conflicting_paths_are_blocking()
    {
        using var env = new FakeEnvironment();
        var phase = Create(new TestConsole(), Models(env), prompter: null);
        var model = new ProjectsModel();

        Assert.Empty(phase.ApplyFlags(model, ["Gallery.Core", "Gallery.Tools"], ["Gallery.Tools=Gallery.Core/src/Gallery.Core.csproj"], ["Gallery.Tools=Gallery.Core/lib"]));
        var errors = await phase.RunAsync(model, CancellationToken.None);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("same project file"));
        Assert.Contains(errors, e => e.Contains("same output folder"));
    }
}
