using System.Globalization;
using System.Text.Json.Serialization;
using Ardalis.Specification;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Files.Contracts.Dtos;
using Backend.Veteriner.Application.Users.Common;
using Backend.Veteriner.Domain.DataExports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Users;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Application.DataExports;

/// <summary>Panel iş satırı (DATA_EXPORT_API_CONTRACT.md §4). Depo anahtarı hiçbir yanıtta yoktur.</summary>
public sealed record DataExportJobDto(
    Guid Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter))] DataExportJobStatus Status,
    DateTime RequestedAtUtc,
    string? RequestedByName,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? ExpiresAtUtc,
    long? FileSizeBytes,
    bool CanDownload);

/// <summary>Sayfa özeti: son işler ve talep kuralı (istemci kuralı yeniden hesaplamaz).</summary>
public sealed record DataExportOverviewDto(
    IReadOnlyList<DataExportJobDto> Items,
    int RetentionDays,
    int MaxRequestsPerWindow,
    int WindowHours,
    int RemainingRequests,
    bool HasOpenJob);

/// <summary>Kiracının tüm verisi için paket talebi. Veri taşınabilirliği kilitlenmez: salt okunur abonelikte de açıktır.</summary>
public sealed record RequestDataExportCommand
    : IRequest<Result<DataExportJobDto>>, IAuditableRequest, IIgnoreTenantWriteSubscriptionGuard
{
    public string AuditAction => "DataExport.Request";
    public string? AuditTarget => null;
}

public sealed record GetDataExportsQuery : IRequest<Result<DataExportOverviewDto>>;

/// <summary>Hazır paketin indirilmesi; her indirme denetim kaydına yazılır.</summary>
public sealed record DownloadDataExportQuery(Guid Id) : IRequest<Result<StoredFileContent>>, IAuditableRequest
{
    public string AuditAction => "DataExport.Download";
    public string? AuditTarget => $"DataExportJobId={Id}";
}

public sealed class DataExportJobsByTenantSpec : Specification<DataExportJob>
{
    public DataExportJobsByTenantSpec(Guid tenantId, int take)
    {
        Query.AsNoTracking();
        Query.Where(j => j.TenantId == tenantId).OrderByDescending(j => j.RequestedAtUtc).Take(take);
    }
}

public sealed class OpenDataExportJobSpec : Specification<DataExportJob>
{
    public OpenDataExportJobSpec(Guid tenantId)
        => Query.Where(j => j.TenantId == tenantId && DataExportJob.OpenStatuses.Contains(j.Status));
}

/// <summary>Talep sınırına sayılan işler: pencere içinde açılmış, başarısız olmayan işler.</summary>
public sealed class CountedDataExportRequestsSpec : Specification<DataExportJob>
{
    public CountedDataExportRequestsSpec(Guid tenantId, DateTime sinceUtc)
        => Query.Where(j => j.TenantId == tenantId && j.RequestedAtUtc > sinceUtc && j.Status != DataExportJobStatus.Failed);
}

public sealed class DataExportJobByIdSpec : Specification<DataExportJob>, ISingleResultSpecification<DataExportJob>
{
    public DataExportJobByIdSpec(Guid tenantId, Guid id)
    {
        Query.AsNoTracking();
        Query.Where(j => j.TenantId == tenantId && j.Id == id);
    }
}

public sealed record DataExportUser(Guid Id, string? DisplayName, string Email);

public sealed class DataExportUsersSpec : Specification<User, DataExportUser>
{
    public DataExportUsersSpec(IReadOnlyCollection<Guid> userIds)
    {
        Query.AsNoTracking();
        Query.Where(u => userIds.Contains(u.Id));
        Query.Select(u => new DataExportUser(u.Id, u.DisplayName, u.Email));
    }
}

/// <summary>Talep kuralı ve hata kodları — tek yer (DATA_EXPORT_API_CONTRACT.md §5).</summary>
public static class DataExportRules
{
    public const int ListSize = 10;
    public const string ZipContentType = "application/zip";

