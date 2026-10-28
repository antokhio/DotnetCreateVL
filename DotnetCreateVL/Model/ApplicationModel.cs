namespace DotnetCreateVL.Model;

/// <summary>Phase 2 slots. Only applicable when the repository kind is Application.</summary>
public sealed class ApplicationModel
{
    public Slot<string> Name { get; } = new("app.name", "--app-name", "valid document name");
    public Slot<string> EntryPath { get; } = new("app.entry.path", "--entry", "repository-relative .vl path");

    public IEnumerable<MissingSlot> Missing()
    {
        if (!Name.IsResolved) yield return Name.AsMissing();
        if (!EntryPath.IsResolved) yield return EntryPath.AsMissing();
    }
}
