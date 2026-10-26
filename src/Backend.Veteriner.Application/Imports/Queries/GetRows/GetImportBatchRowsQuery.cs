using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetRows;

/// <summary>
/// <paramref name="Table"/>: <c>customers|pets|vaccinations</c>; <paramref name="Status"/>: doğrulama durumu
/// (<c>Valid|Warning|Error</c>) veya uygulama durumu (<c>Created|Matched|Skipped|Failed|RolledBack|Kept</c>);
/// <paramref name="Code"/>: satır sorunlarından birinin kodu.
/// </summary>
public sealed record GetImportBatchRowsQuery(
    Guid Id,
    string? Table,
    string? Status,
    string? Code,
    int Page = 1,
    int PageSize = GetImportBatchRowsQuery.DefaultPageSize) : IRequest<Result<PagedResult<ImportRowDto>>>
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
}
