using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Domain.CashRegisters;

/// <summary>
/// Klinik kasası (TIER-008; docs/CASH_REGISTERS_API_CONTRACT.md). Her klinikte silinemeyen ve pasife alınamayan tek "Ana kasa" vardır;
/// kasa belirtilmeyen nakit hareket (eski kayıtlar dahil) Ana kasanındır. Kasa silinmez; pasif kasaya yeni hareket girilmez, geçmişi kalır.
/// </summary>
public sealed class CashRegister : ITenantOwned, IClinicScoped
{
    public const string MainCode = "ANA";
    public const string MainName = "Ana kasa";
    public const int MaxCodeLength = 20;
    public const int MaxNameLength = 100;
    public const string ValidationCode = "CashRegisters.Validation";
    public const string InactiveCode = "CashRegisters.Inactive";

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ClinicId { get; private set; }

    /// <summary>Klinikte benzersiz kısa kod (büyük harf).</summary>
    public string Code { get; private set; } = default!;

    public string Name { get; private set; } = default!;
    public bool IsMain { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private CashRegister() { }

    public static CashRegister CreateMain(Guid tenantId, Guid clinicId, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty || clinicId == Guid.Empty)
            throw new ArgumentException("Ana kasa için kiracı ve klinik zorunludur.");

        return new CashRegister
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            Code = MainCode,
            Name = MainName,
            IsMain = true,
            IsActive = true,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        };
    }

    public static Result<CashRegister> Create(Guid tenantId, Guid clinicId, string? code, string? name, DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Invalid("TenantId geçersiz.");
        if (clinicId == Guid.Empty)
            return Invalid("ClinicId geçersiz.");

        var register = new CashRegister
        {
            TenantId = tenantId,
            ClinicId = clinicId,
            IsMain = false,
            IsActive = true,
            CreatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
        };
        var set = register.SetCodeAndName(code, name);
        return set.IsSuccess ? Result<CashRegister>.Success(register) : Result<CashRegister>.Failure(set.Error);
    }

    /// <summary>Ad, kod ve durum değişir; Ana kasa pasife alınamaz (kasa belirtilmeyen nakit hareket ona yazılır).</summary>
    public Result Update(string? code, string? name, bool isActive, DateTime nowUtc)
    {
        if (IsMain && !isActive)
            return Result.Failure("CashRegisters.MainRegisterLocked", "Ana kasa pasife alınamaz.");

        var set = SetCodeAndName(code, name);
        if (!set.IsSuccess)
            return set;

        IsActive = isActive;
        UpdatedAtUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        return Result.Success();
    }

    /// <summary>Yeni nakit hareket (tahsilat, iade, giriş/çıkış, aktarım, sayım) yalnız aktif kasaya girilir.</summary>
    public Result EnsureAcceptsMovements()
        => IsActive ? Result.Success() : Result.Failure(InactiveCode, $"\"{Name}\" pasif; bu kasaya yeni hareket girilemez.");

    private Result SetCodeAndName(string? code, string? name)
    {
        var c = code?.Trim().ToUpperInvariant();
        var n = name?.Trim();
        if (string.IsNullOrEmpty(c) || c.Length > MaxCodeLength)
            return Result.Failure(ValidationCode, $"Kasa kodu zorunludur ve en fazla {MaxCodeLength} karakter olabilir.");
        if (string.IsNullOrEmpty(n) || n.Length > MaxNameLength)
            return Result.Failure(ValidationCode, $"Kasa adı zorunludur ve en fazla {MaxNameLength} karakter olabilir.");

        Code = c;
        Name = n;
        return Result.Success();
    }

    private static Result<CashRegister> Invalid(string message) => Result<CashRegister>.Failure(ValidationCode, message);
}
