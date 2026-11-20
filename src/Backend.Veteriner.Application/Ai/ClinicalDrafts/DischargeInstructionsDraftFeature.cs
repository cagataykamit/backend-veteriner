using System.Text.Json.Serialization;
using Backend.Veteriner.Application.Ai.Drafts;
using Backend.Veteriner.Application.Auth;
using Backend.Veteriner.Application.CareInstructions.Commands.Create;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.CareInstructions;
using Backend.Veteriner.Domain.Examinations;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Ai.ClinicalDrafts;

// AI-004: taburcu / evde bakım talimatı taslağı (docs/AI_DRAFTS_API_CONTRACT.md §7.3). Kabulde mevcut bakım talimatı kaydı
// (CareInstruction; hasta sahibine verilen taburcu/evde bakım talimatı) CreateCareInstructionCommand ile oluşur. Kaynak muayene
// ya da yatıştır; yatıştan üretilen talimat yatışa (ve yatışın muayenesine) bağlanır.

/// <summary>Talimat taslağının dayandığı kayıt (taslağın hedef türü).</summary>
public enum DischargeDraftSource
{
    Examination = 1,
    Hospitalization = 2,
}

/// <param name="Note">Hekimin talimata eklenmesini istediği noktalar (isteğe bağlı; saklanmaz, denetim yüküne yazılmaz).</param>
public sealed record GenerateDischargeInstructionsDraftCommand(DischargeDraftSource Source, Guid SourceId, [property: JsonIgnore] string? Note)
    : IRequest<Result<AiDraftDto>>, IAuditableRequest
{
    public const int MaxNoteLength = 2000;

    public string AuditAction => "Ai.DischargeInstructions.Generate";
    public string? AuditTarget => $"{Source}Id={SourceId}";
}

public sealed class GenerateDischargeInstructionsDraftCommandValidator : AbstractValidator<GenerateDischargeInstructionsDraftCommand>
{
    public GenerateDischargeInstructionsDraftCommandValidator()
    {
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.SourceId).NotEmpty();
        RuleFor(x => x.Note).MaximumLength(GenerateDischargeInstructionsDraftCommand.MaxNoteLength);
    }
}

public sealed class GenerateDischargeInstructionsDraftCommandHandler
    : IRequestHandler<GenerateDischargeInstructionsDraftCommand, Result<AiDraftDto>>
{
    private readonly AiDraftGenerator _generator;
    private readonly ClinicalAiContextBuilder _context;

    public GenerateDischargeInstructionsDraftCommandHandler(AiDraftGenerator generator, ClinicalAiContextBuilder context)
    {
        _generator = generator;
        _context = context;
    }

    public async ValueTask<Result<AiDraftDto>> Handle(GenerateDischargeInstructionsDraftCommand request, CancellationToken ct)
        => await _generator.GenerateAsync(AiFeature.DischargeInstructions, async (caller, token) =>
        {
            Examination? exam;
            Hospitalization? hospitalization = null;
            if (request.Source == DischargeDraftSource.Hospitalization)
            {
                var loaded = await _context.LoadHospitalizationAsync(caller.TenantId, caller.ClinicId, request.SourceId, token);
                if (!loaded.IsSuccess)
                    return Result<AiDraftRequest>.Failure(loaded.Error);
                hospitalization = loaded.Value!;

                // Yatışın muayenesi varsa girdiye eklenir (bulgu, plan, reçete); yoksa yalnız yatış kaydı.
                exam = hospitalization.ExaminationId is { } examId
                    ? (await _context.LoadExaminationAsync(caller.TenantId, caller.ClinicId, examId, token)).Value
                    : null;
            }
            else
            {
                var loaded = await _context.LoadExaminationAsync(caller.TenantId, caller.ClinicId, request.SourceId, token);
                if (!loaded.IsSuccess)
                    return Result<AiDraftRequest>.Failure(loaded.Error);
                exam = loaded.Value!;
            }

            var petId = hospitalization?.PetId ?? exam!.PetId;
            var input = await _context.BuildAsync(
                caller.TenantId, petId, exam, hospitalization, includeExaminationFields: true, request.Note, token);
            return Result<AiDraftRequest>.Success(new AiDraftRequest(
                new AiDraftTarget(request.Source.ToString(), request.SourceId),
                petId,
                input.Sources,
                ClinicalAiPrompts.DischargeInstructions,
                input.UserContent,
                text => AiDraftTextContent.Parse(text, input.PetName, CareInstruction.MaxTextLength)));
        }, ct);
}

/// <summary>
/// Talimat taslağının kabulü: yeni bakım talimatı (<see cref="CreateCareInstructionCommand"/>). Muayeneden üretilen talimat
/// muayeneye, yatıştan üretilen yatışa bağlanır; geliş ve muayene bağları komutun kendi kuralıyla miras alınır.
/// </summary>
public sealed class DischargeInstructionsDraftApplier : IAiDraftApplier
{
    private readonly ClinicalAiContextBuilder _context;
    private readonly ISender _sender;

    public DischargeInstructionsDraftApplier(ClinicalAiContextBuilder context, ISender sender)
    {
        _context = context;
        _sender = sender;
    }

    public AiFeature Feature => AiFeature.DischargeInstructions;

    public string RequiredPermission => PermissionCatalog.CareInstructions.Create;

    public async Task<Result<Guid?>> ApplyAsync(AiDraftApplication application, CancellationToken ct)
    {
        var text = AiDraftTextContent.Read(application.Content, CareInstruction.MaxTextLength);
        if (!text.IsSuccess)
            return Result<Guid?>.Failure(text.Error);

        CreateCareInstructionCommand command;
        if (application.TargetType == nameof(DischargeDraftSource.Hospitalization))
        {
            var hospitalization = await _context.LoadHospitalizationAsync(application.TenantId, application.ClinicId, application.TargetId, ct);
            if (!hospitalization.IsSuccess)
                return Result<Guid?>.Failure(hospitalization.Error);
            command = new CreateCareInstructionCommand(
                application.ClinicId, hospitalization.Value!.PetId, hospitalization.Value.Id, null, null, text.Value!);
        }
        else
        {
            var exam = await _context.LoadExaminationAsync(application.TenantId, application.ClinicId, application.TargetId, ct);
            if (!exam.IsSuccess)
                return Result<Guid?>.Failure(exam.Error);
            command = new CreateCareInstructionCommand(application.ClinicId, exam.Value!.PetId, null, exam.Value.Id, null, text.Value!);
        }

        var created = await _sender.Send(command, ct);
        return created.IsSuccess ? Result<Guid?>.Success(created.Value) : Result<Guid?>.Failure(created.Error);
    }
}
