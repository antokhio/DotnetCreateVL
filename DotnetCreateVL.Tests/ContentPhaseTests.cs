using DotnetCreateVL.Flow;
using DotnetCreateVL.Model;
using Spectre.Console.Testing;

namespace DotnetCreateVL.Tests;

public sealed class ContentPhaseTests
{
    private sealed record Context(BasicsModel Basics, PackagesModel Packages, ProjectsModel Projects, CSharpModel CSharp, ContentModel Content);

    private static Context Models(FakeEnvironment env, RepositoryKind kind = RepositoryKind.Application, bool withProject = false,
        string[]? packageIds = null, NuGetSource[]? sources = null)
    {
        var b = new BasicsModel();
        b.Kind.Set(kind, SlotSource.Flag);
        b.Name.Set("Gallery", SlotSource.Flag);
        b.Destination.Set(Path.Combine(env.WorkDir, "Gallery"), SlotSource.Default);
        b.Vvvv.Set(env.Install, SlotSource.Flag);
        b.Pipeline.Set(PipelineBackend.None, SlotSource.Flag);

        var pk = new PackagesModel();
        foreach (var id in packageIds ?? [])
        {
            var pkg = new PackageModel(id);
            pkg.Root.Set(id, SlotSource.Flag);
            pk.Packages.Add(pkg);
        }

        var ps = new ProjectsModel();
        if (withProject) ps.Projects.Add(new ProjectModel("Gallery.Core", ProjectRole.ApplicationLibrary));

        var c = new CSharpModel();
        if (withProject) c.Sources.Set(sources ?? [NuGetSource.VvvvPublicFeed], SlotSource.Default);

        return new Context(b, pk, ps, c, new ContentModel());
    }

    private static ContentPhase Create(TestConsole console, Context c, IPrompter? prompter, string? gitName = "Ada Lovelace") =>
        new(console, prompter, c.Basics, c.Packages, c.Projects, c.CSharp, _ => Task.FromResult(gitName));

    [Fact]
    public async Task Application_without_projects_offers_two_files_and_enter_accepts_defaults()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushKey(ConsoleKey.Enter);       // repository files: defaults
        console.Input.PushKey(ConsoleKey.Enter);       // optional folders: assets preselected

        var c = Models(env);
        var phase = Create(console, c, new SpectrePrompter(console));
        Assert.Empty(phase.ApplyFlags(c.Content, null, null, null, null));

        var errors = await phase.RunAsync(c.Content, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(c.Content.Missing());
        Assert.Equal(new HashSet<RepoFile> { RepoFile.GitIgnore, RepoFile.GitAttributes }, c.Content.RepoFiles.Value);
        Assert.Equal(SlotSource.Prompt, c.Content.RepoFiles.Source);
        Assert.Equal(["assets"], c.Content.Folders.Value!.Select(f => f.Path));
        Assert.Contains("[7/9] Content", console.Output);
        Assert.Contains("Repository files:", console.Output);
        Assert.Contains("Optional folders:", console.Output);
        foreach (var label in new[] { "assets\\", "rnd\\", "vl\\", "resources\\", "sandbox\\", "docs\\" })
            Assert.Contains(label, console.Output);
        Assert.DoesNotContain("NuGet.config", console.Output);
        Assert.DoesNotContain("LICENSE", console.Output);
        Assert.DoesNotContain("License:", console.Output);
        Assert.Equal("Content: .gitignore, .gitattributes · assets\\", phase.Summary(c.Content));
    }

    [Fact]
    public async Task Projects_add_nuget_config_and_csharpier_and_folders_can_be_picked()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushKey(ConsoleKey.Enter);       // repository files: defaults (incl. NuGet.config)
        console.Input.PushKey(ConsoleKey.DownArrow);   // folders: assets (ticked), rnd
        console.Input.PushKey(ConsoleKey.Spacebar);    // tick rnd
        for (var i = 0; i < 6; i++) console.Input.PushKey(ConsoleKey.DownArrow);   // → VL.Shapes\assets (8th item)
        console.Input.PushKey(ConsoleKey.Spacebar);    // tick it; VL.Shapes\help stays ticked
        console.Input.PushKey(ConsoleKey.Enter);

