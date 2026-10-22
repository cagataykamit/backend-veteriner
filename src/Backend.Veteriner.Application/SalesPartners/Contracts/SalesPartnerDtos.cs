using Backend.Veteriner.Domain.SalesPartners;

namespace Backend.Veteriner.Application.SalesPartners.Contracts;

/// <summary>Müşteriye gösterilen kod özeti (kayıt yanıtı); ortak bilgisi ve komisyon içermez.</summary>
public sealed record DiscountCodeSummaryDto(string Code, decimal DiscountPercent, int DiscountMonths);

/// <summary>Anonim doğrulama yanıtı: geçersiz her durumda aynı gövde (numaralandırmaya karşı).</summary>
public sealed record DiscountCodeValidationDto(bool Valid, string? Code, decimal? DiscountPercent, int? DiscountMonths)
{
    public static DiscountCodeValidationDto Invalid { get; } = new(false, null, null, null);

    public static DiscountCodeValidationDto From(DiscountCode code)
        => new(true, code.Code, code.DiscountPercent, DiscountRedemption.WindowMonths);
}

/// <summary>Kiracının abonelik sayfasındaki kod durumu; ortak adı/kimliği ve komisyon dönmez.</summary>
public sealed record SubscriptionDiscountDto(
    bool Applied,
    string? Code,
    decimal? DiscountPercent,
    int? DiscountMonths,
    DateTime? WindowStartsAtUtc,
    DateTime? WindowEndsAtUtc,
    bool CanApply)
{
    public static SubscriptionDiscountDto From(DiscountRedemption? redemption, bool canApply)
        => redemption is null
            ? new(false, null, null, null, null, null, canApply)
            : new(true, redemption.Code, redemption.DiscountPercent, DiscountRedemption.WindowMonths,
                redemption.WindowStartsAtUtc, redemption.WindowEndsAtUtc, false);
}

public sealed record SalesPartnerSummaryDto(
    int RedemptionCount,
    int TrialCustomerCount,
    int PaidCustomerCount,
    long RevenueNetMinor,
    long CommissionEarnedMinor,
    long CommissionPaidMinor,
    long CommissionPendingMinor,
    long CommissionClawbackMinor)
{
    /// <summary>Ortak başına rapor (ADR-037 madde 7); tanımlar sözleşme §5'te.</summary>
    public static SalesPartnerSummaryDto From(IReadOnlyCollection<DiscountRedemption> redemptions)
    {
        var collections = redemptions.SelectMany(r => r.Collections).ToList();
        var active = collections.Where(c => c.IsActive).ToList();
        var paidCustomers = redemptions.Count(r => r.HasActiveCollection);

        return new SalesPartnerSummaryDto(
            redemptions.Count,
            redemptions.Count - paidCustomers,
            paidCustomers,
            active.Sum(c => c.NetAmountMinor),
            active.Sum(c => c.CommissionAmountMinor),
            active.Where(c => c.Status == CommissionStatus.Paid).Sum(c => c.CommissionAmountMinor),
            active.Where(c => c.Status == CommissionStatus.Accrued).Sum(c => c.CommissionAmountMinor),
            collections.Where(c => c.IsClawback).Sum(c => c.CommissionAmountMinor));
    }
}

public sealed record SalesPartnerDto(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    Guid? LinkedTenantId,
    bool IsActive,
    DateTime CreatedAtUtc,
    int CodeCount,
    SalesPartnerSummaryDto Summary)
{
    public static SalesPartnerDto From(SalesPartner partner, int codeCount, IReadOnlyCollection<DiscountRedemption> redemptions)
        => new(partner.Id, partner.Name, partner.Email, partner.Phone, partner.LinkedTenantId, partner.IsActive,
            partner.CreatedAtUtc, codeCount, SalesPartnerSummaryDto.From(redemptions));
}

public sealed record DiscountCodeDto(
    Guid Id,
    Guid SalesPartnerId,
    string Code,
    decimal DiscountPercent,
    decimal CommissionPercent,
    DateTime? ValidFromUtc,
    DateTime? ValidUntilUtc,
    int? MaxRedemptions,
    int RedemptionCount,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? DeactivatedAtUtc)
{
    public static DiscountCodeDto From(DiscountCode code, int redemptionCount)
        => new(code.Id, code.SalesPartnerId, code.Code, code.DiscountPercent, code.CommissionPercent, code.ValidFromUtc,
            code.ValidUntilUtc, code.MaxRedemptions, redemptionCount, code.IsActive, code.CreatedAtUtc, code.DeactivatedAtUtc);
}

public sealed record SalesPartnerDetailDto(SalesPartnerDto Partner, IReadOnlyList<DiscountCodeDto> Codes);

public sealed record DiscountCollectionDto(
    Guid Id,
    Guid CheckoutSessionId,
    string BillingPeriod,
    DateTime CollectedAtUtc,
    long GrossAmountMinor,
    long DiscountAmountMinor,
    long NetAmountMinor,
    long CommissionAmountMinor,
    string Status,
    DateTime? PaidAtUtc,
    string? PaidNote,
    DateTime? CancelledAtUtc,
    string? CancelReason)
{
    public static DiscountCollectionDto From(DiscountRedemptionCollection c)
        => new(c.Id, c.CheckoutSessionId, c.BillingPeriod.ToString(), c.CollectedAtUtc, c.GrossAmountMinor, c.DiscountAmountMinor,
            c.NetAmountMinor, c.CommissionAmountMinor, c.Status.ToString(), c.PaidAtUtc, c.PaidNote, c.CancelledAtUtc, c.CancelReason);
}

public sealed record DiscountRedemptionRowDto(
    Guid RedemptionId,
    Guid TenantId,
    string? TenantName,
    string Code,
    decimal DiscountPercent,
    decimal CommissionPercent,
    DateTime RedeemedAtUtc,
    DateTime? WindowStartsAtUtc,
    DateTime? WindowEndsAtUtc,
    string CustomerStatus,
    IReadOnlyList<DiscountCollectionDto> Collections)
{
    public static DiscountRedemptionRowDto From(DiscountRedemption r, string? tenantName)
        => new(r.Id, r.TenantId, tenantName, r.Code, r.DiscountPercent, r.CommissionPercent, r.RedeemedAtUtc,
            r.WindowStartsAtUtc, r.WindowEndsAtUtc, r.HasActiveCollection ? "Paid" : "Trial",
            r.Collections.OrderBy(c => c.CollectedAtUtc).Select(DiscountCollectionDto.From).ToList());
}

public sealed record MarkCommissionsPaidResultDto(int UpdatedCount);
