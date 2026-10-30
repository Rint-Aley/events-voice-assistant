using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TaskManager.Infrastructure.Persistence;

/// <summary>
/// Хранит <c>answers.warnings</c> как <c>jsonb</c>, а не как строку с разделителями.
/// </summary>
internal static class JsonStringListConverter
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<IReadOnlyList<string>, string> Converter = new(
        warnings => JsonSerializer.Serialize(warnings, SerializerOptions),
        json => JsonSerializer.Deserialize<List<string>>(json, SerializerOptions) ?? new List<string>());

    public static readonly ValueComparer<IReadOnlyList<string>> Comparer = new(
        (left, right) => left != null && right != null && left.SequenceEqual(right),
        warnings => warnings.Aggregate(0, (hash, warning) => HashCode.Combine(hash, warning.GetHashCode())),
        warnings => warnings.ToList());
}
