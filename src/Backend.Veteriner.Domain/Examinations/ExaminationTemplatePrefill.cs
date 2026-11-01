namespace Backend.Veteriner.Domain.Examinations;

/// <summary>Şablonun şikâyet (ziyaret sebebi) metnine ne olduğu (<see cref="ExaminationTextMerge.FillIfEmpty"/>).</summary>
public enum TemplateVisitReasonOutcome
{
    /// <summary>Şablonda şikâyet metni yok.</summary>
    NotInTemplate,

    /// <summary>Muayenenin şikâyeti boştu, şablon metniyle dolduruldu.</summary>
    Filled,

    /// <summary>Muayenenin şikâyeti doluydu; şablon metni eklenmedi.</summary>
    SkippedExisting,
}

/// <summary>
/// Şablon metninin muayeneye ne kattığının bölüm bazında özeti (<see cref="Examination.PrefillFromTemplate"/>);
/// arayüz başarı mesajını bundan kurar. <see cref="UpdatedFields"/> metni değişen alanların API adlarıdır.
/// </summary>
public sealed record ExaminationTemplatePrefill(TemplateVisitReasonOutcome VisitReason, int FindingsLinesAdded, int PlanLinesAdded)
{
    public IReadOnlyList<string> UpdatedFields
    {
        get
        {
            var changed = new List<string>(3);
            if (VisitReason == TemplateVisitReasonOutcome.Filled)
                changed.Add("visitReason");
            if (FindingsLinesAdded > 0)
                changed.Add("findings");
            if (PlanLinesAdded > 0)
                changed.Add("plan");
            return changed;
        }
    }
}
