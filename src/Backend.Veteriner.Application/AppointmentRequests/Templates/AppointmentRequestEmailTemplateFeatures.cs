using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.AppointmentRequests;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.AppointmentRequests.Templates;

/// <summary>Şablon ve doldurulabilecek yer tutucular.</summary>
public sealed record AppointmentRequestEmailTemplateDto(
    AppointmentRequestEmailKind Kind,
    string Subject,
    string Body,
    bool IsDefault);

public sealed record AppointmentRequestEmailTemplatesDto(
    IReadOnlyList<AppointmentRequestEmailTemplateDto> Templates,
    IReadOnlyList<string> Placeholders);

/// <summary>Kliniğin dört talep e-postası şablonu (kayıt yoksa Vetinity varsayılanı, <c>isDefault=true</c>).</summary>
public sealed record GetAppointmentRequestEmailTemplatesQuery(Guid ClinicId) : IRequest<Result<AppointmentRequestEmailTemplatesDto>>;

/// <summary>Şablonu kaydeder (ADR-042 K5); ayarlar mevcut klinik ayar yetkisiyle.</summary>
public sealed record UpdateAppointmentRequestEmailTemplateCommand(Guid ClinicId, AppointmentRequestEmailKind Kind, string Subject, string Body)
    : IRequest<Result<AppointmentRequestEmailTemplateDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentRequest.EmailTemplate.Update";
    public string? AuditTarget => $"ClinicId={ClinicId};Kind={Kind}";
}

/// <summary>Kliniğin şablonunu siler; Vetinity varsayılanına döner.</summary>
public sealed record ResetAppointmentRequestEmailTemplateCommand(Guid ClinicId, AppointmentRequestEmailKind Kind)
    : IRequest<Result<AppointmentRequestEmailTemplateDto>>, IAuditableRequest
{
    public string AuditAction => "AppointmentRequest.EmailTemplate.Reset";
    public string? AuditTarget => $"ClinicId={ClinicId};Kind={Kind}";
}

/// <summary>Kaydetmeden önizleme: örnek değerlerle doldurulmuş metin.</summary>
public sealed record PreviewAppointmentRequestEmailTemplateQuery(Guid ClinicId, AppointmentRequestEmailKind Kind, string Subject, string Body)
    : IRequest<Result<AppointmentRequestEmailPreviewDto>>;

public sealed class UpdateAppointmentRequestEmailTemplateCommandValidator : AbstractValidator<UpdateAppointmentRequestEmailTemplateCommand>
{
    public UpdateAppointmentRequestEmailTemplateCommandValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(ClinicAppointmentRequestEmailTemplate.SubjectMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ClinicAppointmentRequestEmailTemplate.BodyMaxLength);
    }
}

public sealed class PreviewAppointmentRequestEmailTemplateQueryValidator : AbstractValidator<PreviewAppointmentRequestEmailTemplateQuery>
{
    public PreviewAppointmentRequestEmailTemplateQueryValidator()
    {
        RuleFor(x => x.ClinicId).NotEmpty();
        RuleFor(x => x.Kind).IsInEnum();
        RuleFor(x => x.Subject).NotEmpty().MaximumLength(ClinicAppointmentRequestEmailTemplate.SubjectMaxLength);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(ClinicAppointmentRequestEmailTemplate.BodyMaxLength);
    }
}

public sealed class GetAppointmentRequestEmailTemplatesQueryHandler
    : IRequestHandler<GetAppointmentRequestEmailTemplatesQuery, Result<AppointmentRequestEmailTemplatesDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly AppointmentRequestEmailComposer _emails;

    public GetAppointmentRequestEmailTemplatesQueryHandler(ClinicDefinitionAccess access, AppointmentRequestEmailComposer emails)
    {
        _access = access;
        _emails = emails;
    }

    public async ValueTask<Result<AppointmentRequestEmailTemplatesDto>> Handle(GetAppointmentRequestEmailTemplatesQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentRequestEmailTemplatesDto>.Failure(access.Error);

        var templates = new List<AppointmentRequestEmailTemplateDto>();
        foreach (var kind in Enum.GetValues<AppointmentRequestEmailKind>())
        {
            var t = await _emails.GetTemplateAsync(access.Value, request.ClinicId, kind, ct);
            templates.Add(new AppointmentRequestEmailTemplateDto(kind, t.Subject, t.Body, t.IsDefault));
        }

        return Result<AppointmentRequestEmailTemplatesDto>.Success(
            new AppointmentRequestEmailTemplatesDto(templates, AppointmentRequestEmails.Placeholders));
    }
}

