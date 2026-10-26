using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Tenants.Commands.ClearExampleClinic;

/// <summary>
/// Kiracıdaki Örnek Veteriner Kliniği verisini kalıcı siler (ADR-002 risk maddesi; EXAMPLE_CLINIC_API_CONTRACT.md §10).
/// Örnek müşteri/hastalar ve onlara bağlı her şey gider; kullanılmış örnek ürün pasife alınır. Tek işlem: ortadaki hata
/// hiçbir şey silmez. Örnek veri yoksa <c>Tenants.ExampleClinicNotLoaded</c>.
/// </summary>
public sealed record ClearExampleClinicCommand(Guid TenantId)
    : IRequest<Result<ExampleClinicDataCountsDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Tenant.ExampleClinicClear";
    public string? AuditTarget => $"TenantId={TenantId}";
}
