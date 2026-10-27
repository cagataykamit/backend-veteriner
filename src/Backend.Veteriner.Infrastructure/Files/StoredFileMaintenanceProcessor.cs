using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Files.Specs;
using Backend.Veteriner.Domain.Files;
using Backend.Veteriner.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Veteriner.Infrastructure.Files;

/// <summary>Dosya bakım işi ayarları (<c>FileStorage:Maintenance</c>; IMAGING_API_CONTRACT.md §6).</summary>
public sealed class StoredFileMaintenanceOptions
{
    public const string SectionName = "FileStorage:Maintenance";

    /// <summary>False ise hosted service çalışmaz (işlemci elle çağrılabilir kalır; testler).</summary>
    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 30;

    public int BatchSize { get; set; } = 100;

    /// <summary>Bu süreden eski <c>Uploading</c> kaydı yarım yükleme sayılır.</summary>
    public int StaleUploadMinutes { get; set; } = 60;
}

/// <summary>
/// Yarım yükleme temizliği (ASAMA6 R4): süresi geçmiş <see cref="StoredFileStatus.Uploading"/> kayıtlarının içeriğini (varsa) siler
/// ve kaydı <see cref="StoredFileStatus.Deleted"/> yapar (silen yok = sistem). Kural <see cref="StoredFile.IsStaleUpload"/>;
/// her dosya kendi kapsamında işlenir, aynı anda biten yükleme rowversion'a çarpar ve atlanır. Günlüğe yalnız kimlik ve sayı yazılır.
/// </summary>
public sealed class StoredFileMaintenanceProcessor
{
    private readonly IServiceScopeFactory _scopes;
    private readonly StoredFileMaintenanceOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StoredFileMaintenanceProcessor> _logger;

    public StoredFileMaintenanceProcessor(
        IServiceScopeFactory scopes,
        IOptions<StoredFileMaintenanceOptions> options,
        TimeProvider timeProvider,
        ILogger<StoredFileMaintenanceProcessor> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <returns>Temizlenen yarım yükleme sayısı.</returns>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var staleAfter = TimeSpan.FromMinutes(Math.Max(1, _options.StaleUploadMinutes));

        // Bakım taraması kiracılar arasıdır (ADR-040); her dosya kendi kiracısının kapsamında temizlenir.
        List<(Guid Id, Guid TenantId)> due;
        await using (var scope = _scopes.CreateSystemScope())
        {
            var files = scope.ServiceProvider.GetRequiredService<IReadRepository<StoredFile>>();
            var stale = await files.ListAsync(
                new StoredFilesStaleUploadSpec(now, staleAfter, Math.Max(1, _options.BatchSize)), ct);
            due = stale.Select(f => (f.Id, f.TenantId)).ToList();
        }

        var cleaned = 0;
        foreach (var (id, tenantId) in due)
        {
            if (await CleanAsync(id, tenantId, now, staleAfter, ct))
                cleaned++;
        }

        if (cleaned > 0)
            _logger.LogInformation("Stale uploads cleaned. Count={Count}", cleaned);
        return cleaned;
    }

    private async Task<bool> CleanAsync(Guid id, Guid tenantId, DateTime now, TimeSpan staleAfter, CancellationToken ct)
    {
        await using var scope = _scopes.CreateTenantScope(tenantId);
        var files = scope.ServiceProvider.GetRequiredService<IRepository<StoredFile>>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        try
        {
            var file = await files.FirstOrDefaultAsync(new StoredFilesStaleUploadByIdSpec(id, now, staleAfter), ct);
            if (file is null)
                return false;

            await storage.DeleteAsync(file.StorageKey, ct);
            file.MarkDeleted(null, now);
            await files.UpdateAsync(file, ct);
            await files.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (Exception ex) when (ex is FileStorageUnavailableException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Stale upload cleanup failed. FileId={FileId} Exception={ExceptionType}", id, ex.GetType().FullName);
            return false;
        }
    }
}
