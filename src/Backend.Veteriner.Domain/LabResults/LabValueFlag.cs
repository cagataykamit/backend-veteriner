namespace Backend.Veteriner.Domain.LabResults;

/// <summary>Değerin sonuçla gelen referans aralığına göre konumu; aritmetik karşılaştırmadır, tanı değildir.</summary>
public enum LabValueFlag
{
    Low = 0,
    Normal = 1,
    High = 2
}

/// <summary>Aynı parametrenin önceki karşılaştırılabilir değerine göre yönü.</summary>
public enum LabValueTrend
{
    Down = 0,
    Unchanged = 1,
    Up = 2
}
