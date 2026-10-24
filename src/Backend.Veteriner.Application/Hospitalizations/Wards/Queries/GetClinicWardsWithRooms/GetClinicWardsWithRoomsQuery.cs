using Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Wards.Queries.GetClinicWardsWithRooms;

/// <summary>Kliniğin bölümleri ve odaları; aktif/pasif dahil, <c>SortOrder</c> sonra ad sıralı. Aktif süzmesi istemcide.</summary>
public sealed record GetClinicWardsWithRoomsQuery(Guid ClinicId) : IRequest<Result<IReadOnlyList<HospitalizationWardDto>>>;
