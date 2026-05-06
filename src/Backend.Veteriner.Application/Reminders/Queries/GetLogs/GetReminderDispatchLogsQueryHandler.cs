using Backend.Veteriner.Application.Clinics.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Application.Reminders.Specs;
using Backend.Veteriner.Domain.Clinics;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reminders.Queries.GetLogs;

public sealed class GetReminderDispatchLogsQueryHandler
    : IRequestHandler<GetReminderDispatchLogsQuery, Result<PagedResult<ReminderDispatchLogItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IClientContext _clientContext;
    private readonly IClinicAssignmentAccessGuard _assignmentGuard;
    private readonly IUserClinicRepository _userClinics;
    private readonly IReadRepository<Clinic> _clinicsRead;
    private readonly IReadRepository<ReminderDispatchLog> _logRead;

    public GetReminderDispatchLogsQueryHandler(
        ITenantContext tenantContext,
        IClientContext clientContext,
        IClinicAssignmentAccessGuard assignmentGuard,
        IUserClinicRepository userClinics,
        IReadRepository<Clinic> clinicsRead,
        IReadRepository<ReminderDispatchLog> logRead)
    {
        _tenantContext = tenantContext;
        _clientContext = clientContext;
        _assignmentGuard = assignmentGuard;
        _userClinics = userClinics;
        _clinicsRead = clinicsRead;
        _logRead = logRead;
    }

    public async Task<Result<PagedResult<ReminderDispatchLogItemDto>>> Handle(GetReminderDispatchLogsQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ReminderDispatchLogItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        if (_clientContext.UserId is not { } userId)
        {
            return Result<PagedResult<ReminderDispatchLogItemDto>>.Failure(
                "Auth.Unauthorized.UserContextMissing",
                "Kullanıcı bağlamı yok.");
        }

        var page = request.PageRequest.Page < 1 ? 1 : request.PageRequest.Page;
        var pageSize = request.PageRequest.PageSize;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        Guid? filterBySingleClinicId = null;
        IReadOnlyList<Guid>? filterByClinicIdsAny = null;

        if (await _assignmentGuard.MustApplyAssignedClinicScopeAsync(userId, ct))
        {
            var accessible = await _userClinics.ListAccessibleClinicsAsync(userId, tenantId, null, ct);
            var accessibleIds = accessible.Select(c => c.Id).ToArray();

            if (request.ClinicId.HasValue)
            {
                if (Array.IndexOf(accessibleIds, request.ClinicId.Value) < 0)
                {
                    return Result<PagedResult<ReminderDispatchLogItemDto>>.Failure(
                        "Clinics.AccessDenied",
                        "Bu klinik için atanmış üyeliğiniz yok.");
                }

                filterBySingleClinicId = request.ClinicId.Value;
            }
            else
            {
                filterByClinicIdsAny = accessibleIds;
            }
        }
        else
        {
            if (request.ClinicId.HasValue)
            {
                var clinic = await _clinicsRead.FirstOrDefaultAsync(
                    new ClinicByIdSpec(tenantId, request.ClinicId.Value), ct);
                if (clinic is null)
                {
                    return Result<PagedResult<ReminderDispatchLogItemDto>>.Failure(
                        "Clinics.NotFound",
                        "Klinik bulunamadı.");
                }

                filterBySingleClinicId = request.ClinicId.Value;
            }
        }

        var listSpec = new ReminderDispatchLogsFilteredPagedSpec(
            tenantId,
            request.ReminderType,
            request.Status,
            request.FromUtc,
            request.ToUtc,
            page,
            pageSize,
            filterBySingleClinicId,
            filterByClinicIdsAny);
        var countSpec = new ReminderDispatchLogsCountSpec(
            tenantId,
            request.ReminderType,
            request.Status,
            request.FromUtc,
            request.ToUtc,
            filterBySingleClinicId,
            filterByClinicIdsAny);

        var items = await _logRead.ListAsync(listSpec, ct);
        var totalItems = await _logRead.CountAsync(countSpec, ct);

        return Result<PagedResult<ReminderDispatchLogItemDto>>.Success(
            PagedResult<ReminderDispatchLogItemDto>.Create(items, totalItems, page, pageSize));
    }
}
