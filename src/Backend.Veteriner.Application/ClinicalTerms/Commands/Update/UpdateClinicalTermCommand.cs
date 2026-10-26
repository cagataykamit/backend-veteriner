using Backend.Veteriner.Application.ClinicalTerms.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.ClinicalTerms.Commands.Update;

/// <summary>Yalnız ad düzeltilir (tür sabit); <see cref="RowVersion"/> zorunludur, eski sürümle yazım 409 döner.</summary>
public sealed record UpdateClinicalTermCommand(Guid Id, string Name, string? RowVersion)
    : IRequest<Result<ClinicalTermDto>>, IAuditableRequest
{
    public string AuditAction => "ClinicalTerm.Update";
    public string? AuditTarget => $"ClinicalTermId={Id}";
}
