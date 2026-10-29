using System.Text.RegularExpressions;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Reports;

/// <summary>
/// Kullanıcının bir rapor için adla kaydettiği filtre seti (REPORT-004). Kiracı + kullanıcı kapsamındadır, paylaşılmaz.
/// Filtre değerleri panelin rapor sorgu parametreleridir (ör. <c>clinicId</c>, <c>method</c>); backend yorumlamaz, yalnız
/// boyut ve ad biçimini sınırlar. Ad (Türkçe harf/büyük-küçük farkı yok sayılarak) kullanıcının o raporunda benzersizdir.
/// Kayıt değişmez: güncellemek için silinip yeniden kaydedilir.
/// </summary>
public sealed partial class ReportSavedFilter : ITenantOwned
{
    public const int MaxNameLength = 100;

    /// <summary>Kullanıcı başına, rapor başına kayıtlı filtre tavanı.</summary>
    public const int MaxPerReport = 20;

    public const int MaxFilterCount = 20;
    public const int MaxFilterKeyLength = 50;
    public const int MaxFilterValueLength = 200;

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string ReportKey { get; private set; } = default!;
    public string Name { get; private set; } = default!;

    /// <summary><see cref="TurkishTextKey.Normalize"/> çıktısı; ad benzersizliği bu kolon üzerinden.</summary>
    public string NormalizedName { get; private set; } = default!;

    public IReadOnlyDictionary<string, string> Filters { get; private set; } = new Dictionary<string, string>();
    public DateTime CreatedAtUtc { get; private set; }

    private ReportSavedFilter() { }

    public static ReportSavedFilter Create(
        Guid tenantId,
        Guid userId,
        string reportKey,
        string name,
        IReadOnlyDictionary<string, string> filters,
        DateTime utcNow)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId geçersiz.", nameof(tenantId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId geçersiz.", nameof(userId));

        var nameCheck = ValidateName(name);
        if (!nameCheck.IsSuccess)
            throw new ArgumentException(nameCheck.Error.Message, nameof(name));

        var filtersCheck = ValidateFilters(filters);
        if (!filtersCheck.IsSuccess)
            throw new ArgumentException(filtersCheck.Error.Message, nameof(filters));

        return new ReportSavedFilter
        {
            TenantId = tenantId,
            UserId = userId,
            ReportKey = Reports.ReportKey.Ensure(reportKey, nameof(reportKey)),
            Name = CollapseWhitespace(name),
            NormalizedName = TurkishTextKey.Normalize(name),
            Filters = new Dictionary<string, string>(filters, StringComparer.Ordinal),
            CreatedAtUtc = utcNow,
        };
    }

    public static Result ValidateName(string? name)
    {
        var length = string.IsNullOrWhiteSpace(name) ? 0 : CollapseWhitespace(name).Length;
        return length is >= 1 and <= MaxNameLength
            ? Result.Success()
            : Result.Failure("ReportSavedFilters.Validation", $"Filtre adı 1-{MaxNameLength} karakter olmalıdır.");
    }

    /// <summary>
    /// En çok <see cref="MaxFilterCount"/> alan; anahtar harfle başlar, harf/rakam içerir (en çok <see cref="MaxFilterKeyLength"/>);
    /// değer boş olamaz, en çok <see cref="MaxFilterValueLength"/> karakter.
    /// </summary>
    public static Result ValidateFilters(IReadOnlyDictionary<string, string>? filters)
    {
        if (filters is null)
            return Result.Failure("ReportSavedFilters.Validation", "Filtre seti zorunludur.");

        if (filters.Count > MaxFilterCount)
            return Result.Failure("ReportSavedFilters.Validation", $"Filtre setinde en çok {MaxFilterCount} alan olabilir.");

        foreach (var (key, value) in filters)
        {
            if (key.Length > MaxFilterKeyLength || !FilterKeyPattern().IsMatch(key))
                return Result.Failure("ReportSavedFilters.Validation", $"Filtre alanı adı geçersiz: '{key}'.");

            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxFilterValueLength)
            {
                return Result.Failure(
                    "ReportSavedFilters.Validation",
                    $"Filtre değeri boş olamaz ve en çok {MaxFilterValueLength} karakter olabilir ('{key}').");
            }
        }

        return Result.Success();
    }

    private static string CollapseWhitespace(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FilterKeyPattern();
}
