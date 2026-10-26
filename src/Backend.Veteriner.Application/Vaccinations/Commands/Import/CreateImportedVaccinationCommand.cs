using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Import;

/// <summary>
/// İçe aktarma aşı yolu (ADR-019, IMPORT_API_CONTRACT.md §6.5): geçmiş tarihli <c>Applied</c> aşıyı partiye bağlı yazar.
/// Yalnız içe aktarma uygulama işinden çağrılır (HTTP ucu yoktur); klinik yazma erişimi ve izinler commit isteğinde
/// denetlenmiştir. Günlük girişteki 7 günlük geçmiş penceresi uygulanmaz; ürün/stok/uygulama kaydı oluşmaz.
/// Transaction sınırı çağıran sahip birimidir.
/// </summary>
public sealed record CreateImportedVaccinationCommand(
    Guid ImportBatchId,
    Guid ClinicId,
    Guid PetId,
    Guid VaccineDefinitionId,
    DateTime AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes)
    : IRequest<Result<Guid>>;
