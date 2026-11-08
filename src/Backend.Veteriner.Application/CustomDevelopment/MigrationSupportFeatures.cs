using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Tenants;
using Backend.Veteriner.Domain.CustomDevelopment;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.CustomDevelopment;

/// <summary>
/// Kiracının geçiş desteği talepleri ve talep ekranının bilgileri (TIER-021): pakete göre ücret, görüşme süresi, açık talep sınırı.
/// </summary>
public sealed record MigrationSupportOverviewDto(
    IReadOnlyList<CustomDevelopmentRequestDto> Items,
    int OpenCount,
    int MaxOpen,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] MigrationSupportFee Fee,
    int MeetingDurationMinutes);

/// <summary>GET /api/v1/custom-development-requests/migration-support. Her pakette açık.</summary>
public sealed record GetMigrationSupportQuery : IRequest<Result<MigrationSupportOverviewDto>>;

/// <summary>
/// POST /api/v1/custom-development-requests/migration-support. Her pakette açılır; ücret bilgisi talep anındaki pakete göre yazılır.
/// <see cref="RequestText"/> kliniğin notudur ve denetim kaydında maskelenir (AuditBehavior). Dosya almaz.
/// </summary>
public sealed record CreateMigrationSupportRequestCommand(
    ImportSourceProfile? SourceProgram,
    string? OtherProgramName,
    MigrationRecordVolume? RecordVolume,
    MigrationMeetingDays? MeetingDays,
    MigrationMeetingTimes? MeetingTimes,
    string? RequestText)
    : IRequest<Result<CustomDevelopmentRequestDto>>, IAuditableRequest
{
    public string AuditAction => "CustomDevelopmentRequest.CreateMigrationSupport";
    public string? AuditTarget => $"SourceProgram={SourceProgram};RecordVolume={RecordVolume}";
}

public sealed class CreateMigrationSupportRequestCommandValidator : AbstractValidator<CreateMigrationSupportRequestCommand>
{
    // Gün ve saat bayraklarının geçerliliği ve metin kuralları domain'de (CustomDevelopmentRequest.CreateMigrationSupport).
    public CreateMigrationSupportRequestCommandValidator()
    {
        RuleFor(x => x.SourceProgram).NotNull().IsInEnum();
        RuleFor(x => x.RecordVolume).NotNull().IsInEnum();
        RuleFor(x => x.MeetingDays).NotNull();
        RuleFor(x => x.MeetingTimes).NotNull();
    }
}

public sealed class MigrationSupportRequestsByTenantSpec : Specification<CustomDevelopmentRequest>
{
    public MigrationSupportRequestsByTenantSpec(Guid tenantId)
    {
        Query.AsNoTracking();
        Query.Where(r => r.TenantId == tenantId && r.TargetKind == CustomDevelopmentTargetKind.Migration)
            .OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id);
    }
}

public sealed class OpenMigrationSupportRequestsByTenantSpec : Specification<CustomDevelopmentRequest>
{
    public OpenMigrationSupportRequestsByTenantSpec(Guid tenantId)
        => Query.Where(r => r.TenantId == tenantId
                            && r.TargetKind == CustomDevelopmentTargetKind.Migration
                            && CustomDevelopmentRequest.OpenStatuses.Contains(r.Status));
}

internal static class MigrationSupportRules
{
    /// <summary>
    /// Ücret kuralı: paket <see cref="PlanFeature.FreeMigrationSupport"/> içeriyorsa (Kurumsal) ücretsiz, değilse ücretli ve fiyat
    /// bildirilir. Paket farkı yalnız <see cref="SubscriptionPlanCatalog"/> tablosundadır; denemede seçilen pakete göre.
    /// </summary>
    public static async Task<MigrationSupportFee> FeeForAsync(TenantPlanFeatureEvaluator planFeatures, Guid tenantId, CancellationToken ct)
        => await planFeatures.IsAllowedAsync(tenantId, PlanFeature.FreeMigrationSupport, ct)
            ? MigrationSupportFee.Free
            : MigrationSupportFee.Quoted;

