using Backend.Veteriner.Application.Clinics.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Clinics.Commands.KvkkFormTemplate.UpdateClinicKvkkFormTemplate;

/// <summary>
/// Klinik KVKK form şablon metnini kaydeder (ADR-021). Kayıt yokken <see cref="RowVersion"/> boş gönderilir ve kayıt oluşur;
/// kayıt varken zorunludur. Sürüm uyuşmazsa 409.
/// </summary>
public sealed record UpdateClinicKvkkFormTemplateCommand(Guid ClinicId, string? Text, string? RowVersion)
    : IRequest<Result<ClinicKvkkFormTemplateDto>>, IAuditableRequest
{
    public string AuditAction => "Clinic.KvkkFormTemplate.Update";
    public string? AuditTarget => $"ClinicId={ClinicId}";
}
