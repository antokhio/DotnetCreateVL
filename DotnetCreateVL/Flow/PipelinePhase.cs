using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>
/// Phase 8: pipeline settings and targets, each a table confirmed once. Settings come from earlier phases and the
/// <c>..\__nugets</c> / <c>..\__sources</c> convention; targets are derived from the entry patch and the package projects.
/// </summary>
internal sealed class PipelinePhase(IAnsiConsole console, IPrompter? prompter, BasicsModel basics, ApplicationModel app,
    PackagesModel packages, ProjectsModel projects, CSharpModel csharp, ContentModel content)
{
    private enum Setting { NugetPath, ExportSources, PackageRepositories, Editable }
    private enum Group { Dev, Build, Prerequisites, ExportRoot, Staging }

    private readonly List<string> warnings = [];
    public IReadOnlyList<string> Warnings => warnings;

    private PipelineBackend Backend => basics.Pipeline.Require();
    private bool IsApplication => basics.Kind.Require() == RepositoryKind.Application;

    /// <summary>Creates the derived targets; call before <see cref="ApplyFlags"/> so target flags can resolve names.</summary>
    public void DeriveTargets(PipelineModel m)
    {
        if (IsApplication)
            m.Targets.Add(new TargetModel(TargetName(app.Name.Require(), m), "application", app.EntryPath.Require(), null));
        foreach (var p in packages.Packages.Where(p => p.HasWrapper))
        {
            var id = p.Id;
            var shortName = id.StartsWith("VL.", StringComparison.OrdinalIgnoreCase) && id.Length > 3 ? id[3..] : id;
            var name = m.Find(Normalize(shortName)) is null ? TargetName(shortName, m) : TargetName(id, m);
            m.Targets.Add(new TargetModel(name, $"package {id}", null, id));
        }
    }

    private static string TargetName(string seed, PipelineModel m)
    {
        var name = Normalize(seed);
        var candidate = name;
        for (var i = 2; m.Find(candidate) is not null; i++) candidate = $"{name}-{i}";
        return candidate;
    }

    /// <summary>A name valid for Just recipes and cmd scripts: original case kept, [A-Za-z0-9_-], no leading digit.</summary>
    public static string Normalize(string seed)
    {
        var chars = seed.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '-').ToArray();
        var collapsed = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length == 0) return "target";
        return char.IsAsciiDigit(collapsed[0]) ? $"t-{collapsed}" : collapsed;
    }

    public IReadOnlyList<string> ApplyFlags(PipelineModel m, string? nugetPath, string? exportSources, IEnumerable<string>? packageRepos,
        IEnumerable<string>? editable, string? defaultTarget, IEnumerable<string>? targetDev, IEnumerable<string>? targetBuild,
        IEnumerable<string>? prereqs, IEnumerable<string>? exportRoots, IEnumerable<string>? stages)
    {
        var errors = new List<string>();

        if (nugetPath is not null)
        {
            if (Validators.FolderOrNone(nugetPath) is { } e) errors.Add($"--nuget-path '{nugetPath}': {e}");
            else m.NugetPath.Set(NoneToEmpty(nugetPath), SlotSource.Flag);
        }
        if (exportSources is not null)
        {
            if (Validators.FolderOrNone(exportSources) is { } e) errors.Add($"--export-sources '{exportSources}': {e}");
            else m.ExportSources.Set(NoneToEmpty(exportSources), SlotSource.Flag);
        }
        if (packageRepos is not null)
        {
            var list = new List<string>();
            foreach (var item in packageRepos)
            {
                if (item.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) { list.Clear(); continue; }
                if (Validators.RepositoryFolder(item, allowEmpty: false) is { } e) { errors.Add($"--package-repo '{item}': {e}"); continue; }
                var folder = Validators.NormalizeFolder(item);
                if (!list.Contains(folder, StringComparer.OrdinalIgnoreCase)) list.Add(folder);
            }
            m.PackageRepositories.Set(list, SlotSource.Flag);
        }
        if (editable is not null)
        {
            var list = new List<string>();
            foreach (var item in editable)
            {
                if (item.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) { list.Clear(); continue; }
                if (Validators.EditablePattern(item) is { } e) { errors.Add($"--editable '{item}': {e}"); continue; }
                if (!list.Contains(item.Trim(), StringComparer.OrdinalIgnoreCase)) list.Add(item.Trim());
            }
            m.Editable.Set(list, SlotSource.Flag);
        }

        foreach (var (t, value, raw) in Assignments(m, targetDev, "--target-dev", errors))
        {
            if (Validators.ParseSwitch(value) is { } on) t.Dev.Set(on, SlotSource.Flag);
            else errors.Add($"--target-dev '{raw}': expected on or off.");
        }
        foreach (var (t, value, raw) in Assignments(m, targetBuild, "--target-build", errors))
        {
            if (Validators.ParseBuildOperation(value) is { } op) t.Build.Set(op, SlotSource.Flag);
            else errors.Add($"--target-build '{raw}': expected export, lib or none.");
        }
        var prereqSets = new Dictionary<TargetModel, List<string>>();
        foreach (var (t, value, raw) in Assignments(m, prereqs, "--prereq", errors))
        {
            if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) { prereqSets[t] = []; continue; }
            if (projects.Find(value) is not { } p) { errors.Add($"--prereq '{raw}': no project named '{value}'."); continue; }
            if (!prereqSets.TryGetValue(t, out var list)) prereqSets[t] = list = [];
            if (!list.Contains(p.Name)) list.Add(p.Name);
        }
        foreach (var (t, list) in prereqSets) t.Prerequisites.Set(Expand(list), SlotSource.Flag);
        foreach (var (t, value, raw) in Assignments(m, exportRoots, "--export-root", errors))
        {
            if (Validators.RepositoryFolder(value, allowEmpty: false) is { } e) errors.Add($"--export-root '{raw}': {e}");
            else t.ExportRoot.Set(Validators.NormalizeFolder(value), SlotSource.Flag);
        }
        var stageSets = new Dictionary<TargetModel, List<StagingRule>>();
        foreach (var (t, value, raw) in Assignments(m, stages, "--stage", errors))
        {
            if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) { stageSets[t] = []; continue; }
            if (Validators.StagingRule(value) is { } e) { errors.Add($"--stage '{raw}': {e}"); continue; }
            if (!stageSets.TryGetValue(t, out var list)) stageSets[t] = list = [];
            var rule = Validators.ParseStagingRule(value);
            if (list.Any(r => r.Destination.Equals(rule.Destination, StringComparison.OrdinalIgnoreCase))) { errors.Add($"--stage '{raw}': {rule.Destination} is already a staging destination for {t.Name}."); continue; }
            list.Add(rule);
        }
        foreach (var (t, list) in stageSets) t.Staging.Set(list, SlotSource.Flag);

        if (defaultTarget is not null)
        {
            var v = defaultTarget.Trim();
            if (v.Equals("all", StringComparison.OrdinalIgnoreCase)) m.DefaultTarget.Set("all", SlotSource.Flag);
            else if (m.Find(v) is { } t) m.DefaultTarget.Set(t.Name, SlotSource.Flag);
            else errors.Add($"--default-target '{defaultTarget}': expected all or one of {Names(m)}.");
        }

        return errors;
    }

    private static string NoneToEmpty(string value) =>
        value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : value.Trim();

    private static string Names(PipelineModel m) =>
        m.Targets.Count == 0 ? "(no targets)" : string.Join(", ", m.Targets.Select(t => t.Name));

    private static IEnumerable<(TargetModel Target, string Value, string Raw)> Assignments(PipelineModel m, IEnumerable<string>? values, string flag, List<string> errors)
    {
        foreach (var raw in values ?? [])
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0 || eq == raw.Length - 1) { errors.Add($"{flag} '{raw}' must have the form <Target>=<value>."); continue; }
            var target = m.Find(raw[..eq].Trim());
            if (target is null) { errors.Add($"{flag} '{raw}': no target named '{raw[..eq].Trim()}'; targets are {Names(m)}."); continue; }
            yield return (target, raw[(eq + 1)..].Trim(), raw);
        }
    }

    /// <summary>The projects plus everything they reference, in dependency order.</summary>
    private IReadOnlyList<string> Expand(IEnumerable<string> names)
    {
        var ordered = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string name)
        {
            if (!seen.Add(name) || projects.Find(name) is not { } p) return;
            foreach (var r in p.References.IsResolved ? p.References.Require() : []) Visit(r);
            ordered.Add(p.Name);
        }
        foreach (var n in names) Visit(n);
        return ordered;
    }

    public async Task<IReadOnlyList<string>> RunAsync(PipelineModel m, CancellationToken ct)
    {
        console.Write(new Rule($"[bold][[8/9]] Pipeline ({Backend})[/]").LeftJustified());
        await AskOptionalFoldersAsync(m, ct);
        ApplySettingDefaults(m);
        console.Write(SettingsTable(m));
        if (prompter is not null)
        {
            while (!await prompter.ConfirmAsync("Use these pipeline settings?", true, ct))
            {
                var picked = await prompter.MultiSelectAsync("Change which settings?", Enum.GetValues<Setting>(), SettingLabel, ct);
                foreach (var s in picked) await EditSettingAsync(m, s, ct);
                console.Write(SettingsTable(m));
            }
        }

        ApplyTargetDefaults(m);
        if (m.Targets.Count == 0)
        {
            warnings.Add("No pipeline targets: no entry patch and no package with a C# project. Only the shared vvvv launch command is generated.");
        }
        else
        {
            console.Write(TargetsTable(m));
            if (prompter is not null)
            {
                while (!await prompter.ConfirmAsync("Use these targets?", true, ct))
                {
                    var target = m.Targets.Count == 1 ? m.Targets[0] : await prompter.SelectAsync("Edit target:", m.Targets, t => t.Name, ct);
                    var groups = await prompter.MultiSelectAsync($"Change what for {target.Name}?", Enum.GetValues<Group>(), GroupLabel, ct);
                    foreach (var g in groups) await EditTargetAsync(target, g, ct);
                    console.Write(TargetsTable(m));
                }
            }
        }

        if (!m.DefaultTarget.IsResolved)
            m.DefaultTarget.Set(m.Targets.Count == 1 ? m.Targets[0].Name : "all", SlotSource.Default);

        CollectWarnings(m);
        return Issues(m).ToList();
    }

    /// <summary>
    /// The shared folders outside the repository are opt-in: vvvv needs them to exist, so they are
    /// only used when asked for explicitly. Export sources are only relevant when something can be exported.
    /// </summary>
    private async Task AskOptionalFoldersAsync(PipelineModel m, CancellationToken ct)
    {
        if (prompter is null) return;
        if (!m.NugetPath.IsResolved)
        {
            var use = await prompter.ConfirmAsync($"Use a shared nuget folder outside the repository ({PipelineModel.ConventionNugetPath})?", false, ct);
            m.NugetPath.Set(use ? PipelineModel.ConventionNugetPath : "", SlotSource.Prompt);
        }
        if (!m.ExportSources.IsResolved && m.Targets.Any(t => t.EntryPath is not null))
        {
            var use = await prompter.ConfirmAsync($"Export package sources outside the repository ({PipelineModel.ConventionExportSources})?", false, ct);
            m.ExportSources.Set(use ? PipelineModel.ConventionExportSources : "", SlotSource.Prompt);
        }
    }

    private void ApplySettingDefaults(PipelineModel m)
    {
        if (!m.NugetPath.IsResolved) m.NugetPath.Set("", SlotSource.Default);
        if (!m.ExportSources.IsResolved) m.ExportSources.Set("", SlotSource.Default);
        if (!m.PackageRepositories.IsResolved)
        {
            var repos = packages.Packages
                .Select(p => Validators.NormalizeFolder(Path.GetDirectoryName(p.Root.Require()) ?? ""))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            m.PackageRepositories.Set(repos, SlotSource.Default);
        }
        if (!m.Editable.IsResolved) m.Editable.Set(packages.Packages.Select(p => p.Id).ToList(), SlotSource.Default);
    }

    private void ApplyTargetDefaults(PipelineModel m)
    {
        var assets = content.Folders.IsResolved ? content.Folders.Require().FirstOrDefault(f => f.Kind == FolderKind.Assets && f.PackageId is null) : null;
        foreach (var t in m.Targets)
        {
            var isApp = t.EntryPath is not null;
            if (!t.Dev.IsResolved) t.Dev.Set(isApp, SlotSource.Default);
            if (!t.Build.IsResolved) t.Build.Set(isApp ? BuildOperation.Export : BuildOperation.Library, SlotSource.Default);
            if (!t.Prerequisites.IsResolved)
            {
                var seeds = isApp ? projects.Projects.Select(p => p.Name) : projects.Projects.Where(p => p.OwnerPackageId == t.PackageId).Select(p => p.Name);
                t.Prerequisites.Set(Expand(seeds), SlotSource.Default);
            }
            if (!t.ExportRoot.IsResolved) t.ExportRoot.Set(PipelineModel.DefaultExportRoot, SlotSource.Default);
            if (!t.Staging.IsResolved)
                t.Staging.Set(isApp && assets is not null ? [new StagingRule(assets.Path, Path.GetFileName(assets.Path))] : [], SlotSource.Default);
        }
    }

    private async Task EditSettingAsync(PipelineModel m, Setting s, CancellationToken ct)
    {
        switch (s)
        {
            case Setting.NugetPath:
                var np = await prompter!.TextAsync("nuget path (e.g. ..\\__nugets, or none):", Display(m.NugetPath.Require()), Validators.FolderOrNone, ct);
                m.NugetPath.Set(NoneToEmpty(np), SlotSource.Prompt);
                break;
            case Setting.ExportSources:
                var es = await prompter!.TextAsync("Export package sources (e.g. ..\\__sources, or none):", Display(m.ExportSources.Require()), Validators.FolderOrNone, ct);
                m.ExportSources.Set(NoneToEmpty(es), SlotSource.Prompt);
                break;
            case Setting.PackageRepositories:
                var pr = await prompter!.TextAsync("Package repositories (e.g. repos, or none):", ListDisplay(m.PackageRepositories.Require()), Validators.RepositoryFolderList, ct);
                m.PackageRepositories.Set(SplitList(pr).Select(Validators.NormalizeFolder).ToList(), SlotSource.Prompt);
                break;
            case Setting.Editable:
                var ed = await prompter!.TextAsync("Editable packages (e.g. VL.Shapes, VL.My*, or none):", ListDisplay(m.Editable.Require()), Validators.EditablePatternList, ct);
                m.Editable.Set(SplitList(ed), SlotSource.Prompt);
                break;
        }
    }

    private async Task EditTargetAsync(TargetModel t, Group g, CancellationToken ct)
    {
        switch (g)
        {
            case Group.Dev:
                if (t.EntryPath is null) { console.MarkupLine($"[grey]{Markup.Escape(t.Name)} has no entry patch to open[/]"); break; }
                t.Dev.Set(await prompter!.ConfirmAsync($"Dev command for {t.Name} (opens {t.EntryPath})?", t.Dev.Require(), ct), SlotSource.Prompt);
                break;
            case Group.Build:
                var ops = t.EntryPath is null ? [BuildOperation.Library, BuildOperation.None] : new[] { BuildOperation.Export, BuildOperation.Library, BuildOperation.None };
                t.Build.Set(await prompter!.SelectAsync($"Build operation for {t.Name}:", ops, BuildLabel, ct), SlotSource.Prompt);
                break;
            case Group.Prerequisites:
                if (projects.Projects.Count == 0) { console.MarkupLine("[grey]no C# projects to build[/]"); break; }
                var current = projects.Projects.Where(p => t.Prerequisites.Require().Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                var picked = await prompter!.MultiSelectAsync($"Prerequisites for {t.Name}:", projects.Projects, p => p.Name, ct, current);
                t.Prerequisites.Set(Expand(picked.Select(p => p.Name)), SlotSource.Prompt);
                break;
            case Group.ExportRoot:
                if (!t.IsExport) { console.MarkupLine($"[grey]{Markup.Escape(t.Name)} does not export[/]"); break; }
                var root = await prompter!.TextAsync($"Export root for {t.Name} (app lands in <root>\\{t.AppName}\\):", t.ExportRoot.Require(), v => Validators.RepositoryFolder(v, allowEmpty: false), ct);
                t.ExportRoot.Set(Validators.NormalizeFolder(root), SlotSource.Prompt);
                break;
            case Group.Staging:
                if (!t.IsExport) { console.MarkupLine($"[grey]{Markup.Escape(t.Name)} does not export[/]"); break; }
                var text = await prompter!.TextAsync($"Staging for {t.Name} (e.g. assets, media:assets, or none):", ListDisplay(t.Staging.Require().Select(r => r.ToString()).ToList()), Validators.StagingRuleList, ct);
                t.Staging.Set(SplitList(text).Select(Validators.ParseStagingRule).ToList(), SlotSource.Prompt);
                break;
        }
    }

    private static string Display(string value) => value.Length == 0 ? "none" : value;
    private static string ListDisplay(IReadOnlyList<string> values) => values.Count == 0 ? "none" : string.Join(", ", values);
    private static List<string> SplitList(string text) =>
        text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) ? [] : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void CollectWarnings(PipelineModel m)
    {
        foreach (var t in m.Targets)
        {
            if (!t.IsExport) continue;
            var planned = content.Folders.IsResolved ? content.Folders.Require().Select(f => f.Path) : [];
            foreach (var r in t.Staging.Require())
            {
                var exists = Directory.Exists(Path.Combine(basics.Destination.Require(), r.Source))
                    || planned.Contains(r.Source, StringComparer.OrdinalIgnoreCase);
                if (!exists) warnings.Add($"{t.Name} stages {r.Source}\\, which does not exist yet; the export fails until it does.");
            }
        }
    }

    private IEnumerable<string> Issues(PipelineModel m)
    {
        var vvvv = basics.Vvvv.Require();
        foreach (var t in m.Targets)
        {
            var op = t.Build.Require();
            if (op == BuildOperation.Export && t.EntryPath is null)
                yield return $"{t.Name} cannot export: it has no entry patch. Use lib or none.";
            if (op == BuildOperation.Export && !vvvv.HasCompiler)
                yield return $"{t.Name} exports with vvvvc.exe, which {vvvv.Path} does not contain.";
            if (op == BuildOperation.Library && t.Prerequisites.Require().Count == 0)
                yield return $"{t.Name} builds libraries but has no prerequisites. Add --prereq {t.Name}=<Project> or use none.";
            if (t.Dev.Require() && !vvvv.HasEditor)
                yield return $"{t.Name} has a dev command but {vvvv.Path} does not contain vvvv.exe.";
            if (t.Dev.Require() && t.EntryPath is null)
                yield return $"{t.Name} has no entry patch for a dev command.";
        }
        foreach (var g in m.Targets.Where(t => t.AppFolder is not null).GroupBy(t => t.AppFolder!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            yield return $"Targets {string.Join(" and ", g.Select(t => t.Name))} export into the same folder {g.Key}\\.";
        if (m.Targets.Count > 0 && m.Targets.All(t => t.Build.Require() == BuildOperation.None && !t.Dev.Require()))
            yield return "No target does anything: enable a dev command or a build operation on at least one target.";
    }

    private Table SettingsTable(PipelineModel m)
    {
        var vvvv = basics.Vvvv.Require();
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Setting");
        table.AddColumn("Value");
        table.AddRow("vvvv", Markup.Escape($"{vvvv.DisplayVersion} - {vvvv.Path}{(vvvv.HasCompiler ? "" : " [vvvvc.exe missing]")}"));
        if (projects.Projects.Count > 0 && csharp.Dotnet.IsResolved)
            table.AddRow("dotnet", Markup.Escape($"{csharp.Dotnet.Require().Describe()} · {csharp.Configuration.Require()}"));
        table.AddRow("nuget path", Markup.Escape(Display(m.NugetPath.Require())));
        table.AddRow("Export package sources", Markup.Escape(Display(m.ExportSources.Require())));
        table.AddRow("Package repositories", Markup.Escape(ListDisplay(m.PackageRepositories.Require().Select(r => r.Length == 0 ? "(repository root)" : r).ToList())));
        table.AddRow("Editable packages", Markup.Escape(ListDisplay(m.Editable.Require())));
        return table;
    }

    private static Table TargetsTable(PipelineModel m)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Target");
        table.AddColumn("Owner");
        table.AddColumn("Dev");
        table.AddColumn("Build");
        table.AddColumn("Prerequisites");
        table.AddColumn("Export");
        foreach (var t in m.Targets)
        {
            var export = !t.IsExport ? "—" : (t.AppFolder ?? t.ExportRoot.Require()) + (t.Staging.Require().Count == 0 ? "" : $" + {string.Join(", ", t.Staging.Require())}");
            table.AddRow(
                Markup.Escape(t.Name),
                Markup.Escape(t.OwnerLabel),
                t.Dev.Require() ? Markup.Escape($"opens {t.EntryPath}") : "—",
                Markup.Escape(BuildLabel(t.Build.Require())),
                Markup.Escape(ListDisplay(t.Prerequisites.Require()).Replace("none", "—")),
                Markup.Escape(export));
        }
        return table;
    }

    private static string BuildLabel(BuildOperation op) => op switch
    {
        BuildOperation.Export => "export",
        BuildOperation.Library => "libraries",
        _ => "none",
    };

    private static string SettingLabel(Setting s) => s switch
    {
        Setting.NugetPath => "nuget path",
        Setting.ExportSources => "Export package sources",
        Setting.PackageRepositories => "Package repositories",
        _ => "Editable packages",
    };

    private static string GroupLabel(Group g) => g switch
    {
        Group.Dev => "Dev command",
        Group.Build => "Build operation",
        Group.Prerequisites => "Prerequisites",
        Group.ExportRoot => "Export root",
        _ => "Staging",
    };

    public string Summary(PipelineModel m)
    {
        if (m.Targets.Count == 0) return $"Pipeline ({Backend}): launch only";
        var targets = m.Targets.Select(t =>
        {
            var parts = new List<string>();
            if (t.Dev.Require()) parts.Add("dev");
            if (t.Build.Require() != BuildOperation.None) parts.Add(BuildLabel(t.Build.Require()));
            return $"{t.Name} ({(parts.Count == 0 ? "nothing" : string.Join(", ", parts))})";
        });
        return $"Pipeline ({Backend}): {string.Join(", ", targets)} · default {m.DefaultTarget.Require()}";
    }
}
