namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>Tarih-saat sütun değeri (UTC an).</summary>
internal readonly record struct LocalDateTime(DateTime Utc);

/// <summary>Para tutarı sütun değeri.</summary>
internal readonly record struct MoneyAmount(decimal Value);

/// <summary>
/// Defter sütununun tek tanımı: CSV ve XLSX yazıcıları başlığı, grubu ve değeri buradan okur.
/// <see cref="Value"/>: metin → <see cref="string"/>, an → UTC <see cref="DateTime"/> (dosyada İstanbul günü), takvim günü → <see cref="DateOnly"/>,
/// sayı → <see cref="decimal"/>; boş → null. Tür, fabrika yöntemleriyle derleme zamanında sabitlenir.
/// </summary>
/// <param name="Group">Üst başlık (gruplu defterler); yoksa null.</param>
public sealed record RegisterColumn<TRow>(string? Group, string Header, Func<TRow, object?> Value)
{
    public static RegisterColumn<TRow> Text(string? group, string header, Func<TRow, string?> value)
        => new(group, header, row => value(row));

    public static RegisterColumn<TRow> Date(string? group, string header, Func<TRow, DateTime?> value)
        => new(group, header, row => value(row));

    /// <summary>Saat dilimsiz takvim günü (SKT, fatura tarihi vb.); dosyada olduğu gibi yazılır, İstanbul dönüşümü yapılmaz.</summary>
    public static RegisterColumn<TRow> CalendarDate(string? group, string header, Func<TRow, DateOnly?> value)
        => new(group, header, row => value(row));

    public static RegisterColumn<TRow> Number(string? group, string header, Func<TRow, decimal?> value)
        => new(group, header, row => value(row));

    /// <summary>Tarih ve saat (UTC an; dosyada İstanbul <c>dd.MM.yyyy HH:mm</c>).</summary>
    public static RegisterColumn<TRow> Time(string? group, string header, Func<TRow, DateTime?> value)
        => new(group, header, row => value(row) is { } utc ? new LocalDateTime(utc) : null);

    /// <summary>Para tutarı: dosyada 2 ondalık (<c>0,00</c>).</summary>
    public static RegisterColumn<TRow> Money(string? group, string header, Func<TRow, decimal?> value)
        => new(group, header, row => value(row) is { } amount ? new MoneyAmount(amount) : null);

    /// <summary>CSV tek satırlık başlık: gruplu sütunda <c>Grup · Başlık</c>.</summary>
    public string CsvHeader => Group is null ? Header : $"{Group} · {Header}";
}
