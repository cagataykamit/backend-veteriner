using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.UpdateWard;

/// <summary>Bölüm adı ve sırası; aktif/pasif ayrı komutla değişir. Yanıt bölümün güncel hali (odalarıyla).</summary>
public sealed record UpdateWardCommand(Guid ClinicId, Guid WardId, string Name, int SortOrder)
    : IRequest<Result<HospitalizationWardDto>>;
