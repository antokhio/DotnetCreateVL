using Spectre.Console;

namespace DotnetCreateVL.Flow;

/// <summary>
/// Prompt primitives used by every phase. The interactive implementation maps to
/// Spectre.Console; in noninteractive mode no prompter exists and phases record missing slots.
/// </summary>
internal interface IPrompter
{
    Task<T> SelectAsync<T>(string title, IReadOnlyList<T> choices, Func<T, string> label, CancellationToken ct) where T : notnull;

    Task<string> TextAsync(string title, string? defaultValue, Func<string, string?> validate, CancellationToken ct, bool allowEmpty = false);

    Task<bool> ConfirmAsync(string title, bool defaultValue, CancellationToken ct);

    Task<IReadOnlyList<T>> MultiSelectAsync<T>(string title, IReadOnlyList<T> choices, Func<T, string> label, CancellationToken ct, IReadOnlyCollection<T>? selected = null) where T : notnull;
}

internal sealed class SpectrePrompter(IAnsiConsole console) : IPrompter
{
    public async Task<T> SelectAsync<T>(string title, IReadOnlyList<T> choices, Func<T, string> label, CancellationToken ct) where T : notnull
    {
        var prompt = new SelectionPrompt<T>()
            .Title($"[bold]?[/] {Markup.Escape(title)}")
            .UseConverter(c => Markup.Escape(label(c)))
            .PageSize(12)
            .MoreChoicesText("[grey](move up and down to reveal more)[/]")
            .AddChoices(choices);
        var picked = await prompt.ShowAsync(console, ct);

        // SelectionPrompt erases itself; echo the answer so the transcript stays a readable record.
        console.MarkupLine($"[bold]?[/] {Markup.Escape(title)} [blue]{Markup.Escape(label(picked))}[/]");
        return picked;
    }

    public Task<string> TextAsync(string title, string? defaultValue, Func<string, string?> validate, CancellationToken ct, bool allowEmpty = false)
    {
        var prompt = new TextPrompt<string>($"[bold]?[/] {Markup.Escape(title)}")
            .Validate(v => validate(v) is { } error ? ValidationResult.Error($"[red]{Markup.Escape(error)}[/]") : ValidationResult.Success());

        if (allowEmpty)
            prompt.AllowEmpty();
        if (defaultValue is not null)
            prompt.DefaultValue(defaultValue).ShowDefaultValue();

        return prompt.ShowAsync(console, ct);
    }

    public Task<bool> ConfirmAsync(string title, bool defaultValue, CancellationToken ct)
    {
        var prompt = new ConfirmationPrompt($"[bold]?[/] {Markup.Escape(title)}") { DefaultValue = defaultValue };
        return prompt.ShowAsync(console, ct);
    }

    public async Task<IReadOnlyList<T>> MultiSelectAsync<T>(string title, IReadOnlyList<T> choices, Func<T, string> label, CancellationToken ct, IReadOnlyCollection<T>? selected = null) where T : notnull
    {
        var prompt = new MultiSelectionPrompt<T>()
            .Title($"[bold]?[/] {Markup.Escape(title)}")
            .UseConverter(c => Markup.Escape(label(c)))
            .NotRequired()
            .PageSize(12)
            .InstructionsText("[grey](space to toggle, enter to accept)[/]");
        foreach (var choice in choices)
        {
            var item = prompt.AddChoice(choice);
            if (selected?.Contains(choice) == true) item.Select();
        }
        var picked = await prompt.ShowAsync(console, ct);

        console.MarkupLine($"[bold]?[/] {Markup.Escape(title)} [blue]{Markup.Escape(picked.Count == 0 ? "none" : string.Join(", ", picked.Select(label)))}[/]");
        return picked;
    }
}
