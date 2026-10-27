using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.SalesPartners;

/// <summary>
/// Kodun bulunması ve kiracı için kullanılması (ADR-037): doğrulama ucu, kayıt ve abonelik akışı aynı yolu kullanır;
/// kural domain'dedir (<see cref="DiscountCode.IsRedeemable"/>, <see cref="DiscountRedemption.Redeem"/>).
/// </summary>
public sealed class DiscountCodeRedemptionService
{
    private readonly IReadRepository<DiscountCode> _codes;
    private readonly IReadRepository<SalesPartner> _partners;
    private readonly ITenantScope _tenantScope;
    private readonly IReadRepository<DiscountRedemption> _redemptions;

    public DiscountCodeRedemptionService(
        IReadRepository<DiscountCode> codes,
        IReadRepository<SalesPartner> partners,
        ITenantScope tenantScope,
        IReadRepository<DiscountRedemption> redemptions)
    {
        _codes = codes;
        _partners = partners;
        _tenantScope = tenantScope;
        _redemptions = redemptions;
    }

    /// <summary>Kod şu an kullanılabiliyorsa kodu döner; biçimsiz, olmayan, pasif, süresi dolmuş, limiti dolmuş kodda <c>null</c>.</summary>
    public async Task<DiscountCode?> FindRedeemableAsync(string? rawCode, DateTime utcNow, CancellationToken ct)
    {
        var found = await FindAsync(rawCode, ct);
        if (found is null)
            return null;

        var (code, partner) = found.Value;
        var count = await CountRedemptionsAsync(code.Id, ct);
        return code.IsRedeemable(partner.IsActive, count, utcNow) ? code : null;
    }

    /// <summary>
    /// Kiracı için kod kullanımı üretir (kaydetmez). Geçersiz kod ve kendi kodunu kullanma tek tip <c>DiscountCodes.Invalid</c>;
    /// kiracının zaten kodu varsa <c>DiscountCodes.DuplicateRedemption</c>.
    /// </summary>
    public async Task<Result<DiscountRedemption>> RedeemAsync(
        Guid tenantId,
        string? redeemerEmail,
        string? rawCode,
        DateTime utcNow,
        CancellationToken ct)
    {
        if (await _redemptions.AnyAsync(new DiscountRedemptionByTenantSpec(tenantId), ct))
            return Result<DiscountRedemption>.Failure(SalesPartnerErrors.DuplicateRedemption);

        var found = await FindAsync(rawCode, ct);
        if (found is null)
            return Result<DiscountRedemption>.Failure(DiscountRedemption.InvalidCode);

        var (code, partner) = found.Value;
        var count = await CountRedemptionsAsync(code.Id, ct);
        return DiscountRedemption.Redeem(tenantId, redeemerEmail, code, partner, count, utcNow);
    }

    /// <summary>
    /// Kodun kullanım limiti tüm kiracılardaki kullanımlarla sayılır (ADR-040, kiracılar arası); yalnız sayı döner.
    /// </summary>
    private async Task<int> CountRedemptionsAsync(Guid codeId, CancellationToken ct)
    {
        using var system = _tenantScope.AsSystem();
        return await _redemptions.CountAsync(new DiscountRedemptionsByCodeSpec(codeId), ct);
    }

    private async Task<(DiscountCode Code, SalesPartner Partner)?> FindAsync(string? rawCode, CancellationToken ct)
    {
        var normalized = DiscountCodeText.Normalize(rawCode);
        if (normalized is null)
            return null;

        var code = await _codes.FirstOrDefaultAsync(new DiscountCodeByNormalizedCodeSpec(normalized), ct);
        if (code is null)
            return null;

        var partner = await _partners.FirstOrDefaultAsync(new SalesPartnerByIdSpec(code.SalesPartnerId), ct);
        return partner is null ? null : (code, partner);
    }
}
