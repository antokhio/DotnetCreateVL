using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>Phase 2: application name and its entry patch. Skipped for package repositories.</summary>
internal sealed class EntriesPhase(IAnsiConsole console, IPrompter? prompter, BasicsModel basics)
{
    /// <summary>Applies <c>--app-name</c> and <c>--entry</c>.</summary>
    public IReadOnlyList<string> ApplyFlags(ApplicationModel m, string? appName, string? entry)
    {
        var errors = new List<string>();

        if (appName is not null)
        {
            if (Validators.DocumentName(appName) is { } e) errors.Add($"--app-name: {e}");
            else m.Name.Set(appName, SlotSource.Flag);
        }

        if (entry is not null)
        {
            if (Validators.RepositoryVlPath(entry) is { } e) errors.Add($"--entry: {e}");
            else m.EntryPath.Set(RepositoryPaths.Normalize(entry), SlotSource.Flag);
        }

        return errors;
    }

    public async Task RunAsync(ApplicationModel m, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[2/9]] Entry[/]").LeftJustified());

        if (basics.Kind.Require() != RepositoryKind.Application)
        {
            console.MarkupLine("[grey]skipped (package repository)[/]");
            return;
        }

        if (!m.Name.IsResolved)
        {
            var suggested = basics.Name.Require();
            var suggestionValid = Validators.DocumentName(suggested) is null;
            if (prompter is null)
            {
                if (suggestionValid) m.Name.Set(suggested, SlotSource.Default);
            }
            else
            {
                var answer = await prompter.TextAsync("Application name:", suggestionValid ? suggested : null, Validators.DocumentName, ct);
                m.Name.Set(answer, answer == suggested ? SlotSource.Default : SlotSource.Prompt);
            }
        }

        if (m.Name.IsResolved && !m.EntryPath.IsResolved)
        {
            var suggested = $"{m.Name.Require()}.vl";
            if (prompter is null)
            {
                m.EntryPath.Set(suggested, SlotSource.Default);
            }
            else
            {
                var answer = await prompter.TextAsync("Entry patch:", suggested, Validators.RepositoryVlPath, ct);
                var normalized = RepositoryPaths.Normalize(answer);
                m.EntryPath.Set(normalized, normalized == suggested ? SlotSource.Default : SlotSource.Prompt);
            }
        }
    }

    public string Summary(ApplicationModel m) => $"Entry: {m.EntryPath.Require()}";
}
