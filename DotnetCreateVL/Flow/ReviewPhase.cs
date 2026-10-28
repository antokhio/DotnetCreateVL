using DotnetCreateVL.Generate;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

public enum ReviewDecision
{
    Generate,
    Declined,
    DryRun,
    /// <summary>No terminal and no <c>--yes</c>: the plan was shown but nothing may be written.</summary>
    NeedsApproval,
}

/// <summary>Phase 9: show the destination tree once and ask a single question.</summary>
internal sealed class ReviewPhase(IAnsiConsole console, IPrompter? prompter)
{
    public async Task<ReviewDecision> RunAsync(GenerationPlan plan, bool yes, bool dryRun, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[9/9]] Review[/]").LeftJustified());
        console.Write(BuildTree(plan));
        console.Write(Legend(plan));

        if (dryRun) return ReviewDecision.DryRun;
        if (yes) return ReviewDecision.Generate;
        if (prompter is null) return ReviewDecision.NeedsApproval;
        return await prompter.ConfirmAsync("Generate?", true, ct) ? ReviewDecision.Generate : ReviewDecision.Declined;
    }

    /// <summary>Folders first, files after, each level sorted; folders only appear when something lives in them.</summary>
    public static Tree BuildTree(GenerationPlan plan)
    {
        var root = new Node("");
        foreach (var e in plan.Entries.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            var parts = e.Path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            for (var i = 0; i < parts.Length - 1; i++) node = node.Child(parts[i]);
            if (e.IsFolder) node.Child(parts[^1]).Entry ??= e;
            else node.Child(parts[^1]).Entry = e;
        }

        var tree = new Tree($"[bold]{Markup.Escape(plan.Destination)}\\[/]").Guide(TreeGuide.Line);
        foreach (var child in root.Ordered()) Render(child, tree.AddNode(Label(child)));
        return tree;
    }

    private static void Render(Node node, TreeNode target)
    {
        foreach (var child in node.Ordered()) Render(child, target.AddNode(Label(child)));
    }

    private static string Label(Node node)
    {
        var isFolder = node.Children.Count > 0 || node.Entry?.IsFolder == true;
        var name = Markup.Escape(node.Name) + (isFolder ? "\\" : "");
        var e = node.Entry;
        if (e is null) return $"[grey]{name}[/]";
        var color = e.Action switch
        {
            PlannedAction.Create => "green",
            PlannedAction.Update => "yellow",
            _ => "grey",
        };
        var note = e.Action switch
        {
            PlannedAction.Create => null,
            PlannedAction.Keep => e.IsFolder ? null : e.Note ?? "exists",
            _ => e.Note,
        };
        return $"[{color}]{name}[/]" + (note is null ? "" : $" [grey]({Markup.Escape(note)})[/]");
    }

    private static Markup Legend(GenerationPlan plan)
    {
        var files = plan.Files.ToList();
        var counts = new List<string>();
        void Count(PlannedAction a, string color, string word)
        {
            var n = files.Count(f => f.Action == a);
            if (n > 0) counts.Add($"[{color}]{n} {word}[/]");
        }
        Count(PlannedAction.Create, "green", "new");
        Count(PlannedAction.Update, "yellow", "updated");
        Count(PlannedAction.Keep, "grey", "kept");
        return new Markup(string.Join(" · ", counts) + Environment.NewLine);
    }

    private sealed class Node(string name)
    {
        public string Name { get; } = name;
        public PlannedEntry? Entry { get; set; }
        public Dictionary<string, Node> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Node Child(string name) => Children.TryGetValue(name, out var n) ? n : Children[name] = new Node(name);

        public IEnumerable<Node> Ordered() => Children.Values
            .OrderByDescending(c => c.Children.Count > 0 || c.Entry?.IsFolder == true)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }
}
