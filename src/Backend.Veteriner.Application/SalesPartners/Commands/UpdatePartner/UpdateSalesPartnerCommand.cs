using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.SalesPartners.Commands.CreatePartner;
using Backend.Veteriner.Application.SalesPartners.Contracts;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.SalesPartners.Commands.UpdatePartner;

/// <summary>Ortak bilgisi ve aktifliği; pasif ortağın kodları kullanılamaz (var olan kullanımlar sürer).</summary>
public sealed record UpdateSalesPartnerCommand(Guid Id, string Name, string? Email, string? Phone, Guid? LinkedTenantId, bool IsActive)
    : IRequest<Result<SalesPartnerDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "SalesPartner.Update";
    public string? AuditTarget => $"SalesPartnerId={Id}";
}

public sealed class UpdateSalesPartnerCommandValidator : AbstractValidator<UpdateSalesPartnerCommand>
{
    public UpdateSalesPartnerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        SalesPartnerInputRules.Apply(this, x => x.Name, x => x.Email, x => x.Phone);
    }
}
