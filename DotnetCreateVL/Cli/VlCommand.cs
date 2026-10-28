using ConsoleAppFramework;
using DotnetCreateVL.Discovery;
using DotnetCreateVL.Flow;
using DotnetCreateVL.Generate;
using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Cli;

public sealed class VlCommand
{
    /// <summary>Create a VL repository (application or package repository) for a selected vvvv installation.</summary>
    /// <param name="kind">-k, Repository kind: application | package.</param>
    /// <param name="name">-n, Repository folder name.</param>
    /// <param name="output">-o, Destination directory; must be new or empty. Default: a folder named after the repository under the current directory.</param>
    /// <param name="vvvvDir">vvvv installation directory containing vvvv.exe and vvvvc.exe.</param>
    /// <param name="pipeline">-p, Pipeline files to generate: none | just | cmd.</param>
    /// <param name="appName">Application name (applications only). Default: the repository name.</param>
    /// <param name="entry">Repository-relative path of the application entry patch. Default: AppName.vl. Written as an empty vvvv document.</param>
    /// <param name="package">Local VL packages (reusable node libraries), comma-separated, each as Name or Name:folder. A package repository needs at least one.</param>
    /// <param name="packageContainer">Repository-relative folder that package folders default to (for example repos). Default: the repository root.</param>
    /// <param name="packageCsharp">Package names that get a C# project at folder/src/Name.csproj, comma-separated.</param>
    /// <param name="lib">C# class libraries besides package projects, comma-separated names (for example Gallery.Core).</param>
    /// <param name="projectPath">Project file overrides, comma-separated Project=path.csproj.</param>
    /// <param name="outputBase">Build output folder overrides, comma-separated Project=folder.</param>
    /// <param name="dotnet">dotnet executable for C# builds. Default: dotnet on PATH when it is x64 and has a suitable SDK.</param>
    /// <param name="tfm">Target framework for C# projects. Default: the vvvv host framework from vvvv.runtimeconfig.json.</param>
    /// <param name="vlVersion">Exact VL package version. Default: read from the installation's VL.Core.nuspec.</param>
    /// <param name="configuration">Default build configuration: Debug | Release.</param>
    /// <param name="solution">Solution name without extension, or none. Default: the repository name.</param>
    /// <param name="solutionFormat">Solution format: slnx | sln. Default: slnx with SDK 9.0.200+, otherwise sln; an existing file keeps its format.</param>
    /// <param name="versions">Dependency version management: shared | inline | keep.</param>
    /// <param name="nugetSource">Restore sources added to the vvvv public feed, comma-separated name=url-or-path; vvvv names the feed itself, none drops all sources.</param>
    /// <param name="vlBinding">How VL reaches a project, comma-separated Project=direct|assembly|none. Default: direct for application libraries, assembly for package projects, none for helpers.</param>
    /// <param name="projectTfm">Per-project target framework overrides, comma-separated Project=tfm.</param>
    /// <param name="feature">Project features, comma-separated Project=windows|winforms|stride (repeat the project for several).</param>
    /// <param name="ref">Local project references, comma-separated Project=OtherProject (repeat the project for several).</param>
    /// <param name="packageRef">NuGet references, comma-separated Project=Id@Version.</param>
    /// <param name="repoFiles">Housekeeping files, comma-separated: gitignore, gitattributes, nuget-config, csharpier, license, or none. Default: gitignore, gitattributes and nuget-config when C# projects exist.</param>
    /// <param name="license">License for package repositories: MIT | Apache-2.0 | BSD-3-Clause | LGPL-3.0-or-later.</param>
    /// <param name="licenseHolder">Copyright holder written into LICENSE. Default: git config user.name.</param>
    /// <param name="folders">Optional content folders, comma-separated. Applications: assets, rnd, vl, resources, sandbox, docs; package repositories: help:PackageId, assets:PackageId. Each accepts =path; or none. Default: assets / help.</param>
    /// <param name="nugetPath">Shared folder vvvv loads packages from (--nuget-path), e.g. ..\__nugets; created by the generated commands. Default: none.</param>
    /// <param name="exportSources">Shared folder for package sources used by exports (--export-package-sources), e.g. ..\__sources. Default: none.</param>
    /// <param name="packageRepo">Package repository folders passed to vvvv, comma-separated, or none. Default: the folders containing the local packages.</param>
    /// <param name="editable">Editable package patterns passed to vvvv, comma-separated, or none. Default: the local package ids.</param>
    /// <param name="defaultTarget">Target run without arguments: a target name or all. Default: the only target, otherwise all.</param>
    /// <param name="targetDev">Dev command per target, comma-separated Target=on|off.</param>
    /// <param name="targetBuild">Build operation per target, comma-separated Target=export|lib|none.</param>
    /// <param name="prereq">Projects a target builds first, comma-separated Target=Project (repeat the target for several; references are added automatically).</param>
    /// <param name="exportRoot">Folder vvvvc exports into per target, comma-separated Target=folder; the app lands in folder\App\. Default: bin.</param>
    /// <param name="stage">Folders copied into the exported application folder, comma-separated Target=src[:dst] (repeat the target for several).</param>
    /// <param name="dryRun">Run every phase and print the plan without writing anything.</param>
    /// <param name="yes">-y, Approve generation without the final confirmation (required without a terminal).</param>
    /// <param name="noInput">Disable prompting even when a terminal is attached; missing values are listed instead.</param>
    /// <param name="verbose">-v, Print discovery details and where each value came from.</param>
    [Command("")]
    public async Task<int> Vl(
        CancellationToken cancellationToken,
        string? kind = null,
        string? name = null,
        string? output = null,
        string? vvvvDir = null,
        string? pipeline = null,
        string? appName = null,
        string? entry = null,
        string[]? package = null,
        string? packageContainer = null,
        string[]? packageCsharp = null,
        string[]? lib = null,
        string[]? projectPath = null,
        string[]? outputBase = null,
        string? dotnet = null,
        string? tfm = null,
        string? vlVersion = null,
        string? configuration = null,
        string? solution = null,
        string? solutionFormat = null,
        string? versions = null,
        string[]? nugetSource = null,
        string[]? vlBinding = null,
        string[]? projectTfm = null,
        string[]? feature = null,
        string[]? @ref = null,
        string[]? packageRef = null,
        string[]? repoFiles = null,
        string? license = null,
        string? licenseHolder = null,
        string[]? folders = null,
        string? nugetPath = null,
        string? exportSources = null,
        string[]? packageRepo = null,
        string[]? editable = null,
        string? defaultTarget = null,
        string[]? targetDev = null,
        string[]? targetBuild = null,
        string[]? prereq = null,
        string[]? exportRoot = null,
        string[]? stage = null,
        bool dryRun = false,
        bool yes = false,
        bool noInput = false,
        bool verbose = false)
    {
        var console = AnsiConsole.Console;
        var interactive = !noInput
            && !Console.IsInputRedirected
            && !Console.IsOutputRedirected
            && console.Profile.Capabilities.Interactive;

        try
        {
            var env = await DiscoveryRunner.RunAsync(console, verbose, cancellationToken);
            IPrompter? prompter = interactive ? new SpectrePrompter(console) : null;

            // Flag parsing for every phase happens up front so a noninteractive run fails fast and completely.
            var flagErrors = new List<string>();
            var basicsModel = new BasicsModel();
            var basics = new BasicsPhase(console, prompter, env);
            flagErrors.AddRange(basics.ApplyFlags(basicsModel, kind, name, output, vvvvDir, pipeline));

            var appModel = new ApplicationModel();
            var entriesFlagsGiven = appName is not null || entry is not null;
            if (flagErrors.Count > 0)
                return Fail(console, flagErrors, ExitCodes.MissingInput);

            var basicsErrors = await basics.RunAsync(basicsModel, cancellationToken);
            if (basicsErrors.Count > 0)
                return Fail(console, basicsErrors, ExitCodes.BlockingIssues);
            if (basicsModel.Missing().ToList() is { Count: > 0 } missingBasics)
                return ReportMissing(console, missingBasics);
            PrintWarnings(console, basics.Warnings);
            console.MarkupLine($"[green]✓[/] {Markup.Escape(basics.Summary(basicsModel))}");

            if (basicsModel.Kind.Require() != RepositoryKind.Application && entriesFlagsGiven)
                return Fail(console, ["--app-name and --entry apply to applications only."], ExitCodes.MissingInput);

            var entries = new EntriesPhase(console, prompter, basicsModel);
            flagErrors.AddRange(entries.ApplyFlags(appModel, appName, entry));
            if (flagErrors.Count > 0)
                return Fail(console, flagErrors, ExitCodes.MissingInput);

            await entries.RunAsync(appModel, cancellationToken);
            if (basicsModel.Kind.Require() == RepositoryKind.Application)
            {
                if (appModel.Missing().ToList() is { Count: > 0 } missingEntries)
                    return ReportMissing(console, missingEntries);
                console.MarkupLine($"[green]✓[/] {Markup.Escape(entries.Summary(appModel))}");
            }

            var packagesModel = new PackagesModel();
            var packages = new PackagesPhase(console, prompter, basicsModel, appModel);
            flagErrors.AddRange(packages.ApplyFlags(packagesModel, package, packageContainer, packageCsharp));
            if (flagErrors.Count > 0)
                return Fail(console, flagErrors, ExitCodes.MissingInput);

            var packageErrors = await packages.RunAsync(packagesModel, cancellationToken);
            if (packageErrors.Count > 0)
                return Fail(console, packageErrors, ExitCodes.BlockingIssues);
            if (packagesModel.Missing(basicsModel.Kind.Require()).ToList() is { Count: > 0 } missingPackages)
                return ReportMissing(console, missingPackages);
            console.MarkupLine($"[green]✓[/] {Markup.Escape(packages.Summary(packagesModel))}");

            var projectsModel = new ProjectsModel();
            var libraries = new LibrariesPhase(console, prompter, basicsModel, appModel, packagesModel);
            flagErrors.AddRange(libraries.ApplyFlags(projectsModel, lib, projectPath, outputBase));
            if (flagErrors.Count > 0)
                return Fail(console, flagErrors, ExitCodes.MissingInput);

            var projectErrors = await libraries.RunAsync(projectsModel, cancellationToken);
            if (projectErrors.Count > 0)
                return Fail(console, projectErrors, ExitCodes.BlockingIssues);
            if (projectsModel.Missing().ToList() is { Count: > 0 } missingProjects)
                return ReportMissing(console, missingProjects);
            console.MarkupLine($"[green]✓[/] {Markup.Escape(libraries.Summary(projectsModel))}");

            var csharpModel = new CSharpModel();
            var csharp = new CSharpSettingsPhase(console, prompter, env, basicsModel);
            var csharpFlagsGiven = dotnet is not null || tfm is not null || vlVersion is not null || configuration is not null
                || solution is not null || solutionFormat is not null || versions is not null || nugetSource is not null;
            if (projectsModel.Projects.Count > 0)
            {
                flagErrors.AddRange(csharp.ApplyFlags(csharpModel, dotnet, tfm, vlVersion, configuration, solution, solutionFormat, versions, nugetSource));
                if (flagErrors.Count > 0)
                    return Fail(console, flagErrors, ExitCodes.MissingInput);

                var csharpErrors = await csharp.RunAsync(csharpModel, cancellationToken);
                if (csharpErrors.Count > 0)
                    return Fail(console, csharpErrors, ExitCodes.BlockingIssues);
                if (csharpModel.Missing().ToList() is { Count: > 0 } missingCsharp)
                    return ReportMissing(console, missingCsharp);
                PrintWarnings(console, csharp.Warnings);
                console.MarkupLine($"[green]✓[/] {Markup.Escape(csharp.Summary(csharpModel))}");

                var projects = new ProjectsPhase(console, prompter, appModel, packagesModel, csharpModel);
                flagErrors.AddRange(projects.ApplyFlags(projectsModel, vlBinding, projectTfm, feature, @ref, packageRef));
                if (flagErrors.Count > 0)
                    return Fail(console, flagErrors, ExitCodes.MissingInput);

                var reviewErrors = await projects.RunAsync(projectsModel, cancellationToken);
                if (reviewErrors.Count > 0)
                    return Fail(console, reviewErrors, ExitCodes.BlockingIssues);
                if (projectsModel.MissingReview().ToList() is { Count: > 0 } missingReview)
                    return ReportMissing(console, missingReview);
                console.MarkupLine($"[green]✓[/] {Markup.Escape(projects.Summary(projectsModel))}");
            }
            else if (csharpFlagsGiven || vlBinding is not null || projectTfm is not null || feature is not null || @ref is not null || packageRef is not null)
            {
                console.MarkupLine("[yellow]warning:[/] C# settings and project flags were ignored because there are no C# projects.");
            }

            var contentModel = new ContentModel();
            var content = new ContentPhase(console, prompter, basicsModel, packagesModel, projectsModel, csharpModel);
            flagErrors.AddRange(content.ApplyFlags(contentModel, repoFiles, license, licenseHolder, folders));
            if (flagErrors.Count > 0)
                return Fail(console, flagErrors, ExitCodes.MissingInput);

            var contentErrors = await content.RunAsync(contentModel, cancellationToken);
            if (contentErrors.Count > 0)
                return Fail(console, contentErrors, ExitCodes.BlockingIssues);
            if (contentModel.Missing().ToList() is { Count: > 0 } missingContent)
                return ReportMissing(console, missingContent);
            PrintWarnings(console, content.Warnings);
            console.MarkupLine($"[green]✓[/] {Markup.Escape(content.Summary(contentModel))}");

            var pipelineModel = new PipelineModel();
            var pipelineFlagsGiven = nugetPath is not null || exportSources is not null || packageRepo is not null || editable is not null
                || defaultTarget is not null || targetDev is not null || targetBuild is not null || prereq is not null || exportRoot is not null || stage is not null;
            if (basicsModel.Pipeline.Require() != PipelineBackend.None)
            {
                var pipelinePhase = new PipelinePhase(console, prompter, basicsModel, appModel, packagesModel, projectsModel, csharpModel, contentModel);
                pipelinePhase.DeriveTargets(pipelineModel);
                flagErrors.AddRange(pipelinePhase.ApplyFlags(pipelineModel, nugetPath, exportSources, packageRepo, editable, defaultTarget, targetDev, targetBuild, prereq, exportRoot, stage));
                if (flagErrors.Count > 0)
                    return Fail(console, flagErrors, ExitCodes.MissingInput);

                var pipelineErrors = await pipelinePhase.RunAsync(pipelineModel, cancellationToken);
                if (pipelineErrors.Count > 0)
                    return Fail(console, pipelineErrors, ExitCodes.BlockingIssues);
                if (pipelineModel.Missing().ToList() is { Count: > 0 } missingPipeline)
                    return ReportMissing(console, missingPipeline);
                PrintWarnings(console, pipelinePhase.Warnings);
                console.MarkupLine($"[green]✓[/] {Markup.Escape(pipelinePhase.Summary(pipelineModel))}");
            }
            else if (pipelineFlagsGiven)
            {
                console.MarkupLine("[yellow]warning:[/] pipeline flags were ignored because no pipeline files are generated (--pipeline none).");
            }

            if (verbose)
                ReportSources(console, basicsModel, appModel, packagesModel, projectsModel, csharpModel, contentModel, pipelineModel);

            // Generated documents carry the installation's VL version; Phase 5 already resolved it when C# projects exist.
            var vvvv = basicsModel.Vvvv.Require();
            var vlLanguageVersion = csharpModel.VlVersion.IsResolved
                ? csharpModel.VlVersion.Require()
                : VvvvHostMetadata.Read(vvvv).VlVersion ?? vvvv.ShortVersion;
            if (vlLanguageVersion is null)
                return Fail(console, [$"cannot determine the VL version of {vvvv.Path} (no packs\\VL.Core\\VL.Core.nuspec); choose a complete installation."], ExitCodes.BlockingIssues);

            var planner = new Planner(basicsModel, appModel, packagesModel, projectsModel, csharpModel, contentModel, pipelineModel, vlLanguageVersion);
            var plan = planner.Build();
            var review = new ReviewPhase(console, prompter);
            switch (await review.RunAsync(plan, yes, dryRun, cancellationToken))
            {
                case ReviewDecision.DryRun:
                    console.MarkupLine("[grey]Dry run. Nothing was written.[/]");
                    return ExitCodes.Success;
                case ReviewDecision.Declined:
                    console.MarkupLine("[grey]Nothing was written.[/]");
                    return ExitCodes.Success;
                case ReviewDecision.NeedsApproval:
                    return Fail(console, ["no terminal to confirm generation; pass --yes to generate or --dry-run to preview."], ExitCodes.MissingInput);
            }

            IReadOnlyList<PlannedEntry> written;
            try
            {
                written = PlanWriter.Write(plan);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Fail(console, [$"writing failed: {ex.Message}"], ExitCodes.Failure);
            }

            console.MarkupLine($"[green]✓[/] {written.Count(w => !w.IsFolder)} files written to {Markup.Escape(plan.Destination)}");
            await ReportNextSteps(console, plan, env, cancellationToken);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            console.WriteLine();
            console.MarkupLine("[yellow]Cancelled. Nothing was written.[/]");
            return ExitCodes.Cancelled;
        }
    }

