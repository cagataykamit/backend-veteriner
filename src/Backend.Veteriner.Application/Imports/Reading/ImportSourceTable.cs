using Backend.Veteriner.Domain.Imports;

namespace Backend.Veteriner.Application.Imports.Reading;

/// <summary>Dosyadan okunan tek veri satırı; <see cref="Cells"/> başlık sırasıyla hizalıdır (boş hücre null).</summary>
public sealed record ImportSourceRow(int RowNumber, IReadOnlyList<string?> Cells);

/// <summary>Bir mantıksal tablonun başlıkları ve veri satırları (tamamen boş satırlar atılmış).</summary>
public sealed record ImportSourceTable(ImportTable Table, IReadOnlyList<string> Headers, IReadOnlyList<ImportSourceRow> Rows);

/// <summary>
/// Hata olmayan okuma bulgusu ("dosya okunurken çıkan not"; IMPORT_API_CONTRACT.md §7.2.1). <see cref="Message"/> tam cümledir ve
/// her notta vardır; diğer alanlar ekranın notu tek satırda göstermesi içindir ve isteğe bağlıdır. Notlar ve türleri tek yerde
/// üretilir: <see cref="ImportReadNotes"/> ve <c>EVetNotes</c>.
/// </summary>
public sealed record ImportReadWarning(string Code, ImportTable? Table, string Message)
{
    /// <summary><see cref="ImportNoteSeverity"/>; yoksa ekran "bilgi" sayar.</summary>
    public string? Severity { get; init; }

    /// <summary>Satırdaki sayı (ham; biçimi ekran yapar).</summary>
    public int? Count { get; init; }

    /// <summary>Sayının birimi: "hasta", "görev", "rapor satırı".</summary>
    public string? Unit { get; init; }

    /// <summary>Sayı ve birimden sonra gelen kısa yüklem: "sahibine bağlanacak".</summary>
    public string? Title { get; init; }

    /// <summary>Notun dayandığı dosya ya da rapor: "Randevu Listesi", "Aşı Bilgisi raporu".</summary>
    public string? Source { get; init; }

    public IReadOnlyList<ImportNotePart>? Parts { get; init; }

    /// <summary>"Ne olacak" cümlesi.</summary>
    public string? Outcome { get; init; }

    /// <summary>Örnek değer: "Pamuk (E-vet 1234)", "1234#56".</summary>
    public string? Example { get; init; }

    public ImportNoteTarget? Target { get; init; }
}

/// <summary>Notun etkisi; ekranda sıra ve renk (karar → dikkat → bilgi → aktarılmayacak).</summary>
public static class ImportNoteSeverity
{
    /// <summary>Kullanıcıdan seçim bekleniyor (Doğrulama adımı "Sizden beklenen").</summary>
    public const string Decision = "decision";

    /// <summary>Aktarılır ama veri değişir ya da kontrol edilmeli.</summary>
    public const string Attention = "attention";

    public const string Info = "info";

    /// <summary>Aktarılmayacak kayıtlar.</summary>
    public const string Skipped = "skipped";
}

/// <summary>Notun alt kırılımı (ör. atlanan görevlerin nedenleri).</summary>
public sealed record ImportNotePart(string Label, int Count, string? Severity = null);

/// <summary>
/// Notun gideceği yer: <see cref="Required"/> Doğrulama adımındaki "Sizden beklenen" bölümü; <see cref="Rows"/> satır incelemesi
/// (<see cref="Table"/> ve <see cref="Reason"/> ile; satırlar neden kodu taşıyana dek üretilmez).
/// </summary>
public sealed record ImportNoteTarget(string Anchor, ImportTable? Table = null, string? Reason = null)
{
    public const string Required = "required";
    public const string Rows = "rows";
}

public sealed record ImportReadResult(
    IReadOnlyList<ImportSourceTable> Tables,
    IReadOnlyList<ImportReadWarning> Warnings);
