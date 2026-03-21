using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Permissions.GetByUserId;

/// <summary>
/// Verilen kullan�c� i�in efektif permission kodlar�n� d�nd�r�r.
/// </summary>
public sealed record GetPermissionsByUserIdQuery(Guid UserId)
    : IRequest<IReadOnlyList<string>>;
