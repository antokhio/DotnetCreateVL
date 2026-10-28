using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>Phase 3: local VL packages. Optional for applications, at least one for package repositories.</summary>
internal sealed class PackagesPhase(IAnsiConsole console, IPrompter? prompter, BasicsModel basics, ApplicationModel app)
{
    /// <summary>
    /// Applies <c>--package Id[:root]</c>, <c>--package-container</c> and <c>--package-csharp Id</c>.
    /// When <c>--package-csharp</c> is given at all, every flagged package's wrapper choice counts as decided.
    /// </summary>
    public IReadOnlyList<string> ApplyFlags(PackagesModel m, IEnumerable<string>? packages, string? container, IEnumerable<string>? csharp)
    {
        var errors = new List<string>();

        if (container is not null)
        {
            if (Validators.RepositoryFolder(container, allowEmpty: true) is { } e) errors.Add($"--package-container: {e}");
            else m.Container.Set(NormalizeFolder(container), SlotSource.Flag);
        }

        foreach (var raw in packages ?? [])
        {
            var colon = raw.IndexOf(':');
            var id = (colon < 0 ? raw : raw[..colon]).Trim();
            var root = colon < 0 ? null : raw[(colon + 1)..].Trim();

            if (Validators.PackageId(id, m.Packages.Select(p => p.Id)) is { } ie) { errors.Add($"--package '{raw}': {ie}"); continue; }
            if (root is not null && Validators.RepositoryFolder(root, allowEmpty: false) is { } re) { errors.Add($"--package '{raw}': {re}"); continue; }

            var package = new PackageModel(id);
            if (root is not null)
                package.Root.Set(NormalizeFolder(root), SlotSource.Flag);
            m.Packages.Add(package);
        }

        if (csharp is not null)
        {
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in csharp)
            {
                if (m.Find(id) is null) errors.Add($"--package-csharp '{id}': no such --package.");
                else wanted.Add(id);
            }
            foreach (var p in m.Packages)
                p.Wrapper.Set(wanted.Contains(p.Id), SlotSource.Flag);
        }

        return errors;
    }

    public async Task<IReadOnlyList<string>> RunAsync(PackagesModel m, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[3/9]] Packages[/]").LeftJustified());
        var isPackageRepo = basics.Kind.Require() == RepositoryKind.Package;

        // Flag-defined packages first: fill their roots and wrapper choices.
        foreach (var p in m.Packages)
            await CompleteAsync(m, p, ct);

        if (prompter is not null)
        {
            var addMore = m.Packages.Count > 0
                ? await prompter.ConfirmAsync("Add another package?", false, ct)
                : isPackageRepo || await prompter.ConfirmAsync("Does this application require an additional VL package (e.g. VL.Shapes)?", false, ct);

            while (addMore)
            {
                var id = await prompter.TextAsync("Package name (e.g. VL.Shapes):", null, v => Validators.PackageId(v, m.Packages.Select(p => p.Id)), ct);
                var p = new PackageModel(id);
                m.Packages.Add(p);
                await CompleteAsync(m, p, ct);
                addMore = await prompter.ConfirmAsync("Add another package?", false, ct);
            }
        }

        return Conflicts(m).ToList();
    }

    private async Task CompleteAsync(PackagesModel m, PackageModel p, CancellationToken ct)
    {
        if (!p.Root.IsResolved)
        {
            var suggested = NormalizeFolder(Path.Combine(m.Container.IsResolved ? m.Container.Require() : "", p.Id));
            if (prompter is null)
            {
                p.Root.Set(suggested, SlotSource.Default);
            }
            else
            {
                var answer = NormalizeFolder(await prompter.TextAsync($"Folder for {p.Id}:", suggested,
                    v => Validators.RepositoryFolder(v, allowEmpty: false) ?? UniqueRoot(m, p, v), ct));
                p.Root.Set(answer, answer == suggested ? SlotSource.Default : SlotSource.Prompt);
            }
        }

        // The first root decides where later packages default to, unless --package-container said otherwise.
        if (!m.Container.IsResolved)
            m.Container.Set(NormalizeFolder(Path.GetDirectoryName(p.Root.Require()) ?? ""), SlotSource.Default);

        if (!p.Wrapper.IsResolved)
        {
            if (prompter is null)
                p.Wrapper.Set(false, SlotSource.Default);
            else
            {
                var answer = await prompter.ConfirmAsync($"Add a C# project to {p.Id} ({p.WrapperProjectPath})?", false, ct);
                p.Wrapper.Set(answer, answer ? SlotSource.Prompt : SlotSource.Default);
            }
        }

        console.MarkupLine($"  [grey]•[/] {Markup.Escape(Describe(p))}");
    }

    private static string? UniqueRoot(PackagesModel m, PackageModel self, string candidate)
    {
        var normalized = NormalizeFolder(candidate);
        return m.Packages.Any(o => o != self && o.Root.IsResolved && string.Equals(o.Root.Require(), normalized, StringComparison.OrdinalIgnoreCase))
            ? "Another package already uses this root."
            : null;
    }

    private IEnumerable<string> Conflicts(PackagesModel m)
    {
        var resolved = m.Packages.Where(p => p.Root.IsResolved).ToList();
        foreach (var g in resolved.GroupBy(p => p.Root.Require(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            yield return $"Packages {string.Join(", ", g.Select(p => p.Id))} share the same root '{g.Key}'.";

        if (app.EntryPath.IsResolved)
        {
            var entry = RepositoryPaths.Normalize(app.EntryPath.Require());
            foreach (var p in resolved.Where(p => string.Equals(RepositoryPaths.Normalize(p.DocumentPath), entry, StringComparison.OrdinalIgnoreCase)))
                yield return $"Package {p.Id}'s document '{p.DocumentPath}' is also the application entry.";
        }
    }

    private static string NormalizeFolder(string folder) =>
        RepositoryPaths.Normalize(folder).TrimEnd('\\');

    private static string Describe(PackageModel p) =>
        $"{p.Id} → {p.DocumentPath}{(p.HasWrapper ? ", C# wrapper" : "")}";

    public string Summary(PackagesModel m) =>
        m.Packages.Count == 0
            ? "Packages: none"
            : $"Packages: {string.Join(", ", m.Packages.Select(p => p.Id + (p.HasWrapper ? " (C#)" : "")))}";
}
