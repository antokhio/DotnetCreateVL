using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class ProjectsPhaseTests
{
    private sealed record Context(ApplicationModel App, PackagesModel Packages, CSharpModel CSharp, ProjectsModel Projects);

    private static Context Models(string? appName = "Gallery", string tfm = "net8.0", params (string Name, ProjectRole Role, string? Owner)[] projects)
    {
        var a = new ApplicationModel();
        if (appName is not null)
        {
            a.Name.Set(appName, SlotSource.Default);
            a.EntryPath.Set($"{appName}.vl", SlotSource.Default);
        }

        var pk = new PackagesModel();
        foreach (var owner in projects.Select(p => p.Owner).Where(o => o is not null).Distinct())
        {
            var pkg = new PackageModel(owner!);
            pkg.Root.Set(owner!, SlotSource.Flag);
            pkg.Wrapper.Set(true, SlotSource.Flag);
            pk.Packages.Add(pkg);
        }

        var c = new CSharpModel();
        c.Tfm.Set(tfm, SlotSource.Default);

        var ps = new ProjectsModel();
        foreach (var (name, role, owner) in projects)
        {
            var p = new ProjectModel(name, role, owner);
            p.ProjectPath.Set($"{name}\\{name}.csproj", SlotSource.Default);
            p.OutputBase.Set("lib", SlotSource.Default);
            ps.Projects.Add(p);
        }
        return new Context(a, pk, c, ps);
    }

    private static ProjectsPhase Create(TestConsole console, Context c, IPrompter? prompter) =>
        new(console, prompter, c.App, c.Packages, c.CSharp);

    private static readonly (string, ProjectRole, string?) Core = ("Gallery.Core", ProjectRole.ApplicationLibrary, null);
    private static readonly (string, ProjectRole, string?) Shapes = ("VL.Shapes", ProjectRole.PackageWrapper, "VL.Shapes");
    private static readonly (string, ProjectRole, string?) Util = ("Gallery.Util", ProjectRole.HelperLibrary, null);

    [Fact]
    public async Task Role_defaults_fill_the_table_and_one_confirm_accepts_it()
    {
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushKey(ConsoleKey.Enter);       // use these project settings? Yes

        var c = Models(projects: [Core, Shapes, Util]);
        var phase = Create(console, c, new SpectrePrompter(console));
        Assert.Empty(phase.ApplyFlags(c.Projects, null, null, null, null, null));

        var errors = await phase.RunAsync(c.Projects, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(c.Projects.MissingReview());
        Assert.Equal(VlBinding.Direct, c.Projects.Find("Gallery.Core")!.Binding.Value);
        Assert.Equal(VlBinding.Assembly, c.Projects.Find("VL.Shapes")!.Binding.Value);
        Assert.Equal(VlBinding.None, c.Projects.Find("Gallery.Util")!.Binding.Value);
        Assert.All(c.Projects.Projects, p => Assert.Equal("net8.0", p.Tfm.Value));
        Assert.All(c.Projects.Projects, p => Assert.Equal(SlotSource.Default, p.Binding.Source));
        Assert.Contains("[6/9] Projects", console.Output);
        Assert.Contains("Use these project settings?", console.Output);
        Assert.DoesNotContain("Edit project:", console.Output);
        Assert.Equal("Projects: Gallery.Core (direct), VL.Shapes (assembly), Gallery.Util (none)", phase.Summary(c.Projects));
    }

    [Fact]
    public async Task Noninteractive_uses_defaults_and_asks_nothing()
    {
        var console = new TestConsole().Width(200);
        var c = Models(projects: [Core]);
        var phase = Create(console, c, null);

        var errors = await phase.RunAsync(c.Projects, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(c.Projects.MissingReview());
        Assert.DoesNotContain("Use these project settings?", console.Output);
        Assert.Contains("direct (.csproj)", console.Output);
    }

    [Fact]
    public async Task Declining_edits_the_chosen_project_and_asks_again()
    {
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("n");          // use these project settings? No
        console.Input.PushKey(ConsoleKey.Enter);       // edit project: Gallery.Core (first)
        console.Input.PushKey(ConsoleKey.Spacebar);    // change what? VL binding
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);    // + Local references
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);    // + NuGet packages
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);   // binding: assembly
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Spacebar);    // references: VL.Shapes
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushTextWithEnter("MessagePack@2.5.124, Newtonsoft.Json@13.0.3");
        console.Input.PushKey(ConsoleKey.Enter);       // use these project settings? Yes

        var c = Models(projects: [Core, Shapes]);
        var phase = Create(console, c, new SpectrePrompter(console));
        var errors = await phase.RunAsync(c.Projects, CancellationToken.None);

        Assert.Empty(errors);
        var core = c.Projects.Find("Gallery.Core")!;
        Assert.Equal(VlBinding.Assembly, core.Binding.Value);
        Assert.Equal(SlotSource.Prompt, core.Binding.Source);
        Assert.Equal(["VL.Shapes"], core.References.Value);
        Assert.Equal([new PackageReference("MessagePack", "2.5.124"), new PackageReference("Newtonsoft.Json", "13.0.3")], core.Packages.Value);
        Assert.Contains("Edit project:", console.Output);
        Assert.Contains("Change what for Gallery.Core?", console.Output);
        Assert.Contains("NuGet packages for Gallery.Core (e.g. MessagePack@2.5.124, or none):", console.Output);
        Assert.Equal(2, console.Output.Split("Use these project settings?").Length - 1);
    }

    [Fact]
    public async Task Single_project_skips_the_project_picker_and_windows_feature_adjusts_the_framework()
    {
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushTextWithEnter("n");          // use these project settings? No
        console.Input.PushKey(ConsoleKey.DownArrow);   // change what? Framework & features
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);   // features: WinForms
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);       // target framework: accept net8.0-windows
        console.Input.PushKey(ConsoleKey.Enter);       // use these project settings? Yes

        var c = Models(projects: [Core]);
        var phase = Create(console, c, new SpectrePrompter(console));
        var errors = await phase.RunAsync(c.Projects, CancellationToken.None);

        Assert.Empty(errors);
        var core = c.Projects.Projects[0];
        Assert.Equal(new HashSet<ProjectFeature> { ProjectFeature.WinForms }, core.Features.Value);
        Assert.Equal("net8.0-windows", core.Tfm.Value);
        Assert.DoesNotContain("Edit project:", console.Output);
        Assert.Contains("Target framework for Gallery.Core:", console.Output);
        Assert.Contains("net8.0-windows + WinForms", console.Output);
    }

    [Fact]
    public void Flags_set_every_slot_with_flag_source()
    {
        var console = new TestConsole().Width(200);
        var c = Models(projects: [Core, Shapes]);
        var phase = Create(console, c, null);

        var errors = phase.ApplyFlags(c.Projects,
            ["Gallery.Core=assembly"],
            ["Gallery.Core=net9.0"],
            ["Gallery.Core=windows", "Gallery.Core=stride"],
            ["Gallery.Core=VL.Shapes", "Gallery.Core=vl.shapes"],
            ["VL.Shapes=MessagePack@2.5.124"]);

        Assert.Empty(errors);
        var core = c.Projects.Find("Gallery.Core")!;
        Assert.Equal(VlBinding.Assembly, core.Binding.Value);
        Assert.Equal(SlotSource.Flag, core.Binding.Source);
        Assert.Equal("net9.0", core.Tfm.Value);
        Assert.Equal(new HashSet<ProjectFeature> { ProjectFeature.Windows, ProjectFeature.Stride }, core.Features.Value);
        Assert.Equal(["VL.Shapes"], core.References.Value);
        Assert.Equal([new PackageReference("MessagePack", "2.5.124")], c.Projects.Find("VL.Shapes")!.Packages.Value);
    }

    [Theory]
    [InlineData("Gallery.Core", "must have the form")]
    [InlineData("Nope=direct", "no such project")]
    [InlineData("Gallery.Core=static", "expected direct, assembly or none")]
    public void Bad_binding_flags_are_reported(string flag, string message)
    {
        var c = Models(projects: [Core]);
        var errors = Create(new TestConsole(), c, null).ApplyFlags(c.Projects, [flag], null, null, null, null);
        Assert.Single(errors);
        Assert.Contains(message, errors[0]);
    }

    [Fact]
    public void Bad_reference_and_package_flags_are_reported()
    {
        var c = Models(projects: [Core, Shapes]);
        var errors = Create(new TestConsole(), c, null).ApplyFlags(c.Projects, null, ["Gallery.Core=net8"], ["Gallery.Core=linux"],
            ["Gallery.Core=Gallery.Core", "Gallery.Core=Missing"], ["Gallery.Core=MessagePack", "Gallery.Core=A@1.0", "Gallery.Core=a@2.0"]);

        Assert.Equal(6, errors.Count);
        Assert.Contains(errors, e => e.Contains("--project-tfm 'Gallery.Core=net8'"));
        Assert.Contains(errors, e => e.Contains("--feature 'Gallery.Core=linux'"));
        Assert.Contains(errors, e => e.Contains("cannot reference itself"));
        Assert.Contains(errors, e => e.Contains("no project named 'Missing'"));
        Assert.Contains(errors, e => e.Contains("--package-ref 'Gallery.Core=MessagePack'"));
        Assert.Contains(errors, e => e.Contains("already referenced"));
    }

    [Fact]
    public async Task Direct_binding_on_a_document_name_is_blocking()
    {
        var console = new TestConsole().Width(200);
        var c = Models(projects: [Shapes, ("Gallery", ProjectRole.ApplicationLibrary, null)]);
        var phase = Create(console, c, null);
        Assert.Empty(phase.ApplyFlags(c.Projects, ["VL.Shapes=direct"], null, null, null, null));

        var errors = await phase.RunAsync(c.Projects, CancellationToken.None);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("VL.Shapes: a direct .csproj binding"));
        Assert.Contains(errors, e => e.StartsWith("Gallery: a direct .csproj binding"));
    }

    [Fact]
    public async Task Reference_cycles_and_platform_mismatches_are_blocking()
    {
        var console = new TestConsole().Width(200);
        var c = Models(projects: [Core, Util]);
        var phase = Create(console, c, null);
        Assert.Empty(phase.ApplyFlags(c.Projects, null, ["Gallery.Util=net8.0"], ["Gallery.Util=winforms"],
            ["Gallery.Core=Gallery.Util", "Gallery.Util=Gallery.Core"], null));

        var errors = await phase.RunAsync(c.Projects, CancellationToken.None);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("Reference cycle: Gallery.Core → Gallery.Util → Gallery.Core"));
        Assert.Contains(errors, e => e.Contains("Gallery.Util: the WinForms feature needs a -windows target framework (for example net8.0-windows), not net8.0"));
    }

    [Fact]
    public async Task Feature_flag_alone_derives_a_windows_framework()
    {
        var console = new TestConsole().Width(200);
        var c = Models(projects: [Core]);
        var phase = Create(console, c, null);
        Assert.Empty(phase.ApplyFlags(c.Projects, null, null, ["Gallery.Core=windows"], null, null));

        var errors = await phase.RunAsync(c.Projects, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal("net8.0-windows", c.Projects.Projects[0].Tfm.Value);
        Assert.Equal(SlotSource.Default, c.Projects.Projects[0].Tfm.Source);
    }
}