    public static readonly Error AnotherJobRunning =
        new("DataExports.AnotherJobRunning", "Hazırlanan bir dışa aktarma var; bitince yenisini isteyebilirsiniz.");

    public static readonly Error LimitReached =
        new("DataExports.LimitReached",
            $"Son {DataExportJob.RequestLimitWindow.TotalHours:0} saatte en çok {DataExportJob.MaxRequestsPerWindow} dışa aktarma istenebilir.");

    public static readonly Error NotFound = new("DataExports.NotFound", "Dışa aktarma bulunamadı.");

    public static readonly Error Expired =
        new("DataExports.Expired", "Paketin saklama süresi doldu; yeni bir dışa aktarma isteyin.");

    public static readonly Error NotReady = new("DataExports.NotReady", "Paket henüz hazır değil.");

    public static Result<(Guid TenantId, Guid UserId)> ResolveActor(ITenantContext tenantContext, IClientContext clientContext)
    {
        if (tenantContext.TenantId is not { } tenantId)
            return Result<(Guid, Guid)>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");
        if (clientContext.UserId is not { } userId)
            return Result<(Guid, Guid)>.Failure("Auth.UserContextMissing", "Kullanıcı bağlamı bulunamadı.");
        return Result<(Guid, Guid)>.Success((tenantId, userId));
    }

    public static DateTime WindowStart(DateTime nowUtc) => nowUtc - DataExportJob.RequestLimitWindow;

    public static int Remaining(int countedRequests) => Math.Max(0, DataExportJob.MaxRequestsPerWindow - countedRequests);

    /// <summary>İndirme dosya adı: paketin hazırlandığı İstanbul anı (<c>vetinity-veri-20261108-1430.zip</c>).</summary>
    public static string DownloadFileName(DataExportJob job)
        => $"vetinity-veri-{OperationDayBounds.ToLocal(job.CompletedAtUtc ?? job.RequestedAtUtc).ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.zip";

    public static async Task<IReadOnlyList<DataExportJobDto>> ToDtosAsync(
        IReadRepository<User> users, IReadOnlyList<DataExportJob> jobs, DateTime nowUtc, CancellationToken ct)
    {
        var ids = jobs.Select(j => j.RequestedByUserId).Distinct().ToArray();
        var names = ids.Length == 0
            ? new Dictionary<Guid, string?>()
            : (await users.ListAsync(new DataExportUsersSpec(ids), ct)).ToDictionary(u => u.Id, u => UserDisplayName.Resolve(u.DisplayName, u.Email));

        return jobs.Select(j => new DataExportJobDto(
                j.Id, j.Status, j.RequestedAtUtc, names.GetValueOrDefault(j.RequestedByUserId), j.StartedAtUtc, j.CompletedAtUtc,
                j.ExpiresAtUtc, j.FileSizeBytes, j.IsDownloadable(nowUtc)))
            .ToList();
    }
}

public sealed class RequestDataExportCommandHandler : IRequestHandler<RequestDataExportCommand, Result<DataExportJobDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IRepository<DataExportJob> _jobs;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public RequestDataExportCommandHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IRepository<DataExportJob> jobs,
        IReadRepository<User> users,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _jobs = jobs;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<DataExportJobDto>> Handle(RequestDataExportCommand request, CancellationToken ct)
    {
        var actor = DataExportRules.ResolveActor(_tenantContext, _clientContext);
        if (!actor.IsSuccess)
            return Result<DataExportJobDto>.Failure(actor.Error);
        var (tenantId, userId) = actor.Value;
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (await _jobs.AnyAsync(new OpenDataExportJobSpec(tenantId), ct))
            return Result<DataExportJobDto>.Failure(DataExportRules.AnotherJobRunning);

        var counted = await _jobs.CountAsync(new CountedDataExportRequestsSpec(tenantId, DataExportRules.WindowStart(now)), ct);
        if (DataExportRules.Remaining(counted) == 0)
            return Result<DataExportJobDto>.Failure(DataExportRules.LimitReached);

        var job = DataExportJob.Create(tenantId, userId, now);
        await _jobs.AddAsync(job, ct);
        try
        {
            await _jobs.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.ToString().Contains(DataExportJob.OpenJobIndexName, StringComparison.Ordinal))
        {
            // Eşzamanlı iki talepten biri: tek açık iş indeksi ikincisini reddeder.
            return Result<DataExportJobDto>.Failure(DataExportRules.AnotherJobRunning);
        }

        return Result<DataExportJobDto>.Success((await DataExportRules.ToDtosAsync(_users, [job], now, ct))[0]);
    }
}

