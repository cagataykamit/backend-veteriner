using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Shared;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Backend.Veteriner.Application.Clients.Queries.GetList;

public sealed class GetClientsListQueryHandler
    : IRequestHandler<GetClientsListQuery, Result<PagedResult<ClientListItemDto>>>
{
    private readonly ITenantContext _tenantContext;
    private readonly IReadRepository<Client> _clients;
    private readonly ILogger<GetClientsListQueryHandler> _logger;

    public GetClientsListQueryHandler(
        ITenantContext tenantContext,
        IReadRepository<Client> clients,
        ILogger<GetClientsListQueryHandler>? logger = null)
    {
        _tenantContext = tenantContext;
        _clients = clients;
        _logger = logger ?? NullLogger<GetClientsListQueryHandler>.Instance;
    }

    public async Task<Result<PagedResult<ClientListItemDto>>> Handle(GetClientsListQuery request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
        {
            return Result<PagedResult<ClientListItemDto>>.Failure(
                "Tenants.ContextMissing",
                "Kiracı bağlamı yok. JWT tenant_id veya sorgu tenantId gerekir.");
        }

        var page = Math.Max(1, request.PageRequest.Page);
        var pageSize = Math.Clamp(request.PageRequest.PageSize, 1, 200);
        var totalSw = Stopwatch.StartNew();
        var stepSw = Stopwatch.StartNew();
        var querySteps = 0;
        var slowestStep = string.Empty;
        long slowestMs = 0;

        void MarkStep(string name)
        {
            querySteps++;
            var elapsed = stepSw.ElapsedMilliseconds;
            if (elapsed > slowestMs)
            {
                slowestMs = elapsed;
                slowestStep = name;
            }

            stepSw.Restart();
        }

        var normalized = ListQueryTextSearch.Normalize(request.PageRequest.Search);
        var searchPattern = normalized is null ? null : ListQueryTextSearch.BuildContainsLikePattern(normalized);

        var total = await _clients.CountAsync(new ClientsByTenantCountSpec(tenantId, searchPattern), ct);
        MarkStep("clientsCount");

        var items = await _clients.ListAsync(
            new ClientsByTenantPagedSpec(tenantId, page, pageSize, searchPattern),
            ct);
        MarkStep("clientsPage");

        _logger.LogInformation(
            "Clients list generated. TenantId={TenantId} Page={Page} PageSize={PageSize} QuerySteps={QuerySteps} SlowestStep={SlowestStep} SlowestStepMs={SlowestStepMs} TotalElapsedMs={TotalElapsedMs}",
            tenantId,
            page,
            pageSize,
            querySteps,
            slowestStep,
            slowestMs,
            totalSw.ElapsedMilliseconds);

        return Result<PagedResult<ClientListItemDto>>.Success(
            PagedResult<ClientListItemDto>.Create(items, total, page, pageSize));
    }
}
