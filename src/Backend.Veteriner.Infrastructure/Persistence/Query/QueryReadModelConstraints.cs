namespace Backend.Veteriner.Infrastructure.Persistence.Query;

/// <summary>
/// Query read-model string uzunlukları; command entity/DTO limitleriyle hizalı.
/// </summary>
internal static class QueryReadModelConstraints
{
    public const int ClinicName = 300;
    public const int PetName = 200;
    public const int SpeciesName = 200;
    public const int ClientName = 300;
    public const int ClientPhone = 50;
    public const int Notes = 2000;
    public const int ProjectionConsumerName = 128;
}
