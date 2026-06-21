namespace Backend.Veteriner.Application.Common.Options;

public sealed class QueryReadModelsOptions
{
    public const string SectionName = "QueryReadModels";

    public bool AppointmentsEnabled { get; set; }

    public bool DashboardAppointmentsEnabled { get; set; }

    public bool ClientsEnabled { get; set; }

    public bool PetsEnabled { get; set; }

    /// <summary>
    /// Paylaşılan client/pet metin araması lookup'ını Query DB read-model'e yönlendirir (12D-3+).
    /// Liste endpoint bayraklarından (<see cref="ClientsEnabled"/>, <see cref="PetsEnabled"/>) bağımsızdır.
    /// </summary>
    public bool SharedSearchLookupEnabled { get; set; }

    /// <summary>
    /// Ödeme listesi Strateji B search resolution'ını Query DB read-model'e yönlendirir (12D-7+).
    /// <see cref="SharedSearchLookupEnabled"/> ve finansal rapor/export yüzeylerinden bağımsızdır.
    /// </summary>
    public bool PaymentsSearchLookupEnabled { get; set; }
}
