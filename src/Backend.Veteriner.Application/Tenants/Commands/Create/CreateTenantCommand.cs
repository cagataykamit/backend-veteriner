using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.Create;

/// <summary>Kiracı ve deneme aboneliği tek veritabanı işleminde yazılır; hata durumunda ikisi de kalmaz.</summary>
public sealed record CreateTenantCommand(string Name) : IRequest<Result<Guid>>, ITransactionalRollbackOnFailureRequest;
