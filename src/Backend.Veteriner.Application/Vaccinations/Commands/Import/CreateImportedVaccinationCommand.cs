using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Vaccinations.Commands.Import;

/// <summary>
/// İçe aktarma aşı yolu (ADR-019, IMPORT_API_CONTRACT.md §6.5): geçmiş tarihli <c>Applied</c> aşıyı partiye bağlı yazar.
/// Yalnız içe aktarma uygulama işinden çağrılır (HTTP ucu yoktur); klinik yazma erişimi ve izinler commit isteğinde
/// denetlenmiştir. Günlük girişteki 7 günlük geçmiş penceresi uygulanmaz; ürün/stok/uygulama kaydı oluşmaz.
/// Transaction sınırı çağıran sahip birimidir. <see cref="SpeciesUnverified"/>: hastanın türü eski programda belirtilmemiş ("Diğer";
/// karar doğrulamada, <c>Imports.VaccineSpeciesUnverified</c>), aşı-tür uyumu denetlenmez. <see cref="DueTimeSpecified"/>: sonraki aşı tarihinin saati
/// kaynakta yazılıydı (takvimde saatiyle durur).
/// </summary>
public sealed record CreateImportedVaccinationCommand(
    Guid ImportBatchId,
    Guid ClinicId,
    Guid PetId,
    Guid VaccineDefinitionId,
    DateTime AppliedAtUtc,
    DateTime? DueAtUtc,
    string? Notes,
    bool SpeciesUnverified = false,
    bool DueTimeSpecified = false)
    : IRequest<Result<Guid>>;
