using Backend.Veteriner.Application.Common.Models;
using Backend.Veteriner.Application.Imaging.Contracts.Dtos;
using Backend.Veteriner.Domain.Imaging;
using Backend.Veteriner.Domain.Shared;
using MediatR;

namespace Backend.Veteriner.Application.Imaging.Queries.GetList;

public sealed record GetImagingRecordsListQuery(
    PageRequest PageRequest,
    Guid? ClinicId = null,
    Guid? PetId = null,
    Guid? ExaminationId = null,
    ImagingModality? Modality = null,
    ImagingRecordStatus? Status = null,
    DateTime? DateFromUtc = null,
    DateTime? DateToUtc = null)
    : IRequest<Result<PagedResult<ImagingRecordListItemDto>>>;
