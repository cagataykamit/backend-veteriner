using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Notifications;
using Backend.Veteriner.Domain.DataExports;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.DataExports;

/// <summary>Tüm veri dışa aktarma işçisi ayarları (<c>DataExports:Worker</c>).</summary>
public sealed class DataExportWorkerOptions
{
    public const string SectionName = "DataExports:Worker";

    /// <summary>False ise hosted service çalışmaz (işlemci elle çağrılabilir kalır; testler).</summary>
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 15;

    public int MaintenanceIntervalMinutes { get; set; } = 60;
}

/// <summary>
/// Tüm veri dışa aktarma işi (DATA_EXPORT_API_CONTRACT.md §3). İş kuyruğu <c>DataExportJobs</c> tablosudur: açık ve kirası
/// boş/dolmuş iş alınır (rowversion: iki örnekten biri). Paket kiracı kapsamında geçici dosyaya yazılır, sonra depoya taşınır;
/// iş <see cref="DataExportJobStatus.Ready"/> olur ve talep edene bildirim düşer. Çöken iş kira dolunca yeniden denenir
/// (<see cref="DataExportJob.MaxAttempts"/>); beklenmeyen hata işi hemen başarısız kapatır. Bakım turu süresi dolan paketleri
/// siler. Günlüklere yalnız iş kimliği, sayı ve istisna türü yazılır (paket kişisel veridir).
/// </summary>
public sealed class DataExportProcessor
{
    private const int MaintenanceBatchSize = 100;

    private readonly IServiceScopeFactory _scopes;
    private readonly IFileStorage _storage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DataExportProcessor> _logger;

