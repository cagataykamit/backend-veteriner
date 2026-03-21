using Backend.Veteriner.Application.Auth.Contracts.Dtos;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Models;
using MediatR;

namespace Backend.Veteriner.Application.Auth.Queries.Permissions.GetAll;

public sealed class GetAllPermissionsQueryHandler
    : IRequestHandler<GetAllPermissionsQuery, PagedResult<PermissionDto>>
{
    private const int MaxPageSize = 200;

    private readonly IPermissionRepository _repo;
    public GetAllPermissionsQueryHandler(IPermissionRepository repo) => _repo = repo;

    public async Task<PagedResult<PermissionDto>> Handle(GetAllPermissionsQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Req.Page);
        var pageSize = Math.Clamp(q.Req.PageSize, 1, MaxPageSize);

        // Repository'den "queryable" alam�yorsan (�u an �yle g�r�n�yor),
        // en g�venli standart yakla��m: filtreli listeyi �ek, sonra sayfala.
        // (Permission say�s� tipik olarak d���k oldu�u i�in pratikte sorun yaratmaz.)
        var all = await _repo.GetListAsync(q.Req.Search, ct);

        var totalItems = all.Count;
        var items = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return PagedResult<PermissionDto>.Create(items, totalItems, page, pageSize);
    }
}
