using Ardalis.Specification;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Reports.DailyTransactions;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.CashRegisters;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;

namespace Backend.Veteriner.Application.CashRegisters;

/// <summary>
/// Kasanın bir dönemdeki hareket toplamları. Beklenen nakdin ve kasa bakiyesinin tek hesap yeri (<see cref="NetAmount"/>):
/// nakit tahsilat neti (tahsilat − iptal − kredi iadesi; gün sonu kasa özetinin nakit satırıyla aynı kaynak) + giriş − çıkış
/// + gelen aktarım − giden aktarım. İptal edilen kasa hareketi toplama girmez.
/// </summary>
public sealed record CashRegisterTotalsDto(
    decimal CashNetAmount,
    decimal InAmount,
    decimal OutAmount,
    decimal TransferInAmount,
    decimal TransferOutAmount)
{
    public decimal NetAmount => CashNetAmount + InAmount - OutAmount + TransferInAmount - TransferOutAmount;

    public static CashRegisterTotalsDto From(decimal cashNetAmount, IEnumerable<CashMovementAmount> movements)
    {
        var list = movements.ToList();
        decimal Sum(CashMovementKind kind) => list.Where(m => m.Kind == kind).Sum(m => m.Amount);
        return new CashRegisterTotalsDto(
            cashNetAmount,
            Sum(CashMovementKind.In),
            Sum(CashMovementKind.Out),
            Sum(CashMovementKind.TransferIn),
            Sum(CashMovementKind.TransferOut));
    }
}

public sealed record CashMovementAmount(CashMovementKind Kind, decimal Amount);

/// <summary>Çözülmüş kasa: <c>Register</c> boşsa klinikte henüz Ana kasa kaydı yoktur (yalnız kasasız nakit okunur).</summary>
public sealed record ResolvedCashRegister(CashRegister? Register, CashRegisterScope Scope)
{
    public Guid? Id => Register?.Id;
    public string Name => Register?.Name ?? CashRegister.MainName;
    public bool IsMain => Register?.IsMain ?? true;
}

public sealed class CashRegistersByClinicSpec : Specification<CashRegister>
{
    public CashRegistersByClinicSpec(Guid tenantId, Guid clinicId, bool includeInactive)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && r.ClinicId == clinicId);
        if (!includeInactive)
            Query.Where(r => r.IsActive);
        Query.OrderByDescending(r => r.IsMain).ThenBy(r => r.Name);
    }
}

public sealed class CashRegisterByIdSpec : Specification<CashRegister>
{
    public CashRegisterByIdSpec(Guid tenantId, Guid id) => Query.Where(r => r.TenantId == tenantId && r.Id == id);
}

public sealed class MainCashRegisterSpec : Specification<CashRegister>
{
    public MainCashRegisterSpec(Guid tenantId, Guid clinicId) => Query.Where(r => r.TenantId == tenantId && r.ClinicId == clinicId && r.IsMain);
}

/// <summary>Kasa hareketleri: kasa boşsa klinikteki tüm kasalar; varsayılan iptal edilenler hariç.</summary>
public sealed class CashMovementsSpec : Specification<CashMovement>
{
    public CashMovementsSpec(Guid tenantId, Guid clinicId, Guid? cashRegisterId, DateOnly? from, DateOnly toInclusive, bool includeVoided)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId && m.ClinicId == clinicId && m.BusinessDate <= toInclusive);
        if (from is { } f)
            Query.Where(m => m.BusinessDate >= f);
        if (cashRegisterId is { } registerId)
            Query.Where(m => m.CashRegisterId == registerId);
        if (!includeVoided)
            Query.Where(m => m.VoidedAtUtc == null);
        Query.OrderBy(m => m.BusinessDate).ThenBy(m => m.CreatedAtUtc).ThenBy(m => m.Kind).ThenBy(m => m.Id);
    }
}

/// <summary>Devreden bakiye ve gün neti için yalnız tür ve tutar (iptal edilenler hariç).</summary>
public sealed class CashMovementAmountsSpec : Specification<CashMovement, CashMovementAmount>
{
    public CashMovementAmountsSpec(Guid tenantId, Guid clinicId, Guid? cashRegisterId, DateOnly? from, DateOnly toInclusive)
    {
        Query.AsNoTracking();
        Query.Where(m => m.TenantId == tenantId && m.ClinicId == clinicId && m.BusinessDate <= toInclusive && m.VoidedAtUtc == null);
        if (from is { } f)
            Query.Where(m => m.BusinessDate >= f);
        if (cashRegisterId is { } registerId)
            Query.Where(m => m.CashRegisterId == registerId);
        Query.Select(m => new CashMovementAmount(m.Kind, m.Amount));
    }
}

/// <summary>
/// Kasa kuralları tek yerde: klinik kapsamı (gün sonu raporlarıyla aynı çözümleyici), Ana kasa, kasa seçimi ve paket kilidi.
/// Kasası boş nakit tahsilat ve iade kliniğin Ana kasasınındır (eski kayıtlar, örnek veri); okuma bu kuralla yapılır.
/// </summary>
internal static class CashRegisterRules
{
    public const string NotFoundCode = "CashRegisters.NotFound";

    public static async Task<Result<FinanceEventFilter>> ResolveClinicAsync(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver scopeResolver,
        DateOnly from,
        DateOnly to,
        Guid? clinicId,
        string module,
        CancellationToken ct)
    {
        var filter = await DailyTransactionsReportFilter.ResolveAsync(
            tenantContext, clinicContext, scopeResolver, from, to, clinicId, null, null, null, ct);
        if (!filter.IsSuccess)
            return filter;

        return filter.Value!.ClinicId is null
            ? Result<FinanceEventFilter>.Failure(
                $"{module}.ClinicScopeRequired",
                "İşlem için klinik belirlenemedi (clinicId veya aktif klinik bağlamı gerekir).")
            : filter;
    }

