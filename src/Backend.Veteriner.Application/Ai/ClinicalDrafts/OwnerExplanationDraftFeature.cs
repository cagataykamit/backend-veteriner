using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.OwnerExplanations;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.OwnerExplanations;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Ai.ClinicalDrafts;

// AI-003: muayene kaydından hasta sahibine sade açıklama taslağı (docs/AI_DRAFTS_API_CONTRACT.md §7.2). Kabulde muayeneye bağlı
// ayrı bir OwnerExplanation kaydı oluşur (CreateOwnerExplanationCommand); muayenenin kendisi değişmez, kesinleşmiş olabilir.

/// <param name="Note">Hekimin vurgulanmasını istediği noktalar (isteğe bağlı; saklanmaz, denetim yüküne yazılmaz).</param>
public sealed record GenerateOwnerExplanationDraftCommand(Guid ExaminationId, [property: JsonIgnore] string? Note)
    : IRequest<Result<AiDraftDto>>, IAuditableRequest
{
    public const int MaxNoteLength = 2000;

    public string AuditAction => "Ai.OwnerExplanation.Generate";
    public string? AuditTarget => $"ExaminationId={ExaminationId}";
}

public sealed class GenerateOwnerExplanationDraftCommandValidator : AbstractValidator<GenerateOwnerExplanationDraftCommand>
{
    public GenerateOwnerExplanationDraftCommandValidator()
    {
        RuleFor(x => x.ExaminationId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(GenerateOwnerExplanationDraftCommand.MaxNoteLength);
    }
}

public sealed class GenerateOwnerExplanationDraftCommandHandler
    : IRequestHandler<GenerateOwnerExplanationDraftCommand, Result<AiDraftDto>>
{
    private readonly AiDraftGenerator _generator;
    private readonly ClinicalAiContextBuilder _context;

    public GenerateOwnerExplanationDraftCommandHandler(AiDraftGenerator generator, ClinicalAiContextBuilder context)
    {
        _generator = generator;
        _context = context;
    }

    public async ValueTask<Result<AiDraftDto>> Handle(GenerateOwnerExplanationDraftCommand request, CancellationToken ct)
        => await _generator.GenerateAsync(AiFeature.OwnerExplanation, async (caller, token) =>
        {
            var exam = await _context.LoadExaminationAsync(caller.TenantId, caller.ClinicId, request.ExaminationId, token);
            if (!exam.IsSuccess)
                return Result<AiDraftRequest>.Failure(exam.Error);

            var input = await _context.BuildAsync(
                caller.TenantId, exam.Value!.PetId, exam.Value, null, includeExaminationFields: true, request.Note, token);
            return Result<AiDraftRequest>.Success(new AiDraftRequest(
                new AiDraftTarget(nameof(Examination), exam.Value.Id),
                exam.Value.PetId,
                input.Sources,
                ClinicalAiPrompts.OwnerExplanation,
                input.UserContent,
                text => AiDraftTextContent.Parse(text, input.PetName, OwnerExplanation.MaxTextLength)));
        }, ct);
}

/// <summary>Açıklama taslağının kabulü: muayeneye bağlı yeni <see cref="OwnerExplanation"/> (kaynak taslak kimliğiyle).</summary>
public sealed class OwnerExplanationDraftApplier : IAiDraftApplier
{
    private readonly ISender _sender;

    public OwnerExplanationDraftApplier(ISender sender) => _sender = sender;

    public AiFeature Feature => AiFeature.OwnerExplanation;

    public string RequiredPermission => PermissionCatalog.Examinations.Update;

    public async Task<Result<Guid?>> ApplyAsync(AiDraftApplication application, CancellationToken ct)
    {
        var text = AiDraftTextContent.Read(application.Content, OwnerExplanation.MaxTextLength);
        if (!text.IsSuccess)
            return Result<Guid?>.Failure(text.Error);

        var created = await _sender.Send(
            new CreateOwnerExplanationCommand(application.TargetId, text.Value!, application.DraftId), ct);
        return created.IsSuccess ? Result<Guid?>.Success(created.Value) : Result<Guid?>.Failure(created.Error);
    }
}
