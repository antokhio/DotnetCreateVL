using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>Phase 4: C# libraries plus the layout (project path, output base) of every project including package wrappers.</summary>
internal sealed class LibrariesPhase(IAnsiConsole console, IPrompter? prompter, BasicsModel basics, ApplicationModel app, PackagesModel packages)
{
    private ProjectRole LibraryRole => basics.Kind.Require() == RepositoryKind.Application ? ProjectRole.ApplicationLibrary : ProjectRole.HelperLibrary;

    private IEnumerable<string> DocumentNames()
    {
        if (app.Name.IsResolved) yield return app.Name.Require();
        if (app.EntryPath.IsResolved) yield return Path.GetFileNameWithoutExtension(app.EntryPath.Require());
        foreach (var p in packages.Packages) yield return p.Id;
    }

    /// <summary>Registers wrappers from Phase 3, then applies <c>--lib</c>, <c>--project-path</c> and <c>--output-base</c>.</summary>
    public IReadOnlyList<string> ApplyFlags(ProjectsModel m, IEnumerable<string>? libs, IEnumerable<string>? projectPaths, IEnumerable<string>? outputBases)
    {
        var errors = new List<string>();

        foreach (var p in packages.Packages.Where(p => p.HasWrapper))
            m.Projects.Add(new ProjectModel(p.Id, ProjectRole.PackageWrapper, p.Id));

        foreach (var raw in libs ?? [])
        {
            var name = raw.Trim();
            if (Validators.ProjectName(name, m.Projects.Select(p => p.Name), DocumentNames()) is { } e) { errors.Add($"--lib '{raw}': {e}"); continue; }
            m.Projects.Add(new ProjectModel(name, LibraryRole));
        }

        ApplyAssignments(m, projectPaths, "--project-path", errors, Validators.RepositoryProjectPath, (p, v) => p.ProjectPath.Set(v, SlotSource.Flag));
        ApplyAssignments(m, outputBases, "--output-base", errors, v => Validators.RepositoryFolder(v, allowEmpty: false), (p, v) => p.OutputBase.Set(v, SlotSource.Flag));
        return errors;
    }

    private static void ApplyAssignments(ProjectsModel m, IEnumerable<string>? values, string flag, List<string> errors,
        Func<string, string?> validate, Action<ProjectModel, string> set)
    {
        foreach (var raw in values ?? [])
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0 || eq == raw.Length - 1) { errors.Add($"{flag} '{raw}' must have the form <Project>=<path>."); continue; }
            var project = m.Find(raw[..eq].Trim());
            if (project is null) { errors.Add($"{flag} '{raw}': no such project."); continue; }
            var value = raw[(eq + 1)..].Trim();
            if (validate(value) is { } e) { errors.Add($"{flag} '{raw}': {e}"); continue; }
            set(project, Normalize(value));
        }
    }

    public async Task<IReadOnlyList<string>> RunAsync(ProjectsModel m, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[4/9]] C# libraries[/]").LeftJustified());
        var isApp = basics.Kind.Require() == RepositoryKind.Application;

        if (prompter is not null)
        {
            var example = isApp && app.Name.IsResolved ? $"{app.Name.Require()}.Core" : $"{basics.Name.Require()}.Common";
            var addMore = m.Libraries.Any()
                ? await prompter.ConfirmAsync("Add another library?", false, ct)
                : await prompter.ConfirmAsync(isApp
                    ? $"Does this application require a C# library (e.g. {example})?"
                    : $"Does this repository require a shared C# library besides the package projects (e.g. {example})?", false, ct);

            while (addMore)
            {
                var suggested = isApp && !m.Libraries.Any() && Validators.ProjectName(example, m.Projects.Select(p => p.Name), DocumentNames()) is null ? example : null;
                var name = await prompter.TextAsync("Library name:", suggested,
                    v => Validators.ProjectName(v, m.Projects.Select(p => p.Name), DocumentNames()), ct);
                m.Projects.Add(new ProjectModel(name, LibraryRole));
                addMore = await prompter.ConfirmAsync("Add another library?", false, ct);
            }
        }

        if (m.Projects.Count == 0)
        {
            console.MarkupLine("[grey]no C# projects; the C# settings and project phases will be skipped[/]");
            return [];
        }

        ApplyDefaultLayout(m);
        console.Write(LayoutTable(m));

        if (prompter is not null && !await prompter.ConfirmAsync("Use this layout?", true, ct))
        {
            foreach (var p in m.Projects)
            {
                var path = Normalize(await prompter.TextAsync($"Project file for {p.Name}:", p.ProjectPath.Require(), Validators.RepositoryProjectPath, ct));
                p.ProjectPath.Set(path, path == p.ProjectPath.Require() ? p.ProjectPath.Source : SlotSource.Prompt);
                var output = Normalize(await prompter.TextAsync($"Output folder for {p.Name}:", p.OutputBase.Require(), v => Validators.RepositoryFolder(v, allowEmpty: false), ct));
                p.OutputBase.Set(output, output == p.OutputBase.Require() ? p.OutputBase.Source : SlotSource.Prompt);
            }
            console.Write(LayoutTable(m));
        }

        return Conflicts(m).ToList();
    }

    /// <summary>Layout rules: wrappers live in their package; a lone application library uses root src\ and lib\; anything else gets its own folder.</summary>
    private void ApplyDefaultLayout(ProjectsModel m)
    {
        var singleAppLibrary = basics.Kind.Require() == RepositoryKind.Application && m.Libraries.Count() == 1;
        foreach (var p in m.Projects)
        {
            string root;
            if (p.IsWrapper)
                root = packages.Find(p.OwnerPackageId!)!.Root.Require();
            else if (singleAppLibrary)
                root = "";
            else
                root = p.Name;

            if (!p.ProjectPath.IsResolved)
                p.ProjectPath.Set(Normalize(Path.Combine(root, "src", $"{p.Name}.csproj")), SlotSource.Default);
            if (!p.OutputBase.IsResolved)
                p.OutputBase.Set(Normalize(Path.Combine(root, "lib")), SlotSource.Default);
        }
    }

    private static Table LayoutTable(ProjectsModel m)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Project");
        table.AddColumn("Role");
        table.AddColumn("Project file");
        table.AddColumn("Output folder");
        foreach (var p in m.Projects)
            table.AddRow(Markup.Escape(p.Name), Markup.Escape(RoleLabel(p)), Markup.Escape(p.ProjectPath.Require()), Markup.Escape(p.OutputBase.Require() + "\\"));
        return table;
    }

    private static string RoleLabel(ProjectModel p) => p.Role switch
    {
        ProjectRole.PackageWrapper => $"package {p.OwnerPackageId}",
        ProjectRole.ApplicationLibrary => "application library",
        _ => "helper library",
    };

    private static IEnumerable<string> Conflicts(ProjectsModel m)
    {
        foreach (var g in m.Projects.GroupBy(p => p.ProjectPath.Require(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            yield return $"Projects {string.Join(", ", g.Select(p => p.Name))} share the same project file '{g.Key}'.";
        foreach (var g in m.Projects.GroupBy(p => p.OutputBase.Require(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            yield return $"Projects {string.Join(", ", g.Select(p => p.Name))} share the same output folder '{g.Key}'.";
    }

    private static string Normalize(string path) => RepositoryPaths.Normalize(path).TrimEnd('\\');

    public string Summary(ProjectsModel m) =>
        m.Projects.Count == 0
            ? "C# projects: none"
            : $"C# projects: {string.Join(", ", m.Projects.Select(p => $"{p.Name} → {p.ProjectPath.Require()}"))}";
}