public sealed class GetDataExportsQueryHandler : IRequestHandler<GetDataExportsQuery, Result<DataExportOverviewDto>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<DataExportJob> _jobs;
    private readonly IReadRepository<User> _users;
    private readonly TimeProvider _timeProvider;

    public GetDataExportsQueryHandler(
        ITenantContext tenantContext, IReadRepository<DataExportJob> jobs, IReadRepository<User> users, TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _jobs = jobs;
        _users = users;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<DataExportOverviewDto>> Handle(GetDataExportsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<DataExportOverviewDto>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var jobs = await _jobs.ListAsync(new DataExportJobsByTenantSpec(tenantId, DataExportRules.ListSize), ct);
        var counted = await _jobs.CountAsync(new CountedDataExportRequestsSpec(tenantId, DataExportRules.WindowStart(now)), ct);
        var hasOpen = await _jobs.AnyAsync(new OpenDataExportJobSpec(tenantId), ct);

        return Result<DataExportOverviewDto>.Success(new DataExportOverviewDto(
            await DataExportRules.ToDtosAsync(_users, jobs, now, ct),
            (int)DataExportJob.Retention.TotalDays,
            DataExportJob.MaxRequestsPerWindow,
            (int)DataExportJob.RequestLimitWindow.TotalHours,
            DataExportRules.Remaining(counted),
            hasOpen));
    }
}

public sealed class DownloadDataExportQueryHandler : IRequestHandler<DownloadDataExportQuery, Result<StoredFileContent>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<DataExportJob> _jobs;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DownloadDataExportQueryHandler> _logger;

    public DownloadDataExportQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<DataExportJob> jobs,
        IFileStorage storage,
        TimeProvider timeProvider,
        ILogger<DownloadDataExportQueryHandler> logger)
    {
        _tenantContext = tenantContext;
        _jobs = jobs;
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async ValueTask<Result<StoredFileContent>> Handle(DownloadDataExportQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<StoredFileContent>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var job = await _jobs.FirstOrDefaultAsync(new DataExportJobByIdSpec(tenantId, request.Id), ct);
        if (job is null)
            return Result<StoredFileContent>.Failure(DataExportRules.NotFound);
        if (job.Status == DataExportJobStatus.Expired || job.IsExpiryDue(_timeProvider.GetUtcNow().UtcDateTime))
            return Result<StoredFileContent>.Failure(DataExportRules.Expired);
        if (job.Status != DataExportJobStatus.Ready || job.StorageKey is null)
            return Result<StoredFileContent>.Failure(DataExportRules.NotReady);

        Stream? content;
        try
        {
            content = await _storage.OpenReadAsync(job.StorageKey, ct);
        }
        catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError("Data export read failed. Job={JobId} Exception={ExceptionType}", job.Id, ex.GetType().FullName);
            return Result<StoredFileContent>.Failure("Files.StorageUnavailable", "Dosya deposu şu anda kullanılamıyor; daha sonra tekrar deneyin.");
        }

        if (content is null)
        {
            _logger.LogError("Data export file missing. Job={JobId}", job.Id);
            return Result<StoredFileContent>.Failure(DataExportRules.NotFound);
        }

        return Result<StoredFileContent>.Success(
            new StoredFileContent(content, DataExportRules.ZipContentType, DataExportRules.DownloadFileName(job), job.FileSizeBytes ?? content.Length));
    }
}
