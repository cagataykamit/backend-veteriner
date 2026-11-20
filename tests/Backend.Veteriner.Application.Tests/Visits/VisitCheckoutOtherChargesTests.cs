using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Contracts.Dtos;
using Backend.Veteriner.Application.Visits.Queries.GetCheckoutSummary;
using Backend.Veteriner.Domain.Visits;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Visits;

/// <summary>"Hesabı kapat" panelinde müşterinin diğer açık ücretleri (VISIT_CHECKOUT_API_CONTRACT.md §2, K3).</summary>
public sealed class VisitCheckoutOtherChargesTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();
    private static readonly Guid OtherClinicId = Guid.NewGuid();
    private static readonly Guid VisitId = Guid.NewGuid();
    private static readonly Guid OldVisitId = Guid.NewGuid();
    private static readonly Guid PetId = Guid.NewGuid();
    private static readonly DateTime T0 = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Should_Return_Only_Same_Clinic_Open_Charges_Of_Other_Visits_Oldest_First()
    {
        var thisVisit = Charge(ClinicId, 600m, T0.AddDays(30));
        var linkedByExamination = Charge(ClinicId, 200m, T0.AddDays(30));
        var oldNewer = Charge(ClinicId, 150m, T0.AddDays(2));
        var oldOlder = Charge(ClinicId, 90m, T0);
        var otherClinic = Charge(OtherClinicId, 300m, T0);
        var paid = Charge(ClinicId, 0m, T0);

        var balance = Balance(
            Visit(VisitId, thisVisit),
            Visit(null, linkedByExamination),
            Visit(OldVisitId, oldNewer, oldOlder, otherClinic, paid));

        var result = GetVisitCheckoutSummaryQueryHandler.OtherOpenCharges(balance, VisitDto(), new HashSet<Guid> { linkedByExamination.ChargeId });

        result.Select(c => c.ChargeId).Should().Equal(oldOlder.ChargeId, oldNewer.ChargeId);
        result[0].Should().BeEquivalentTo(new
        {
            VisitId = (Guid?)OldVisitId,
            PetId = (Guid?)PetId,
            PetName = "Boncuk",
            Description = "Kalem",
            OutstandingAmount = 90m,
        });
    }

    private static ClientBalanceChargeDto Charge(Guid clinicId, decimal outstanding, DateTime createdAtUtc)
        => new(Guid.NewGuid(), clinicId, null, "Kalem", outstanding + 10m, 10m, outstanding, createdAtUtc);

    private static ClientBalanceVisitDto Visit(Guid? visitId, params ClientBalanceChargeDto[] charges)
        => new(visitId, visitId is null ? null : T0, charges.Sum(c => c.OutstandingAmount), charges);

    private static ClientBalanceDto Balance(params ClientBalanceVisitDto[] visits)
        => new(
            Guid.NewGuid(), "Ayşe", null, "TRY", 0m, 0m, 0m, 0,
            [new ClientBalancePetDto(PetId, "Boncuk", visits.Sum(v => v.OutstandingAmount), visits)],
            new ClientBalanceUnallocatedPaymentsDto(0m, 0, false, []));

    private static VisitDto VisitDto()
        => new(
            VisitId, ClinicId, PetId, null, null, null, T0.AddDays(30), VisitCareStatus.InProgress,
            T0.AddDays(30), null, false, null, Guid.NewGuid(), T0.AddDays(30), false, null, null);
}
