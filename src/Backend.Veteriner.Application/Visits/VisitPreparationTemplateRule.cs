using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Shared;

namespace Backend.Veteriner.Application.Visits;

/// <summary>
/// Geliş hazırlığında seçilen şablon/paket (CHECKIN-002/003) kiracıya görünür (sistem veya kendi) ve aktif olmalı. Geliş kaydı ve
/// hazırlık güncellemesi aynı kuralı kullanır. Pasifleşmiş şablon mevcut hazırlıkta kalabilir; yalnız yeni seçim denetlenir.
/// </summary>
internal static class VisitPreparationTemplateRule
{
    public static async Task<Result> EnsureSelectableAsync(
        IReadRepository<ExaminationTemplate> templates, Guid tenantId, Guid? templateId, CancellationToken ct)
    {
        if (templateId is not { } id)
            return Result.Success();

        var template = await templates.FirstOrDefaultAsync(new ExaminationTemplateVisibleByIdSpec(tenantId, id), ct);
        if (template is null)
            return Result.Failure("ExaminationTemplates.NotFound", "Muayene şablonu bulunamadı.");

        return template.IsActive
            ? Result.Success()
            : Result.Failure("Visits.Validation", "Pasif şablon gelişe hazırlanamaz.");
    }
}