    /// <summary>Talepte görünen program adı: listedeki programın adı; genel seçimde kliniğin yazdığı ad, yoksa profil adı.</summary>
    public static string ProgramName(ImportSourceProfile program, string? otherProgramName)
        => program == ImportSourceProfile.Generic && !string.IsNullOrWhiteSpace(otherProgramName)
            ? otherProgramName.Trim()
            : ImportSourceProfiles.Get(program).DisplayName;
}

public sealed class GetMigrationSupportQueryHandler : IRequestHandler<GetMigrationSupportQuery, Result<MigrationSupportOverviewDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IReadRepository<CustomDevelopmentRequest> _requests;
    private readonly IReadRepository<User> _users;

    public GetMigrationSupportQueryHandler(
        ITenantContext tenantContext,
        TenantPlanFeatureEvaluator planFeatures,
        IReadRepository<CustomDevelopmentRequest> requests,
        IReadRepository<User> users)
    {
        _tenantContext = tenantContext;
        _planFeatures = planFeatures;
        _requests = requests;
        _users = users;
    }

    public async ValueTask<Result<MigrationSupportOverviewDto>> Handle(GetMigrationSupportQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<MigrationSupportOverviewDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var rows = await _requests.ListAsync(new MigrationSupportRequestsByTenantSpec(tenantId), ct);
        var items = await CustomDevelopmentRequestRules.ToDtosAsync(_users, rows, ct);
        return Result<MigrationSupportOverviewDto>.Success(new MigrationSupportOverviewDto(
            items,
            items.Count(i => i.IsOpen),
            CustomDevelopmentRequest.MaxOpenMigrationPerTenant,
            await MigrationSupportRules.FeeForAsync(_planFeatures, tenantId, ct),
            CustomDevelopmentRequest.MeetingDurationMinutes));
    }
}

public sealed class CreateMigrationSupportRequestCommandHandler
    : IRequestHandler<CreateMigrationSupportRequestCommand, Result<CustomDevelopmentRequestDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly TenantPlanFeatureEvaluator _planFeatures;
    private readonly IRepository<CustomDevelopmentRequest> _requests;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public CreateMigrationSupportRequestCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        TenantPlanFeatureEvaluator planFeatures,
        IRepository<CustomDevelopmentRequest> requests,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _planFeatures = planFeatures;
        _requests = requests;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<CustomDevelopmentRequestDto>> Handle(CreateMigrationSupportRequestCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<CustomDevelopmentRequestDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");
        if (_clientContext.UserId is not { } userId)
            return Result<CustomDevelopmentRequestDto>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");

        if (await _requests.CountAsync(new OpenMigrationSupportRequestsByTenantSpec(tenantId), ct)
            >= CustomDevelopmentRequest.MaxOpenMigrationPerTenant)
        {
            return Result<CustomDevelopmentRequestDto>.Failure(
                "CustomDevelopment.MigrationAlreadyOpen",
                "Açık bir geçiş desteği talebiniz var; sonuçlanınca yenisini açabilirsiniz.");
        }

        var program = request.SourceProgram!.Value;
        var created = CustomDevelopmentRequest.CreateMigrationSupport(
            tenantId,
            program,
            MigrationSupportRules.ProgramName(program, request.OtherProgramName),
            request.RecordVolume!.Value,
            request.MeetingDays!.Value,
            request.MeetingTimes!.Value,
            request.RequestText,
            await MigrationSupportRules.FeeForAsync(_planFeatures, tenantId, ct),
            userId,
            _timeProvider.GetUtcNow().UtcDateTime);
        if (!created.IsSuccess)
            return Result<CustomDevelopmentRequestDto>.Failure(created.Error);

        await _requests.AddAsync(created.Value!, ct);
        await _requests.SaveChangesAsync(ct);

        return Result<CustomDevelopmentRequestDto>.Success((await CustomDevelopmentRequestRules.ToDtosAsync(_users, [created.Value!], ct))[0]);
    }
}
