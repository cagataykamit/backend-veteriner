using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Reminders.Contracts.Dtos;
using Backend.Veteriner.Application.Reminders.Specs;
using Backend.Veteriner.Domain.Reminders;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Reminders.Queries.GetLogs;

public sealed class GetReminderDispatchLogsQueryHandler
    : IRequestHandler<GetReminderDispatchLogsQuery, Result<PagedResult<ReminderDispatchLogItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<ReminderDispatchLog> _logRead;

    public GetReminderDispatchLogsQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<ReminderDispatchLog> logRead)
    {
        _tenantContext = tenantContext;
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

        var page = request.PageRequest.Page < 1 ? 1 : request.PageRequest.Page;
        var pageSize = request.PageRequest.PageSize;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var listSpec = new ReminderDispatchLogsFilteredPagedSpec(
            tenantId, request.ReminderType, request.Status, request.FromUtc, request.ToUtc, page, pageSize);
        var countSpec = new ReminderDispatchLogsCountSpec(
            tenantId, request.ReminderType, request.Status, request.FromUtc, request.ToUtc);

        var items = await _logRead.ListAsync(listSpec, ct);
        var totalItems = await _logRead.CountAsync(countSpec, ct);

        return Result<PagedResult<ReminderDispatchLogItemDto>>.Success(
            PagedResult<ReminderDispatchLogItemDto>.Create(items, totalItems, page, pageSize));
    }
}