public sealed class UpdateAppointmentRequestEmailTemplateCommandHandler
    : IRequestHandler<UpdateAppointmentRequestEmailTemplateCommand, Result<AppointmentRequestEmailTemplateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicAppointmentRequestEmailTemplate> _templates;
    private readonly TimeProvider _timeProvider;

    public UpdateAppointmentRequestEmailTemplateCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicAppointmentRequestEmailTemplate> templates,
        TimeProvider timeProvider)
    {
        _access = access;
        _templates = templates;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentRequestEmailTemplateDto>> Handle(UpdateAppointmentRequestEmailTemplateCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentRequestEmailTemplateDto>.Failure(access.Error);
        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

        var row = await _templates.FirstOrDefaultAsync(
            new ClinicAppointmentRequestEmailTemplatesSpec(access.Value, request.ClinicId, request.Kind), ct);
        try
        {
            if (row is null)
            {
                row = ClinicAppointmentRequestEmailTemplate.Create(access.Value, request.ClinicId, request.Kind, request.Subject, request.Body, utcNow);
                await _templates.AddAsync(row, ct);
            }
            else
            {
                row.Update(request.Subject, request.Body, utcNow);
            }
        }
        catch (ArgumentException ex)
        {
            return Result<AppointmentRequestEmailTemplateDto>.Failure("AppointmentRequests.Validation", ex.Message);
        }

        await _templates.SaveChangesAsync(ct);
        return Result<AppointmentRequestEmailTemplateDto>.Success(new AppointmentRequestEmailTemplateDto(row.Kind, row.Subject, row.Body, false));
    }
}

public sealed class ResetAppointmentRequestEmailTemplateCommandHandler
    : IRequestHandler<ResetAppointmentRequestEmailTemplateCommand, Result<AppointmentRequestEmailTemplateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IRepository<ClinicAppointmentRequestEmailTemplate> _templates;

    public ResetAppointmentRequestEmailTemplateCommandHandler(
        ClinicDefinitionAccess access,
        IRepository<ClinicAppointmentRequestEmailTemplate> templates)
    {
        _access = access;
        _templates = templates;
    }

    public async ValueTask<Result<AppointmentRequestEmailTemplateDto>> Handle(ResetAppointmentRequestEmailTemplateCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentRequestEmailTemplateDto>.Failure(access.Error);

        var row = await _templates.FirstOrDefaultAsync(
            new ClinicAppointmentRequestEmailTemplatesSpec(access.Value, request.ClinicId, request.Kind), ct);
        if (row is not null)
        {
            await _templates.DeleteAsync(row, ct);
            await _templates.SaveChangesAsync(ct);
        }

        var fallback = AppointmentRequestEmails.Default(request.Kind);
        return Result<AppointmentRequestEmailTemplateDto>.Success(
            new AppointmentRequestEmailTemplateDto(request.Kind, fallback.Subject, fallback.Body, true));
    }
}

public sealed class PreviewAppointmentRequestEmailTemplateQueryHandler
    : IRequestHandler<PreviewAppointmentRequestEmailTemplateQuery, Result<AppointmentRequestEmailPreviewDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly AppointmentRequestEmailComposer _emails;
    private readonly TimeProvider _timeProvider;

    public PreviewAppointmentRequestEmailTemplateQueryHandler(
        ClinicDefinitionAccess access,
        AppointmentRequestEmailComposer emails,
        TimeProvider timeProvider)
    {
        _access = access;
        _emails = emails;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<AppointmentRequestEmailPreviewDto>> Handle(PreviewAppointmentRequestEmailTemplateQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<AppointmentRequestEmailPreviewDto>.Failure(access.Error);

        var sampleStart = _timeProvider.GetUtcNow().UtcDateTime.Date.AddDays(1).AddHours(7);
        var message = await _emails.PreviewAsync(access.Value, request.ClinicId, request.Subject, request.Body, sampleStart, ct);
        return Result<AppointmentRequestEmailPreviewDto>.Success(new AppointmentRequestEmailPreviewDto(request.Kind, message.Subject, message.Body));
    }
}
