namespace Backend.Veteriner.Application.Hospitalizations.Contracts.Dtos;

/// <summary>Klinik yatış bölümü ve altındaki odalar (aktif/pasif dahil; <c>SortOrder</c>, sonra ad sıralı).</summary>
public sealed record HospitalizationWardDto(
    Guid Id,
    Guid ClinicId,
    string Name,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<HospitalizationRoomDto> Rooms);

/// <summary>Yatış odası/kafesi; <c>WardId</c> ait olduğu bölümdür.</summary>
public sealed record HospitalizationRoomDto(
    Guid Id,
    Guid WardId,
    string Name,
    int SortOrder,
    bool IsActive);
