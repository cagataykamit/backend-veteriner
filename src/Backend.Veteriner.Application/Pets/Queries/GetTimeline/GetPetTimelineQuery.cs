using Backend.Veteriner.Application.Pets.Contracts.Dtos;
using Backend.Veteriner.Application.Pets.Timeline;
using Backend.Veteriner.Domain.Shared;
using Mediator;

namespace Backend.Veteriner.Application.Pets.Queries.GetTimeline;

/// <param name="Types">Boşsa yetkili tüm türler; bilinmeyen ad geçersizdir.</param>
/// <param name="ClinicId">Verilirse aktif klinik bağlamını geçersiz kılar.</param>
/// <param name="IncludeVoided">true ise yanlış işaretli geliş ve iptal aşı da döner.</param>
/// <param name="Limit">null ise <see cref="PetTimelineConstants.DefaultLimit"/>.</param>
public sealed record GetPetTimelineQuery(
    Guid PetId,
    IReadOnlyList<string>? Types = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    Guid? ClinicId = null,
    bool IncludeVoided = false,
    int? Limit = null,
    string? Cursor = null)
    : IRequest<Result<PetTimelineDto>>;
