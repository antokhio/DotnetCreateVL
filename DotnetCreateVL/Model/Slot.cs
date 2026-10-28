namespace DotnetCreateVL.Model;

public enum SlotSource
{
    Unresolved,
    Flag,
    Prompt,
    Default,
}

/// <summary>
/// One field of the resolved model. A slot is filled by exactly one of: a CLI flag,
/// an interactive prompt, or an accepted default. Unresolved required slots are
/// reported (never guessed) in noninteractive mode.
/// </summary>
public sealed class Slot<T>(string id, string flag, string? allowedValues = null)
{
    public string Id { get; } = id;
    public string Flag { get; } = flag;
    public string? AllowedValues { get; } = allowedValues;

    public T? Value { get; private set; }
    public SlotSource Source { get; private set; } = SlotSource.Unresolved;
    public bool IsResolved => Source != SlotSource.Unresolved;

    public void Set(T value, SlotSource source)
    {
        Value = value;
        Source = source;
    }

    public T Require() =>
        IsResolved ? Value! : throw new InvalidOperationException($"Slot '{Id}' is unresolved.");

    public MissingSlot AsMissing() => new(Id, Flag, AllowedValues);
}

public sealed record MissingSlot(string Id, string Flag, string? AllowedValues);
