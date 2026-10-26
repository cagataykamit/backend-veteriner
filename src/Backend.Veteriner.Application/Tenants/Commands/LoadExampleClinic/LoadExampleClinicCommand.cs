using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Tenants.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Tenants.Commands.LoadExampleClinic;

/// <summary>
/// Kiracının ilk kliniğine Örnek Veteriner Kliniği verisini yükler (ADR-002, TRIAL-002/003). Yalnız deneme (Trialing)
/// aboneliğinde ve kiracıda henüz müşteri/hasta yokken çalışır; ikinci çağrı <c>Tenants.ExampleClinicAlreadyLoaded</c>.
/// Tüm kayıtlar tek işlemde yazılır; ortadaki hata hiçbir şey bırakmaz.
/// </summary>
public sealed record LoadExampleClinicCommand(Guid TenantId)
    : IRequest<Result<ExampleClinicLoadSummaryDto>>, ITransactionalRollbackOnFailureRequest, IAuditableRequest
{
    public string AuditAction => "Tenant.ExampleClinicLoad";
    public string? AuditTarget => $"TenantId={TenantId}";
}
