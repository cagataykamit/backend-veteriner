namespace Backend.Veteriner.Infrastructure.Persistence.Query;

/// <summary>
/// Query read-model string uzunlukları; command entity/DTO limitleriyle hizalı.
/// </summary>
internal static class QueryReadModelConstraints
{
    public const int ClinicName = 300;
    public const int PetName = 200;
    public const int PetNameNormalized = 200;
    public const int SpeciesName = 200;
    public const int SpeciesNameNormalized = 200;
    public const int ClientName = 300;
    public const int ClientNameNormalized = 300;
    public const int ClientPhone = 50;
    public const int ClientPhoneNormalized = 50;
    public const int ClientEmail = 320;
    public const int PetBreed = 150;
    public const int PetBreedRefName = 200;
    public const int PetColorName = 200;
    public const int PetColorNameNormalized = 200;
    public const int Notes = 2000;
    public const int PaymentNotes = 4000;
    public const int PaymentNotesNormalized = 4000;
    public const int ProjectionConsumerName = 128;
}