    private static int Fail(IAnsiConsole console, IReadOnlyList<string> errors, int exitCode)
    {
        foreach (var e in errors)
            console.MarkupLine($"[red]error:[/] {Markup.Escape(e)}");
        return exitCode;
    }

    private static void PrintWarnings(IAnsiConsole console, IEnumerable<string> warnings)
    {
        foreach (var w in warnings)
            console.MarkupLine($"[yellow]warning:[/] {Markup.Escape(w)}");
    }

    private static async Task ReportNextSteps(IAnsiConsole console, GenerationPlan plan, DiscoveredEnvironment env, CancellationToken ct)
    {
        if (plan.Commands.Count > 0)
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("Command");
            table.AddColumn("Does");
            foreach (var (command, description) in plan.Commands)
                table.AddRow(Markup.Escape(command), Markup.Escape(description.Replace("`", "")));
            console.Write(table);
        }

        // The generated justfile is validated with just itself; nothing is built.
        var justfile = plan.Full(".justfile");
        if (env.Just.Found && plan.Written.Any(e => e.Path == ".justfile") && File.Exists(justfile))
        {
            var result = await ProcessRunner.RunAsync("just", $"--justfile \"{justfile}\" --list", TimeSpan.FromSeconds(15), ct);
            if (result is { ExitCode: not 0 })
                console.MarkupLine($"[yellow]warning:[/] just --list failed: {Markup.Escape(result.StdErr.Trim())}");
        }
    }

    private static int ReportMissing(IAnsiConsole console, IReadOnlyList<MissingSlot> missing)
    {
        console.MarkupLine("[red]error:[/] required values are missing and prompting is disabled.");
        var table = new Table().Border(TableBorder.Simple);
        table.AddColumn("Slot");
        table.AddColumn("Flag");
        table.AddColumn("Allowed values");
        foreach (var m in missing)
            table.AddRow(Markup.Escape(m.Id), Markup.Escape(m.Flag), Markup.Escape(m.AllowedValues ?? ""));
        console.Write(table);
        return ExitCodes.MissingInput;
    }

    private static void ReportSources(IAnsiConsole console, BasicsModel b, ApplicationModel a, PackagesModel p, ProjectsModel projects, CSharpModel c, ContentModel content, PipelineModel pipeline)
    {
        var table = new Table().Border(TableBorder.Simple).Title("[grey]Resolved slots[/]");
        table.AddColumn("Slot");
        table.AddColumn("Value");
        table.AddColumn("Source");

        void Row<T>(Slot<T> slot, Func<T, string> format)
        {
            if (slot.IsResolved)
                table.AddRow(Markup.Escape(slot.Id), Markup.Escape(format(slot.Require())), slot.Source.ToString());
        }

        Row(b.Kind, v => v.ToString());
        Row(b.Name, v => v);
        Row(b.Destination, v => v);
        Row(b.Vvvv, v => $"{v.Path} ({v.Version} via {v.VersionSource})");
        Row(b.Pipeline, v => v.ToString());
        Row(a.Name, v => v);
        Row(a.EntryPath, v => v);
        Row(p.Container, v => v.Length == 0 ? "(repository root)" : v);
        foreach (var pkg in p.Packages)
        {
            Row(pkg.Root, v => v);
            Row(pkg.Wrapper, v => v ? "yes" : "no");
        }
        foreach (var proj in projects.Projects)
        {
            Row(proj.ProjectPath, v => v);
            Row(proj.OutputBase, v => v);
            Row(proj.Binding, v => v.ToString().ToLowerInvariant());
            Row(proj.Tfm, v => v);
            Row(proj.Features, v => v.Count == 0 ? "none" : string.Join(", ", v));
            Row(proj.References, v => v.Count == 0 ? "none" : string.Join(", ", v));
            Row(proj.Packages, v => v.Count == 0 ? "none" : string.Join(", ", v));
        }
        Row(c.Dotnet, v => v.Describe());
        Row(c.Tfm, v => v);
        Row(c.VlVersion, v => v);
        Row(c.Configuration, v => v);
        Row(c.Solution, v => v.FileName);
        Row(c.Versions, v => v.ToString().ToLowerInvariant());
        Row(c.Sources, v => v.Count == 0 ? "none" : string.Join(", ", v.Select(s => $"{s.Name}={s.Location}")));
        Row(content.RepoFiles, v => v.Count == 0 ? "none" : string.Join(", ", v.OrderBy(f => f).Select(ContentPhase.FileLabel)));
        Row(content.License, v => v.ToString());
        Row(content.Folders, v => v.Count == 0 ? "none" : string.Join(", ", v.Select(f => $"{f.Key}={f.Path}")));
        Row(pipeline.NugetPath, v => v.Length == 0 ? "none" : v);
        Row(pipeline.ExportSources, v => v.Length == 0 ? "none" : v);
        Row(pipeline.PackageRepositories, v => v.Count == 0 ? "none" : string.Join(", ", v.Select(r => r.Length == 0 ? "(repository root)" : r)));
        Row(pipeline.Editable, v => v.Count == 0 ? "none" : string.Join(", ", v));
        foreach (var t in pipeline.Targets)
        {
            Row(t.Dev, v => v ? "on" : "off");
            Row(t.Build, v => v.ToString().ToLowerInvariant());
            Row(t.Prerequisites, v => v.Count == 0 ? "none" : string.Join(", ", v));
            Row(t.ExportRoot, v => v);
            Row(t.Staging, v => v.Count == 0 ? "none" : string.Join(", ", v));
        }
        Row(pipeline.DefaultTarget, v => v);
        console.Write(table);
    }
}