    /// <summary>
    /// Okuma için kasa: verilmezse Ana kasa (kaydı yoksa sanal Ana kasa). Başka klinik ya da kiracının kasası bulunmaz.
    /// </summary>
    public static async Task<Result<ResolvedCashRegister>> ResolveAsync(
        IReadRepositoryBase<CashRegister> registers, Guid tenantId, Guid clinicId, Guid? cashRegisterId, CancellationToken ct)
    {
        if (cashRegisterId is { } id)
        {
            var register = await registers.FirstOrDefaultAsync(new CashRegisterByIdSpec(tenantId, id), ct);
            return register is null || register.ClinicId != clinicId
                ? Result<ResolvedCashRegister>.Failure(NotFoundCode, "Kasa bulunamadı.")
                : Result<ResolvedCashRegister>.Success(ToResolved(register));
        }

        var main = await registers.FirstOrDefaultAsync(new MainCashRegisterSpec(tenantId, clinicId), ct);
        return Result<ResolvedCashRegister>.Success(main is null ? new ResolvedCashRegister(null, new CashRegisterScope(null, true)) : ToResolved(main));
    }

    public static ResolvedCashRegister ToResolved(CashRegister register)
        => new(register, new CashRegisterScope(register.Id, register.IsMain));

    /// <summary>
    /// Kliniğin Ana kasası; yoksa (migration sonrası açılan ve uygulama yolu dışında kurulan klinik) oluşturulur. Yeni klinikte
    /// <see cref="CashRegister.CreateMain"/> klinikle birlikte yazılır; bu yol yalnız eksik kalanı tamamlar.
    /// </summary>
    public static async Task<CashRegister> EnsureMainAsync(
        IRepositoryBase<CashRegister> registers, Guid tenantId, Guid clinicId, DateTime nowUtc, CancellationToken ct)
    {
        var main = await registers.FirstOrDefaultAsync(new MainCashRegisterSpec(tenantId, clinicId), ct);
        if (main is not null)
            return main;

        main = CashRegister.CreateMain(tenantId, clinicId, nowUtc);
        await registers.AddAsync(main, ct);
        await registers.SaveChangesAsync(ct);
        return main;
    }

    /// <summary>
    /// Nakit tahsilat ya da iadede kasa: verilmezse boş kalır, okuma kuralıyla kliniğin Ana kasasıdır (eski istek bugünkü gibi çalışır,
    /// ek sorgu yok). Verilen kasa aynı klinikte ve aktif olmalı; Ana kasa dışındaki kasa kasa yönetimi paketini ister (paket düşünce
    /// yeni işlem yalnız Ana kasaya).
    /// </summary>
    public static async Task<Result<Guid?>> ResolveForCashAsync(
        IReadRepositoryBase<CashRegister> registers,
        TenantPlanFeatureEvaluator planFeatures,
        Guid tenantId,
        Guid clinicId,
        Guid? cashRegisterId,
        CancellationToken ct)
    {
        if (cashRegisterId is null)
            return Result<Guid?>.Success(null);

        var resolved = await ResolveAsync(registers, tenantId, clinicId, cashRegisterId, ct);
        if (!resolved.IsSuccess)
            return Result<Guid?>.Failure(resolved.Error);

        if (resolved.Value!.Register is not { } register)
            return Result<Guid?>.Success(null);

        var accepts = register.EnsureAcceptsMovements();
        if (!accepts.IsSuccess)
            return Result<Guid?>.Failure(accepts.Error);

        if (!register.IsMain)
        {
            var planAllows = await planFeatures.EnsureAllowedAsync(tenantId, PlanFeature.CashManagement, ct);
            if (!planAllows.IsSuccess)
                return Result<Guid?>.Failure(planAllows.Error);
        }

        return Result<Guid?>.Success(register.Id);
    }

    /// <summary>Devreden bakiyenin başladığı gün; öncesinde kayıt yoktur (yalnız sorgu penceresi alt sınırı).</summary>
    public static readonly DateOnly LedgerStart = new(2000, 1, 1);

    /// <summary>
    /// Kasanın dönem toplamları (<paramref name="from"/> boşsa başlangıçtan): kasa filtreli nakit tahsilat neti ve iptal edilmemiş
    /// kasa hareketleri. <paramref name="register"/> boşsa klinikteki tüm kasalar (aktarımlar birbirini götürür). Sanal Ana kasada
    /// (klinikte kasa kaydı yok) hareket de yoktur.
    /// </summary>
    public static async Task<CashRegisterTotalsDto> LoadTotalsAsync(
        ICashEventsReader cashEvents,
        IReadRepositoryBase<CashMovement> movements,
        Guid tenantId,
        Guid clinicId,
        ResolvedCashRegister? register,
        DateOnly? from,
        DateOnly to,
        CancellationToken ct)
    {
        var filter = DailyTransactionsReportFilter.ForScope(tenantId, clinicId, null, from ?? LedgerStart, to) with
        {
            CashRegister = register?.Scope,
            PaymentMethod = Domain.Payments.PaymentMethod.Cash,
        };

        var cash = await cashEvents.SumAsync(filter, ct);
        var amounts = await movements.ListAsync(new CashMovementAmountsSpec(tenantId, clinicId, register?.Id, from, to), ct);
        return CashRegisterTotalsDto.From(cash.NetAmount, amounts);
    }

    public static Error MovementNotFound() => new("CashMovements.NotFound", "Kasa hareketi bulunamadı.");
}
