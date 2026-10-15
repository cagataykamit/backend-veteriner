using Ardalis.Specification;
using Backend.Veteriner.Application.Common;
using Backend.Veteriner.Application.Hospitalizations;
using Backend.Veteriner.Application.Hospitalizations.Specs;
using Backend.Veteriner.Application.LabResults;
using Backend.Veteriner.Application.LabResults.Specs;
using Backend.Veteriner.Application.Prescriptions;
using Backend.Veteriner.Application.Prescriptions.Specs;
using Backend.Veteriner.Application.Treatments;
using Backend.Veteriner.Application.Treatments.Specs;
using Backend.Veteriner.Domain.Hospitalizations;
using Backend.Veteriner.Domain.LabResults;
using Backend.Veteriner.Domain.Prescriptions;
using Backend.Veteriner.Domain.Treatments;
using FluentAssertions;

namespace Backend.Veteriner.Application.Tests.Common;

/// <summary>Tedavi/reçete/lab/yatış listelerinde sort yoksa en yeni üstte; sort verilince yön uygulanır.</summary>
public sealed class ClinicalListSortSpecTests
{
    private static readonly DateTime Older = DateTime.UtcNow.AddDays(-3);
    private static readonly DateTime Newer = DateTime.UtcNow.AddDays(-1);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _clinicId = Guid.NewGuid();

    private static Guid[] Expected(Guid olderId, Guid newerId, bool? descending)
        => (descending ?? true) ? new[] { newerId, olderId } : new[] { olderId, newerId };

    private static ListSortChoice<T>? Choice<T>(T field, bool? descending)
        where T : struct, Enum
        => descending is { } d ? new ListSortChoice<T>(field, d) : null;

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void Treatments(bool? descending)
    {
        Treatment New(DateTime at) => new(_tenantId, _clinicId, Guid.NewGuid(), null, at, "Tedavi", "Açıklama", null, null);
        var older = New(Older);
        var newer = New(Newer);

        var spec = new TreatmentsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, 1, 20, null, [], null,
            Choice(TreatmentListSortField.TreatmentDateUtc, descending));

        spec.Evaluate([older, newer]).Select(r => r.Id).Should().Equal(Expected(older.Id, newer.Id, descending));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void Prescriptions(bool? descending)
    {
        Prescription New(DateTime at) => new(_tenantId, _clinicId, Guid.NewGuid(), null, null, at, "Reçete", "İçerik", null, null);
        var older = New(Older);
        var newer = New(Newer);

        var spec = new PrescriptionsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, 1, 20, null, [], null,
            Choice(PrescriptionListSortField.PrescribedAtUtc, descending));

        spec.Evaluate([older, newer]).Select(r => r.Id).Should().Equal(Expected(older.Id, newer.Id, descending));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void LabResults(bool? descending)
    {
        // Sonuçlu kayıtta RequestedAtUtc = resultDateUtc.
        LabResult New(DateTime at) => new(_tenantId, _clinicId, Guid.NewGuid(), null, null, null, at, "Hemogram", "Normal", null, null);
        var older = New(Older);
        var newer = New(Newer);

        var spec = new LabResultsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, 1, 20, null, [], null, null,
            Choice(LabResultListSortField.RequestedAtUtc, descending));

        spec.Evaluate([older, newer]).Select(r => r.Id).Should().Equal(Expected(older.Id, newer.Id, descending));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void Hospitalizations(bool? descending)
    {
        Hospitalization New(DateTime at) => new(_tenantId, _clinicId, Guid.NewGuid(), null, at, null, "Gözlem", null);
        var older = New(Older);
        var newer = New(Newer);

        var spec = new HospitalizationsFilteredPagedSpec(_tenantId, _clinicId, null, null, null, null, 1, 20, null, [], null,
            Choice(HospitalizationListSortField.AdmittedAtUtc, descending));

        spec.Evaluate([older, newer]).Select(r => r.Id).Should().Equal(Expected(older.Id, newer.Id, descending));
    }
}
