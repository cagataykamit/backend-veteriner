using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Imports.Contracts.Dtos;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Queries.GetList;

/// <summary>
/// Kiracının partileri, yeniden eskiye. Klinik bağlı partiler yalnız erişilebilen kliniklerde görünür. Durum verilmezse
/// kapanmış partiler (geri alınmış, silinmiş veya süresi dolmuş taslak) yalnız <see cref="IncludeClosed"/> ile gelir.
/// </summary>
public sealed record GetImportBatchesListQuery(
    int Page = 1, int PageSize = 20, ImportBatchStatus? Status = null, bool IncludeClosed = false)
    : IRequest<Result<PagedResult<ImportBatchDto>>>;
