using DotnetCreateVL.Generate;
using DotnetCreateVL.Model;

namespace DotnetCreateVL.Tests;

public sealed class PlannerTests
{
    private static Planner Planner(ResolvedModels m) => new(m.Basics, m.App, m.Packages, m.Projects, m.CSharp, m.Content, m.Pipeline, "2025.7.4");

    private static PlannedEntry Entry(GenerationPlan plan, string path) =>
        Assert.Single(plan.Entries, e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    private static ResolvedModels GalleryWithLibraryAndPackage(FakeEnvironment env, PipelineBackend backend = PipelineBackend.Just)
    {
        var m = ResolvedModels.Application(env, backend);
        m.AddPackage("VL.Shapes", wrapper: true);
        m.AddProject("Gallery.Core", ProjectRole.ApplicationLibrary, "src\\Gallery.Core.csproj", "lib", VlBinding.Direct, references: ["VL.Shapes"]);
        m.AddTarget("Gallery", "Gallery.vl", null, dev: true, BuildOperation.Export, ["VL.Shapes", "Gallery.Core"], [new StagingRule("assets", "assets")]);
        m.AddTarget("Shapes", null, "VL.Shapes", dev: false, BuildOperation.Library, ["VL.Shapes"]);
        return m;
    }

    [Fact]
    public void Application_plan_covers_documents_folders_projects_repo_files_pipeline_and_readme()
    {
        using var env = new FakeEnvironment();
        var m = GalleryWithLibraryAndPackage(env);

        var plan = Planner(m).Build();

        var entry = Entry(plan, "Gallery.vl");
        Assert.Equal(PlannedAction.Create, entry.Action);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", entry.Content);
        Assert.Contains("LanguageVersion=\"2025.7.4\"", entry.Content);
        Assert.Contains("Location=\"VL.CoreLib\" Version=\"2025.7.4\"", entry.Content);
        Assert.Contains("<Node Name=\"Application\"", entry.Content);
        var pkgDoc = Entry(plan, "VL.Shapes\\VL.Shapes.vl");
        Assert.Equal(PlannedAction.Create, pkgDoc.Action);
        Assert.Contains("<Node Name=\"Application\"", pkgDoc.Content);
        Assert.NotEqual(entry.Content, pkgDoc.Content);     // fresh ids per document
        Assert.True(Entry(plan, "VL.Shapes").IsFolder);
        Assert.True(Entry(plan, "assets").IsFolder);
        Assert.Equal(PlannedAction.Create, Entry(plan, "assets\\.gitkeep").Action);

        var core = Entry(plan, "src\\Gallery.Core.csproj");
        Assert.Equal(PlannedAction.Create, core.Action);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", core.Content);
        Assert.Contains("<BaseOutputPath>..\\lib\\</BaseOutputPath>", core.Content);
        Assert.Contains("<PackageReference Include=\"VL.Core\" Version=\"$(VLVersion)\" />", core.Content);
        Assert.Contains("<ProjectReference Include=\"..\\VL.Shapes\\src\\VL.Shapes.csproj\" />", core.Content);
        Assert.Contains("[assembly: ImportAsIs]", Entry(plan, "src\\Properties\\AssemblyInfo.cs").Content);
        Assert.Contains("[assembly: ImportAsIs]", Entry(plan, "VL.Shapes\\src\\Properties\\AssemblyInfo.cs").Content);
        Assert.Contains("<VLVersion>2025.7.4</VLVersion>", Entry(plan, "Directory.Packages.props").Content);
        var slnx = Entry(plan, "Gallery.slnx").Content!;
        Assert.Contains("<Project Path=\"VL.Shapes/src/VL.Shapes.csproj\" />", slnx);
        Assert.Contains("<Project Path=\"src/Gallery.Core.csproj\" />", slnx);

        var ignore = Entry(plan, ".gitignore").Content!;
        Assert.Contains("# dotnet create-vl", ignore);
        Assert.Contains("\nbin/", ignore);
        Assert.Contains("\nlib/", ignore);
        Assert.Contains("\nVL.Shapes/lib/", ignore);
        Assert.DoesNotContain("artifacts", ignore);
        Assert.Contains("*.vl diff=html", Entry(plan, ".gitattributes").Content);
        Assert.Contains("vvvv-public-feed", Entry(plan, "NuGet.config").Content);
        Assert.DoesNotContain(plan.Entries, e => e.Path == "LICENSE" || e.Path == ".csharpierrc");

        var root = Entry(plan, ".justfile").Content!;
        Assert.Contains("[private]\nmod _build '.just/build.just'", root.Replace("\r\n", "\n"));
        Assert.Contains("default *args: (_build::all args)", root);
        // `just dev --flag` must reach the default target: a leading known name selects the recipe, anything else falls back.
        Assert.Contains("_dev::{{ if replace_regex(args, '^(\\S*).*$', '$1') =~ '^(Gallery|vvvv)$' { args } else { trim('Gallery ' + args) } }}; exit $LASTEXITCODE", root);
        Assert.Contains("_build::{{ if replace_regex(args, '^(\\S*).*$', '$1') =~ '^(all|Gallery|Shapes)$' { args } else { trim('all ' + args) } }}", root);
        var dev = Entry(plan, ".just\\dev.just").Content!;
        Assert.Contains("vvvv *args: (_launch '' args)", dev);
        Assert.Contains("# Open Gallery.vl (also `just dev`)\nGallery *args: (_launch 'Gallery.vl' args)", dev.Replace("\r\n", "\n"));
        // `just dev` runs the first recipe, so the document comes before the bare launch.
        Assert.True(dev.IndexOf("Gallery *args:") < dev.IndexOf("vvvv *args:"));
        Assert.Contains("editable_packages := 'VL.Shapes'", dev);
        // Quoted values must be single array elements: the comma binds tighter than + in PowerShell.
        Assert.Contains("$null = New-Item -ItemType Directory -Force '{{nuget_path}}'; $a += @('--nuget-path', ('\"' + [IO.Path]::GetFullPath('{{nuget_path}}') + '\"'));", dev);
        Assert.Contains("$a += @('--package-repositories', '\"{{package_repositories}}\"');", dev);
        Assert.Contains("Start-Process -FilePath '{{vvvv_exe}}' -ArgumentList $a -WorkingDirectory '{{root}}'", dev);
        var build = Entry(plan, ".just\\build.just").Content!;
        Assert.Contains("all *args: (Gallery args) (Shapes args)", build);
        Assert.Contains("Gallery *args: (_build 'VL.Shapes/src/VL.Shapes.csproj;src/Gallery.Core.csproj' '') (_export 'Gallery.vl' 'bin' 'Gallery' args)", build);
        Assert.Contains("# Build VL.Shapes, Gallery.Core, then export Gallery.vl to bin\\Gallery\\ and copy assets into it", build);
        Assert.Contains("Copy-Tree '{{root / 'assets'}}' \"$app/assets\"", build);
        Assert.DoesNotContain("$to", build);                                          // no mirror copy
        Assert.Contains("_export patch out app args:", build);
        Assert.Contains("$app = '{{root / out / app}}'; if (Test-Path $app) { Remove-Item $app -Recurse -Force };", build);
        Assert.Contains("'--output-directory', '{{root / out}}'", build);
        Assert.Contains("Shapes *args: (_build 'VL.Shapes/src/VL.Shapes.csproj' args)", build);
        Assert.Contains("$null = New-Item -ItemType Directory -Force '{{sources_path}}'; $a += @('--export-package-sources', [IO.Path]::GetFullPath('{{sources_path}}'));", build);

        Assert.Equal(["just dev Gallery", "just dev vvvv", "just build Gallery", "just build Shapes", "just build all"], plan.Commands.Select(c => c.Command));
        Assert.Equal("opens `Gallery.vl` in vvvv (also `just dev`)", plan.Commands[0].Description);
        var readme = Entry(plan, "README.md").Content!;
        Assert.Contains("| `just build Gallery` |", readme);
        Assert.Contains("- `Gallery.vl` — entry patch", readme);
        Assert.Contains("- `VL.Shapes\\` — package `VL.Shapes` with C# project `VL.Shapes\\src\\VL.Shapes.csproj`", readme);
    }

    [Fact]
    public void Package_repository_with_cmd_sln_inline_versions_and_license()
    {
        using var env = new FakeEnvironment();
        var m = ResolvedModels.PackageRepository(env);
        m.EnsureCSharp(VersionManagement.Inline, "sln");
        m.AddPackage("VL.Mapper", wrapper: true);
        m.AddPackage("VL.Mapper.Skia", wrapper: true);
        var skia = m.Projects.Find("VL.Mapper.Skia")!;
        skia.References.Set(["VL.Mapper"], SlotSource.Flag);
        skia.Packages.Set([new PackageReference("VL.Skia", "2025.7.4")], SlotSource.Flag);
        skia.Features.Set(new HashSet<ProjectFeature> { ProjectFeature.Windows }, SlotSource.Flag);
        skia.Tfm.Set("net8.0-windows", SlotSource.Flag);
        m.AddTarget("Mapper", null, "VL.Mapper", dev: false, BuildOperation.Library, ["VL.Mapper"]);
        m.AddTarget("Mapper-Skia", null, "VL.Mapper.Skia", dev: false, BuildOperation.Library, ["VL.Mapper", "VL.Mapper.Skia"]);

        var plan = Planner(m).Build();

        var csproj = Entry(plan, "VL.Mapper.Skia\\src\\VL.Mapper.Skia.csproj").Content!;
        Assert.Contains("<TargetFramework>net8.0-windows</TargetFramework>", csproj);
        Assert.Contains("<PackageReference Include=\"VL.Core\" Version=\"2025.7.4\" />", csproj);
        Assert.Contains("<PackageReference Include=\"VL.Skia\" Version=\"2025.7.4\" />", csproj);
        Assert.Contains("<ProjectReference Include=\"..\\..\\VL.Mapper\\src\\VL.Mapper.csproj\" />", csproj);
        Assert.DoesNotContain(plan.Entries, e => e.Path == "Directory.Packages.props");

        var sln = Entry(plan, "VL.Mapper.sln").Content!;
        Assert.Contains("Project(\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\") = \"VL.Mapper\", \"VL.Mapper\\src\\VL.Mapper.csproj\"", sln);
        Assert.Contains("GlobalSection(ProjectConfigurationPlatforms)", sln);

        var license = Entry(plan, "LICENSE").Content!;
        Assert.StartsWith("MIT License", license);
        Assert.Contains($"Copyright (c) {DateTime.Now.Year} Test Holder", license);

        Assert.Contains("start \"\" %VVVV%", Entry(plan, "_dev.cmd").Content);
        Assert.Contains("if not exist \"%ROOT%..\\__nugets\" mkdir \"%ROOT%..\\__nugets\"\r\nstart \"\" %VVVV% --nuget-path \"%ROOT%..\\__nugets\"", Entry(plan, "_dev.cmd").Content);
        Assert.Contains("--editable-packages \"VL.Mapper;VL.Mapper.Skia\"", Entry(plan, "_dev.cmd").Content);
        var build = Entry(plan, "_build_Mapper-Skia.cmd").Content!;
        Assert.Contains("%DOTNET% build \"%ROOT%VL.Mapper\\src\\VL.Mapper.csproj\"", build);
        Assert.Contains("%DOTNET% build \"%ROOT%VL.Mapper.Skia\\src\\VL.Mapper.Skia.csproj\"", build);
        Assert.Contains("call \"%ROOT%_build_Mapper.cmd\"", Entry(plan, "_build_all.cmd").Content);
        Assert.DoesNotContain(plan.Entries, e => e.Path == ".justfile");
        Assert.Equal(["_dev.cmd", "_build_Mapper.cmd", "_build_Mapper-Skia.cmd", "_build_all.cmd"], plan.Commands.Select(c => c.Command));
    }

    [Fact]
    public void Existing_files_are_kept_or_merged_never_replaced()
    {
        using var env = new FakeEnvironment();
        var m = GalleryWithLibraryAndPackage(env);
        m.Content.RepoFiles.Set(new HashSet<RepoFile> { RepoFile.GitIgnore, RepoFile.GitAttributes, RepoFile.NuGetConfig, RepoFile.CSharpier }, SlotSource.Flag);
        m.CSharp.Versions.Set(VersionManagement.Keep, SlotSource.Flag);
        var dest = m.Destination;
        Directory.CreateDirectory(Path.Combine(dest, "src"));
        Directory.CreateDirectory(Path.Combine(dest, "assets"));
        File.WriteAllText(Path.Combine(dest, "assets", "logo.png"), "x");
        File.WriteAllText(Path.Combine(dest, ".gitignore"), "node_modules/\n");
        File.WriteAllText(Path.Combine(dest, ".gitattributes"), "* text=auto\n");
        File.WriteAllText(Path.Combine(dest, "NuGet.config"), """<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><add key="mine" value="C:\feed" /></packageSources></configuration>""");
        Directory.CreateDirectory(Path.Combine(dest, ".config"));
        File.WriteAllText(Path.Combine(dest, ".config", "dotnet-tools.json"), """{ "version": 1, "isRoot": true, "tools": { "other": { "version": "1.0.0", "commands": ["other"] } } }""");
        File.WriteAllText(Path.Combine(dest, "src", "Gallery.Core.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(dest, "Directory.Packages.props"), "<Project><PropertyGroup><VLVersion>2025.7.3</VLVersion></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(dest, "Gallery.slnx"), "<Solution><Project Path=\"src/Gallery.Core.csproj\" /></Solution>");
        File.WriteAllText(Path.Combine(dest, "README.md"), "mine");

        var plan = Planner(m).Build();

        Assert.DoesNotContain(plan.Entries, e => e.Path == "assets\\.gitkeep");
        var ignore = Entry(plan, ".gitignore");
        Assert.Equal(PlannedAction.Update, ignore.Action);
        Assert.StartsWith("node_modules/", ignore.Content);
        Assert.Contains("# dotnet create-vl", ignore.Content);
        var attributes = Entry(plan, ".gitattributes");
        Assert.Equal(PlannedAction.Update, attributes.Action);
        Assert.Equal("* text=auto" + Environment.NewLine + "*.vl diff=html" + Environment.NewLine, attributes.Content);
        var nuget = Entry(plan, "NuGet.config");
        Assert.Equal(PlannedAction.Update, nuget.Action);
        Assert.Contains("key=\"mine\"", nuget.Content);
        Assert.Contains("key=\"vvvv-public-feed\"", nuget.Content);
        var tools = Entry(plan, ".config\\dotnet-tools.json");
        Assert.Equal(PlannedAction.Update, tools.Action);
        Assert.Contains("\"other\"", tools.Content);
        Assert.Contains("\"csharpier\"", tools.Content);
        Assert.Equal(PlannedAction.Create, Entry(plan, ".csharpierrc").Action);

        Assert.Equal(PlannedAction.Keep, Entry(plan, "src\\Gallery.Core.csproj").Action);
        Assert.DoesNotContain(plan.Entries, e => e.Path == "Directory.Packages.props");
        // Keep mode with an existing props file: new projects still reference the shared property.
        Assert.Contains("Version=\"$(VLVersion)\"", Entry(plan, "VL.Shapes\\src\\VL.Shapes.csproj").Content);
        var solution = Entry(plan, "Gallery.slnx");
        Assert.Equal(PlannedAction.Keep, solution.Action);
        Assert.Equal("exists — add VL.Shapes by hand", solution.Note);
        Assert.Equal(PlannedAction.Keep, Entry(plan, "README.md").Action);
        Assert.Equal(PlannedAction.Create, Entry(plan, ".justfile").Action);
    }

    [Fact]
    public void Second_run_keeps_everything_it_wrote()
    {
        using var env = new FakeEnvironment();
        var m = GalleryWithLibraryAndPackage(env);
        PlanWriter.Write(Planner(m).Build());

        var again = Planner(m).Build();

        Assert.DoesNotContain(again.Files, e => e.Action is PlannedAction.Create or PlannedAction.Update);
        Assert.Equal(PlannedAction.Keep, Entry(again, "Gallery.vl").Action);
    }

    [Fact]
    public void Helper_projects_get_no_vl_reference_and_features_shape_the_csproj()
    {
        using var env = new FakeEnvironment();
        var m = ResolvedModels.Application(env, PipelineBackend.None);
        m.Content.RepoFiles.Set(new HashSet<RepoFile>(), SlotSource.Flag);
        m.AddProject("Gallery.Core", ProjectRole.ApplicationLibrary, "src\\Gallery.Core.csproj", "lib", VlBinding.Direct,
            tfm: "net8.0-windows", features: [ProjectFeature.WinForms, ProjectFeature.Stride], packages: [new PackageReference("MessagePack", "3.1.0")]);
        m.AddProject("Gallery.Util", ProjectRole.HelperLibrary, "Gallery.Util\\src\\Gallery.Util.csproj", "Gallery.Util\\lib", VlBinding.None);

        var plan = Planner(m).Build();

        var core = Entry(plan, "src\\Gallery.Core.csproj").Content!;
        Assert.Contains("<UseWindowsForms>true</UseWindowsForms>", core);
        Assert.Contains("<PackageReference Include=\"VL.Stride\" Version=\"$(VLVersion)\" />", core);
        Assert.Contains("<PackageReference Include=\"MessagePack\" Version=\"$(MessagePackVersion)\" />", core);
        Assert.Contains("<MessagePackVersion>3.1.0</MessagePackVersion>", Entry(plan, "Directory.Packages.props").Content);

        var util = Entry(plan, "Gallery.Util\\src\\Gallery.Util.csproj").Content!;
        Assert.DoesNotContain("VL.Core", util);
        Assert.Contains("<BaseOutputPath>..\\lib\\</BaseOutputPath>", util);
        Assert.DoesNotContain(plan.Entries, e => e.Path == "Gallery.Util\\src\\Properties\\AssemblyInfo.cs");

        Assert.DoesNotContain(plan.Entries, e => e.Path.EndsWith(".just", StringComparison.Ordinal) || e.Path == ".justfile" || e.Path.EndsWith(".cmd", StringComparison.Ordinal));
        Assert.Empty(plan.Commands);
        Assert.DoesNotContain("## Commands", Entry(plan, "README.md").Content);
    }

    [Fact]
    public void Cmd_export_writes_into_the_export_root_and_stages_into_the_app_folder()
    {
        using var env = new FakeEnvironment();
        var m = GalleryWithLibraryAndPackage(env, PipelineBackend.Cmd);

        var plan = Planner(m).Build();

        var build = Entry(plan, "_build_Gallery.cmd").Content!;
        Assert.Contains("set OUT=%ROOT%bin\r\nset APP=%OUT%\\Gallery", build);
        Assert.Contains("if exist \"%APP%\" rmdir /s /q \"%APP%\"", build);
        Assert.Contains("--output-directory \"%OUT%\"", build);
        Assert.Contains("robocopy \"%ROOT%assets\" \"%APP%\\assets\"", build);
        Assert.DoesNotContain("artifacts", build);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(build, "robocopy"));    // staging only, no mirror copy
        Assert.Equal("builds `VL.Shapes`, `Gallery.Core`, then exports `Gallery.vl` to `bin\\Gallery\\` with `assets`",
            plan.Commands.Single(c => c.Command == "_build_Gallery.cmd").Description);
    }

    [Fact]
    public void Just_default_falls_back_to_dev_when_nothing_builds()
    {
        using var env = new FakeEnvironment();
        var m = ResolvedModels.Application(env);
        m.AddTarget("Gallery", "Gallery.vl", null, dev: true, BuildOperation.None, []);

        var plan = Planner(m).Build();

        Assert.Contains("default *args: (_dev::Gallery args)", Entry(plan, ".justfile").Content);
        Assert.DoesNotContain("mod _build", Entry(plan, ".justfile").Content);
        Assert.DoesNotContain("build *args:", Entry(plan, ".justfile").Content);
        Assert.DoesNotContain(plan.Entries, e => e.Path == ".just\\build.just");
        Assert.Equal(["just dev Gallery", "just dev vvvv"], plan.Commands.Select(c => c.Command));
    }
}
