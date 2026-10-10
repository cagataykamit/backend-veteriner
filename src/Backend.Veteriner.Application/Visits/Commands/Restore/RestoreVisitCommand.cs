using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Visits.Commands.Restore;

/// <summary>Yanlış geliş işaretini gerekçeyle geri alır (<c>Visits.Correct</c>).</summary>
public sealed record RestoreVisitCommand(Guid VisitId, string Reason)
    : IRequest<Result<VisitDto>>, IAuditableRequest
{
    public string AuditAction => "Visit.Restore";
    public string? AuditTarget => $"VisitId={VisitId}";
}
