namespace DotnetCreateVL.Generate;

public enum PlannedAction
{
    /// <summary>A new file or folder.</summary>
    Create,
    /// <summary>An existing file whose content is left untouched (identical or not ours to change).</summary>
    Keep,
    /// <summary>An existing file that receives additions (ignore block, attributes, sources, tools).</summary>
    Update,
}

/// <summary>One repository-relative file or folder the generator will produce or report.</summary>
public sealed record PlannedEntry(string Path, PlannedAction Action, string? Content = null, bool IsFolder = false, string? Note = null)
{
    public bool Writes => Action is PlannedAction.Create or PlannedAction.Update;
}

/// <summary>Everything generation will do, computed before anything is written so it can be previewed.</summary>
public sealed class GenerationPlan(string destination)
{
    public string Destination { get; } = destination;
    public List<PlannedEntry> Entries { get; } = [];
    /// <summary>Commands the README and the final report tell the user about, in order.</summary>
    public List<(string Command, string Description)> Commands { get; } = [];

    public IEnumerable<PlannedEntry> Files => Entries.Where(e => !e.IsFolder);
    public IEnumerable<PlannedEntry> Written => Entries.Where(e => e.Writes);

    public void Folder(string path) => Entries.Add(new PlannedEntry(path, Directory.Exists(Full(path)) ? PlannedAction.Keep : PlannedAction.Create, IsFolder: true));

    /// <summary>A file that is written when missing and otherwise kept as it is.</summary>
    public void File(string path, string content, string? note = null)
    {
        if (System.IO.File.Exists(Full(path)))
            Entries.Add(new PlannedEntry(path, PlannedAction.Keep, Note: note ?? "exists"));
        else
            Entries.Add(new PlannedEntry(path, PlannedAction.Create, content, Note: note));
    }

    /// <summary>A file whose existing content is merged with ours; <paramref name="merge"/> returns null when nothing changes.</summary>
    public void Merge(string path, string fresh, Func<string, string?> merge, string updateNote)
    {
        var full = Full(path);
        if (!System.IO.File.Exists(full)) { Entries.Add(new PlannedEntry(path, PlannedAction.Create, fresh)); return; }
        var merged = merge(System.IO.File.ReadAllText(full));
        Entries.Add(merged is null
            ? new PlannedEntry(path, PlannedAction.Keep, Note: "exists")
            : new PlannedEntry(path, PlannedAction.Update, merged, Note: updateNote));
    }

    public string Full(string relative) => System.IO.Path.Combine(Destination, relative);
}
