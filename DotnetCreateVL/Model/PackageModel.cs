namespace DotnetCreateVL.Model;

/// <summary>One local VL package: identity, repository-relative root, its generated document and optional C# wrapper.</summary>
public sealed class PackageModel(string id)
{
    public string Id { get; } = id;
    public Slot<string> Root { get; } = new($"pkg.{id}.root", $"--package {id}:<root>", "repository-relative folder");
    public Slot<bool> Wrapper { get; } = new($"pkg.{id}.csharp", "--package-csharp", id);

    public string DocumentPath => Path.Combine(Root.Require(), $"{Id}.vl");
    public string WrapperProjectPath => Path.Combine(Root.Require(), "src", $"{Id}.csproj");
    public bool HasWrapper => Wrapper.IsResolved && Wrapper.Require();

    public IEnumerable<MissingSlot> Missing()
    {
        if (!Root.IsResolved) yield return Root.AsMissing();
        if (!Wrapper.IsResolved) yield return Wrapper.AsMissing();
    }
}

/// <summary>Phase 3 slots: zero or more packages for an application, at least one for a package repository.</summary>
public sealed class PackagesModel
{
    /// <summary>Parent folder new packages default to; set by <c>--package-container</c> or the first prompted root.</summary>
    public Slot<string> Container { get; } = new("packages.container", "--package-container", "repository-relative folder or empty");
    public List<PackageModel> Packages { get; } = [];

    public PackageModel? Find(string id) =>
        Packages.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<MissingSlot> Missing(RepositoryKind kind)
    {
        if (kind == RepositoryKind.Package && Packages.Count == 0)
            yield return new MissingSlot("pkg[0].id", "--package", "<Id>[:<root>]");
        foreach (var p in Packages)
            foreach (var m in p.Missing())
                yield return m;
    }
}
