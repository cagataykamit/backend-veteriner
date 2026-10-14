using Backend.Veteriner.Application.Administrations.Commands.Create;
using Backend.Veteriner.Application.Administrations.Contracts.Dtos;
using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Access;
using Backend.Veteriner.Application.ExaminationTemplates.Contracts.Dtos;
using Backend.Veteriner.Application.ExaminationTemplates.Specs;
using Backend.Veteriner.Domain.ChargeSuggestions;
using Backend.Veteriner.Domain.Catalog;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Tenants;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Application.ExaminationTemplates.Commands.Apply;

/// <summary>
/// Şablon uygulaması yeni bir yazma yolu açmaz (ADR-018 akışı aynen):
/// <list type="bullet">
/// <item>Metin: <see cref="Examination.PrefillFromTemplate"/> (birleştirme kuralı <see cref="ExaminationTextMerge"/>; kesinleşmiş muayene reddedilir).</item>
/// <item>Stoklu ürün satırı: mevcut <see cref="CreateAdministrationCommand"/> (FEFO, süresi geçmiş parti, yetersiz bakiye,
/// kontrollü ürün kuralları ve "ücret bekliyor" kalemi orada).</item>
/// <item>Hizmet satırı: stok yok; fiyatlıysa <see cref="ChargeSuggestion.ForExaminationService"/> kalemi (uygulamayla aynı fiyat kuralı).</item>
/// </list>
/// Satır ürünleri uygulama anında yeniden doğrulanır (<see cref="ExaminationTemplateLineResolver"/>). Atomiklik
/// <see cref="ITransactionalRollbackOnFailureRequest"/> ile sağlanır; muayene sürümü ilk yazımda denetlenir, böylece çift tıklama stok düşmeden reddedilir.
/// </summary>
public sealed class ApplyExaminationTemplateCommandHandler
    : IRequestHandler<ApplyExaminationTemplateCommand, Result<ExaminationTemplateApplyResultDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClinicContext _clinicContext;
    private readonly IClinicReadScopeResolver _clinicScopeResolver;
    private readonly IReadRepository<Tenant> _tenants;
    private readonly IReadRepository<Examination> _examinationsRead;
    private readonly IRepository<Examination> _examinationsWrite;
    private readonly IReadRepository<ExaminationTemplate> _templates;
    private readonly IReadRepository<Product> _products;
    private readonly IRepository<ChargeSuggestion> _chargeSuggestions;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public ApplyExaminationTemplateCommandHandler(
        ITenantContext tenantContext,
        IClinicContext clinicContext,
        IClinicReadScopeResolver clinicScopeResolver,
        IReadRepository<Tenant> tenants,
        IReadRepository<Examination> examinationsRead,
        IRepository<Examination> examinationsWrite,
        IReadRepository<ExaminationTemplate> templates,
        IReadRepository<Product> products,
        IRepository<ChargeSuggestion> chargeSuggestions,
        ISender sender,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clinicContext = clinicContext;
        _clinicScopeResolver = clinicScopeResolver;
        _tenants = tenants;
        _examinationsRead = examinationsRead;
        _examinationsWrite = examinationsWrite;
        _templates = templates;
        _products = products;
        _chargeSuggestions = chargeSuggestions;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async Task<Result<ExaminationTemplateApplyResultDto>> Handle(
        ApplyExaminationTemplateCommand request, CancellationToken ct)
    {
        if (!ExaminationRowVersion.TryDecode(request.RowVersion, out var expectedRowVersion))
        {
            return Fail(new Error("Examinations.Validation", "RowVersion zorunludur ve geçerli Base64 (8 bayt) olmalıdır."));
        }

        var loaded = await ExaminationWriteAccess.LoadAsync(
            _tenantContext, _clinicContext, _clinicScopeResolver, _tenants, _examinationsRead, request.ExaminationId, ct);
        if (!loaded.IsSuccess)
            return Fail(loaded.Error);

        var examination = loaded.Value!;
        var tenantId = examination.TenantId;

        var template = await _templates.FirstOrDefaultAsync(
            new ExaminationTemplateVisibleByIdSpec(tenantId, request.TemplateId), ct);
        if (template is null)
            return Fail(ExaminationTemplateErrors.NotFound);

        if (!template.IsActive)
            return Fail(ExaminationTemplateErrors.Inactive);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var prefill = examination.PrefillFromTemplate(template.VisitReason, template.Findings, template.Plan, nowUtc);
        if (!prefill.IsSuccess)
            return Fail(prefill.Error);

        var lines = template.Lines.OrderBy(l => l.SortOrder).ToList();
        var products = await ExaminationTemplateLineResolver.ResolveAsync(
            _products, tenantId, lines.Select(l => l.ProductId).ToList(), ct);
        if (!products.IsSuccess)
            return Fail(products.Error);

        examination.SetExpectedRowVersion(expectedRowVersion);
        try
        {
            await _examinationsWrite.UpdateAsync(examination, ct);
            await _examinationsWrite.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail(new Error(
                "Examinations.ConcurrencyConflict",
                "Muayene kaydı eşzamanlı olarak güncellendi; formu yenileyip tekrar deneyin."));
        }

        var administrations = new List<AdministrationDto>();
        var services = new List<AppliedServiceLineDto>();
        foreach (var line in lines)
        {
            var product = products.Value![line.ProductId];
            if (product.IsStockTracked)
            {
                var administration = await _sender.Send(
                    new CreateAdministrationCommand(
                        examination.ClinicId,
                        examination.PetId,
                        examination.Id,
                        TreatmentId: null,
                        product.Id,
                        line.Quantity,
                        AdministeredAtUtc: nowUtc,
                        Notes: null,
                        LateEntryReason: null,
                        RequestId: null),
                    ct);
                if (!administration.IsSuccess)
                    return Fail(WithProduct(administration.Error, product.Id));

                administrations.Add(administration.Value!.Administration);
                continue;
            }

            Guid? suggestionId = null;
            if (ChargeSuggestion.IsChargeable(product))
            {
                var suggestion = ChargeSuggestion.ForExaminationService(examination, product, line.Quantity, nowUtc);
                if (!suggestion.IsSuccess)
                    return Fail(WithProduct(suggestion.Error, product.Id));

                await _chargeSuggestions.AddAsync(suggestion.Value!, ct);
                suggestionId = suggestion.Value!.Id;
            }

            services.Add(new AppliedServiceLineDto(product.Id, product.Name, product.Unit, line.Quantity, suggestionId));
        }

        return Result<ExaminationTemplateApplyResultDto>.Success(new ExaminationTemplateApplyResultDto(
            examination.Id,
            ExaminationRowVersion.Encode(examination.RowVersion),
            prefill.Value!,
            administrations,
            services));
    }

    /// <summary>Reddedilen satırın ürünü ProblemDetails'te <c>productId</c> olarak döner (arayüz satırı işaretler).</summary>
    private static Error WithProduct(Error error, Guid productId)
        => new(error.Code, error.Message, ExaminationTemplateLineResolver.ProductExtension(productId));

    private static Result<ExaminationTemplateApplyResultDto> Fail(Error error)
        => Result<ExaminationTemplateApplyResultDto>.Failure(error);
}
