namespace Backend.Veteriner.Application.Clients;

/// <summary>Müşteri veri kopyası (GET /clients/{id}/data-export) bölüm başına üst sınır; fazlası kesilir ve işaretlenir.</summary>
public static class ClientDataExportConstants
{
    public const int MaxRowsPerSection = 1000;
}
