using Backend.Veteriner.Application.LabResults.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.LabResults.Commands.Cancel;

/// <summary>POST /api/v1/lab-results/{id}/cancel. Gerekçe 5–500 karakter; <see cref="RowVersion"/> zorunludur.</summary>
public sealed record CancelLabResultCommand(Guid Id, string? Reason, string? RowVersion)
    : IRequest<Result<LabResultWriteResultDto>>;
