using Backend.Veteriner.Domain.LabResults;

namespace Backend.Veteriner.Application.LabResults.Contracts.Dtos;

/// <summary>Değer satırı isteği (REF-001 Dilim 3). Referans aralığı sonuç raporundaki aralıktır; işaret sunucuda hesaplanır.</summary>
public sealed record LabResultLineRequest(
    string? ParameterName,
    decimal? Value,
    string? Unit = null,
    decimal? ReferenceLow = null,
    decimal? ReferenceHigh = null)
{
    /// <summary>Doğrulayıcıdan geçmiş istek için alan girdisi.</summary>
    public LabResultLineDraft ToDraft() => new(ParameterName ?? string.Empty, Value ?? 0m, Unit, ReferenceLow, ReferenceHigh);
}
