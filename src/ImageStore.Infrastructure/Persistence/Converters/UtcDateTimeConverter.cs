using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ImageStore.Infrastructure.Persistence.Converters;

/// <summary>Stores DateTime values as UTC and marks values read from the database as UTC.</summary>
internal sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(
        value => ToUtc(value),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
{
    internal static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}

internal sealed class NullableUtcDateTimeConverter()
    : ValueConverter<DateTime?, DateTime?>(
        value => value.HasValue ? UtcDateTimeConverter.ToUtc(value.Value) : value,
        value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : value);
