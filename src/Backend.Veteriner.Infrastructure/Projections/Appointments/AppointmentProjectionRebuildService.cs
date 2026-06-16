using Backend.Veteriner.Application.Appointments.IntegrationEvents;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Outbox;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Veteriner.Infrastructure.Projections.Appointments;

public class AppointmentProjectionRebuildService : IAppointmentProjectionRebuildService
{
    public const int DefaultBatchSize = 1000;

    private static readonly Guid RebuildEventId = Guid.Empty;

    private readonly AppDbContext _commandDb;
    private readonly QueryDbContext _queryDb;
    private readonly ILogger<AppointmentProjectionRebuildService> _logger;

    public AppointmentProjectionRebuildService(
        AppDbContext commandDb,
        QueryDbContext queryDb,
        ILogger<AppointmentProjectionRebuildService> logger)
    {
        _commandDb = commandDb;
        _queryDb = queryDb;
        _logger = logger;
    }

    public async Task<AppointmentProjectionRebuildResult> RebuildAsync(
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        var started = DateTime.UtcNow;
        batchSize = Math.Max(1, batchSize);

        await EnsureDistinctDatabasesAsync(cancellationToken);

        var pendingOutboxCount = await CountPendingAppointmentOutboxAsync(cancellationToken);
        var deadLetterOutboxCount = await CountDeadLetterAppointmentOutboxAsync(cancellationToken);

        if (pendingOutboxCount > 0 || deadLetterOutboxCount > 0)
        {
            throw new AppointmentProjectionRebuildException(
                "Projection rebuild öncesinde bekleyen appointment outbox mesajları işlenmeli veya operasyonel olarak çözümlenmelidir.",
                pendingOutboxCount,
                deadLetterOutboxCount);
        }

        var commandAppointmentCount = await _commandDb.Appointments.CountAsync(cancellationToken);
        var rebuildStartedAtUtc = DateTime.UtcNow;

        var petActivity = new Dictionary<(Guid TenantId, Guid ClinicId, Guid PetId), ActivitySnapshot>();
        var clientActivity = new Dictionary<(Guid TenantId, Guid ClinicId, Guid ClientId), ActivitySnapshot>();
        var dailyStats = new Dictionary<(Guid TenantId, Guid ClinicId, DateOnly LocalDate), DailyStatsAccumulator>();

        await using var transaction = await _queryDb.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await ClearQueryProjectionTablesAsync(cancellationToken);

            var batchIndex = 0;
            var skip = 0;
            while (true)
            {
                var batch = await LoadAppointmentBatchAsync(skip, batchSize, cancellationToken);
                if (batch.Count == 0)
                    break;

                foreach (var row in batch)
                {
                    TrackAggregates(row, petActivity, clientActivity, dailyStats);
                    _queryDb.AppointmentReadModels.Add(MapToReadModel(row, rebuildStartedAtUtc));
                }

                await _queryDb.SaveChangesAsync(cancellationToken);
                await OnAfterBatchSavedAsync(batchIndex, cancellationToken);
                _queryDb.ChangeTracker.Clear();

                skip += batch.Count;
                batchIndex++;
            }

            WritePetActivityRows(petActivity, rebuildStartedAtUtc);
            WriteClientActivityRows(clientActivity, rebuildStartedAtUtc);
            WriteDailyStatsRows(dailyStats, rebuildStartedAtUtc);

            await _queryDb.SaveChangesAsync(cancellationToken);
            await ValidateRebuildAsync(commandAppointmentCount, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        var duration = DateTime.UtcNow - started;
        var result = new AppointmentProjectionRebuildResult(
            Success: true,
            CommandAppointmentCount: commandAppointmentCount,
            QueryAppointmentCount: commandAppointmentCount,
            PetActivityCount: petActivity.Count,
            ClientActivityCount: clientActivity.Count,
            DailyStatsCount: dailyStats.Count,
            PendingAppointmentOutboxCount: pendingOutboxCount,
            DeadLetterAppointmentOutboxCount: deadLetterOutboxCount,
            Duration: duration);

        _logger.LogInformation(
            "Appointment projection rebuild completed. Command={CommandCount} Query={QueryCount} PetActivity={PetActivity} ClientActivity={ClientActivity} DailyStats={DailyStats} DurationMs={DurationMs}",
            result.CommandAppointmentCount,
            result.QueryAppointmentCount,
            result.PetActivityCount,
            result.ClientActivityCount,
            result.DailyStatsCount,
            (int)result.Duration.TotalMilliseconds);

        return result;
    }

    protected virtual Task OnAfterBatchSavedAsync(int batchIndex, CancellationToken cancellationToken)
        => Task.CompletedTask;

    private async Task EnsureDistinctDatabasesAsync(CancellationToken cancellationToken)
    {
        var commandConnection = _commandDb.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Command DB connection string bulunamadı.");
        var queryConnection = _queryDb.Database.GetConnectionString()
            ?? throw new InvalidOperationException("Query DB connection string bulunamadı.");

        var commandCatalog = GetInitialCatalog(commandConnection);
        var queryCatalog = GetInitialCatalog(queryConnection);

        if (string.Equals(commandCatalog, queryCatalog, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Appointment projection rebuild için Command DB ve Query DB farklı veritabanları olmalıdır.");
        }
    }

    private static string GetInitialCatalog(string connectionString)
    {
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        return builder.InitialCatalog;
    }

    private Task<int> CountPendingAppointmentOutboxAsync(CancellationToken cancellationToken)
        => OutboxMessageQueryFilters
            .AppointmentIntegrationEventsOnly(_commandDb.OutboxMessages.AsNoTracking())
            .CountAsync(m => m.ProcessedAtUtc == null && m.DeadLetterAtUtc == null, cancellationToken);

    private Task<int> CountDeadLetterAppointmentOutboxAsync(CancellationToken cancellationToken)
        => OutboxMessageQueryFilters
            .AppointmentIntegrationEventsOnly(_commandDb.OutboxMessages.AsNoTracking())
            .CountAsync(m => m.DeadLetterAtUtc != null, cancellationToken);

    private async Task ClearQueryProjectionTablesAsync(CancellationToken cancellationToken)
    {
        await _queryDb.ProcessedProjectionEvents.ExecuteDeleteAsync(cancellationToken);
        await _queryDb.ClinicDailyAppointmentStatsReadModels.ExecuteDeleteAsync(cancellationToken);
        await _queryDb.ClinicClientActivityReadModels.ExecuteDeleteAsync(cancellationToken);
        await _queryDb.ClinicPetActivityReadModels.ExecuteDeleteAsync(cancellationToken);
        await _queryDb.AppointmentReadModels.ExecuteDeleteAsync(cancellationToken);
    }

    private Task<List<RebuildAppointmentRow>> LoadAppointmentBatchAsync(
        int skip,
        int take,
        CancellationToken cancellationToken)
        => BuildAppointmentProjectionQuery(skip, take)
            .ToListAsync(cancellationToken);

    private IQueryable<RebuildAppointmentRow> BuildAppointmentProjectionQuery(int skip, int take)
        => (from a in _commandDb.Appointments.AsNoTracking()
            join p in _commandDb.Pets.AsNoTracking()
                on new { a.TenantId, PetId = a.PetId }
                equals new { p.TenantId, PetId = p.Id }
            join c in _commandDb.Clinics.AsNoTracking()
                on new { a.TenantId, ClinicId = a.ClinicId }
                equals new { c.TenantId, ClinicId = c.Id }
            join s in _commandDb.Species.AsNoTracking() on p.SpeciesId equals s.Id
            join cl in _commandDb.Clients.AsNoTracking()
                on new { p.TenantId, p.ClientId }
                equals new { cl.TenantId, ClientId = cl.Id }
            join br in _commandDb.Breeds.AsNoTracking() on p.BreedId equals br.Id into breedJoin
            from br in breedJoin.DefaultIfEmpty()
            orderby a.TenantId, a.Id
            select new RebuildAppointmentRow(
                a.Id,
                a.TenantId,
                a.ClinicId,
                c.Name,
                a.PetId,
                p.Name,
                p.SpeciesId,
                s.Name,
                p.Breed,
                br != null ? br.Name : null,
                p.ClientId,
                cl.FullName,
                cl.Phone,
                cl.Email,
                cl.PhoneNormalized,
                a.ScheduledAtUtc,
                a.DurationMinutes,
                (int)a.AppointmentType,
                (int)a.Status,
                a.Notes))
            .Skip(skip)
            .Take(take);

    private static void TrackAggregates(
        RebuildAppointmentRow row,
        Dictionary<(Guid, Guid, Guid), ActivitySnapshot> petActivity,
        Dictionary<(Guid, Guid, Guid), ActivitySnapshot> clientActivity,
        Dictionary<(Guid, Guid, DateOnly), DailyStatsAccumulator> dailyStats)
    {
        var snapshot = ActivitySnapshot.FromRow(row);
        var petKey = (row.TenantId, row.ClinicId, row.PetId);
        if (!petActivity.TryGetValue(petKey, out var currentPet) || IsPreferredLatest(snapshot, currentPet))
            petActivity[petKey] = snapshot;

        var clientKey = (row.TenantId, row.ClinicId, row.ClientId);
        if (!clientActivity.TryGetValue(clientKey, out var currentClient) || IsPreferredLatest(snapshot, currentClient))
            clientActivity[clientKey] = snapshot;

        var localDate = OperationDayBounds.ToLocalDate(row.ScheduledAtUtc);
        var statsKey = (row.TenantId, row.ClinicId, localDate);
        if (!dailyStats.TryGetValue(statsKey, out var accumulator))
        {
            accumulator = new DailyStatsAccumulator();
            dailyStats[statsKey] = accumulator;
        }

        accumulator.Increment(row.Status);
    }

    /// <summary>
    /// Projector ile aynı tie-break: ScheduledAtUtc DESC, AppointmentId ASC.
    /// </summary>
    private static bool IsPreferredLatest(ActivitySnapshot candidate, ActivitySnapshot current)
    {
        if (candidate.ScheduledAtUtc != current.ScheduledAtUtc)
            return candidate.ScheduledAtUtc > current.ScheduledAtUtc;

        return candidate.AppointmentId < current.AppointmentId;
    }

    private static AppointmentReadModel MapToReadModel(RebuildAppointmentRow row, DateTime projectedAtUtc)
        => new()
        {
            AppointmentId = row.AppointmentId,
            TenantId = row.TenantId,
            ClinicId = row.ClinicId,
            ClinicName = row.ClinicName,
            PetId = row.PetId,
            PetName = row.PetName,
            SpeciesId = row.SpeciesId,
            SpeciesName = row.SpeciesName,
            ClientId = row.ClientId,
            ClientName = row.ClientName,
            ClientPhone = row.ClientPhone,
            ClientPhoneNormalized = row.ClientPhoneNormalized,
            ClientEmail = row.ClientEmail,
            PetBreed = row.PetBreed,
            PetBreedRefName = row.PetBreedRefName,
            ScheduledAtUtc = row.ScheduledAtUtc,
            ScheduledEndUtc = row.ScheduledAtUtc.AddMinutes(row.DurationMinutes),
            DurationMinutes = row.DurationMinutes,
            AppointmentType = row.AppointmentType,
            Status = row.Status,
            Notes = row.Notes,
            LastEventId = RebuildEventId,
            LastProjectedAtUtc = projectedAtUtc
        };

    private void WritePetActivityRows(
        Dictionary<(Guid TenantId, Guid ClinicId, Guid PetId), ActivitySnapshot> petActivity,
        DateTime projectedAtUtc)
    {
        foreach (var ((tenantId, clinicId, petId), latest) in petActivity)
        {
            _queryDb.ClinicPetActivityReadModels.Add(new ClinicPetActivityReadModel
            {
                TenantId = tenantId,
                ClinicId = clinicId,
                PetId = petId,
                ClientId = latest.ClientId,
                PetName = latest.PetName,
                SpeciesId = latest.SpeciesId,
                SpeciesName = latest.SpeciesName,
                LastAppointmentAtUtc = latest.ScheduledAtUtc,
                LastEventId = RebuildEventId,
                LastProjectedAtUtc = projectedAtUtc
            });
        }
    }

    private void WriteClientActivityRows(
        Dictionary<(Guid TenantId, Guid ClinicId, Guid ClientId), ActivitySnapshot> clientActivity,
        DateTime projectedAtUtc)
    {
        foreach (var ((tenantId, clinicId, clientId), latest) in clientActivity)
        {
            _queryDb.ClinicClientActivityReadModels.Add(new ClinicClientActivityReadModel
            {
                TenantId = tenantId,
                ClinicId = clinicId,
                ClientId = clientId,
                ClientName = latest.ClientName,
                ClientPhone = latest.ClientPhone,
                LastAppointmentAtUtc = latest.ScheduledAtUtc,
                LastEventId = RebuildEventId,
                LastProjectedAtUtc = projectedAtUtc
            });
        }
    }

    private void WriteDailyStatsRows(
        Dictionary<(Guid TenantId, Guid ClinicId, DateOnly LocalDate), DailyStatsAccumulator> dailyStats,
        DateTime projectedAtUtc)
    {
        foreach (var ((tenantId, clinicId, localDate), accumulator) in dailyStats)
        {
            _queryDb.ClinicDailyAppointmentStatsReadModels.Add(new ClinicDailyAppointmentStatsReadModel
            {
                TenantId = tenantId,
                ClinicId = clinicId,
                LocalDate = localDate,
                ScheduledCount = accumulator.ScheduledCount,
                CompletedCount = accumulator.CompletedCount,
                CancelledCount = accumulator.CancelledCount,
                TotalCount = accumulator.TotalCount,
                LastEventId = RebuildEventId,
                LastProjectedAtUtc = projectedAtUtc
            });
        }
    }

    private async Task ValidateRebuildAsync(int expectedAppointmentCount, CancellationToken cancellationToken)
    {
        var queryAppointmentCount = await _queryDb.AppointmentReadModels.CountAsync(cancellationToken);
        if (queryAppointmentCount != expectedAppointmentCount)
        {
            throw new InvalidOperationException(
                $"Appointment projection rebuild parity hatası: Command={expectedAppointmentCount}, Query={queryAppointmentCount}.");
        }

        var duplicateIds = await _queryDb.AppointmentReadModels
            .GroupBy(x => x.AppointmentId)
            .Where(g => g.Count() > 1)
            .CountAsync(cancellationToken);

        if (duplicateIds > 0)
            throw new InvalidOperationException("Appointment projection rebuild parity hatası: yinelenen AppointmentId bulundu.");

        var invalidGuids = await _queryDb.AppointmentReadModels
            .AnyAsync(x =>
                x.TenantId == Guid.Empty
                || x.ClinicId == Guid.Empty
                || x.PetId == Guid.Empty
                || x.ClientId == Guid.Empty,
                cancellationToken);

        if (invalidGuids)
            throw new InvalidOperationException("Appointment projection rebuild parity hatası: boş Guid alanı bulundu.");

        var invalidSchedule = await _queryDb.AppointmentReadModels
            .AnyAsync(x => x.ScheduledEndUtc < x.ScheduledAtUtc, cancellationToken);

        if (invalidSchedule)
            throw new InvalidOperationException("Appointment projection rebuild parity hatası: ScheduledEndUtc geçersiz.");

        var invalidStats = await _queryDb.ClinicDailyAppointmentStatsReadModels
            .AnyAsync(x =>
                x.ScheduledCount + x.CompletedCount + x.CancelledCount != x.TotalCount,
                cancellationToken);

        if (invalidStats)
            throw new InvalidOperationException("Appointment projection rebuild parity hatası: daily stats toplamı uyumsuz.");

        var processedEvents = await _queryDb.ProcessedProjectionEvents.CountAsync(cancellationToken);
        if (processedEvents > 0)
        {
            throw new InvalidOperationException(
                "Appointment projection rebuild parity hatası: ProcessedProjectionEvents boş olmalıdır.");
        }
    }

    private sealed record RebuildAppointmentRow(
        Guid AppointmentId,
        Guid TenantId,
        Guid ClinicId,
        string ClinicName,
        Guid PetId,
        string PetName,
        Guid SpeciesId,
        string SpeciesName,
        string? PetBreed,
        string? PetBreedRefName,
        Guid ClientId,
        string ClientName,
        string? ClientPhone,
        string? ClientEmail,
        string? ClientPhoneNormalized,
        DateTime ScheduledAtUtc,
        int DurationMinutes,
        int AppointmentType,
        int Status,
        string? Notes);

    private sealed record ActivitySnapshot(
        Guid AppointmentId,
        DateTime ScheduledAtUtc,
        Guid ClientId,
        string PetName,
        Guid SpeciesId,
        string SpeciesName,
        string ClientName,
        string? ClientPhone)
    {
        public static ActivitySnapshot FromRow(RebuildAppointmentRow row)
            => new(
                row.AppointmentId,
                row.ScheduledAtUtc,
                row.ClientId,
                row.PetName,
                row.SpeciesId,
                row.SpeciesName,
                row.ClientName,
                row.ClientPhone);
    }

    private sealed class DailyStatsAccumulator
    {
        public int ScheduledCount { get; private set; }
        public int CompletedCount { get; private set; }
        public int CancelledCount { get; private set; }
        public int TotalCount { get; private set; }

        public void Increment(int status)
        {
            TotalCount++;
            switch (status)
            {
                case (int)AppointmentStatus.Scheduled:
                    ScheduledCount++;
                    break;
                case (int)AppointmentStatus.Completed:
                    CompletedCount++;
                    break;
                case (int)AppointmentStatus.Cancelled:
                    CancelledCount++;
                    break;
            }
        }
    }
}
