using DotnetCreateVL.Discovery;
using DotnetCreateVL.Model;
using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>Phase 1: repository kind, name, destination, vvvv installation, pipeline backend.</summary>
internal sealed class BasicsPhase(IAnsiConsole console, IPrompter? prompter, DiscoveredEnvironment env)
{
    private const string ManualEntry = "Enter installation path…";

    public List<string> Warnings { get; } = [];

    /// <summary>Applies CLI flag values. Returns validation errors; nothing is guessed.</summary>
    public IReadOnlyList<string> ApplyFlags(BasicsModel m, string? kind, string? name, string? output, string? vvvvDir, string? pipeline)
    {
        var errors = new List<string>();

        if (kind is not null)
        {
            if (Validators.ParseKind(kind) is { } k) m.Kind.Set(k, SlotSource.Flag);
            else errors.Add($"--kind '{kind}' is not valid; expected application | package.");
        }

        if (name is not null)
        {
            if (Validators.FolderName(name) is { } e) errors.Add($"--name: {e}");
            else m.Name.Set(name, SlotSource.Flag);
        }

        if (output is not null)
        {
            if (Validators.DestinationPath(output) is { } e) errors.Add($"--output: {e}");
            else m.Destination.Set(Path.GetFullPath(output), SlotSource.Flag);
        }

        if (vvvvDir is not null)
        {
            if (Validators.VvvvDirectory(vvvvDir) is { } e) errors.Add($"--vvvv-dir: {e}");
            else m.Vvvv.Set(VvvvInstallation.FromDirectory(vvvvDir, "--vvvv-dir"), SlotSource.Flag);
        }

        if (pipeline is not null)
        {
            if (Validators.ParsePipeline(pipeline) is { } p) m.Pipeline.Set(p, SlotSource.Flag);
            else errors.Add($"--pipeline '{pipeline}' is not valid; expected none | just | cmd.");
        }

        return errors;
    }

    /// <summary>Resolves remaining slots by prompting. Without a prompter, unresolved slots stay unresolved. Returns blocking issues.</summary>
    public async Task<IReadOnlyList<string>> RunAsync(BasicsModel m, CancellationToken ct)
    {
        console.Write(new Rule("[bold][[1/9]] Basics[/]").LeftJustified());

        if (!m.Kind.IsResolved && prompter is not null)
        {
            var kind = await prompter.SelectAsync("What are you creating?",
                [RepositoryKind.Application, RepositoryKind.Package],
                k => k == RepositoryKind.Application ? "Application" : "Package repository", ct);
            m.Kind.Set(kind, SlotSource.Prompt);
        }

        if (!m.Name.IsResolved && prompter is not null)
        {
            var name = await prompter.TextAsync("Repository name:", null, Validators.FolderName, ct);
            m.Name.Set(name, SlotSource.Prompt);
        }

        if (!m.Destination.IsResolved && m.Name.IsResolved)
        {
            var suggested = Path.Combine(env.WorkingDirectory, m.Name.Require());
            if (prompter is null)
            {
                m.Destination.Set(suggested, SlotSource.Default);
            }
            else
            {
                var answer = await prompter.TextAsync("Destination:", suggested, Validators.DestinationPath, ct);
                var full = Path.GetFullPath(answer);
                m.Destination.Set(full, string.Equals(full, suggested, StringComparison.OrdinalIgnoreCase) ? SlotSource.Default : SlotSource.Prompt);
            }
        }

        var errors = new List<string>();
        if (m.Destination.IsResolved && Validators.DestinationPath(m.Destination.Require()) is { } destinationError)
            errors.Add($"{m.Destination.Require()}: {destinationError}");

        if (!m.Vvvv.IsResolved && prompter is not null)
            await PromptVvvvAsync(m, ct);

        if (m.Vvvv.IsResolved)
        {
            var v = m.Vvvv.Require();
            if (!v.HasEditor) Warnings.Add($"vvvv.exe not found in {v.Path}; dev commands cannot be validated.");
            if (!v.HasCompiler) Warnings.Add($"vvvvc.exe not found in {v.Path}; export cannot be validated.");
        }

        if (!m.Pipeline.IsResolved && prompter is not null)
        {
            var backend = await prompter.SelectAsync("Pipeline files:",
                [PipelineBackend.None, PipelineBackend.Just, PipelineBackend.Cmd],
                b => b switch { PipelineBackend.None => "None", PipelineBackend.Just => "Just", _ => "CMD" }, ct);
            m.Pipeline.Set(backend, SlotSource.Prompt);
        }

        if (m.Pipeline.IsResolved && m.Pipeline.Require() == PipelineBackend.Just && !env.Just.Found)
            Warnings.Add("just was not found on PATH; Just recipe validation will be reported as not performed.");
        return errors;
    }

    private async Task PromptVvvvAsync(BasicsModel m, CancellationToken ct)
    {
        var choices = new List<VvvvChoice>();
        if (env.Vvvv.EnvironmentHint is { } hint)
            choices.Add(new VvvvChoice(hint, $"{Describe(hint)}  (VVVV_DIR)"));
        choices.AddRange(env.Vvvv.Installations.Select(i => new VvvvChoice(i, Describe(i))));
        choices.Add(new VvvvChoice(null, ManualEntry));

        var picked = await prompter!.SelectAsync("Which vvvv installation should this repository use?", choices, c => c.Label, ct);

        if (picked.Installation is { } install)
        {
            m.Vvvv.Set(install, SlotSource.Prompt);
            return;
        }

        var path = await prompter.TextAsync("vvvv installation directory:", null, Validators.VvvvDirectory, ct);
        m.Vvvv.Set(VvvvInstallation.FromDirectory(path, "manual"), SlotSource.Prompt);
    }

    private static string Describe(VvvvInstallation i)
    {
        var flags = new List<string>();
        if (!i.HasEditor) flags.Add("no vvvv.exe");
        if (!i.HasCompiler) flags.Add("no vvvvc.exe");
        var suffix = flags.Count == 0 ? "" : $"  [{string.Join(", ", flags)}]";
        return $"{i.DisplayVersion} — {i.Path}{suffix}";
    }

    public string Summary(BasicsModel m)
    {
        var kind = m.Kind.Require() == RepositoryKind.Application ? "Application" : "Package repository";
        return $"{kind} \"{m.Name.Require()}\" → {m.Destination.Require()} · vvvv {m.Vvvv.Require().DisplayVersion} · pipeline: {m.Pipeline.Require()}";
    }

    private sealed record VvvvChoice(VvvvInstallation? Installation, string Label);
}
