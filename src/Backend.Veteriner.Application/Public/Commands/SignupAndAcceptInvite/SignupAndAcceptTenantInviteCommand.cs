using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Public.Commands.SignupAndAcceptInvite;

// Public davet akışı (anonim signup + accept) tenant abonelik durumundan bağımsızdır;
// guard çağıran tarafı (henüz tenant context yok) blokladığında akış kullanılamaz hale gelir.
// AcceptedLegalDocuments: davetteki role gereken belgelerin güncel sürümü zorunlu (ADR-029; davet detayı requiredLegalDocuments).
// Kullanıcı, davet kabulü (üyelik + yetki) ve belge kabulleri tek veritabanı işleminde yazılır; kabul başarısızsa kullanıcı da kalmaz.
public sealed record SignupAndAcceptTenantInviteCommand(
    string RawToken,
    string Password,
    IReadOnlyList<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments = null)
    : IRequest<Result<TenantInviteAcceptResultDto>>, IIgnoreTenantWriteSubscriptionGuard, ITransactionalRollbackOnFailureRequest;
