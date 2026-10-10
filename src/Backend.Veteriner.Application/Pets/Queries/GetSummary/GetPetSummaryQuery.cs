using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Pets.Queries.GetSummary;

/// <param name="ClinicId">Verilirse aktif klinik bağlamını geçersiz kılar.</param>
public sealed record GetPetSummaryQuery(Guid PetId, Guid? ClinicId = null)
    : IRequest<Result<PetSummaryDto>>;
