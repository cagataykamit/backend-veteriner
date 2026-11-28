using Backend.Veteriner.Application.Clinics.Access;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Domain.Ai;
using Backend.Veteriner.Domain.Shared;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Ai;

// Klinik ayarlarında "Yapay zekâ" (ADR-044 E1, TIER-013 Dilim 1): durum, kredi, kredi geçmişi, klinik yöneticisinin aç/kapa ve
// bilgilendirme kabulü. Sözleşme: docs/AI_HISTORY_SUMMARY_API_CONTRACT.md §3a. Dolar ve platform iç notu dönmez.

/// <summary>
/// Klinik yöneticisinin AI'yi açmadan önce kabul ettiği bilgilendirme; metnin tek yeri. Metin değişince <see cref="Version"/>
/// artırılır: açık klinik açık kalır, yeniden açmak isteyen yeni sürümü kabul eder.
/// </summary>
public static class AiAcknowledgement
{
    public const int Version = 1;

    public static readonly IReadOnlyList<string> Items =
    [
        "Yapay zekâ yalnız taslak üretir; hekim kontrol edip onaylamadan hiçbir kayda yazılmaz.",
        "Taslak hatalı ya da eksik olabilir; tanı ve tedavi kararı hekimindir.",
        "İstek sırasında ilgili klinik kayıtları yurt dışındaki yapay zekâ sağlayıcısına (Anthropic) gönderilir. Hasta sahibinin kimlik ve iletişim bilgileri gönderilmez; serbest notlara yazılan başka kişi adları gönderilebilir.",
        "Sağlayıcı bu verileri model eğitmek için kullanmaz.",
        "Kullanım krediyle ölçülür. Önce bu ayın paket kredisi, sonra eklenen kredi harcanır. Kredi bitince yalnız yapay zekâ durur, diğer işler etkilenmez.",
    ];
}

public sealed record ClinicAiAcknowledgementDto(int Version, IReadOnlyList<string> Items);

/// <summary>Kliniğe eklenen kredi (platform iç notu ve ekleyen kişi dönmez).</summary>
public sealed record ClinicAiCreditGrantDto(Guid Id, string Kind, int Credits, DateTime CreatedAtUtc);

/// <param name="UnavailableCode">Kullanılamıyorsa ilk engelin kodu (<c>/ai/status</c> ile aynı sıra); kullanılabiliyorsa <c>null</c>.</param>
/// <param name="HasAcknowledged">Güncel bilgilendirme kabul edilmiş mi; değilse açarken <see cref="Acknowledgement"/> gösterilir.</param>
/// <param name="MonthlyCredits">Bu ayın paket kredisi (denemede 0).</param>
/// <param name="FundCredits">Eklenen kredi bakiyesi (devreder).</param>
/// <param name="UsedPercent">Bu ayın kullanım oranı (0–100; <see cref="AiClinicQuota.UsedPercent"/>).</param>
/// <param name="CreditGrants">Son eklenen krediler, yeniden eskiye.</param>
public sealed record ClinicAiSettingsDto(
    Guid ClinicId,
    bool IsConfigured,
    bool IsEnabled,
    bool IsAvailable,
    string? UnavailableCode,
    DateTime? UpdatedAtUtc,
    bool HasAcknowledged,
    DateTime? AcknowledgedAtUtc,
    ClinicAiAcknowledgementDto Acknowledgement,
    int MonthlyCredits,
    decimal CreditsUsedThisMonth,
    decimal CreditsRemaining,
    decimal FundCredits,
    int UsedPercent,
    DateTime QuotaResetsAtUtc,
    IReadOnlyList<ClinicAiCreditGrantDto> CreditGrants);

/// <summary>Kliniğin AI ayarı ve kredi durumu; <c>Clinics.Read</c> ve klinik kapsamı.</summary>
public sealed record GetClinicAiSettingsQuery(Guid ClinicId) : IRequest<Result<ClinicAiSettingsDto>>;

/// <summary>
/// Klinik yöneticisi AI'yi açar/kapatır (<c>Clinics.Update</c>). Açarken güncel bilgilendirme daha önce kabul edilmemişse
/// <paramref name="AcknowledgementVersion"/> güncel sürüm olmalıdır; değilse <c>Ai.AcknowledgementRequired</c>. Denetime yazılır.
/// </summary>
public sealed record UpdateClinicAiSettingsCommand(Guid ClinicId, bool IsEnabled, int? AcknowledgementVersion)
    : IRequest<Result<ClinicAiSettingsDto>>, IAuditableRequest
{
    public string AuditAction => "Ai.ClinicSettings.UpdateByClinic";
    public string? AuditTarget => $"ClinicId={ClinicId};IsEnabled={IsEnabled};AcknowledgementVersion={AcknowledgementVersion}";
}

public sealed class GetClinicAiSettingsQueryValidator : AbstractValidator<GetClinicAiSettingsQuery>
{
    public GetClinicAiSettingsQueryValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

public sealed class UpdateClinicAiSettingsCommandValidator : AbstractValidator<UpdateClinicAiSettingsCommand>
{
    public UpdateClinicAiSettingsCommandValidator() => RuleFor(x => x.ClinicId).NotEmpty();
}

/// <summary>Klinik ayar yanıtını kurar: kullanılabilirlik ve kredi <see cref="AiUsageGuard"/>'dan, kredi geçmişi fon kayıtlarından.</summary>
public sealed class ClinicAiSettingsReader
{
    public const int MaxCreditGrants = 20;

