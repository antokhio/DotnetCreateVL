using DotnetCreateVL.Model;

namespace DotnetCreateVL.Generate;

/// <summary>Turns the resolved models of Phases 1–8 into a <see cref="GenerationPlan"/>; nothing is written here.</summary>
internal sealed class Planner(
    BasicsModel basics,
    ApplicationModel app,
    PackagesModel packages,
    ProjectsModel projects,
    CSharpModel csharp,
    ContentModel content,
    PipelineModel pipeline,
    string vlLanguageVersion)
{
    public GenerationPlan Build()
    {
        var plan = new GenerationPlan(basics.Destination.Require());

        PlanDocuments(plan);
        PlanFolders(plan);
        PlanProjects(plan);
        PlanRepoFiles(plan);
        PlanPipeline(plan);
        plan.File("README.md", ReadmeTemplate.Render(basics, app, packages, projects, plan));
        return plan;
    }

    /// <summary>Every VL document gets vvvv's own File → New skeleton; patching happens in vvvv.</summary>
    private void PlanDocuments(GenerationPlan plan)
    {
        if (basics.Kind.Require() == RepositoryKind.Application)
            plan.File(app.EntryPath.Require(), VlTemplates.Document(vlLanguageVersion));
        foreach (var p in packages.Packages)
            plan.File(p.DocumentPath, VlTemplates.Document(vlLanguageVersion));
    }

    private void PlanFolders(GenerationPlan plan)
    {
        foreach (var p in packages.Packages.Where(p => p.Root.Require().Length > 0))
            plan.Folder(p.Root.Require());
        foreach (var f in content.Folders.Require())
        {
            plan.Folder(f.Path);
            var full = plan.Full(f.Path);
            if (!Directory.Exists(full) || !Directory.EnumerateFileSystemEntries(full).Any())
                plan.File(Path.Combine(f.Path, ".gitkeep"), "");
        }
    }

    private void PlanProjects(GenerationPlan plan)
    {
        if (projects.Projects.Count == 0) return;
        var versions = csharp.Versions.Require();
        // In keep mode an existing Directory.Packages.props still decides how new projects reference versions.
        var shared = versions == VersionManagement.Shared
            || (versions == VersionManagement.Keep && File.Exists(plan.Full("Directory.Packages.props")));

        foreach (var p in projects.Projects)
        {
            var path = p.ProjectPath.Require();
            plan.File(path, ProjectTemplates.Csproj(p, projects, csharp, shared));
            if (p.ExposesVL)
                plan.File(Path.Combine(Path.GetDirectoryName(path) ?? "", "Properties", "AssemblyInfo.cs"), ProjectTemplates.AssemblyInfo());
        }

        if (versions == VersionManagement.Shared)
            plan.File("Directory.Packages.props", ProjectTemplates.DirectoryPackagesProps(projects.Projects, csharp.VlVersion.Require()));

        var solution = csharp.Solution.Require();
        if (solution.IsNone) return;
        var content = solution.Format == "sln" ? ProjectTemplates.Sln(projects.Projects) : ProjectTemplates.Slnx(projects.Projects);
        string? note = null;
        if (File.Exists(plan.Full(solution.FileName)))
        {
            var existing = File.ReadAllText(plan.Full(solution.FileName));
            var missing = projects.Projects.Where(p => !existing.Contains(Path.GetFileName(p.ProjectPath.Require()), StringComparison.OrdinalIgnoreCase)).Select(p => p.Name).ToList();
            note = missing.Count == 0 ? "exists" : $"exists — add {string.Join(", ", missing)} by hand";
        }
        plan.File(solution.FileName, content, note);
    }

    private void PlanRepoFiles(GenerationPlan plan)
    {
        if (content.Wants(RepoFile.GitIgnore))
        {
            var extra = projects.Projects.Select(p => p.OutputBase.Require())
                .Concat(pipeline.Targets.Where(t => t.ExportRoot.IsResolved).Select(t => t.ExportRoot.Require()))
                .Where(f => !f.StartsWith("..", StringComparison.Ordinal))
                .ToList();
            plan.Merge(".gitignore", RepoFileTemplates.GitIgnore(extra), e => RepoFileTemplates.MergeGitIgnore(e, extra), "ignore block appended");
        }
        if (content.Wants(RepoFile.GitAttributes))
            plan.Merge(".gitattributes", RepoFileTemplates.GitAttributes(), RepoFileTemplates.MergeGitAttributes, "attributes appended");
        if (content.Wants(RepoFile.NuGetConfig))
        {
            var sources = csharp.Sources.IsResolved ? csharp.Sources.Require() : [NuGetSource.VvvvPublicFeed];
            plan.Merge("NuGet.config", RepoFileTemplates.NuGetConfig(sources), e => RepoFileTemplates.MergeNuGetConfig(e, sources), "sources added");
        }
        if (content.Wants(RepoFile.CSharpier))
        {
            plan.File(".csharpierrc", RepoFileTemplates.CSharpierRc());
            plan.Merge(Path.Combine(".config", "dotnet-tools.json"), RepoFileTemplates.ToolsManifest(), RepoFileTemplates.MergeToolsManifest, "csharpier tool added");
        }
        if (content.Wants(RepoFile.License))
            plan.File("LICENSE", RepoFileTemplates.License(content.License.Require()));
    }

    private void PlanPipeline(GenerationPlan plan)
    {
        var backend = basics.Pipeline.Require();
        if (backend == PipelineBackend.None) return;
        var c = Context();

        if (backend == PipelineBackend.Just)
        {
            plan.File(".justfile", JustTemplates.Root(c));
            plan.File(Path.Combine(".just", "dev.just"), JustTemplates.Dev(c));
            if (c.BuildTargets.Any())
                plan.File(Path.Combine(".just", "build.just"), JustTemplates.Build(c));

            var firstDev = JustTemplates.DefaultDevTarget(c);
            foreach (var t in JustTemplates.OrderedDevTargets(c))
                plan.Commands.Add(($"just dev {t.Name}", t == firstDev ? $"opens `{t.EntryPath}` in vvvv (also `just dev`)" : $"opens `{t.EntryPath}` in vvvv"));
            plan.Commands.Add(($"just dev {c.BareLaunch}", firstDev is null
                ? "launches vvvv with the repository's package settings (also `just dev`)"
                : "launches vvvv with the repository's package settings"));
            foreach (var t in c.BuildTargets)
                plan.Commands.Add(($"just build {t.Name}", BuildDescription(t)));
            if (c.BuildTargets.Count() > 1)
                plan.Commands.Add(("just build all", "builds every target"));
        }
        else
        {
            var devTargets = c.DevTargets.ToList();
            if (devTargets.Count == 0)
            {
                plan.File("_dev.cmd", CmdTemplates.Launch(c, null));
                plan.Commands.Add(("_dev.cmd", "launches vvvv with the repository's package settings"));
            }
            foreach (var t in devTargets)
            {
                var file = CmdTemplates.DevFile(c, t);
                plan.File(file, CmdTemplates.Launch(c, t));
                plan.Commands.Add((file, $"opens `{t.EntryPath}` in vvvv"));
            }
            var buildTargets = c.BuildTargets.ToList();
            foreach (var t in buildTargets)
            {
                var file = CmdTemplates.BuildFile(c, t);
                plan.File(file, CmdTemplates.Build(c, t));
                plan.Commands.Add((file, BuildDescription(t)));
            }
            if (buildTargets.Count > 1)
            {
                plan.File("_build_all.cmd", CmdTemplates.BuildAll(c));
                plan.Commands.Add(("_build_all.cmd", "builds every target"));
            }
        }
    }

    private static string BuildDescription(TargetPlan t)
    {
        var parts = new List<string>();
        if (t.ProjectPaths.Count > 0)
            parts.Add($"builds {string.Join(", ", t.ProjectPaths.Select(p => $"`{Path.GetFileNameWithoutExtension(p)}`"))}");
        if (t.Exports)
            parts.Add($"exports `{t.EntryPath}` to `{t.ExportRoot}\\{t.AppName}\\`" + (t.Staging.Count == 0 ? "" : $" with {string.Join(", ", t.Staging.Select(s => $"`{s.Source}`"))}"));
        return string.Join(", then ", parts);
    }

    private PipelineContext Context()
    {
        var vvvv = basics.Vvvv.Require();
        var targets = pipeline.Targets.Select(t => new TargetPlan(
            t.Name,
            t.EntryPath,
            t.AppName,
            t.Dev.Require(),
            t.Build.Require(),
            t.Prerequisites.Require().Select(projects.Find).Where(p => p is not null).Select(p => p!.ProjectPath.Require()).ToList(),
            t.ExportRoot.IsResolved ? t.ExportRoot.Require() : null,
            t.Staging.IsResolved ? t.Staging.Require() : [])).ToList();

        var defaultTarget = pipeline.DefaultTarget.Require();
        var buildTargets = targets.Where(t => t.Builds).ToList();
        if (defaultTarget == "all" && buildTargets.Count == 1) defaultTarget = buildTargets[0].Name;

        return new PipelineContext(
            vvvv.EditorPath,
            vvvv.CompilerPath,
            csharp.Dotnet.IsResolved ? csharp.Dotnet.Require().Executable : "dotnet",
            csharp.Configuration.IsResolved ? csharp.Configuration.Require() : "Debug",
            Optional(pipeline.NugetPath.Require()),
            Optional(pipeline.ExportSources.Require()),
            pipeline.PackageRepositories.Require(),
            pipeline.Editable.Require(),
            defaultTarget,
            targets);
    }

    private static string? Optional(string value) => value.Length == 0 ? null : value;
}
