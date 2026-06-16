using Backend.Veteriner.Application.Appointments.Contracts.Dtos;
using Backend.Veteriner.Application.Appointments.ReadModels;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Infrastructure.Persistence;
using Backend.Veteriner.Infrastructure.Persistence.Query.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Veteriner.Infrastructure.Query.Appointments;

public sealed class AppointmentReadModelReader : IAppointmentReadModelReader
{
    private readonly QueryDbContext _queryDb;

    public AppointmentReadModelReader(QueryDbContext queryDb) => _queryDb = queryDb;

    public async Task<AppointmentListReadResult> GetListAsync(
        AppointmentListReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var filtered = ApplyListFilters(_queryDb.AppointmentReadModels.AsNoTracking(), request);

        var total = await filtered.CountAsync(cancellationToken);

        var sorted = request.ScheduledAtDescending
            ? filtered.OrderByDescending(x => x.ScheduledAtUtc).ThenByDescending(x => x.AppointmentId)
            : filtered.OrderBy(x => x.ScheduledAtUtc).ThenBy(x => x.AppointmentId);

        var rows = await sorted
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(MapListItem).ToList();
        return new AppointmentListReadResult(items, total);
    }

    public async Task<IReadOnlyList<AppointmentCalendarItemDto>> GetCalendarAsync(
        AppointmentCalendarReadRequest request,
        CancellationToken cancellationToken = default)
    {
        var query = _queryDb.AppointmentReadModels.AsNoTracking()
            .Where(x =>
                x.TenantId == request.Scope.TenantId
                && x.ClinicId == request.Scope.ClinicId
                && x.ScheduledAtUtc >= request.DateFromUtc
                && x.ScheduledAtUtc < request.DateToUtc);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == (int)request.Status.Value);

        var rows = await query
            .OrderBy(x => x.ScheduledAtUtc)
            .ThenBy(x => x.AppointmentId)
            .ToListAsync(cancellationToken);

        return rows.Select(MapCalendarItem).ToList();
    }

    private static IQueryable<AppointmentReadModel> ApplyListFilters(
        IQueryable<AppointmentReadModel> query,
        AppointmentListReadRequest request)
    {
        query = query.Where(x =>
            x.TenantId == request.Scope.TenantId
            && x.ClinicId == request.Scope.ClinicId);

        if (request.PetId.HasValue)
            query = query.Where(x => x.PetId == request.PetId.Value);

        if (request.Status.HasValue)
            query = query.Where(x => x.Status == (int)request.Status.Value);

        if (request.DateFromUtc.HasValue)
            query = query.Where(x => x.ScheduledAtUtc >= request.DateFromUtc.Value);

        if (request.DateToUtc.HasValue)
            query = query.Where(x => x.ScheduledAtUtc <= request.DateToUtc.Value);

        if (request.SearchContainsLikePattern is { } pattern)
            query = ApplyListSearchFilter(query, pattern);

        return query;
    }

    /// <summary>
    /// Command DB <see cref="AppointmentsFilteredPagedSpec"/> + <see cref="ListSearchPetIds"/> ile aynı alan kümesi.
    /// </summary>
    private static IQueryable<AppointmentReadModel> ApplyListSearchFilter(
        IQueryable<AppointmentReadModel> query,
        string pattern)
        => query.Where(x =>
            (x.Notes != null && EF.Functions.Like(x.Notes, pattern))
            || EF.Functions.Like(x.PetName, pattern)
            || EF.Functions.Like(x.SpeciesName, pattern)
            || (x.PetBreed != null && EF.Functions.Like(x.PetBreed, pattern))
            || (x.PetBreedRefName != null && EF.Functions.Like(x.PetBreedRefName, pattern))
            || EF.Functions.Like(x.ClientName, pattern)
            || (x.ClientEmail != null && EF.Functions.Like(x.ClientEmail, pattern))
            || (x.ClientPhone != null && EF.Functions.Like(x.ClientPhone, pattern))
            || (x.ClientPhoneNormalized != null && EF.Functions.Like(x.ClientPhoneNormalized, pattern)));

    private static AppointmentListItemDto MapListItem(AppointmentReadModel x)
        => new(
            x.AppointmentId,
            x.TenantId,
            x.ClinicId,
            x.ClinicName,
            x.PetId,
            x.PetName,
            x.SpeciesId,
            x.SpeciesName,
            (AppointmentType)x.AppointmentType,
            x.ClientId,
            x.ClientName,
            x.ScheduledAtUtc,
            x.DurationMinutes,
            x.ScheduledEndUtc,
            (AppointmentStatus)x.Status,
            x.Notes);

    private static AppointmentCalendarItemDto MapCalendarItem(AppointmentReadModel x)
        => new(
            x.AppointmentId,
            x.ClinicId,
            x.PetId,
            x.ClientId,
            x.ScheduledAtUtc,
            x.DurationMinutes,
            x.ScheduledEndUtc,
            (AppointmentStatus)x.Status,
            (AppointmentType)x.AppointmentType,
            x.PetName,
            x.ClientName);
}
