using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Clinics.Commands.KvkkFormTemplate.UpdateClinicKvkkFormTemplate;

/// <summary>
/// Klinik KVKK şablon metnini (<see cref="Kind"/>: açık rıza formu ADR-021 veya aydınlatma metni ADR-024) kaydeder.
/// Kayıt yokken <see cref="RowVersion"/> boş gönderilir ve kayıt oluşur; kayıt varken zorunludur. Sürüm uyuşmazsa 409.
/// </summary>
public sealed record UpdateClinicKvkkFormTemplateCommand(
    Guid ClinicId,
    ClinicKvkkFormKind Kind,
    string? Text,
    string? RowVersion)
    : IRequest<Result<ClinicKvkkFormTemplateDto>>, IAuditableRequest
{
    public string AuditAction => Kind == ClinicKvkkFormKind.Disclosure
        ? "Clinic.KvkkDisclosureTemplate.Update"
        : "Clinic.KvkkFormTemplate.Update";

    public string? AuditTarget => $"ClinicId={ClinicId}";
}
