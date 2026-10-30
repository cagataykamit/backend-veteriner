using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.Administrations;

/// <summary>Uygulama yolu (RECORD-005). Değerler kalıcıdır (int sütun); yeni yol listenin sonuna eklenir.</summary>
public enum AdministrationRoute
{
    Oral = 1,
    Subcutaneous = 2,
    Intramuscular = 3,
    Intravenous = 4,
    Intranasal = 5,
    Intradermal = 6,
    Topical = 7,
    Ophthalmic = 8,
    Otic = 9,
    Rectal = 10,
    Other = 99,
}

/// <summary>
/// Uygulama yolu ve bölgesi kuralı tek yerde (RECORD-005): bağımsız uygulama kaydı ve aşı kaydı kullanır. Bölge serbest
/// metindir (ör. "sağ arka bacak, kas içi"); ikisi de isteğe bağlıdır.
/// </summary>
public static class AdministrationRouteSite
{
    public const int SiteMaxLength = 100;

    public static Result<(AdministrationRoute? Route, string? Site)> Normalize(AdministrationRoute? route, string? site, string errorCode)
    {
        if (route is { } r && !Enum.IsDefined(r))
            return Result<(AdministrationRoute?, string?)>.Failure(errorCode, "Uygulama yolu geçersiz.");

        var text = string.IsNullOrWhiteSpace(site) ? null : site.Trim();
        if (text is not null && text.Length > SiteMaxLength)
            return Result<(AdministrationRoute?, string?)>.Failure(errorCode, $"Uygulama bölgesi en fazla {SiteMaxLength} karakter olabilir.");

        return Result<(AdministrationRoute?, string?)>.Success((route, text));
    }
}
