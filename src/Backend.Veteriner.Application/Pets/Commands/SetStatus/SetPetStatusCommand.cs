using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Pets.Commands.SetStatus;

/// <summary>
/// Hayvanı vefat olarak işaretler (tarih opsiyonel) veya yanlış girişi geri alarak Aktife döndürür.
/// <see cref="RowVersion"/> zorunludur; eski sürümle yazım 409 döner.
/// </summary>
public sealed record SetPetStatusCommand(
    Guid Id,
    PetStatus Status,
    DateOnly? DeceasedOn,
    string? RowVersion)
    : IRequest<Result<PetStatusDto>>, IAuditableRequest
{
    public string AuditAction => Status == PetStatus.Deceased ? "Pet.MarkDeceased" : "Pet.MarkActive";
    public string? AuditTarget => $"PetId={Id}";
}
