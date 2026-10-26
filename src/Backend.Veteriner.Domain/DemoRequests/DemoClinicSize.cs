namespace Backend.Veteriner.Domain.DemoRequests;

/// <summary>Demo talebindeki klinik büyüklüğü (TRIAL-005); satış önceliği için kaba sınıf.</summary>
public enum DemoClinicSize
{
    /// <summary>Tek hekim.</summary>
    SingleVet = 1,

    /// <summary>2–5 hekim.</summary>
    SmallTeam = 2,

    /// <summary>6+ hekim veya birden fazla şube/hastane.</summary>
    MultiBranch = 3
}