    public DataExportProcessor(
        IServiceScopeFactory scopes,
        IFileStorage storage,
        TimeProvider timeProvider,
        ILogger<DataExportProcessor> logger,
        string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId) || workerId.Length > DataExportJob.MaxLeaseOwnerLength)
            throw new ArgumentException("İşçi kimliği boş olamaz ve kira sahibi uzunluğunu aşamaz.", nameof(workerId));

        _scopes = scopes;
        _storage = storage;
        _timeProvider = timeProvider;
        _logger = logger;
        WorkerId = workerId;
    }

    public string WorkerId { get; }

    public static string NewWorkerId()
    {
        var id = $"export:{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        return id.Length <= DataExportJob.MaxLeaseOwnerLength ? id : id[^DataExportJob.MaxLeaseOwnerLength..];
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>Bir işi alıp sonuna kadar işler; alınacak iş yoksa false.</summary>
    public async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        var candidate = await FindCandidateAsync(ct);
        if (candidate is not { } job)
            return false;

        if (job.Attempts >= DataExportJob.MaxAttempts)
        {
            // Kirası dolmuş ve deneme hakkı bitmiş iş (art arda çöken paket): yeniden denenmez.
            _logger.LogWarning("Data export attempts exhausted. Job={JobId} Attempts={Attempts}", job.Id, job.Attempts);
            await FinishAsync(job.Id, job.TenantId, j => j.MarkFailed(Now), ct);
            return true;
        }

        if (!await TryClaimAsync(job.Id, job.TenantId, ct))
            return false;

        await RunAsync(job.Id, job.TenantId, job.RequestedAtUtc, ct);
        return true;
    }

    /// <summary>Saklama süresi dolan paketlerin dosyasını siler ve işi kapatır; kapatılan iş sayısı.</summary>
    public async Task<int> RunMaintenanceAsync(CancellationToken ct)
    {
        var now = Now;
        List<(Guid Id, Guid TenantId)> due;

        // Bakım taraması kiracılar arasıdır (ADR-040); her iş kendi kiracısının kapsamında kapanır.
        await using (var scope = _scopes.CreateSystemScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            due = (await db.DataExportJobs.AsNoTracking()
                    .Where(j => j.Status == DataExportJobStatus.Ready && j.ExpiresAtUtc <= now)
                    .OrderBy(j => j.ExpiresAtUtc)
                    .Take(MaintenanceBatchSize)
                    .Select(j => new { j.Id, j.TenantId })
                    .ToListAsync(ct))
                .Select(j => (j.Id, j.TenantId))
                .ToList();
        }

        var expired = 0;
        foreach (var (id, tenantId) in due)
        {
            await using var scope = _scopes.CreateTenantScope(tenantId);
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = await db.DataExportJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
            if (job?.StorageKey is not { } key || !job.IsExpiryDue(now))
                continue;

            // Önce dosya: silme başarısızsa iş hazır kalır ve sonraki turda yeniden denenir.
            await _storage.DeleteAsync(key, ct);
            if (!job.Expire(now).IsSuccess)
                continue;

            try
            {
                await db.SaveChangesAsync(ct);
                expired++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Aynı anda başka bakım turu kapattı.
            }
        }

        if (expired > 0)
            _logger.LogInformation("Data export maintenance expired packages. Count={Count}", expired);
        return expired;
    }

    private async Task<DataExportJob?> FindCandidateAsync(CancellationToken ct)
    {
        // Kuyruk taraması kiracılar arasıdır (ADR-040); işin kendisi kendi kiracısının kapsamında döner.
        await using var scope = _scopes.CreateSystemScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = Now;

        return await db.DataExportJobs.AsNoTracking()
            .Where(j => DataExportJob.OpenStatuses.Contains(j.Status)
                        && (j.LeaseUntilUtc == null || j.LeaseUntilUtc <= now || j.LeaseOwner == WorkerId))
            .OrderBy(j => j.RequestedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<bool> TryClaimAsync(Guid jobId, Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.DataExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !job.ClaimLease(WorkerId, Now).IsSuccess)
            return false;

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false; // Başka işçi aynı anda aldı.
        }
    }

    private async Task RunAsync(Guid jobId, Guid tenantId, DateTime requestedAtUtc, CancellationToken ct)
    {
        _logger.LogInformation("Data export started. Job={JobId} Worker={Worker}", jobId, WorkerId);
        var tempPath = Path.Combine(Path.GetTempPath(), $"vetinity-export-{jobId:N}-{Guid.NewGuid():N}.zip");
        try
        {
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await using (var scope = _scopes.CreateTenantScope(tenantId))
                {
                    var writer = scope.ServiceProvider.GetRequiredService<DataExportPackageWriter>();
                    await writer.WriteAsync(tenantId, Now, file, c => RenewLeaseOrThrowAsync(jobId, tenantId, c), ct);
                }

                // Anahtar talep anına bağlıdır: çöken denemenin yazdığı dosya aynı anahtardadır ve yenisiyle değişir.
                var key = DataExportJob.BuildStorageKey(tenantId, requestedAtUtc, jobId);
                await _storage.DeleteAsync(key, ct);
                file.Position = 0;
                await _storage.WriteAsync(key, file, ct);

                var size = file.Length;
                var finished = await FinishAsync(jobId, tenantId, j => j.MarkReady(WorkerId, key, size, Now), ct);
                if (!finished)
                {
                    LogLeaseLost(jobId);
                    return;
                }

                _logger.LogInformation("Data export ready. Job={JobId} Bytes={Bytes}", jobId, size);
            }
        }
        catch (DataExportLeaseLostException)
        {
            LogLeaseLost(jobId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Data export interrupted by shutdown; lease expires and the job resumes. Job={JobId}", jobId);
        }
        catch (Exception ex)
        {
            // İstisna mesajı kişisel veri içerebilir: yalnız tür ve yığın yazılır.
            _logger.LogError(
                "Data export failed unexpectedly. Job={JobId} Exception={ExceptionType} Inner={InnerType} Stack={Stack}",
                jobId, ex.GetType().FullName, ex.InnerException?.GetType().FullName, ex.StackTrace);
            await FinishAsync(jobId, tenantId, j => j.MarkFailed(Now), CancellationToken.None);
        }
        finally
        {
            TryDeleteTemp(tempPath);
        }
    }

    private async Task RenewLeaseOrThrowAsync(Guid jobId, Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.DataExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !job.RenewLease(WorkerId, Now).IsSuccess)
            throw new DataExportLeaseLostException();

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DataExportLeaseLostException();
        }
    }

    /// <summary>İşi sonlandırır ve talep edene bildirimi aynı kayıtla yazar; geçiş reddedilir ya da çakışırsa false.</summary>
    private async Task<bool> FinishAsync(Guid jobId, Guid tenantId, Func<DataExportJob, Result> transition, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.DataExportJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || !transition(job).IsSuccess)
            return false;

        await scope.ServiceProvider.GetRequiredService<IUserNotificationPublisher>().DataExportFinishedAsync(job, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }

    private void LogLeaseLost(Guid jobId)
        => _logger.LogWarning("Data export lease lost; another worker owns the job. Job={JobId} Worker={Worker}", jobId, WorkerId);

    private static void TryDeleteTemp(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class DataExportLeaseLostException : Exception;
}
