using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.SalesPartners.Commands.CreateCode;

public sealed class CreateDiscountCodeCommandHandler : IRequestHandler<CreateDiscountCodeCommand, Result<DiscountCodeDto>>
{
    /// <summary><c>DiscountCodeConfiguration.NormalizedCodeIndexName</c> ile aynı.</summary>
    private const string NormalizedCodeIndexName = "IX_DiscountCodes_NormalizedCode";

    private readonly IReadRepository<SalesPartner> _partners;
    private readonly IRepository<DiscountCode> _codes;
    private readonly TimeProvider _timeProvider;

    public CreateDiscountCodeCommandHandler(IReadRepository<SalesPartner> partners, IRepository<DiscountCode> codes, TimeProvider timeProvider)
    {
        _partners = partners;
        _codes = codes;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<DiscountCodeDto>> Handle(CreateDiscountCodeCommand request, CancellationToken ct)
    {
        if (!await _partners.AnyAsync(new SalesPartnerByIdSpec(request.SalesPartnerId), ct))
            return Result<DiscountCodeDto>.Failure(SalesPartnerErrors.PartnerNotFound);

        var created = DiscountCode.Create(
            request.SalesPartnerId,
            request.Code,
            request.DiscountPercent,
            request.CommissionPercent,
            request.ValidFromUtc,
            request.ValidUntilUtc,
            request.MaxRedemptions,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<DiscountCodeDto>.Failure(created.Error);

        var code = created.Value!;
        if (await _codes.AnyAsync(new DiscountCodeByNormalizedCodeSpec(code.NormalizedCode), ct))
            return Result<DiscountCodeDto>.Failure(SalesPartnerErrors.DuplicateCode);

        try
        {
            await _codes.AddAsync(code, ct);
            await _codes.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SalesPartnerErrors.IsUniqueViolation(ex, NormalizedCodeIndexName))
        {
            return Result<DiscountCodeDto>.Failure(SalesPartnerErrors.DuplicateCode);
        }

        return Result<DiscountCodeDto>.Success(DiscountCodeDto.From(code, 0));
    }
}
