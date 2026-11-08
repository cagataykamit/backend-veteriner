using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.CashRegisters;

/// <summary>
/// Kasa giriş/çıkışının gelir/gider türü (kiracı tanımlı küçük liste). Silinmez; pasif tür yeni harekette seçilemez, eski hareketlerde kalır.
/// </summary>
public sealed class CashMovementCategory : ITenantOwned
{
    public const int MaxNameLength = 100;

    /// <summary>Kiracıya ilk verilen türler; klinik ad değiştirir, pasife alır ya da yenisini ekler.</summary>
    public static readonly IReadOnlyList<string> DefaultNames = ["Bankaya yatırma", "Masraf", "Bozuk para", "Diğer"];

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private CashMovementCategory() { }

    public static Result<CashMovementCategory> Create(Guid tenantId, string? name, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result<CashMovementCategory>.Failure(CashRegister.ValidationCode, "TenantId geçersiz.");

        var category = new CashMovementCategory
        {
            TenantId = tenantId,
            IsActive = true,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        };
        var named = category.SetName(name);
        return named.IsSuccess ? Result<CashMovementCategory>.Success(category) : Result<CashMovementCategory>.Failure(named.Error);
    }

    public static IReadOnlyList<CashMovementCategory> CreateDefaults(Guid tenantId, DateTime nowUtc)
        => DefaultNames.Select(n => Create(tenantId, n, nowUtc).Value!).ToList();

    public Result Update(string? name, bool isActive, DateTime nowUtc)
    {
        var named = SetName(name);
        if (!named.IsSuccess)
            return named;

        IsActive = isActive;
        UpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return Result.Success();
    }

    private Result SetName(string? name)
    {
        var n = name?.Trim();
        if (string.IsNullOrEmpty(n) || n.Length > MaxNameLength)
            return Result.Failure(CashRegister.ValidationCode, $"Tür adı zorunludur ve en fazla {MaxNameLength} karakter olabilir.");

        Name = n;
        return Result.Success();
    }
}
