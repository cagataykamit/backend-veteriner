using Backend.Veteriner.Application.Imports.Rollback;
using Backend.Veteriner.Domain.Imports;
using AwesomeAssertions;

namespace Backend.Veteriner.Application.Tests.Imports;

/// <summary>B5 geri alma kuralı (IMPORT_API_CONTRACT.md §9): silinebilirlik, neden kodları, sahip birimleri. Sentetik kimlikler.</summary>
public sealed class ImportRollbackPlannerTests
{
    private static readonly Guid Client1 = Guid.NewGuid();
    private static readonly Guid Client2 = Guid.NewGuid();
    private static readonly Guid MatchedClient = Guid.NewGuid();
    private static readonly Guid Pet1 = Guid.NewGuid();
    private static readonly Guid Pet2 = Guid.NewGuid();
    private static readonly Guid PetOfMatched = Guid.NewGuid();
    private static readonly Guid ExistingPet = Guid.NewGuid();
    private static readonly Guid Vac1 = Guid.NewGuid();
    private static readonly Guid Vac2 = Guid.NewGuid();
    private static readonly Guid VacOnExisting = Guid.NewGuid();

    /// <summary>
    /// M1 (Client1) → H1 (Pet1) → A1 (Vac1); M2 (Client2) → H2 (Pet2) → A2 (Vac2); mevcut müşterinin (MatchedClient)
    /// yeni hastası H3 (PetOfMatched); mevcut hastaya (ExistingPet, sahibi MatchedClient) A3 (VacOnExisting); eşleşen
    /// müşteri satırı (Matched) hedef değildir.
    /// </summary>
    private static List<ImportRollbackRowInfo> Rows() =>
    [
        Row(ImportTable.Customers, 2, Client1),
        Row(ImportTable.Customers, 3, Client2),
        Row(ImportTable.Customers, 4, MatchedClient, ImportRowApplyStatus.Matched),
        Row(ImportTable.Pets, 2, Pet1),
        Row(ImportTable.Pets, 3, Pet2),
        Row(ImportTable.Pets, 4, PetOfMatched),
        Row(ImportTable.Vaccinations, 2, Vac1),
        Row(ImportTable.Vaccinations, 3, Vac2),
        Row(ImportTable.Vaccinations, 4, VacOnExisting),
        Row(ImportTable.Vaccinations, 5, null, ImportRowApplyStatus.Skipped),
    ];

    private static ImportRollbackRowInfo Row(ImportTable table, int number, Guid? target, ImportRowApplyStatus status = ImportRowApplyStatus.Created)
        => new(Guid.NewGuid(), table, number, status, target);

    private sealed class Facts
    {
        public Dictionary<Guid, ImportRollbackVaccinationFact> Vaccinations { get; } = new()
        {
            [Vac1] = new(Pet1, false, false, false),
            [Vac2] = new(Pet2, false, false, false),
            [VacOnExisting] = new(ExistingPet, false, false, false),
        };

        public Dictionary<Guid, Guid> PetOwners { get; } = new()
        {
            [Pet1] = Client1, [Pet2] = Client2, [PetOfMatched] = MatchedClient, [ExistingPet] = MatchedClient,
        };

        public Dictionary<Guid, List<Guid>> PetVaccinations { get; } = new()
        {
            [Pet1] = [Vac1], [Pet2] = [Vac2],
        };

        public Dictionary<Guid, HashSet<string>> PetBlockers { get; } = new();
        public Dictionary<Guid, bool> ClientModified { get; } = new() { [Client1] = false, [Client2] = false };

        public Dictionary<Guid, List<Guid>> ClientPets { get; } = new()
        {
            [Client1] = [Pet1], [Client2] = [Pet2],
        };

        public Dictionary<Guid, HashSet<string>> ClientBlockers { get; } = new();

