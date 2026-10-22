namespace Backend.Veteriner.Domain.SalesPartners;

internal static class DiscountTime
{
    public static DateTime ToUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

    public static DateTime? ToUtc(DateTime? value) => value.HasValue ? ToUtc(value.Value) : null;
}