    private readonly AiUsageGuard _guard;
    private readonly IReadRepository<AiCreditGrant> _grants;

    public ClinicAiSettingsReader(AiUsageGuard guard, IReadRepository<AiCreditGrant> grants)
    {
        _guard = guard;
        _grants = grants;
    }

    public async Task<ClinicAiSettingsDto> ReadAsync(Guid tenantId, Guid clinicId, ClinicAiSettings? row, CancellationToken ct)
    {
        var availability = await _guard.GetAvailabilityAsync(tenantId, clinicId, ct);
        var quota = availability.Quota;
        var grants = await _grants.ListAsync(new AiCreditGrantsByClinicSpec(clinicId, MaxCreditGrants), ct);

        return new ClinicAiSettingsDto(
            clinicId,
            availability.IsConfigured,
            availability.IsClinicEnabled,
            availability.Blocker is null,
            availability.Blocker?.Code,
            row?.UpdatedAtUtc ?? row?.CreatedAtUtc,
            row?.HasAcknowledged(AiAcknowledgement.Version) ?? false,
            row?.AcknowledgedAtUtc,
            new ClinicAiAcknowledgementDto(AiAcknowledgement.Version, AiAcknowledgement.Items),
            quota.MonthlyCredits,
            decimal.Round(quota.MonthCreditsUsed, 2),
            decimal.Round(quota.CreditsRemaining, 2),
            decimal.Round(quota.FundRemaining, 2),
            quota.UsedPercent,
            quota.ResetsAtUtc,
            grants.Select(g => new ClinicAiCreditGrantDto(g.Id, g.Kind.ToString(), g.Credits, g.CreatedAtUtc)).ToList());
    }
}

public sealed class GetClinicAiSettingsQueryHandler : IRequestHandler<GetClinicAiSettingsQuery, Result<ClinicAiSettingsDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IReadRepository<ClinicAiSettings> _settings;
    private readonly ClinicAiSettingsReader _reader;

    public GetClinicAiSettingsQueryHandler(
        ClinicDefinitionAccess access, IReadRepository<ClinicAiSettings> settings, ClinicAiSettingsReader reader)
    {
        _access = access;
        _settings = settings;
        _reader = reader;
    }

    public async ValueTask<Result<ClinicAiSettingsDto>> Handle(GetClinicAiSettingsQuery request, CancellationToken ct)
    {
        var access = await _access.EnsureReadAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicAiSettingsDto>.Failure(access.Error);

        var row = await _settings.FirstOrDefaultAsync(new ClinicAiSettingsByClinicSpec(access.Value, request.ClinicId), ct);
        return Result<ClinicAiSettingsDto>.Success(await _reader.ReadAsync(access.Value, request.ClinicId, row, ct));
    }
}

public sealed class UpdateClinicAiSettingsCommandHandler : IRequestHandler<UpdateClinicAiSettingsCommand, Result<ClinicAiSettingsDto>>
{
    private readonly ClinicDefinitionAccess _access;
    private readonly IClientContext _clientContext;
    private readonly IRepository<ClinicAiSettings> _settings;
    private readonly ClinicAiSettingsReader _reader;
    private readonly TimeProvider _timeProvider;

    public UpdateClinicAiSettingsCommandHandler(
        ClinicDefinitionAccess access,
        IClientContext clientContext,
        IRepository<ClinicAiSettings> settings,
        ClinicAiSettingsReader reader,
        TimeProvider timeProvider)
    {
        _access = access;
        _clientContext = clientContext;
        _settings = settings;
        _reader = reader;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ClinicAiSettingsDto>> Handle(UpdateClinicAiSettingsCommand request, CancellationToken ct)
    {
        var access = await _access.EnsureWriteAccessAsync(request.ClinicId, ct);
        if (!access.IsSuccess)
            return Result<ClinicAiSettingsDto>.Failure(access.Error);
        var tenantId = access.Value;

        if (_clientContext.UserId is not { } userId)
            return Result<ClinicAiSettingsDto>.Failure("Auth.Unauthorized.UserContextMissing", "Kullanıcı bağlamı yok.");

        var utcNow = _timeProvider.GetUtcNow().UtcDateTime;
        var row = await _settings.FirstOrDefaultAsync(new ClinicAiSettingsByClinicSpec(tenantId, request.ClinicId), ct);
        var isNew = row is null;
        row ??= ClinicAiSettings.Create(tenantId, request.ClinicId, utcNow);

        var changed = row.SetEnabledByClinic(request.IsEnabled, AiAcknowledgement.Version, request.AcknowledgementVersion, userId, utcNow);
        if (!changed.IsSuccess)
            return Result<ClinicAiSettingsDto>.Failure(changed.Error);

        if (isNew)
            await _settings.AddAsync(row, ct);
        await _settings.SaveChangesAsync(ct);
        return Result<ClinicAiSettingsDto>.Success(await _reader.ReadAsync(tenantId, request.ClinicId, row, ct));
    }
}
