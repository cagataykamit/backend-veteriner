using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.LegalAcceptances;
using Backend.Veteriner.Application.Public.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Public.Commands.OwnerSignup;

/// <param name="AcceptedLegalDocuments">Kayıt formunda kabul edilen belgeler ve sürümleri; üç belgenin güncel sürümü zorunlu (ADR-029).</param>
/// <param name="DiscountCode">İsteğe bağlı satış ortağı kodu (ADR-037); sunucuda yeniden doğrulanır, geçersizse kayıt yapılmaz.</param>
/// <param name="StartWithExampleClinic">
/// Onboarding seçimi (ADR-002, TRIAL-002): <c>true</c> ise kayıtla aynı işlemde yeni kliniğe Örnek Veteriner Kliniği
/// verisi yüklenir; yükleme başarısızsa kayıt da geri alınır. Varsayılan boş klinik.
/// </param>
/// <remarks>Kullanıcı, kiracı, abonelik, klinik, üyelikler, kabuller ve (istenirse) örnek klinik verisi tek veritabanı işleminde yazılır; hata durumunda hiçbiri kalmaz.</remarks>
public sealed record PublicOwnerSignupCommand(
    string PlanCode,
    string TenantName,
    string ClinicName,
    string ClinicCity,
    string Email,
    string Password,
    IReadOnlyList<LegalDocumentAcceptanceInput>? AcceptedLegalDocuments = null,
    string? DiscountCode = null,
    bool StartWithExampleClinic = false) : IRequest<Result<PublicOwnerSignupResultDto>>, ITransactionalRollbackOnFailureRequest;
