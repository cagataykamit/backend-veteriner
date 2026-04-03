using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Hospitalizations.Commands.Discharge;

/// <summary>
/// When <see cref="Notes"/> is non-null, replaces hospitalization notes (empty string clears after trim).
/// When null, notes are left unchanged.
/// </summary>
public sealed record DischargeHospitalizationCommand(
    Guid Id,
    DateTime DischargedAtUtc,
    string? Notes)
    : IRequest<Result>;
