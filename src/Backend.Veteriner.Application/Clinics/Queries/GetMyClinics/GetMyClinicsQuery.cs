using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.GetMyClinics;

public sealed record GetMyClinicsQuery(bool? IsActive = null) : IRequest<Result<IReadOnlyList<ClinicListItemDto>>>;

