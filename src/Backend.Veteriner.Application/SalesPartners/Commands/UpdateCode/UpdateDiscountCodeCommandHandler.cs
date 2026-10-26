using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.UpdateCode;

public sealed class UpdateDiscountCodeCommandHandler : IRequestHandler<UpdateDiscountCodeCommand, Result<DiscountCodeDto>>
{
    private readonly IRepository<DiscountCode> _codes;
    private readonly IReadRepository<DiscountRedemption> _redemptions;

    public UpdateDiscountCodeCommandHandler(IRepository<DiscountCode> codes, IReadRepository<DiscountRedemption> redemptions)
    {
        _codes = codes;
        _redemptions = redemptions;
    }

    public async ValueTask<Result<DiscountCodeDto>> Handle(UpdateDiscountCodeCommand request, CancellationToken ct)
    {
        var code = await _codes.FirstOrDefaultAsync(new DiscountCodeByIdSpec(request.SalesPartnerId, request.CodeId), ct);
        if (code is null)
            return Result<DiscountCodeDto>.Failure(SalesPartnerErrors.CodeNotFound);

        var updated = code.UpdateTerms(
            request.DiscountPercent,
            request.CommissionPercent,
            request.ValidFromUtc,
            request.ValidUntilUtc,
            request.MaxRedemptions,
            DateTime.UtcNow);
        if (!updated.IsSuccess)
            return Result<DiscountCodeDto>.Failure(updated.Error);

        await _codes.SaveChangesAsync(ct);
        var count = await _redemptions.CountAsync(new DiscountRedemptionsByCodeSpec(code.Id), ct);
        return Result<DiscountCodeDto>.Success(DiscountCodeDto.From(code, count));
    }
}
