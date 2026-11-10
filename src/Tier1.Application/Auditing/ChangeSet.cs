using System.Globalization;

namespace Tier1.Application.Auditing;

/// <summary>يجمع الحقول التي تغيرت فعلًا لحدث السجل، بتمثيل نصي ثابت للقيم.</summary>
public sealed class ChangeSet
{
    private readonly List<FieldChange> _changes = [];

    public bool IsEmpty => _changes.Count == 0;

    public ChangeSet Add<T>(string field, T before, T after)
    {
        var a = Text(before);
        var b = Text(after);
        if (!string.Equals(a, b, StringComparison.Ordinal)) _changes.Add(new FieldChange(field, a, b));
        return this;
    }

    public IReadOnlyList<FieldChange> ToList() => _changes;

    private static string? Text<T>(T value) => value switch
    {
        null => null,
        string s => s,
        decimal d => d.ToString(CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
