using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>
/// Phase 6: per-project settings (VL binding, framework and features, local references, NuGet packages)
/// shown as one table and confirmed once. Every slot has a role-derived default, so nothing is asked up front.
/// </summary>
internal sealed class ProjectsPhase(IAnsiConsole console, IPrompter? prompter, ApplicationModel app, PackagesModel packages, CSharpModel csharp)
{
    private enum Group { Binding, Framework, References, Packages }

    private IEnumerable<string> DocumentNames()
    {
        if (app.Name.IsResolved) yield return app.Name.Require();
        if (app.EntryPath.IsResolved) yield return Path.GetFileNameWithoutExtension(app.EntryPath.Require());
        foreach (var p in packages.Packages) yield return p.Id;
    }

    public IReadOnlyList<string> ApplyFlags(ProjectsModel m, IEnumerable<string>? bindings, IEnumerable<string>? tfms,
        IEnumerable<string>? features, IEnumerable<string>? refs, IEnumerable<string>? packageRefs)
    {
        var errors = new List<string>();

        foreach (var (p, value, raw) in Assignments(m, bindings, "--vl-binding", errors))
        {
            if (Validators.ParseBinding(value) is { } b) p.Binding.Set(b, SlotSource.Flag);
            else errors.Add($"--vl-binding '{raw}': expected direct, assembly or none.");
        }
        foreach (var (p, value, raw) in Assignments(m, tfms, "--project-tfm", errors))
        {
            if (Validators.Tfm(value) is { } e) errors.Add($"--project-tfm '{raw}': {e}");
            else p.Tfm.Set(value.ToLowerInvariant(), SlotSource.Flag);
        }

        var featureSets = new Dictionary<ProjectModel, HashSet<ProjectFeature>>();
        foreach (var (p, value, raw) in Assignments(m, features, "--feature", errors))
        {
            if (Validators.ParseFeature(value) is { } f) featureSets.GetOrAdd(p).Add(f);
            else errors.Add($"--feature '{raw}': expected windows, winforms or stride.");
        }
        foreach (var (p, set) in featureSets) p.Features.Set(set, SlotSource.Flag);

        var refSets = new Dictionary<ProjectModel, List<string>>();
        foreach (var (p, value, raw) in Assignments(m, refs, "--ref", errors))
        {
            var target = m.Find(value);
            if (target is null) { errors.Add($"--ref '{raw}': no project named '{value}'."); continue; }
            if (target == p) { errors.Add($"--ref '{raw}': a project cannot reference itself."); continue; }
            var list = refSets.GetOrAdd(p);
            if (!list.Contains(target.Name)) list.Add(target.Name);
        }
        foreach (var (p, list) in refSets) p.References.Set(list, SlotSource.Flag);

        var pkgSets = new Dictionary<ProjectModel, List<PackageReference>>();
        foreach (var (p, value, raw) in Assignments(m, packageRefs, "--package-ref", errors))
        {
            if (Validators.PackageReference(value) is { } e) { errors.Add($"--package-ref '{raw}': {e}"); continue; }
            var pr = Validators.ParsePackageReference(value);
            var list = pkgSets.GetOrAdd(p);
            if (list.Any(x => x.Id.Equals(pr.Id, StringComparison.OrdinalIgnoreCase))) { errors.Add($"--package-ref '{raw}': '{pr.Id}' is already referenced by {p.Name}."); continue; }
            list.Add(pr);
        }
        foreach (var (p, list) in pkgSets) p.Packages.Set(list, SlotSource.Flag);

        return errors;
    }

    private static IEnumerable<(ProjectModel Project, string Value, string Raw)> Assignments(ProjectsModel m, IEnumerable<string>? values, string flag, List<string> errors)
    {
        foreach (var raw in values ?? [])
        {
            var eq = raw.IndexOf('=');
            if (eq <= 0 || eq == raw.Length - 1) { errors.Add($"{flag} '{raw}' must have the form <Project>=<value>."); continue; }
            var project = m.Find(raw[..eq].Trim());
            if (project is null) { errors.Add($"{flag} '{raw}': no such project."); continue; }
            yield return (project, raw[(eq + 1)..].Trim(), raw);
        }
    }

    public async Task<IReadOnlyList<string>> RunAsync(ProjectsModel m, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[6/9]] Projects[/]").LeftJustified());
        ApplyDefaults(m);
        console.Write(ProjectTable(m));