        var c = Models(env, withProject: true, packageIds: ["VL.Shapes"]);
        var phase = Create(console, c, new SpectrePrompter(console));
        var errors = await phase.RunAsync(c.Content, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal(new HashSet<RepoFile> { RepoFile.GitIgnore, RepoFile.GitAttributes, RepoFile.NuGetConfig }, c.Content.RepoFiles.Value);
        Assert.Equal(["assets", "rnd", "VL.Shapes\\help", "VL.Shapes\\assets"], c.Content.Folders.Value!.Select(f => f.Path));
        Assert.Equal(["assets", "rnd", "help:VL.Shapes", "assets:VL.Shapes"], c.Content.Folders.Value!.Select(f => f.Key));
        Assert.Contains("CSharpier (.csharpierrc + .config/dotnet-tools.json)", console.Output);
        Assert.Contains("VL.Shapes\\help\\", console.Output);
        Assert.Equal("Content: .gitignore, .gitattributes, NuGet.config · assets\\, rnd\\, VL.Shapes\\help\\, VL.Shapes\\assets\\", phase.Summary(c.Content));
    }

    [Fact]
    public async Task Package_repository_asks_license_and_holder_only_when_ticked()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushKey(ConsoleKey.DownArrow);   // files: move to LICENSE (3rd item: .gitignore, .gitattributes, LICENSE)
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Spacebar);
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.DownArrow);   // license: Apache-2.0
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);       // holder: Ada Lovelace (git)
        console.Input.PushKey(ConsoleKey.Enter);       // folders: VL.Shapes\help preselected

        var c = Models(env, RepositoryKind.Package, packageIds: ["VL.Shapes"]);
        var phase = Create(console, c, new SpectrePrompter(console));
        var errors = await phase.RunAsync(c.Content, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal(new LicenseChoice("Apache-2.0", "Ada Lovelace"), c.Content.License.Value);
        Assert.Equal(SlotSource.Prompt, c.Content.License.Source);
        Assert.Contains("Copyright holder:", console.Output);
        Assert.Contains("(Ada Lovelace)", console.Output);
        Assert.DoesNotContain("rnd\\", console.Output);
        Assert.Equal("Content: .gitignore, .gitattributes, LICENSE (Apache-2.0) · VL.Shapes\\help\\", phase.Summary(c.Content));
    }

    [Fact]
    public async Task Holder_may_be_empty_when_git_has_no_name()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Interactive().Width(200);
        console.Input.PushKey(ConsoleKey.Enter);       // files: defaults (license already included through --license)
        console.Input.PushKey(ConsoleKey.Enter);       // holder: empty
        console.Input.PushKey(ConsoleKey.Enter);       // folders

        var c = Models(env, RepositoryKind.Package, packageIds: ["VL.Shapes"]);
        var phase = Create(console, c, new SpectrePrompter(console), gitName: null);
        Assert.Empty(phase.ApplyFlags(c.Content, null, "mit", null, null));
        var errors = await phase.RunAsync(c.Content, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Equal(new LicenseChoice("MIT", null), c.Content.License.Value);
        Assert.True(c.Content.Wants(RepoFile.License));
        Assert.DoesNotContain("License:", console.Output);
    }

    [Fact]
    public async Task Noninteractive_defaults_and_git_holder()
    {
        using var env = new FakeEnvironment();
        var console = new TestConsole().Width(200);
        var c = Models(env, RepositoryKind.Package, withProject: true, packageIds: ["VL.Shapes"], sources: [NuGetSource.VvvvPublicFeed, new NuGetSource("local", "D:\\feed")]);
        var phase = Create(console, c, null);
        Assert.Empty(phase.ApplyFlags(c.Content, ["license"], "MIT", null, ["help:VL.Shapes=VL.Shapes/docs", "assets:VL.Shapes"]));

        var errors = await phase.RunAsync(c.Content, CancellationToken.None);

        Assert.Empty(errors);
        Assert.Empty(c.Content.Missing());
        Assert.Equal(new HashSet<RepoFile> { RepoFile.License }, c.Content.RepoFiles.Value);
        Assert.Equal(new LicenseChoice("MIT", "Ada Lovelace"), c.Content.License.Value);
        Assert.Equal(SlotSource.Flag, c.Content.License.Source);
        Assert.Equal(["VL.Shapes\\docs", "VL.Shapes\\assets"], c.Content.Folders.Value!.Select(f => f.Path));
        Assert.Contains(phase.Warnings, w => w.Contains("NuGet.config is not generated"));
        Assert.DoesNotContain("Repository files:", console.Output);
    }

    [Fact]
    public async Task Noninteractive_license_ticked_without_spdx_is_missing()
    {
        using var env = new FakeEnvironment();
        var c = Models(env, RepositoryKind.Package, packageIds: ["VL.Shapes"]);
        var phase = Create(new TestConsole(), c, null);
        Assert.Empty(phase.ApplyFlags(c.Content, ["gitignore", "license"], null, null, null));

        await phase.RunAsync(c.Content, CancellationToken.None);

        var missing = Assert.Single(c.Content.Missing());
        Assert.Equal("content.license", missing.Id);
    }

    [Fact]
    public async Task Default_sources_do_not_warn_when_nuget_config_is_dropped()
    {
        using var env = new FakeEnvironment();
        var c = Models(env, withProject: true);
        var phase = Create(new TestConsole(), c, null);
        Assert.Empty(phase.ApplyFlags(c.Content, ["none"], null, null, ["assets=media"]));

        await phase.RunAsync(c.Content, CancellationToken.None);

        Assert.Empty(phase.Warnings);
        Assert.Empty(c.Content.RepoFiles.Value!);
        Assert.Equal("media", Assert.Single(c.Content.Folders.Value!).Path);
    }

    [Theory]
    [InlineData("nuget-config", false, RepositoryKind.Application, "applies only when the repository has C# projects")]
    [InlineData("license", false, RepositoryKind.Application, "package repositories only")]
    [InlineData("editorconfig", false, RepositoryKind.Application, "expected gitignore")]
    public void Bad_repo_file_flags_are_reported(string item, bool withProject, RepositoryKind kind, string message)
    {
        using var env = new FakeEnvironment();
        var c = Models(env, kind, withProject);
        var errors = Create(new TestConsole(), c, null).ApplyFlags(c.Content, [item], null, null, null);
        Assert.Contains(message, Assert.Single(errors));
    }

    [Fact]
    public void Bad_license_and_folder_flags_are_reported()
    {
        using var env = new FakeEnvironment();
        var c = Models(env, RepositoryKind.Package, packageIds: ["VL.Shapes"]);
        var phase = Create(new TestConsole(), c, null);

        var errors = phase.ApplyFlags(c.Content, ["gitignore"], "GPL-2.0", "bad\u0007", ["assets", "help:Nope", "playground", "help:VL.Shapes=..\\x", "assets:VL.Shapes", "assets:VL.Shapes"]);

        Assert.Equal(7, errors.Count);
        Assert.Contains(errors, e => e.Contains("--license 'GPL-2.0'"));
        Assert.Contains(errors, e => e.Contains("--license-holder"));
        Assert.Contains(errors, e => e.Contains("--folders 'assets': expected help:VL.Shapes, assets:VL.Shapes or none"));
        Assert.Contains(errors, e => e.Contains("no package named 'Nope'"));
        Assert.Contains(errors, e => e.Contains("--folders 'playground'"));
        Assert.Contains(errors, e => e.Contains("must not leave the repository"));
        Assert.Contains(errors, e => e.Contains("listed twice"));

        var mismatch = phase.ApplyFlags(new ContentModel(), ["gitignore"], "MIT", null, null);
        Assert.Contains("--license needs license in --repo-files", Assert.Single(mismatch));
    }

    [Fact]
    public async Task Folder_on_an_existing_file_and_shared_paths_are_blocking()
    {
        using var env = new FakeEnvironment();
        var c = Models(env, packageIds: ["VL.Shapes"]);
        Directory.CreateDirectory(c.Basics.Destination.Value!);
        File.WriteAllText(Path.Combine(c.Basics.Destination.Value!, "assets"), "");
        var phase = Create(new TestConsole(), c, null);
        Assert.Empty(phase.ApplyFlags(c.Content, null, null, null, ["assets", "rnd=shared", "help:VL.Shapes=shared"]));

        var errors = await phase.RunAsync(c.Content, CancellationToken.None);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("assets exists as a file"));
        Assert.Contains(errors, e => e.Contains("rnd and help:VL.Shapes share the path shared\\"));
    }
}
