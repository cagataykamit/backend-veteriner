using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Examinations;
using Backend.Veteriner.Application.Examinations.Commands.Update;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Ai.ClinicalDrafts;

// AI-001: hekimin serbest notunu muayene alanlarına ayıran taslak (docs/AI_DRAFTS_API_CONTRACT.md §7.1). Serbest not
// saklanmaz: yalnız modele (kimlik bilgisi temizlenerek) gider; taslakta yalnız ayrılmış alanlar durur, kabulde muayeneye
// mevcut UpdateExaminationCommand ile yazılır. "AI taslağından" izi taslağın karar alanlarıdır (AiProvenanceReader).

/// <summary>Serbest muayene notundan alan taslağı üretir; muayeneye yazmaz. Not denetim yüküne yazılmaz.</summary>
public sealed record GenerateExamNoteDraftCommand(Guid ExaminationId, [property: JsonIgnore] string Text)
    : IRequest<Result<AiDraftDto>>, IAuditableRequest
{
    /// <summary>Serbest not üst sınırı (muayenenin en uzun alanıyla aynı).</summary>
    public const int MaxTextLength = Examination.MaxFindingsLength;

    public string AuditAction => "Ai.ExamNoteStructure.Generate";
    public string? AuditTarget => $"ExaminationId={ExaminationId}";
}

public sealed class GenerateExamNoteDraftCommandValidator : AbstractValidator<GenerateExamNoteDraftCommand>
{
    public GenerateExamNoteDraftCommandValidator()
    {
        RuleFor(x => x.ExaminationId).NotEmpty();
        RuleFor(x => x.Text).NotEmpty().MaximumLength(GenerateExamNoteDraftCommand.MaxTextLength);
    }
}

public sealed class GenerateExamNoteDraftCommandHandler : IRequestHandler<GenerateExamNoteDraftCommand, Result<AiDraftDto>>
{
    private readonly AiDraftGenerator _generator;
    private readonly ClinicalAiContextBuilder _context;

    public GenerateExamNoteDraftCommandHandler(AiDraftGenerator generator, ClinicalAiContextBuilder context)
    {
        _generator = generator;
        _context = context;
    }

    public async ValueTask<Result<AiDraftDto>> Handle(GenerateExamNoteDraftCommand request, CancellationToken ct)
        => await _generator.GenerateAsync(AiFeature.ExamNoteStructure, async (caller, token) =>
        {
            var exam = await _context.LoadExaminationAsync(caller.TenantId, caller.ClinicId, request.ExaminationId, token);
            if (!exam.IsSuccess)
                return Result<AiDraftRequest>.Failure(exam.Error);
            if (exam.Value!.IsFinalized)
                return Result<AiDraftRequest>.Failure(ExamNoteDraftApplier.FinalizedError);

            // Model yalnız notu böler: muayenenin mevcut alanları gönderilmez (hasta profili yalnız bağlam).
            var input = await _context.BuildAsync(
                caller.TenantId, exam.Value.PetId, exam.Value, null, includeExaminationFields: false, request.Text, token);
            return Result<AiDraftRequest>.Success(new AiDraftRequest(
                new AiDraftTarget(nameof(Examination), exam.Value.Id),
                exam.Value.PetId,
                input.Sources,
                ClinicalAiPrompts.ExamNote,
                input.UserContent,
                text => ExamNoteDraftContent.Parse(text, input.PetName)));
        }, ct);
}

/// <summary>
/// Muayene notu taslağının kabulü: alanlar mevcut <see cref="UpdateExaminationCommand"/> ile yazılır (kesinleşme, uzunluk ve
/// vital kuralları orada). Metin alanı varsayılan olarak mevcut metnin sonuna satır satır eklenir
/// (<see cref="ExaminationTextMerge.MergeLines"/>; aynı satır tekrar eklenmez); <c>replaceFields</c>'taki alan yerine yazılır.
/// Vital değer verildiyse mevcut değerin yerine geçer.
/// </summary>
public sealed class ExamNoteDraftApplier : IAiDraftApplier
{
    public static readonly Error FinalizedError = new("Examinations.Finalized", "Kesinleşmiş muayene düzenlenemez; ek not ekleyin.");

    private readonly ClinicalAiContextBuilder _context;
    private readonly ISender _sender;

    public ExamNoteDraftApplier(ClinicalAiContextBuilder context, ISender sender)
    {
        _context = context;
        _sender = sender;
    }

    public AiFeature Feature => AiFeature.ExamNoteStructure;

    public string RequiredPermission => PermissionCatalog.Examinations.Update;

    public bool IsEdited(JsonObject generated, JsonObject accepted) => ExamNoteDraftContent.IsEdited(generated, accepted);

    public async Task<Result<Guid?>> ApplyAsync(AiDraftApplication application, CancellationToken ct)
    {
        var fields = ExamNoteDraftContent.Read(application.Content);
        if (!fields.IsSuccess)
            return Result<Guid?>.Failure(fields.Error);

        var loaded = await _context.LoadExaminationAsync(application.TenantId, application.ClinicId, application.TargetId, ct);
        if (!loaded.IsSuccess)
            return Result<Guid?>.Failure(loaded.Error);
        var exam = loaded.Value!;
        var f = fields.Value!;

        string? Merge(string field, string? current)
        {
            if (!f.Texts.TryGetValue(field, out var text))
                return null;
            return f.Replace.Contains(field) ? text : ExaminationTextMerge.MergeLines(current, text).Text;
        }

        var updated = await _sender.Send(
            new UpdateExaminationCommand(
                exam.Id,
                null,
                null,
                null,
                exam.ExaminedAtUtc,
                Merge(ExamNoteDraftContent.VisitReason, exam.VisitReason) ?? exam.VisitReason,
                Merge(ExamNoteDraftContent.Findings, exam.Findings),
                Merge(ExamNoteDraftContent.Assessment, exam.Assessment),
                Merge(ExamNoteDraftContent.Notes, exam.Notes),
                ExaminationRowVersion.Encode(exam.RowVersion),
                Merge(ExamNoteDraftContent.Anamnesis, exam.Anamnesis),
                Merge(ExamNoteDraftContent.Plan, exam.Plan),
                f.WeightKg,
                f.TemperatureC,
                f.HeartRateBpm,
                f.RespiratoryRatePerMin),
            ct);
        return updated.IsSuccess ? Result<Guid?>.Success(exam.Id) : Result<Guid?>.Failure(updated.Error);
    }
}
