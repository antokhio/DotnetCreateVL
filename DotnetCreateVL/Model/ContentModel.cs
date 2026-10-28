namespace DotnetCreateVL.Model;

public enum RepoFile
{
    GitIgnore,
    GitAttributes,
    NuGetConfig,
    CSharpier,
    License,
}

public enum FolderKind
{
    Assets,
    Rnd,
    Vl,
    Resources,
    Sandbox,
    Docs,
    Help,
}

public sealed record LicenseChoice(string Spdx, string? Holder)
{
    public static readonly IReadOnlyList<string> KnownSpdx = ["MIT", "Apache-2.0", "BSD-3-Clause", "LGPL-3.0-or-later"];

    public override string ToString() => Holder is null ? Spdx : $"{Spdx} ({Holder})";
}

/// <summary>An optional content folder, either at the repository root or inside one package root.</summary>
public sealed record ContentFolder(FolderKind Kind, string? PackageId, string Path)
{
    public string KindName => Kind.ToString().ToLowerInvariant();

    public string Key => PackageId is null ? KindName : $"{KindName}:{PackageId}";

    public string Label => $"{Path}\\";

    public override string ToString() => $"{Path}\\";
}

/// <summary>Phase 7 slots: housekeeping files and optional content folders.</summary>
public sealed class ContentModel
{
    public Slot<IReadOnlySet<RepoFile>> RepoFiles { get; } = new("content.repoFiles", "--repo-files", "gitignore, gitattributes, nuget-config, csharpier, license | none");
    public Slot<LicenseChoice> License { get; } = new("content.license", "--license <SPDX> [--license-holder <name>]", string.Join(", ", LicenseChoice.KnownSpdx));
    public Slot<IReadOnlyList<ContentFolder>> Folders { get; } = new("content.folders", "--folders", "assets, rnd, vl, resources, sandbox, docs (applications) | help:<Id>, assets:<Id> (package repositories); [=path] | none");

    public bool Wants(RepoFile f) => RepoFiles.IsResolved && RepoFiles.Require().Contains(f);

    public IEnumerable<MissingSlot> Missing()
    {
        if (!RepoFiles.IsResolved) yield return RepoFiles.AsMissing();
        if (Wants(RepoFile.License) && !License.IsResolved) yield return License.AsMissing();
        if (!Folders.IsResolved) yield return Folders.AsMissing();
    }
}
