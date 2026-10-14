using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Queries.KvkkFormTemplate.GetClinicKvkkFormTemplate;

public sealed record GetClinicKvkkFormTemplateQuery(Guid ClinicId)
    : IRequest<Result<ClinicKvkkFormTemplateDto>>;
