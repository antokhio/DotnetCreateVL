namespace DotnetCreateVL.Model;

public enum ProjectRole
{
    ApplicationLibrary,
    HelperLibrary,
    PackageWrapper,
}

/// <summary>How VL documents reach the project's nodes.</summary>
public enum VlBinding
{
    /// <summary>The .vl references the .csproj; vvvv compiles it live.</summary>
    Direct,
    /// <summary>The .vl imports the built assembly from the output folder.</summary>
    Assembly,
    /// <summary>Internal helper; no VL document references it.</summary>
    None,
}

public enum ProjectFeature
{
    Windows,
    WinForms,
    Stride,
}

public sealed record PackageReference(string Id, string Version)
{
    public override string ToString() => $"{Id}@{Version}";
}

/// <summary>One authored C# class library: an application/helper library or a package wrapper.</summary>
public sealed class ProjectModel(string name, ProjectRole role, string? ownerPackageId = null)
{
    public string Name { get; } = name;
    public ProjectRole Role { get; } = role;
    public string? OwnerPackageId { get; } = ownerPackageId;
    public Slot<string> ProjectPath { get; } = new($"project.{name}.path", $"--project-path {name}=<csproj>", "repository-relative .csproj path");
    public Slot<string> OutputBase { get; } = new($"project.{name}.output", $"--output-base {name}=<dir>", "repository-relative folder");
    public Slot<VlBinding> Binding { get; } = new($"project.{name}.binding", $"--vl-binding {name}=<binding>", "direct | assembly | none");
    public Slot<string> Tfm { get; } = new($"project.{name}.tfm", $"--project-tfm {name}=<tfm>", "target framework moniker");
    public Slot<IReadOnlySet<ProjectFeature>> Features { get; } = new($"project.{name}.features", $"--feature {name}=<feature>", "windows | winforms | stride");
    public Slot<IReadOnlyList<string>> References { get; } = new($"project.{name}.references", $"--ref {name}=<Project>", "names of other registered projects");
    public Slot<IReadOnlyList<PackageReference>> Packages { get; } = new($"project.{name}.packages", $"--package-ref {name}=<Id>@<Version>", "NuGet package id and exact version");

    public bool IsWrapper => Role == ProjectRole.PackageWrapper;

    /// <summary>Projects that VL documents reference need the VL.Core dependency; helpers do not.</summary>
    public bool ExposesVL => Binding.IsResolved && Binding.Require() != VlBinding.None;

    public IEnumerable<MissingSlot> Missing()
    {
        if (!ProjectPath.IsResolved) yield return ProjectPath.AsMissing();
        if (!OutputBase.IsResolved) yield return OutputBase.AsMissing();
    }

    public IEnumerable<MissingSlot> MissingReview()
    {
        if (!Binding.IsResolved) yield return Binding.AsMissing();
        if (!Tfm.IsResolved) yield return Tfm.AsMissing();
        if (!Features.IsResolved) yield return Features.AsMissing();
        if (!References.IsResolved) yield return References.AsMissing();
        if (!Packages.IsResolved) yield return Packages.AsMissing();
    }
}

/// <summary>Phase 4 slots: every C# project in the repository, wrappers included, with their layout.</summary>
public sealed class ProjectsModel
{
    public List<ProjectModel> Projects { get; } = [];

    public IEnumerable<ProjectModel> Libraries => Projects.Where(p => !p.IsWrapper);

    public ProjectModel? Find(string name) =>
        Projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<MissingSlot> Missing() => Projects.SelectMany(p => p.Missing());

    public IEnumerable<MissingSlot> MissingReview() => Projects.SelectMany(p => p.MissingReview());
}
