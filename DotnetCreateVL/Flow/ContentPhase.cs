using DotnetCreateVL.Discovery;
using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>
/// Phase 7: housekeeping files (one MultiSelect with the common ones ticked), the license when it is ticked,
/// and optional content folders (one MultiSelect; assets / per-package help ticked). Paths are fixed; flags can override them.
/// </summary>
internal sealed class ContentPhase(IAnsiConsole console, IPrompter? prompter, BasicsModel basics, PackagesModel packages,
    ProjectsModel projects, CSharpModel csharp, Func<CancellationToken, Task<string?>>? gitUserName = null)
{
    private readonly Func<CancellationToken, Task<string?>> gitUserName = gitUserName ?? GitIdentity.UserNameAsync;
    private readonly List<string> warnings = [];
    private string? licenseSpdx;
    private string? licenseHolder;

    public IReadOnlyList<string> Warnings => warnings;

    private bool IsApplication => basics.Kind.Require() == RepositoryKind.Application;
    private bool HasProjects => projects.Projects.Count > 0;

    private IReadOnlyList<RepoFile> ApplicableFiles()
    {
        var files = new List<RepoFile> { RepoFile.GitIgnore, RepoFile.GitAttributes };
        if (HasProjects) files.AddRange([RepoFile.NuGetConfig, RepoFile.CSharpier]);
        if (!IsApplication) files.Add(RepoFile.License);
        return files;
    }

    private string? Inapplicable(RepoFile f) => f switch
    {
        RepoFile.NuGetConfig or RepoFile.CSharpier when !HasProjects => $"{FileLabel(f)} applies only when the repository has C# projects.",
        RepoFile.License when IsApplication => "LICENSE applies to package repositories only.",
        _ => null,
    };

    private static readonly FolderKind[] ApplicationFolders = [FolderKind.Assets, FolderKind.Rnd, FolderKind.Vl, FolderKind.Resources, FolderKind.Sandbox, FolderKind.Docs];
    private static readonly FolderKind[] PackageFolders = [FolderKind.Help, FolderKind.Assets];

    private IReadOnlyList<ContentFolder> ApplicableFolders()
    {
        var folders = new List<ContentFolder>();
        if (IsApplication)
            folders.AddRange(ApplicationFolders.Select(k => new ContentFolder(k, null, k.ToString().ToLowerInvariant())));
        foreach (var p in packages.Packages)
            folders.AddRange(PackageFolders.Select(k => new ContentFolder(k, p.Id, Path.Combine(p.Root.Require(), k.ToString().ToLowerInvariant()))));
        return folders;
    }

    /// <summary>Preselected folders: <c>assets</c> for applications, <c>help</c> for every package.</summary>
    private static bool IsDefault(ContentFolder f) => f.Kind is FolderKind.Assets && f.PackageId is null || f.Kind is FolderKind.Help;

    public IReadOnlyList<string> ApplyFlags(ContentModel m, IEnumerable<string>? repoFiles, string? license, string? holder, IEnumerable<string>? folders)
    {
        var errors = new List<string>();

        if (repoFiles is not null)
        {
            var set = new HashSet<RepoFile>();
            foreach (var item in repoFiles)
            {
                if (item.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)) { set.Clear(); continue; }
                if (Validators.ParseRepoFile(item) is not { } f) { errors.Add($"--repo-files '{item}': expected gitignore, gitattributes, nuget-config, csharpier, license or none."); continue; }
                if (Inapplicable(f) is { } why) { errors.Add($"--repo-files '{item}': {why}"); continue; }
                set.Add(f);
            }
            m.RepoFiles.Set(set, SlotSource.Flag);
        }

        if (license is not null)
        {
            if (Validators.ParseLicense(license) is not { } spdx)
                errors.Add($"--license '{license}': expected one of {string.Join(", ", LicenseChoice.KnownSpdx)}.");
            else if (IsApplication)
                errors.Add("--license: LICENSE applies to package repositories only.");
            else if (m.RepoFiles.IsResolved && !m.Wants(RepoFile.License))
                errors.Add("--license needs license in --repo-files.");
            else
                licenseSpdx = spdx;
        }
        if (holder is not null)
        {
            if (Validators.LicenseHolder(holder) is { } e) errors.Add($"--license-holder: {e}");
            else licenseHolder = holder.Trim();
        }

        if (folders is not null)
        {
            var applicable = ApplicableFolders();
            var list = new List<ContentFolder>();
            foreach (var item in folders)
            {
                var raw = item.Trim();
                if (raw.Equals("none", StringComparison.OrdinalIgnoreCase)) { list.Clear(); continue; }
                var eq = raw.IndexOf('=');
                var key = eq < 0 ? raw : raw[..eq].Trim();
                var path = eq < 0 ? null : raw[(eq + 1)..].Trim();
                var folder = applicable.FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (folder is null)
                {
                    var colon = key.IndexOf(':');
                    var kind = colon < 0 ? key : key[..colon];
                    var knownKind = Enum.GetNames<FolderKind>().Any(n => n.Equals(kind, StringComparison.OrdinalIgnoreCase));
                    errors.Add(knownKind && colon >= 0 && !packages.Packages.Any(p => p.Id.Equals(key[(colon + 1)..], StringComparison.OrdinalIgnoreCase))
                        ? $"--folders '{item}': no package named '{key[(colon + 1)..]}'."
                        : $"--folders '{item}': expected {string.Join(", ", applicable.Select(f => f.Key))} or none.");
                    continue;
                }
                if (path is not null)
                {
                    if (Validators.RepositoryFolder(path, allowEmpty: false) is { } e) { errors.Add($"--folders '{item}': {e}"); continue; }
                    folder = folder with { Path = Normalize(path) };
                }
                if (list.Any(f => f.Key == folder.Key)) { errors.Add($"--folders '{item}': {folder.Key} is listed twice."); continue; }
                list.Add(folder);
            }
            m.Folders.Set(list, SlotSource.Flag);
        }

        return errors;
    }

    private static string Normalize(string path) =>
        string.Join('\\', path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Where(s => s != "."));

    public async Task<IReadOnlyList<string>> RunAsync(ContentModel m, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[7/9]] Content[/]").LeftJustified());

        var applicable = ApplicableFiles();
        var defaults = applicable.Where(f => f is RepoFile.GitIgnore or RepoFile.GitAttributes or RepoFile.NuGetConfig).ToHashSet();
        if (licenseSpdx is not null) defaults.Add(RepoFile.License);
        if (!m.RepoFiles.IsResolved)
        {
            if (prompter is not null)
                m.RepoFiles.Set((await prompter.MultiSelectAsync("Repository files:", applicable, FileLabel, ct, defaults)).ToHashSet(), SlotSource.Prompt);
            else
                m.RepoFiles.Set(defaults, SlotSource.Default);
        }

        if (m.Wants(RepoFile.License) && !m.License.IsResolved)
        {
            var spdx = licenseSpdx;
            if (spdx is null && prompter is not null)
                spdx = await prompter.SelectAsync("License:", LicenseChoice.KnownSpdx, s => s, ct);
            if (spdx is not null)
            {
                var holder = licenseHolder ?? await gitUserName(ct);
                if (licenseHolder is null && prompter is not null)
                    holder = (await prompter.TextAsync("Copyright holder:", holder, Validators.LicenseHolder, ct, allowEmpty: true)).Trim();
                m.License.Set(new LicenseChoice(spdx, string.IsNullOrWhiteSpace(holder) ? null : holder),
                    licenseSpdx is not null && (licenseHolder is not null || prompter is null) ? SlotSource.Flag : SlotSource.Prompt);
            }
        }

        if (!m.Folders.IsResolved)
        {
            var choices = ApplicableFolders();
            var preselected = choices.Where(IsDefault).ToList();
            if (prompter is not null)
                m.Folders.Set((await prompter.MultiSelectAsync("Optional folders:", choices, f => f.Label, ct, preselected)).ToList(), SlotSource.Prompt);
            else
                m.Folders.Set(preselected, SlotSource.Default);
        }

        if (HasProjects && !m.Wants(RepoFile.NuGetConfig) && csharp.Sources.IsResolved
            && !csharp.Sources.Require().SequenceEqual([NuGetSource.VvvvPublicFeed]))
            warnings.Add("NuGet.config is not generated, so the configured NuGet sources will not be written anywhere.");

        return Issues(m).ToList();
    }

    /// <summary>Blocking problems: a folder path that is an existing file, or two folders on the same path.</summary>
    private IEnumerable<string> Issues(ContentModel m)
    {
        var folders = m.Folders.Require();
        foreach (var dup in folders.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            yield return $"Folders {string.Join(" and ", dup.Select(f => f.Key))} share the path {dup.Key}\\.";
        var destination = basics.Destination.Require();
        foreach (var f in folders)
        {
            if (File.Exists(Path.Combine(destination, f.Path)))
                yield return $"{f.Path} exists as a file; the {f.Key} folder cannot be created there.";
        }
    }

    public static string FileLabel(RepoFile f) => f switch
    {
        RepoFile.GitIgnore => ".gitignore",
        RepoFile.GitAttributes => ".gitattributes",
        RepoFile.NuGetConfig => "NuGet.config",
        RepoFile.CSharpier => "CSharpier (.csharpierrc + .config/dotnet-tools.json)",
        _ => "LICENSE",
    };

    public string Summary(ContentModel m)
    {
        var files = m.RepoFiles.Require().OrderBy(f => f).Select(f => f == RepoFile.License && m.License.IsResolved
            ? $"LICENSE ({m.License.Require().Spdx})"
            : FileLabel(f).Split(' ')[0]).ToList();
        var folders = m.Folders.Require();
        return $"Content: {(files.Count == 0 ? "no files" : string.Join(", ", files))} · {(folders.Count == 0 ? "no folders" : string.Join(", ", folders))}";
    }
}
