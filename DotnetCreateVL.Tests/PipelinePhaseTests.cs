using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class PipelinePhaseTests
{
    private sealed record Context(BasicsModel Basics, ApplicationModel App, PackagesModel Packages, ProjectsModel Projects, CSharpModel CSharp, ContentModel Content, PipelineModel Pipeline);

    private static Context Models(FakeEnvironment env, RepositoryKind kind = RepositoryKind.Application, PipelineBackend backend = PipelineBackend.Just,
        (string Id, string Root, bool Wrapper)[]? packages = null, string[]? libs = null, (string From, string To)[]? refs = null, bool assets = false)
    {
        var b = new BasicsModel();
        b.Kind.Set(kind, SlotSource.Flag);
        b.Name.Set("Gallery", SlotSource.Flag);
        b.Destination.Set(Path.Combine(env.WorkDir, "Gallery"), SlotSource.Default);
        b.Vvvv.Set(env.Install, SlotSource.Flag);
        b.Pipeline.Set(backend, SlotSource.Flag);

        var a = new ApplicationModel();
        if (kind == RepositoryKind.Application)
        {
            a.Name.Set("Gallery", SlotSource.Default);
            a.EntryPath.Set("Gallery.vl", SlotSource.Default);
        }

        var pk = new PackagesModel();
        var ps = new ProjectsModel();
        foreach (var (id, root, wrapper) in packages ?? [])
        {
            var pkg = new PackageModel(id);
            pkg.Root.Set(root, SlotSource.Flag);
            pkg.Wrapper.Set(wrapper, SlotSource.Flag);
            pk.Packages.Add(pkg);
            if (wrapper) ps.Projects.Add(new ProjectModel(id, ProjectRole.PackageWrapper, id));
        }
        foreach (var lib in libs ?? [])
            ps.Projects.Add(new ProjectModel(lib, ProjectRole.ApplicationLibrary));
        foreach (var p in ps.Projects)
            p.References.Set((refs ?? []).Where(r => r.From == p.Name).Select(r => r.To).ToList(), SlotSource.Default);

        var c = new CSharpModel();
        if (ps.Projects.Count > 0)
        {
            c.Dotnet.Set(new DotnetSelection("dotnet", "10.0.100", "x64"), SlotSource.Default);
            c.Configuration.Set("Debug", SlotSource.Default);
        }

        var content = new ContentModel();
        content.Folders.Set(assets ? [new ContentFolder(FolderKind.Assets, null, "assets")] : [], SlotSource.Default);

        return new Context(b, a, pk, ps, c, content, new PipelineModel());
    }

    private static PipelinePhase Create(TestConsole console, Context c, IPrompter? prompter)
    {
        var phase = new PipelinePhase(console, prompter, c.Basics, c.App, c.Packages, c.Projects, c.CSharp, c.Content);
        phase.DeriveTargets(c.Pipeline);
        return phase;
    }

    private static IReadOnlyList<string> NoFlags(PipelinePhase phase, PipelineModel m) =>
        phase.ApplyFlags(m, null, null, null, null, null, null, null, null, null, null);

    [Fact]
    public async Task Application_with_library_and_package_derives_two_targets_and_two_confirms_accept()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(220);
        console.Input.PushTextWithEnter("y");          // shared nuget folder? yes
        console.Input.PushTextWithEnter("y");          // export sources folder? yes
        console.Input.PushKey(ConsoleKey.Enter);       // use these pipeline settings? Yes
        console.Input.PushKey(ConsoleKey.Enter);       // use these targets? Yes

        var c = Models(env, packages: [("VL.Shapes", "VL.Shapes", true)], libs: ["Gallery.Core"], refs: [("Gallery.Core", "VL.Shapes")], assets: true);
        var phase = Create(console, c, new SpectrePrompter(console));
        Assert.Empty(NoFlags(phase, c.Pipeline));

        var errors = await phase.RunAsync(c.Pipeline, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(c.Pipeline.Missing());
        Assert.Contains("Use a shared nuget folder outside the repository (..\\__nugets)?", console.Output);
        Assert.Contains("Export package sources outside the repository (..\\__sources)?", console.Output);
        Assert.Equal("..\\__nugets", c.Pipeline.NugetPath.Value);
        Assert.Equal("..\\__sources", c.Pipeline.ExportSources.Value);
        Assert.Equal([""], c.Pipeline.PackageRepositories.Value);
        Assert.Equal(["VL.Shapes"], c.Pipeline.Editable.Value);

        var gallery = c.Pipeline.Find("Gallery")!;
        Assert.True(gallery.Dev.Value);
        Assert.Equal(BuildOperation.Export, gallery.Build.Value);
        Assert.Equal(["VL.Shapes", "Gallery.Core"], gallery.Prerequisites.Value);     // dependency order
        Assert.Equal("bin", gallery.ExportRoot.Value);
        Assert.Equal("bin\\Gallery", gallery.AppFolder);
        Assert.Equal([new StagingRule("assets", "assets")], gallery.Staging.Value);

        var shapes = c.Pipeline.Find("Shapes")!;
        Assert.False(shapes.Dev.Value);
        Assert.Equal(BuildOperation.Library, shapes.Build.Value);
        Assert.Equal(["VL.Shapes"], shapes.Prerequisites.Value);
        Assert.Equal("all", c.Pipeline.DefaultTarget.Value);

        Assert.Contains("[8/9] Pipeline (Just)", console.Output);
        Assert.Contains("(repository root)", console.Output);
        Assert.Contains("opens Gallery.vl", console.Output);
        Assert.Contains("bin\\Gallery + assets", console.Output);
        Assert.Empty(phase.Warnings);
        Assert.Equal("Pipeline (Just): Gallery (dev, export), Shapes (libraries) · default all", phase.Summary(c.Pipeline));
    }

    [Fact]
    public async Task Single_target_is_the_default_and_a_declined_settings_table_edits_only_picked_rows()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(220);
        console.Input.PushTextWithEnter("y");          // shared nuget folder? yes
        console.Input.PushKey(ConsoleKey.Enter);       // export sources folder? default No
        console.Input.PushTextWithEnter("n");          // settings? No
        console.Input.PushKey(ConsoleKey.Spacebar);    // nuget path
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);    // editable
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("D:\\nugets"); // nuget path
        console.Input.PushTextWithEnter("VL.My*");     // editable
        console.Input.PushKey(ConsoleKey.Enter);       // settings? Yes
        console.Input.PushKey(ConsoleKey.Enter);       // targets? Yes

        var c = Models(env);
        var phase = Create(console, c, new SpectrePrompter(console));
        var errors = await phase.RunAsync(c.Pipeline, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal("D:\\nugets", c.Pipeline.NugetPath.Value);
        Assert.Equal(SlotSource.Prompt, c.Pipeline.NugetPath.Source);
        Assert.Equal("", c.Pipeline.ExportSources.Value);
        Assert.Equal(SlotSource.Prompt, c.Pipeline.ExportSources.Source);
        Assert.Equal(["VL.My*"], c.Pipeline.Editable.Value);
        Assert.Equal("Gallery", c.Pipeline.DefaultTarget.Value);
        Assert.Empty(c.Pipeline.Find("Gallery")!.Prerequisites.Value!);
        Assert.Empty(phase.Warnings);
        Assert.DoesNotContain("Export package sources (e.g.", console.Output);
        Assert.Equal(2, console.Output.Split("Use these pipeline settings?").Length - 1);
    }

    [Fact]
    public async Task Declined_targets_edit_the_picked_target_groups()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(220);
        console.Input.PushKey(ConsoleKey.Enter);       // shared nuget folder? default No
        console.Input.PushKey(ConsoleKey.Enter);       // export sources folder? default No
        console.Input.PushKey(ConsoleKey.Enter);       // settings? Yes
        console.Input.PushTextWithEnter("n");          // targets? No
        console.Input.PushKey(ConsoleKey.Enter);       // edit target: gallery
        console.Input.PushKey(ConsoleKey.Spacebar);    // dev
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);    // prerequisites
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);    // staging
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("n");          // dev command? No
        console.Input.PushKey(ConsoleKey.Spacebar);    // prerequisites: untick VL.Shapes (first, preselected)
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("media:assets, docs");
        console.Input.PushKey(ConsoleKey.Enter);       // targets? Yes

        var c = Models(env, packages: [("VL.Shapes", "repos\\VL.Shapes", true)], libs: ["Gallery.Core"]);
        var phase = Create(console, c, new SpectrePrompter(console));
        var errors = await phase.RunAsync(c.Pipeline, CancellationToken.None);

        Assert.Empty(errors);
        var gallery = c.Pipeline.Find("Gallery")!;
        Assert.False(gallery.Dev.Value);
        Assert.Equal(SlotSource.Prompt, gallery.Dev.Source);
        Assert.Equal(["Gallery.Core"], gallery.Prerequisites.Value);
        Assert.Equal([new StagingRule("media", "assets"), new StagingRule("docs", "docs")], gallery.Staging.Value);
        Assert.Equal(["repos"], c.Pipeline.PackageRepositories.Value);
        Assert.Equal("", c.Pipeline.NugetPath.Value);
        Assert.Equal("", c.Pipeline.ExportSources.Value);
        Assert.Contains("Edit target:", console.Output);
        Assert.Contains("Dev command for Gallery (opens Gallery.vl)?", console.Output);
        Assert.Equal(2, phase.Warnings.Count);        // media and docs do not exist yet
        Assert.All(phase.Warnings, w => Assert.Contains("does not exist yet", w));
    }

    [Fact]
    public async Task Package_repository_without_wrappers_has_no_targets()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Width(220);
        var c = Models(env, RepositoryKind.Package, PipelineBackend.Cmd, packages: [("VL.Shapes", "VL.Shapes", false)]);
        var phase = Create(console, c, null);

        var errors = await phase.RunAsync(c.Pipeline, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(c.Pipeline.Targets);
        Assert.Empty(c.Pipeline.Missing());
        Assert.Equal("", c.Pipeline.NugetPath.Value);          // shared folders are opt-in
        Assert.Equal("", c.Pipeline.ExportSources.Value);
        Assert.Contains(phase.Warnings, w => w.Contains("No pipeline targets"));
        Assert.DoesNotContain("Use these", console.Output);
        Assert.Equal("Pipeline (Cmd): launch only", phase.Summary(c.Pipeline));
    }

    [Fact]
    public void Flags_set_settings_and_target_slots()
    {
        using var env = new FakeEnvironment();
        var c = Models(env, packages: [("VL.Shapes", "VL.Shapes", true)], libs: ["Gallery.Core"], refs: [("Gallery.Core", "VL.Shapes")]);
        var phase = Create(new TestConsole(), c, null);

        var errors = phase.ApplyFlags(c.Pipeline, "D:\\nugets", "none", ["repos", "none", "pkgs"], ["VL.Shapes", "vl.shapes", "VL.My*"], "Shapes",
            ["Gallery=off"], ["Shapes=none"], ["Gallery=Gallery.Core"], ["Gallery=out/app"], ["Gallery=assets", "Gallery=media:assets2"]);

        Assert.Empty(errors);
        Assert.Equal("D:\\nugets", c.Pipeline.NugetPath.Value);
        Assert.Equal("", c.Pipeline.ExportSources.Value);
        Assert.Equal(["pkgs"], c.Pipeline.PackageRepositories.Value);
        Assert.Equal(["VL.Shapes", "VL.My*"], c.Pipeline.Editable.Value);
        Assert.Equal("Shapes", c.Pipeline.DefaultTarget.Value);
        var gallery = c.Pipeline.Find("Gallery")!;
        Assert.False(gallery.Dev.Value);
        Assert.Equal(["VL.Shapes", "Gallery.Core"], gallery.Prerequisites.Value);    // reference expanded first
        Assert.Equal("out\\app", gallery.ExportRoot.Value);
        Assert.Equal([new StagingRule("assets", "assets"), new StagingRule("media", "assets2")], gallery.Staging.Value);
        Assert.Equal(BuildOperation.None, c.Pipeline.Find("Shapes")!.Build.Value);
    }

    [Fact]
    public void Bad_flags_are_reported()
    {
        using var env = new FakeEnvironment();
        var c = Models(env, packages: [("VL.Shapes", "VL.Shapes", true)]);
        var phase = Create(new TestConsole(), c, null);

        var errors = phase.ApplyFlags(c.Pipeline, "", null, ["..\\outside"], ["*", "VL.Shapes"], "nope",
            ["Gallery=maybe", "nope=on"], ["Gallery=publish"], ["Gallery=Missing"], ["Gallery=C:\\abs"], ["Gallery=assets", "Gallery=media:assets"]);

        Assert.Equal(10, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("--nuget-path ''"));
        Assert.Contains(errors, e => e.Contains("--package-repo '..\\outside'"));
        Assert.Contains(errors, e => e.Contains("--editable '*'"));
        Assert.Contains(errors, e => e.Contains("--default-target 'nope': expected all or one of Gallery, Shapes"));
        Assert.Contains(errors, e => e.Contains("--target-dev 'Gallery=maybe'"));
        Assert.Contains(errors, e => e.Contains("no target named 'nope'"));
        Assert.Contains(errors, e => e.Contains("--target-build 'Gallery=publish'"));
        Assert.Contains(errors, e => e.Contains("no project named 'Missing'"));
        Assert.Contains(errors, e => e.Contains("--export-root 'Gallery=C:\\abs'"));
        Assert.Contains(errors, e => e.Contains("already a staging destination"));
    }

    [Fact]
    public async Task Blocking_issues_cover_export_without_entry_empty_library_builds_and_missing_compiler()
    {
        using var env = new FakeEnvironment(withEditor: true, withCompiler: false);
        var c = Models(env, packages: [("VL.Shapes", "VL.Shapes", true), ("VL.Tools", "VL.Tools", true)]);
        var phase = Create(new TestConsole(), c, null);
        Assert.Empty(phase.ApplyFlags(c.Pipeline, null, null, null, null, null, null, ["Shapes=export", "Tools=lib"], ["Tools=none"], ["Gallery=out", "Shapes=out"], null));

        var errors = await phase.RunAsync(c.Pipeline, CancellationToken.None);

        Assert.Contains(errors, e => e == "Shapes cannot export: it has no entry patch. Use lib or none.");
        Assert.Contains(errors, e => e.StartsWith("Gallery exports with vvvvc.exe"));
        Assert.Contains(errors, e => e.StartsWith("Shapes exports with vvvvc.exe"));
        Assert.Contains(errors, e => e.StartsWith("Tools builds libraries but has no prerequisites"));
        Assert.Equal(4, errors.Count);                 // a shared export root is fine: apps land in <root>\<App>\
        Assert.Equal("out\\Gallery", c.Pipeline.Find("Gallery")!.AppFolder);
        Assert.Null(c.Pipeline.Find("Shapes")!.AppFolder);
    }

    [Theory]
    [InlineData("Gallery", "Gallery")]
    [InlineData("My App 2", "My-App-2")]
    [InlineData("2D", "t-2D")]
    [InlineData("VL.Shapes", "VL-Shapes")]
    public void Target_names_are_backend_safe(string seed, string expected) =>
        Assert.Equal(expected, PipelinePhase.Normalize(seed));

    [Fact]
    public void Package_targets_drop_the_vl_prefix_unless_it_collides()
    {
        using var env = new FakeEnvironment();
        var c = Models(env, RepositoryKind.Package, packages: [("VL.Shapes", "VL.Shapes", true), ("Shapes", "Shapes", true)]);
        Create(new TestConsole(), c, null);

        Assert.Equal(["Shapes", "Shapes-2"], c.Pipeline.Targets.Select(t => t.Name));
    }
}
