namespace Backend.Veteriner.Application.Reports.Registers;

/// <summary>
/// Defter sütununun tek tanımı: CSV ve XLSX yazıcıları başlığı, grubu ve değeri buradan okur.
/// <see cref="Value"/>: metin → <see cref="string"/>, tarih → UTC <see cref="DateTime"/> (dosyada İstanbul günü),
/// sayı → <see cref="decimal"/>; boş → null. Tür, fabrika yöntemleriyle derleme zamanında sabitlenir.
/// </summary>
/// <param name="Group">Üst başlık (gruplu defterler); yoksa null.</param>
public sealed record RegisterColumn<TRow>(string? Group, string Header, Func<TRow, object?> Value)
{
    public static RegisterColumn<TRow> Text(string? group, string header, Func<TRow, string?> value)
        => new(group, header, row => value(row));

    public static RegisterColumn<TRow> Date(string? group, string header, Func<TRow, DateTime?> value)
        => new(group, header, row => value(row));

    public static RegisterColumn<TRow> Number(string? group, string header, Func<TRow, decimal?> value)
        => new(group, header, row => value(row));

    /// <summary>CSV tek satırlık başlık: gruplu sütunda <c>Grup · Başlık</c>.</summary>
    public string CsvHeader => Group is null ? Header : $"{Group} · {Header}";
}
