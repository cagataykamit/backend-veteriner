namespace Backend.Veteriner.Api.Controllers;

/// <summary>POST /api/v1/clinics/{id}/hospitalization-wards body.</summary>
public sealed class CreateHospitalizationWardBody
{
    public string Name { get; init; } = default!;
    public int SortOrder { get; init; }
}

/// <summary>PUT /api/v1/clinics/{id}/hospitalization-wards/{wardId} body; route id is source of truth.</summary>
public sealed class UpdateHospitalizationWardBody
{
    /// <summary>Optional; when set must match route wardId.</summary>
    public Guid? Id { get; init; }

    public string Name { get; init; } = default!;
    public int SortOrder { get; init; }
}

/// <summary>POST /api/v1/clinics/{id}/hospitalization-wards/{wardId}/rooms body.</summary>
public sealed class CreateHospitalizationRoomBody
{
    public string Name { get; init; } = default!;
    public int SortOrder { get; init; }
}

/// <summary>PUT /api/v1/clinics/{id}/hospitalization-wards/{wardId}/rooms/{roomId} body; route id is source of truth.</summary>
public sealed class UpdateHospitalizationRoomBody
{
    /// <summary>Optional; when set must match route roomId.</summary>
    public Guid? Id { get; init; }

    public string Name { get; init; } = default!;
    public int SortOrder { get; init; }
}