        public ImportRollbackFacts Build() => new(
            Vaccinations,
            PetOwners,
            PetVaccinations.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Guid>)kv.Value),
            PetBlockers.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value),
            ClientModified,
            ClientPets.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<Guid>)kv.Value),
            ClientBlockers.ToDictionary(kv => kv.Key, kv => (IReadOnlySet<string>)kv.Value));
    }

    private static ImportRollbackItem Item(ImportRollbackPlan plan, Guid target) => plan.Items.Single(i => i.TargetId == target);

    [Fact]
    public void Without_Dependencies_All_Created_Records_Should_Be_Deletable_And_Matched_Never_Targeted()
    {
        var plan = ImportRollbackPlanner.Build(Rows(), new Facts().Build());

        plan.Items.Should().HaveCount(8, "yalnız Created satırlar hedeftir (Matched ve Skipped değil)");
        plan.Items.Should().OnlyContain(i => i.Decision == ImportRollbackDecision.Delete);
        plan.Items.Should().NotContain(i => i.TargetId == MatchedClient);
        plan.Count(ImportTable.Customers, ImportRollbackDecision.Delete).Should().Be(2);
        plan.HasBlocked.Should().BeFalse();
    }

    [Fact]
    public void Units_Should_Group_By_Owner_Including_Existing_Owner()
    {
        var rows = Rows();
        var plan = ImportRollbackPlanner.Build(rows, new Facts().Build());

        plan.Units.Should().HaveCount(3);
        Guid RowOf(Guid target) => rows.Single(r => r.TargetId == target).RowId;
        plan.Units.Single(u => u.OwnerKey == Client1).RowIds.Should().BeEquivalentTo([RowOf(Client1), RowOf(Pet1), RowOf(Vac1)]);
        plan.Units.Single(u => u.OwnerKey == MatchedClient).RowIds
            .Should().BeEquivalentTo([RowOf(PetOfMatched), RowOf(VacOnExisting)], "mevcut sahibin yeni hastası ve mevcut hastanın yeni aşısı");
        plan.Units[0].OwnerKey.Should().Be(Client1, "birimler ilk satır sırasıyla");
    }

    [Fact]
    public void Pet_Dependency_Should_Keep_Pet_And_Its_Client_But_Not_Unrelated_Records()
    {
        var facts = new Facts();
        facts.PetBlockers[Pet1] = [ImportRollbackReasons.HasVisits];

        var plan = ImportRollbackPlanner.Build(Rows(), facts.Build());

        Item(plan, Pet1).Decision.Should().Be(ImportRollbackDecision.Keep);
        Item(plan, Pet1).Reasons.Should().Equal(ImportRollbackReasons.HasVisits);
        Item(plan, Client1).Reasons.Should().Equal(ImportRollbackReasons.HasKeptPets);
        Item(plan, Vac1).Decision.Should().Be(ImportRollbackDecision.Delete, "aşının kendi bağı yok");
        Item(plan, Client2).Decision.Should().Be(ImportRollbackDecision.Delete);
        plan.Count(ImportTable.Pets, ImportRollbackDecision.Keep).Should().Be(1);
    }

    [Fact]
    public void Vaccination_Links_Should_Keep_Vaccination_And_Cascade_To_Pet_And_Client()
    {
        var facts = new Facts();
        facts.Vaccinations[Vac2] = new(Pet2, LinkedToExamination: true, HasAdministration: true, Modified: true);

        var plan = ImportRollbackPlanner.Build(Rows(), facts.Build());

        Item(plan, Vac2).Reasons.Should().BeEquivalentTo(
            [ImportRollbackReasons.LinkedToExamination, ImportRollbackReasons.HasAdministration, ImportRollbackReasons.VaccinationModified]);
        Item(plan, Pet2).Reasons.Should().Equal(ImportRollbackReasons.HasKeptVaccinations);
        Item(plan, Client2).Reasons.Should().Equal(ImportRollbackReasons.HasKeptPets);
    }

    [Fact]
    public void Other_Records_And_Client_Edit_Should_Block()
    {
        var facts = new Facts();
        var manualVaccination = Guid.NewGuid();
        var manualPet = Guid.NewGuid();
        facts.PetVaccinations[Pet1] = [Vac1, manualVaccination];
        facts.ClientPets[Client2] = [Pet2, manualPet];
        facts.ClientModified[Client2] = true;
        facts.ClientBlockers[Client2] = [ImportRollbackReasons.HasCharges];

        var plan = ImportRollbackPlanner.Build(Rows(), facts.Build());

        Item(plan, Pet1).Reasons.Should().Equal(ImportRollbackReasons.HasOtherVaccinations);
        Item(plan, Client2).Reasons.Should().BeEquivalentTo(
            [ImportRollbackReasons.ClientModified, ImportRollbackReasons.HasCharges, ImportRollbackReasons.HasOtherPets]);
        Item(plan, Pet2).Decision.Should().Be(ImportRollbackDecision.Delete, "müşterinin bağı hastayı engellemez");
    }

    [Fact]
    public void Missing_Records_Should_Be_Gone_And_Kept_Rows_Should_Be_Reevaluated()
    {
        var rows = Rows();
        rows[0] = rows[0] with { ApplyStatus = ImportRowApplyStatus.Kept };
        rows.Add(Row(ImportTable.Customers, 9, Guid.NewGuid(), ImportRowApplyStatus.RolledBack));
        var facts = new Facts();
        facts.Vaccinations.Remove(Vac1);
        facts.PetVaccinations[Pet1] = [];

        var plan = ImportRollbackPlanner.Build(rows, facts.Build());

        Item(plan, Vac1).Decision.Should().Be(ImportRollbackDecision.Gone);
        Item(plan, Client1).Decision.Should().Be(ImportRollbackDecision.Delete, "önceki Kept satır yeniden değerlendirilir");
        plan.Items.Should().HaveCount(8, "RolledBack satır hedef değildir");
    }

    [Fact]
    public void KeepWholeUnitIfBlocked_Should_Keep_Deletable_Items_With_OwnerUnitBlocked()
    {
        var facts = new Facts();
        facts.PetBlockers[Pet1] = [ImportRollbackReasons.HasAppointments];
        var plan = ImportRollbackPlanner.Build(Rows().Where(r => r.TargetId == Client1 || r.TargetId == Pet1 || r.TargetId == Vac1), facts.Build());

        var unit = ImportRollbackPlanner.KeepWholeUnitIfBlocked(plan);

        unit.Items.Should().OnlyContain(i => i.Decision == ImportRollbackDecision.Keep);
        Item(unit, Vac1).Reasons.Should().Equal(ImportRollbackReasons.OwnerUnitBlocked);
        Item(unit, Pet1).Reasons.Should().Equal(ImportRollbackReasons.HasAppointments);

        var clean = ImportRollbackPlanner.Build(Rows(), new Facts().Build());
        ImportRollbackPlanner.KeepWholeUnitIfBlocked(clean).Should().BeSameAs(clean);
    }

    [Fact]
    public void Targets_Should_Contain_Only_Created_And_Kept_Targets()
    {
        var rows = Rows();
        rows.Add(Row(ImportTable.Pets, 8, Guid.NewGuid(), ImportRowApplyStatus.RolledBack));

        var targets = ImportRollbackPlanner.Targets(rows);

        targets.ClientIds.Should().BeEquivalentTo([Client1, Client2]);
        targets.PetIds.Should().BeEquivalentTo([Pet1, Pet2, PetOfMatched]);
        targets.VaccinationIds.Should().BeEquivalentTo([Vac1, Vac2, VacOnExisting]);
    }
}
