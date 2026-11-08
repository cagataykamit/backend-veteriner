using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Reminders.Marketing;

/// <summary>
/// Kliniğin ticari ileti kapısını açar ya da kapatır (ADR-046 K2). Açmak İYS beyanını ister (400
/// <see cref="ClinicMarketingSettings.DeclarationRequiredCode"/>); beyan ve kapatma denetim kaydına yazılır (kullanıcı ve zaman denetimde,
/// beyan sürümü hedefte). Yetki rotada <c>Reminders.Manage</c>; paket aranmaz (kapatmak her zaman serbest, açmak gönderim başlatmaz).
/// </summary>
public sealed record UpdateClinicMarketingGateCommand(Guid ClinicId, bool IsEnabled, bool IysDeclarationAccepted)
    : IRequest<Result<ClinicMarketingGateDto>>, IAuditableRequest
{
    public string AuditAction => "Reminders.MarketingGate.Update";

    public string? AuditTarget => IsEnabled
        ? $"ClinicId={ClinicId};IsEnabled=True;IysDeclarationAccepted={IysDeclarationAccepted};DeclarationVersion={ClinicMarketingSettings.CurrentDeclarationVersion}"
        : $"ClinicId={ClinicId};IsEnabled=False";
}

public sealed class UpdateClinicMarketingGateCommandValidator : AbstractValidator<UpdateClinicMarketingGateCommand>
{
    public UpdateClinicMarketingGateCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class UpdateClinicMarketingGateCommandHandler : IRequestHandler<UpdateClinicMarketingGateCommand, Result<ClinicMarketingGateDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ClinicMarketingSettings> _settings;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicMarketingGateCommandHandler(
        ClinicDefinitionAccess access,
        IClientContext clientContext,
        IRepository<ClinicMarketingSettings> settings,
        TimeProvider timeProvider)
    {
        _access = access;
        _clientContext = clientContext;
        _settings = settings;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicMarketingGateDto>> Handle(UpdateClinicMarketingGateCommand request, CancellationToken ct)
    {
        // Kural düzenlemesiyle aynı: yetki rotada, burada klinik kapsamı.
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicMarketingGateDto>.Failure(access.Error);
        var tenantId = access.Value;

        if (_clientContext.UserId is not { } userId)
            return Result<ClinicMarketingGateDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var row = await _settings.FirstOrDefaultAsync(new ClinicMarketingSettingsByClinicSpec(tenantId, request.ClinicId), ct);
        var isNew = row is null;
        row ??= ClinicMarketingSettings.Create(tenantId, request.ClinicId, utcNow);

        if (request.IsEnabled)
        {
            var enabled = row.Enable(request.IysDeclarationAccepted, userId, utcNow);
            if (!enabled.IsSuccess)
                return Result<ClinicMarketingGateDto>.Failure(enabled.Error);
        }
        else
        {
            row.Disable(userId, utcNow);
        }

        if (isNew)
            await _settings.AddAsync(row, ct);
        await _settings.SaveChangesAsync(ct);
        return Result<ClinicMarketingGateDto>.Success(ClinicMarketingGateDto.From(row));
    }
}
