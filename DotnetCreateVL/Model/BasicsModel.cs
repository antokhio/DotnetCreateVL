namespace DotnetCreateVL.Model;

public enum RepositoryKind
{
    Application,
    Package,
}

public enum PipelineBackend
{
    None,
    Just,
    Cmd,
}

/// <summary>Phase 1 slots. Every slot maps 1:1 to a CLI flag of <c>dotnet create vl</c>.</summary>
public sealed class BasicsModel
{
    public Slot<RepositoryKind> Kind { get; } = new("repo.kind", "--kind", "application | package");
    public Slot<string> Name { get; } = new("repo.name", "--name", "valid Windows folder name");
    public Slot<string> Destination { get; } = new("repo.destination", "--output", "directory path");
    public Slot<VvvvInstallation> Vvvv { get; } = new("vvvv.install", "--vvvv-dir", "vvvv installation directory");
    public Slot<PipelineBackend> Pipeline { get; } = new("pipeline.backend", "--pipeline", "none | just | cmd");

    public IEnumerable<MissingSlot> Missing()
    {
        if (!Kind.IsResolved) yield return Kind.AsMissing();
        if (!Name.IsResolved) yield return Name.AsMissing();
        if (!Destination.IsResolved) yield return Destination.AsMissing();
        if (!Vvvv.IsResolved) yield return Vvvv.AsMissing();
        if (!Pipeline.IsResolved) yield return Pipeline.AsMissing();
    }
}
