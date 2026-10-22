using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Application.SalesPartners.Specs;
using Backend.Veteriner.Domain.SalesPartners;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.SalesPartners.Commands.MarkCommissionsPaid;

public sealed class MarkCommissionsPaidCommandHandler
    : IRequestHandler<MarkCommissionsPaidCommand, Result<MarkCommissionsPaidResultDto>>
{
    private readonly IRepository<DiscountRedemption> _redemptions;

    public MarkCommissionsPaidCommandHandler(IRepository<DiscountRedemption> redemptions) => _redemptions = redemptions;

    public async Task<Result<MarkCommissionsPaidResultDto>> Handle(MarkCommissionsPaidCommand request, CancellationToken ct)
    {
        var redemptions = await _redemptions.ListAsync(new DiscountRedemptionsByPartnerSpec(request.SalesPartnerId, tracked: true), ct);
        var owners = redemptions
            .SelectMany(r => r.Collections.Select(c => (Redemption: r, Collection: c)))
            .ToDictionary(x => x.Collection.Id, x => x.Redemption);

        var ids = request.CollectionIds.Distinct().ToList();
        if (ids.Any(id => !owners.ContainsKey(id)))
            return Result<MarkCommissionsPaidResultDto>.Failure(DiscountRedemption.CollectionNotFound);

        // Hepsi ya da hiçbiri: önce durum denetlenir, sonra işaretlenir.
        var notAccrued = ids.FirstOrDefault(id => owners[id].Collections.Single(c => c.Id == id).Status != CommissionStatus.Accrued);
        if (notAccrued != Guid.Empty)
        {
            // Domain hatası (CommissionNotAccrued) satır değişmeden döner.
            return Result<MarkCommissionsPaidResultDto>.Failure(
                owners[notAccrued].MarkCommissionPaid(notAccrued, request.PaidAtUtc, request.Note).Error);
        }

        foreach (var id in ids)
        {
            var marked = owners[id].MarkCommissionPaid(id, request.PaidAtUtc, request.Note);
            if (!marked.IsSuccess)
                return Result<MarkCommissionsPaidResultDto>.Failure(marked.Error);
        }

        await _redemptions.SaveChangesAsync(ct);
        return Result<MarkCommissionsPaidResultDto>.Success(new MarkCommissionsPaidResultDto(ids.Count));
    }
}
