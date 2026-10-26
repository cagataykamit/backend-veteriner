using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Commands.CreateWard;

/// <summary>Klinikte yeni yatış bölümü; aktif bölümler arasında ad benzersizdir.</summary>
public sealed record CreateWardCommand(Guid ClinicId, string Name, int SortOrder) : IRequest<Result<Guid>>;
