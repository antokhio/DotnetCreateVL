namespace DotnetCreateVL.Model;

public enum BuildOperation
{
    /// <summary>Build the prerequisites, then export the entry patch with vvvvc.</summary>
    Export,
    /// <summary>Build the prerequisite C# projects only.</summary>
    Library,
    None,
}

/// <summary>A folder copied next to the exported application, both relative paths.</summary>
public sealed record StagingRule(string Source, string Destination)
{
    public override string ToString() => Source == Destination ? Source : $"{Source}:{Destination}";
}

/// <summary>One pipeline target: a recipe (Just) or script (cmd) with dev, build and export behaviour.</summary>
public sealed class TargetModel(string name, string ownerLabel, string? entryPath, string? packageId)
{
    public string Name { get; } = name;
    /// <summary>What the target was derived from, for the table: <c>application</c> or <c>package X</c>.</summary>
    public string OwnerLabel { get; } = ownerLabel;
    /// <summary>Repository-relative .vl the dev command opens and the export compiles; null for package targets.</summary>
    public string? EntryPath { get; } = entryPath;
    public string? PackageId { get; } = packageId;

    public Slot<bool> Dev { get; } = new($"target.{name}.dev", $"--target-dev {name}=on|off");
    public Slot<BuildOperation> Build { get; } = new($"target.{name}.build", $"--target-build {name}=<op>", "export | lib | none");
    public Slot<IReadOnlyList<string>> Prerequisites { get; } = new($"target.{name}.prereqs", $"--prereq {name}=<Project>", "registered project names");
    public Slot<string> ExportRoot { get; } = new($"target.{name}.exportRoot", $"--export-root {name}=<dir>", "repository-relative folder");
    public Slot<IReadOnlyList<StagingRule>> Staging { get; } = new($"target.{name}.stage", $"--stage {name}=<src>[:<dst>]", "repository-relative folders");

    public bool IsExport => Build.IsResolved && Build.Require() == BuildOperation.Export;

    /// <summary>Name of the exported application: vvvvc writes <c>&lt;ExportRoot&gt;\&lt;AppName&gt;\&lt;AppName&gt;.exe</c>.</summary>
    public string? AppName => EntryPath is null ? null : Path.GetFileNameWithoutExtension(EntryPath);

    /// <summary>Folder the exported application lands in, <c>&lt;ExportRoot&gt;\&lt;AppName&gt;</c>.</summary>
    public string? AppFolder => IsExport && ExportRoot.IsResolved && AppName is not null ? Path.Combine(ExportRoot.Require(), AppName) : null;

    public IEnumerable<MissingSlot> Missing()
    {
        if (!Dev.IsResolved) yield return Dev.AsMissing();
        if (!Build.IsResolved) yield return Build.AsMissing();
        if (!Prerequisites.IsResolved) yield return Prerequisites.AsMissing();
        if (!ExportRoot.IsResolved) yield return ExportRoot.AsMissing();
        if (!Staging.IsResolved) yield return Staging.AsMissing();
    }
}

/// <summary>Phase 8 slots: shared vvvv launch/export settings plus the derived targets.</summary>
public sealed class PipelineModel
{
    public const string ConventionNugetPath = "..\\__nugets";
    public const string ConventionExportSources = "..\\__sources";
    /// <summary>Default export root; vvvvc places the app in <c>bin\&lt;App&gt;\</c> and its generated C# in <c>bin\src\</c>.</summary>
    public const string DefaultExportRoot = "bin";

    /// <summary>Folder passed as <c>--nuget-path</c>; empty means vvvv's default.</summary>
    public Slot<string> NugetPath { get; } = new("pipeline.nugetPath", "--nuget-path", "folder or none");
    /// <summary>Folder passed as <c>--export-package-sources</c>; empty means not passed.</summary>
    public Slot<string> ExportSources { get; } = new("pipeline.exportSources", "--export-sources", "folder or none");
    public Slot<IReadOnlyList<string>> PackageRepositories { get; } = new("pipeline.packageRepos", "--package-repo", "repository-relative folders or none");
    public Slot<IReadOnlyList<string>> Editable { get; } = new("pipeline.editable", "--editable", "package id patterns or none");
    public Slot<string> DefaultTarget { get; } = new("pipeline.defaultTarget", "--default-target", "target name or all");
    public List<TargetModel> Targets { get; } = [];

    public TargetModel? Find(string name) =>
        Targets.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<MissingSlot> Missing()
    {
        if (!NugetPath.IsResolved) yield return NugetPath.AsMissing();
        if (!ExportSources.IsResolved) yield return ExportSources.AsMissing();
        if (!PackageRepositories.IsResolved) yield return PackageRepositories.AsMissing();
        if (!Editable.IsResolved) yield return Editable.AsMissing();
        if (!DefaultTarget.IsResolved) yield return DefaultTarget.AsMissing();
        foreach (var t in Targets)
            foreach (var m in t.Missing())
                yield return m;
    }
}