        if (prompter is not null)
        {
            while (!await prompter.ConfirmAsync("Use these project settings?", true, ct))
            {
                var project = m.Projects.Count == 1
                    ? m.Projects[0]
                    : await prompter.SelectAsync("Edit project:", m.Projects, p => p.Name, ct);
                var groups = await prompter.MultiSelectAsync($"Change what for {project.Name}?", Enum.GetValues<Group>(), GroupLabel, ct);
                foreach (var g in groups)
                    await EditAsync(m, project, g, ct);
                console.Write(ProjectTable(m));
            }
        }

        return Issues(m).ToList();
    }

    private void ApplyDefaults(ProjectsModel m)
    {
        foreach (var p in m.Projects)
        {
            if (!p.Binding.IsResolved)
                p.Binding.Set(p.Role switch
                {
                    ProjectRole.ApplicationLibrary => VlBinding.Direct,
                    ProjectRole.PackageWrapper => VlBinding.Assembly,
                    _ => VlBinding.None,
                }, SlotSource.Default);
            if (!p.Features.IsResolved) p.Features.Set(new HashSet<ProjectFeature>(), SlotSource.Default);
            if (!p.Tfm.IsResolved) p.Tfm.Set(TfmFor(p.Features.Require()), SlotSource.Default);
            if (!p.References.IsResolved) p.References.Set([], SlotSource.Default);
            if (!p.Packages.IsResolved) p.Packages.Set([], SlotSource.Default);
        }
    }

    /// <summary>The shared framework, with the -windows platform when a feature needs it.</summary>
    private string TfmFor(IReadOnlySet<ProjectFeature> features)
    {
        var tfm = csharp.Tfm.Require();
        var needsWindows = features.Contains(ProjectFeature.Windows) || features.Contains(ProjectFeature.WinForms);
        return needsWindows && !tfm.Contains('-') ? $"{tfm}-windows" : tfm;
    }

    private async Task EditAsync(ProjectsModel m, ProjectModel p, Group group, CancellationToken ct)
    {
        switch (group)
        {
            case Group.Binding:
                var binding = await prompter!.SelectAsync($"VL binding for {p.Name}:", [VlBinding.Direct, VlBinding.Assembly, VlBinding.None], BindingLabel, ct);
                if (binding != p.Binding.Require()) p.Binding.Set(binding, SlotSource.Prompt);
                break;
            case Group.Framework:
                var features = (await prompter!.MultiSelectAsync($"Features for {p.Name}:", Enum.GetValues<ProjectFeature>(), FeatureLabel, ct, p.Features.Require())).ToHashSet();
                if (!features.SetEquals(p.Features.Require()))
                {
                    p.Features.Set(features, SlotSource.Prompt);
                    if (p.Tfm.Source == SlotSource.Default) p.Tfm.Set(TfmFor(features), SlotSource.Default);
                }
                var tfm = (await prompter.TextAsync($"Target framework for {p.Name}:", p.Tfm.Require(), Validators.Tfm, ct)).Trim().ToLowerInvariant();
                if (tfm != p.Tfm.Require()) p.Tfm.Set(tfm, SlotSource.Prompt);
                break;
            case Group.References:
                var others = m.Projects.Where(x => x != p).ToList();
                if (others.Count == 0) { console.MarkupLine("[grey]no other projects to reference[/]"); break; }
                var current = others.Where(x => p.References.Require().Contains(x.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                var picked = await prompter!.MultiSelectAsync($"Local references for {p.Name}:", others, x => x.Name, ct, current);
                var names = picked.Select(x => x.Name).ToList();
                if (!names.SequenceEqual(p.References.Require())) p.References.Set(names, SlotSource.Prompt);
                break;
            case Group.Packages:
                var existing = string.Join(", ", p.Packages.Require());
                var text = await prompter!.TextAsync($"NuGet packages for {p.Name} (e.g. MessagePack@2.5.124, or none):", existing.Length == 0 ? "none" : existing, Validators.PackageReferenceList, ct);
                var list = text.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? []
                    : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(Validators.ParsePackageReference).ToList();
                if (!list.SequenceEqual(p.Packages.Require())) p.Packages.Set(list, SlotSource.Prompt);
                break;
        }
    }

    /// <summary>Blocking problems: direct bindings colliding with a document name, broken or cyclic references, platform mismatches.</summary>
    private IEnumerable<string> Issues(ProjectsModel m)
    {
        var docs = DocumentNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in m.Projects)
        {
            if (p.Binding.Require() == VlBinding.Direct && docs.Contains(p.Name))
                yield return $"{p.Name}: a direct .csproj binding needs a name different from the VL document '{p.Name}'; use the assembly binding or rename the library.";
            var f = p.Features.Require();
            if ((f.Contains(ProjectFeature.Windows) || f.Contains(ProjectFeature.WinForms)) && !p.Tfm.Require().Contains("-windows", StringComparison.OrdinalIgnoreCase))
                yield return $"{p.Name}: the {string.Join("/", f.Where(x => x != ProjectFeature.Stride).Select(FeatureLabel))} feature needs a -windows target framework (for example {csharp.Tfm.Require()}-windows), not {p.Tfm.Require()}.";
            foreach (var r in p.References.Require().Where(r => m.Find(r) is null))
                yield return $"{p.Name} references '{r}', which is not a registered project.";
        }
        foreach (var cycle in Cycles(m))
            yield return $"Reference cycle: {string.Join(" → ", cycle)}.";
    }

    private static IEnumerable<IReadOnlyList<string>> Cycles(ProjectsModel m)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        foreach (var p in m.Projects)
            foreach (var cycle in Visit(p.Name)) yield return cycle;

        IEnumerable<IReadOnlyList<string>> Visit(string name)
        {
            if (state.TryGetValue(name, out var s))
            {
                if (s == 1)
                {
                    var start = stack.FindIndex(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
                    yield return [.. stack.Skip(start), name];
                }
                yield break;
            }
            state[name] = 1;
            stack.Add(name);
            foreach (var r in m.Find(name)?.References.Require() ?? [])
                foreach (var cycle in Visit(r)) yield return cycle;
            stack.RemoveAt(stack.Count - 1);
            state[name] = 2;
        }
    }

    private static Table ProjectTable(ProjectsModel m)
    {
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Project");
        table.AddColumn("Role");
        table.AddColumn("VL binding");
        table.AddColumn("Framework");
        table.AddColumn("References");
        table.AddColumn("Packages");
        foreach (var p in m.Projects)
        {
            var features = p.Features.Require();
            table.AddRow(
                Markup.Escape(p.Name),
                Markup.Escape(RoleLabel(p)),
                Markup.Escape(BindingLabel(p.Binding.Require())),
                Markup.Escape(p.Tfm.Require() + (features.Count == 0 ? "" : $" + {string.Join(", ", features.Select(FeatureLabel))}")),
                Markup.Escape(p.References.Require().Count == 0 ? "—" : string.Join(", ", p.References.Require())),
                Markup.Escape(p.Packages.Require().Count == 0 ? "—" : string.Join(", ", p.Packages.Require())));
        }
        return table;
    }

    private static string RoleLabel(ProjectModel p) => p.Role switch
    {
        ProjectRole.PackageWrapper => $"package {p.OwnerPackageId}",
        ProjectRole.ApplicationLibrary => "application library",
        _ => "helper library",
    };

    private static string BindingLabel(VlBinding b) => b switch
    {
        VlBinding.Direct => "direct (.csproj)",
        VlBinding.Assembly => "assembly (lib\\)",
        _ => "none",
    };

    private static string FeatureLabel(ProjectFeature f) => f switch
    {
        ProjectFeature.Windows => "Windows",
        ProjectFeature.WinForms => "WinForms",
        _ => "Stride",
    };

    private static string GroupLabel(Group g) => g switch
    {
        Group.Binding => "VL binding",
        Group.Framework => "Framework & features",
        Group.References => "Local references",
        _ => "NuGet packages",
    };

    public string Summary(ProjectsModel m) =>
        $"Projects: {string.Join(", ", m.Projects.Select(p => $"{p.Name} ({BindingLabel(p.Binding.Require()).Split(' ')[0]})"))}";
}

file static class DictionaryExtensions
{
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> d, TKey key) where TKey : notnull where TValue : new()
    {
        if (!d.TryGetValue(key, out var v)) d[key] = v = new TValue();
        return v;
    }
}
