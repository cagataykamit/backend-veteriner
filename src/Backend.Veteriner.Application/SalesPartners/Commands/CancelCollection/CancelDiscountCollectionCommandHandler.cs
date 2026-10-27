using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.CancelCollection;

public sealed class CancelDiscountCollectionCommandHandler
    : IRequestHandler<CancelDiscountCollectionCommand, Result<DiscountCollectionDto>>
{
    private readonly IRepository<DiscountRedemption> _redemptions;
    private readonly TimeProvider _timeProvider;

    public CancelDiscountCollectionCommandHandler(IRepository<DiscountRedemption> redemptions, TimeProvider timeProvider)
    {
        _redemptions = redemptions;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<DiscountCollectionDto>> Handle(CancelDiscountCollectionCommand request, CancellationToken ct)
    {
        var redemptions = await _redemptions.ListAsync(new DiscountRedemptionsByPartnerSpec(request.SalesPartnerId, tracked: true), ct);
        var owner = redemptions.FirstOrDefault(r => r.Collections.Any(c => c.Id == request.CollectionId));
        if (owner is null)
            return Result<DiscountCollectionDto>.Failure(DiscountRedemption.CollectionNotFound);

        var cancelled = owner.CancelCollection(request.CollectionId, request.Reason, _timeProvider.GetUtcNow().UtcDateTime);
        if (!cancelled.IsSuccess)
            return Result<DiscountCollectionDto>.Failure(cancelled.Error);

        await _redemptions.SaveChangesAsync(ct);
        return Result<DiscountCollectionDto>.Success(DiscountCollectionDto.From(cancelled.Value!));
    }
}
