using System.Text;

namespace DotnetCreateVL.Generate;

internal static class PlanWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes every Create/Update entry. Folders are created first so files never race their parents.</summary>
    public static IReadOnlyList<PlannedEntry> Write(GenerationPlan plan)
    {
        var written = new List<PlannedEntry>();
        Directory.CreateDirectory(plan.Destination);
        foreach (var folder in plan.Entries.Where(e => e.IsFolder && e.Action == PlannedAction.Create))
        {
            Directory.CreateDirectory(plan.Full(folder.Path));
            written.Add(folder);
        }
        foreach (var file in plan.Files.Where(e => e.Writes))
        {
            var full = plan.Full(file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, file.Content ?? "", Utf8NoBom);
            written.Add(file);
        }
        return written;
    }
}
