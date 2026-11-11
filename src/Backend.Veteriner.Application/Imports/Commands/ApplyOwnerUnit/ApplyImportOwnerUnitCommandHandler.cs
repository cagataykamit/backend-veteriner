using Backend.Veteriner.Application.Appointments.Commands.Import;
using Backend.Veteriner.Application.Clients.Commands.Create;
using Backend.Veteriner.Application.Clients.Contracts.Dtos;
using Backend.Veteriner.Application.Clients.Specs;
using Backend.Veteriner.Application.Common.Abstractions;
using Backend.Veteriner.Application.Common.Time;
using Backend.Veteriner.Application.Imports.Commit;
using Backend.Veteriner.Application.Imports.Mapping;
using Backend.Veteriner.Application.Imports.Specs;
using Backend.Veteriner.Application.Imports.Validation;
using Backend.Veteriner.Application.ImportedRecords.Commands;
using Backend.Veteriner.Application.Pets.Commands.Create;
using Backend.Veteriner.Application.Pets.Specs;
using Backend.Veteriner.Application.Products.Commands.Create;
using Backend.Veteriner.Application.Products.Specs;
using Backend.Veteriner.Application.Vaccinations.Commands.Import;
using Backend.Veteriner.Domain.Appointments;
using Backend.Veteriner.Domain.Charges;
using Backend.Veteriner.Domain.ClientCredits;
using Backend.Veteriner.Domain.Clients;
using Backend.Veteriner.Domain.Imports;
using Backend.Veteriner.Domain.Pets;
using Backend.Veteriner.Domain.Products;
using Backend.Veteriner.Domain.Shared;
using Backend.Veteriner.Domain.Vaccinations;
using FluentValidation;
using Mediator;

namespace Backend.Veteriner.Application.Imports.Commands.ApplyOwnerUnit;

/// <summary>
/// Sahip birimini mevcut komutlarla yazar (kural kopyalanmaz): müşteri <see cref="CreateClientCommand"/>, hasta
/// <see cref="CreatePetCommand"/>, aşı <see cref="CreateImportedVaccinationCommand"/>, klinik geçmiş
/// <see cref="CreateImportedClinicalRecordCommand"/>, hesap hareketi <see cref="CreateImportedAccountEntryCommand"/>, randevu
/// <see cref="CreateImportedAppointmentCommand"/>, ürün <see cref="CreateProductCommand"/>; devir bakiyesi
/// <see cref="Charge.CreateOpeningBalance"/>, devir alacağı <see cref="ClientCredit.FromImport"/> (ADR-048). Önizlemedeki eşleşmeler uygulama
/// anında yeniden çözülür (K4: arada eklenen müşteri/hasta <c>Matched</c>; K10: arada çipi kaydedilen hasta hata; aynı
/// gün aşısı <c>Skipped</c>). Satır sonuçları ve parti kirası birimin sonunda aynı <c>SaveChanges</c>'te yazılır: kira
/// başka işçiye geçtiyse rowversion çakışması birimi geri aldırır (aynı satır iki kez yazılmaz).
/// </summary>
public sealed class ApplyImportOwnerUnitCommandHandler
    : IRequestHandler<ApplyImportOwnerUnitCommand, Result<ImportOwnerUnitResult>>
{
    private const string ClientEntity = "Client";
    private const string PetEntity = "Pet";
    private const string VaccinationEntity = "Vaccination";
    private const string ClinicalRecordEntity = "ClinicalRecord";
    private const string AccountEntryEntity = "AccountEntry";
    private const string AppointmentEntity = "Appointment";
    private const string ProductEntity = "Product";
    private const string OpeningBalanceDescription = "Devir bakiyesi (eski program)";

    private readonly ITenantContext _tenantContext;
    private readonly IRepository<ImportBatch> _batches;
    private readonly IRepository<ImportBatchRow> _rows;
    private readonly IReadRepository<Client> _clients;
    private readonly IReadRepository<Pet> _pets;
    private readonly IRepository<Pet> _petsWrite;
    private readonly IReadRepository<Vaccination> _vaccinations;
    private readonly IReadRepository<Appointment> _appointments;
    private readonly IReadRepository<Product> _products;
    private readonly IRepository<Charge> _charges;
    private readonly IRepository<ClientCredit> _credits;
    private readonly ISender _sender;
    private readonly TimeProvider _timeProvider;

    public ApplyImportOwnerUnitCommandHandler(
        ITenantContext tenantContext,
        IRepository<ImportBatch> batches,
        IRepository<ImportBatchRow> rows,
        IReadRepository<Client> clients,
        IReadRepository<Pet> pets,
        IRepository<Pet> petsWrite,
        IReadRepository<Vaccination> vaccinations,
        IReadRepository<Appointment> appointments,
        IReadRepository<Product> products,
        IRepository<Charge> charges,
        IRepository<ClientCredit> credits,
        ISender sender,
        TimeProvider timeProvider)
    {
        _tenantContext = tenantContext;
        _batches = batches;
        _rows = rows;
        _clients = clients;
        _pets = pets;
        _petsWrite = petsWrite;
        _vaccinations = vaccinations;
        _appointments = appointments;
        _products = products;
        _charges = charges;
        _credits = credits;
        _sender = sender;
        _timeProvider = timeProvider;
    }

    public async ValueTask<Result<ImportOwnerUnitResult>> Handle(ApplyImportOwnerUnitCommand request, CancellationToken ct)
    {
        if (_tenantContext.TenantId is not { } tenantId)
            return Result<ImportOwnerUnitResult>.Failure("Tenants.ContextMissing", "Kiracı bağlamı yok.");

        var batch = await _batches.FirstOrDefaultAsync(new ImportBatchByIdSpec(tenantId, request.BatchId, tracking: true), ct);
        if (batch is null || batch.Status != ImportBatchStatus.Committing || batch.LeaseOwner != request.LeaseOwner)
            return Result<ImportOwnerUnitResult>.Failure(ImportOwnerUnitErrors.LeaseLost());

        var rows = await _rows.ListAsync(new ImportBatchRowsByIdsSpec(batch.Id, request.RowIds), ct);
        if (rows.Count == 0 || rows.Any(r => r.ApplyStatus != ImportRowApplyStatus.Pending))
            return Result<ImportOwnerUnitResult>.Success(new ImportOwnerUnitResult(AlreadyApplied: true));

        var unit = new UnitState(tenantId, batch, rows, ImportOptionsDocument.Parse(batch.OptionsJson));

        var applied = ImportCommitKeys.IsProductUnit(request.OwnerKey)
            ? await ApplyProductsAsync(unit, ct)
            : await ApplyOwnerAsync(unit, request.OwnerKey, ct);
        if (!applied.IsSuccess)
            return Result<ImportOwnerUnitResult>.Failure(applied.Error);

        unit.SkipUnresolvedRows();

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!batch.RenewCommitLease(request.LeaseOwner, now).IsSuccess)
            return Result<ImportOwnerUnitResult>.Failure(ImportOwnerUnitErrors.LeaseLost());

        foreach (var outcome in unit.Outcomes)
        {
            outcome.Row.RecordApplyResult(
                outcome.Status,
                outcome.TargetType,
                outcome.TargetId,
                outcome.Matched,
                now,
                outcome.Issue is null ? null : ImportIssueJson.Append(outcome.Row.IssuesJson, outcome.Issue));
        }

        // Satır sonuçları + kira yenilemesi; parti rowversion'ı değiştiyse (devralındı) birim geri alınır.
        await _rows.SaveChangesAsync(ct);
        return Result<ImportOwnerUnitResult>.Success(new ImportOwnerUnitResult(AlreadyApplied: false));
    }

    /// <summary>
    /// Sahip birimi sırası: müşteri (+ devir bakiyesi) → hesap hareketi → hasta → aşı → klinik geçmiş → randevu → vefat durumu
    /// (geçmiş kayıtlar yazıldıktan sonra; vefat eden hastaya yeni kayıt açılmaz kuralı geçmişi engellemesin).
    /// </summary>
    private async Task<Result> ApplyOwnerAsync(UnitState unit, string ownerKey, CancellationToken ct)
    {
        var steps = new Func<Task<Result>>[]
        {
            () => ApplyCustomersAsync(unit, ownerKey, ct),
            () => ApplyAccountEntriesAsync(unit, ct),
            () => ApplyPetsAsync(unit, ct),
            () => ApplyVaccinationsAsync(unit, ct),
            () => ApplyClinicalRecordsAsync(unit, ct),
            () => ApplyAppointmentsAsync(unit, ct),
            () => ApplyDeceasedStatusAsync(unit, ct),
        };

        foreach (var step in steps)
        {
            var result = await step();
            if (!result.IsSuccess)
                return result;
        }

        return Result.Success();
    }

    // ---------------------------------------------------------------- Müşteri

    private async Task<Result> ApplyCustomersAsync(UnitState unit, string ownerKey, CancellationToken ct)
    {
        var customers = unit.Rows<ImportNormalizedCustomer>(ImportTable.Customers);
        var byRow = customers.ToDictionary(c => c.Row.RowNumber, c => c.Data);

        if (ImportCommitKeys.TryParseDb(ownerKey, out var existingId))
        {
            var client = await _clients.FirstOrDefaultAsync(new ClientByIdSpec(unit.TenantId, existingId), ct);
            if (client is null)
                return Fail(unit.FirstRow, "Clients.NotFound", "Eşleşen müşteri artık bulunamadı.");
            unit.ClientId = client.Id;
        }

        foreach (var (row, data) in customers)
        {
            var key = ImportCommitKeys.Customer(row.RowNumber, data, n => byRow.GetValueOrDefault(n));
            if (data.Action == ImportRowAction.Create && key == ownerKey)
            {
                var created = await CreateOrMatchClientAsync(unit, row, data, ct);
                if (!created.IsSuccess)
                    return created;
                continue;
            }

            // Mevcutla eşleşen (Match / Skip modu) veya dosya içi mükerrer satır: birimin müşterisine işaret eder.
            var status = data.Action == ImportRowAction.Match ? ImportRowApplyStatus.Matched : ImportRowApplyStatus.Skipped;
            unit.Add(row, status, ClientEntity, unit.ClientId, matched: data.TargetId is not null);
        }

        if (unit.ClientId is null)
            return Fail(unit.FirstRow, "Imports.OwnerNotFound", "Birimin sahibi çözülemedi.");

        return Result.Success();
    }

    /// <summary>K4: oluşturmadan önce oluşturma yolunun aynı kuralıyla (ad + telefon, sonra ad + e-posta) mevcutu arar.</summary>
    private async Task<Result> CreateOrMatchClientAsync(
        UnitState unit, ImportBatchRow row, ImportNormalizedCustomer data, CancellationToken ct)
    {
        var existing = await FindExistingClientAsync(unit.TenantId, data, ct);
        if (existing is not null)
        {
            if (unit.Options.OnDuplicateClient == ImportOptionsDocument.Error)
                return Fail(row, ImportRules.DuplicateClient.Code, ImportRules.DuplicateClient.Message);

            unit.ClientId = existing.Id;
            var status = unit.Options.OnDuplicateClient == ImportOptionsDocument.Skip
                ? ImportRowApplyStatus.Skipped
                : ImportRowApplyStatus.Matched;
            unit.Add(row, status, ClientEntity, existing.Id, matched: true, ImportRules.MatchedAtCommit.At());
            return Result.Success();
        }

        var created = await SendAsync(
            new CreateClientCommand(
                data.FullName!,
                data.Email,
                data.Phone,
                data.Address,
                RemindersOptedOut: data.RemindersOptedOut ? true : null,
                InvoicingIdentity: InvoicingIdentityOf(data)),
            ct);
        if (!created.IsSuccess)
            return Fail(row, created.Error);

        unit.ClientId = created.Value!.Id;
        unit.Add(row, ImportRowApplyStatus.Created, ClientEntity, unit.ClientId, matched: false);
        return data.OpeningBalance switch
        {
            > 0 => await CreateOpeningBalanceAsync(unit, row, data, ct),
            < 0 => await CreateOpeningCreditAsync(unit, row, data, ct),
            _ => Result.Success(),
        };
    }

    /// <summary>Doğrulanmış fatura kimliği: TCKN → bireysel; VKN → kurumsal (unvan müşteri adı, vergi dairesiyle).</summary>
    private static ClientInvoicingIdentityDto? InvoicingIdentityOf(ImportNormalizedCustomer data)
    {
        if (data.NationalId is { } nationalId)
            return new ClientInvoicingIdentityDto(ClientPersonType.Individual, nationalId, null, null, null, null, null, null);

        if (data.TaxNumber is { } taxNumber)
            return new ClientInvoicingIdentityDto(ClientPersonType.Corporate, null, taxNumber, data.TaxOffice, data.FullName, null, null, null);

        return null;
    }

    /// <summary>
    /// Devir bakiyesi (ADR-048 T3): yalnız oluşturulan müşteriye tek ücret; ciro/gün sonu/e-belge dışıdır (<c>ImportBatchId</c>).
    /// İstek anahtarı satır kimliğidir; kaydı uygulamayı başlatan kullanıcı açmış sayılır.
    /// </summary>
    private async Task<Result> CreateOpeningBalanceAsync(
        UnitState unit, ImportBatchRow row, ImportNormalizedCustomer data, CancellationToken ct)
    {
        if (unit.Batch.ClinicId is not { } clinicId)
            return Fail(row, ImportRules.ClinicRequired.Code, ImportRules.ClinicRequired.Message);

        var description = data.BalanceDate is { } asOf
            ? $"Devir bakiyesi (eski program, {asOf:dd.MM.yyyy})"
            : OpeningBalanceDescription;
        var charge = Charge.CreateOpeningBalance(
            unit.TenantId,
            clinicId,
            unit.ClientId!.Value,
            description,
            data.OpeningBalance!.Value,
            unit.Options.CommittedByUserId ?? unit.Batch.CreatedByUserId,
            _timeProvider.GetUtcNow().UtcDateTime,
            row.Id,
            unit.Batch.Id);
        if (!charge.IsSuccess)
            return Fail(row, charge.Error);

        await _charges.AddAsync(charge.Value!, ct);
        await _charges.SaveChangesAsync(ct);
        return Result.Success();
    }

    /// <summary>
    /// Devir alacağı (ADR-048 T3): eksi bakiyenin mutlak değeri kadar, yalnız oluşturulan müşteriye tek müşteri kredisi; ödeme
    /// yazılmaz, bu yüzden ciroya, gün sonuna, kasaya ve e-belgeye girmez. Açanı devir ücretiyle aynı kuraldır.
    /// </summary>
    private async Task<Result> CreateOpeningCreditAsync(
        UnitState unit, ImportBatchRow row, ImportNormalizedCustomer data, CancellationToken ct)
    {
        if (unit.Batch.ClinicId is not { } clinicId)
            return Fail(row, ImportRules.ClinicRequired.Code, ImportRules.ClinicRequired.Message);

        var credit = ClientCredit.FromImport(
            unit.TenantId,
            clinicId,
            unit.ClientId!.Value,
            -data.OpeningBalance!.Value,
            unit.Options.CommittedByUserId ?? unit.Batch.CreatedByUserId,
            _timeProvider.GetUtcNow().UtcDateTime,
            unit.Batch.Id);
        if (!credit.IsSuccess)
            return Fail(row, credit.Error);

        await _credits.AddAsync(credit.Value!, ct);
        await _credits.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Client?> FindExistingClientAsync(Guid tenantId, ImportNormalizedCustomer data, CancellationToken ct)
    {
        var nameKey = Client.NormalizeFullNameForDuplicateCheck(data.FullName!);
        if (data.Phone is { } phone
            && await _clients.FirstOrDefaultAsync(new ClientByTenantNormalizedFullNameAndPhoneSpec(tenantId, nameKey, phone), ct) is { } byPhone)
        {
            return byPhone;
        }

        if (data.Email is { } email)
            return await _clients.FirstOrDefaultAsync(new ClientByTenantNormalizedFullNameAndEmailSpec(tenantId, nameKey, email), ct);

        return null;
    }

    // ---------------------------------------------------------------- Hasta

    private async Task<Result> ApplyPetsAsync(UnitState unit, CancellationToken ct)
    {
        var pets = unit.Rows<ImportNormalizedPet>(ImportTable.Pets);
        var byRow = pets.ToDictionary(p => p.Row.RowNumber, p => p.Data);
        var clientId = unit.ClientId!.Value;

        foreach (var (row, data) in pets)
        {
            var key = ImportCommitKeys.Pet(row.RowNumber, data, n => byRow.GetValueOrDefault(n));
            switch (data.Action)
            {
                case ImportRowAction.Create:
                {
                    var created = await CreateOrMatchPetAsync(unit, row, data, clientId, ct);
                    if (!created.IsSuccess)
                        return Result.Failure(created.Error);
                    unit.PetIds[key!] = created.Value;
                    break;
                }

                case ImportRowAction.Match when data.TargetId is { } petId:
                {
                    var pet = await _pets.FirstOrDefaultAsync(new PetByIdSpec(unit.TenantId, petId), ct);
                    if (pet is null)
                        return Fail(row, "Pets.NotFound", "Eşleşen hasta artık bulunamadı.");
                    unit.PetIds[key!] = pet.Id;
                    unit.ExistingPetIds.Add(pet.Id);
                    unit.Add(row, ImportRowApplyStatus.Matched, PetEntity, pet.Id, matched: true);
                    break;
                }

                default:
                    // Dosya içi mükerrer (ilk satırın hayvanı) veya bilinçli atlanan tür.
                    var target = key is not null && unit.PetIds.TryGetValue(key, out var mergedId) ? mergedId : (Guid?)null;
                    unit.Add(row, ImportRowApplyStatus.Skipped, PetEntity, target, matched: false);
                    break;
            }
        }

        return Result.Success();
    }

    private async Task<Result<Guid>> CreateOrMatchPetAsync(
        UnitState unit, ImportBatchRow row, ImportNormalizedPet data, Guid clientId, CancellationToken ct)
    {
        // Mevcut hayvan mükerrer kuralı (CreatePetCommand ile aynı anahtar): aynı müşteri + Türkçe duyarlı ad anahtarı + tür.
        var nameKey = Pet.NormalizeNameForDuplicateCheck(data.Name!);
        var existing = await _pets.FirstOrDefaultAsync(new PetByClientNameAndSpeciesIdSpec(clientId, nameKey, data.SpeciesId!.Value), ct);
        if (existing is not null)
        {
            unit.ExistingPetIds.Add(existing.Id);
            unit.Add(row, ImportRowApplyStatus.Matched, PetEntity, existing.Id, matched: true, ImportRules.MatchedAtCommit.At());
            return Result<Guid>.Success(existing.Id);
        }

        // K10: önizlemeden sonra aynı çip başka hayvana kaydedilmiş olabilir.
        if (data.Microchip is { } chip)
        {
            var sameChip = await _pets.ListAsync(ImportExistingPetsSpec.ByMicrochips(unit.TenantId, [chip]), ct);
            if (sameChip.Count > 0)
                return FailOf<Guid>(row, ImportRules.MicrochipConflict.Code, ImportRules.MicrochipConflict.Message);
        }

        var created = await SendAsync(
            new CreatePetCommand(
                clientId,
                data.Name!,
                data.SpeciesId.Value,
                data.Breed,
                data.BirthDate,
                data.BreedId,
                data.Gender,
                data.ColorId,
                data.Weight,
                data.Notes,
                data.Microchip,
                data.PassportOrTagNumber,
                IsNeutered: data.IsNeutered,
                AlertFlags: data.AlertNote is null ? null : [nameof(PetAlertFlags.Allergy)],
                AlertNote: data.AlertNote),
            ct);
        if (!created.IsSuccess)
            return Result<Guid>.Failure(ImportOwnerUnitErrors.ForRow(row.Id, created.Error));

        unit.Add(row, ImportRowApplyStatus.Created, PetEntity, created.Value, matched: false);
        if (data.Deceased)
            unit.DeceasedPets.Add((row, created.Value, data.DeceasedOn));
        return Result<Guid>.Success(created.Value);
    }

    /// <summary>
    /// Vefat durumu yalnız birimde oluşturulan hastaya yazılır (eşleşen mevcut hasta ezilmez, K4); kural <see cref="Pet.SetStatus"/>.
    /// Ayrı komut yerine aynı işlemde yazılır: durum değişikliği satır verisidir, tek tek audit'lenmez (bölüm 10).
    /// </summary>
    private async Task<Result> ApplyDeceasedStatusAsync(UnitState unit, CancellationToken ct)
    {
        if (unit.DeceasedPets.Count == 0)
            return Result.Success();

        var today = OperationDayBounds.ToLocalDate(_timeProvider.GetUtcNow().UtcDateTime);
        foreach (var (row, petId, deceasedOn) in unit.DeceasedPets)
        {
            // Hasta bu birimde (aynı bağlamda) oluşturuldu ve izleniyor; kimlikle okuma izlenen örneği verir.
            var pet = await _petsWrite.GetByIdAsync(petId, ct);
            if (pet is null || pet.TenantId != unit.TenantId)
                return Fail(row, "Pets.NotFound", "Oluşturulan hasta bulunamadı.");

            var status = pet.SetStatus(PetStatus.Deceased, deceasedOn, today);
            if (!status.IsSuccess)
                return Fail(row, status.Error);
        }

        await _petsWrite.SaveChangesAsync(ct);
        return Result.Success();
    }

    // ---------------------------------------------------------------- Aşı

    private async Task<Result> ApplyVaccinationsAsync(UnitState unit, CancellationToken ct)
    {
        foreach (var (row, data) in unit.Rows<ImportNormalizedVaccination>(ImportTable.Vaccinations))
        {
            if (data.Action != ImportRowAction.Create)
            {
                unit.Add(row, ImportRowApplyStatus.Skipped, VaccinationEntity, null, matched: false);
                continue;
            }

            if (ResolvePetId(unit, data.PetKey) is not { } petId)
                return Fail(row, ImportRules.PetNotFound.Code, ImportRules.PetNotFound.Message);

            if (unit.Batch.ClinicId is not { } clinicId)
                return Fail(row, ImportRules.ClinicRequired.Code, ImportRules.ClinicRequired.Message);

            var day = OperationDayBounds.ToLocalDate(data.AppliedAtUtc!.Value);
            if (unit.ExistingPetIds.Contains(petId)
                && (await ExistingVaccinationDaysAsync(unit, petId, ct)).Contains((data.VaccineDefinitionId!.Value, day)))
            {
                unit.Add(row, ImportRowApplyStatus.Skipped, VaccinationEntity, null, matched: false, ImportRules.DuplicateVaccination.At());
                continue;
            }

            var created = await SendAsync(
                new CreateImportedVaccinationCommand(
                    unit.Batch.Id, clinicId, petId, data.VaccineDefinitionId!.Value, data.AppliedAtUtc.Value, data.DueAtUtc, data.Notes),
                ct);
            if (!created.IsSuccess)
                return Fail(row, created.Error);

            unit.Add(row, ImportRowApplyStatus.Created, VaccinationEntity, created.Value, matched: false);
        }

        return Result.Success();
    }

    /// <summary>Mevcut hastanın uygulanmış aşıları (tanım + İstanbul günü); hasta başına bir sorgu.</summary>
    private async Task<HashSet<(Guid DefinitionId, DateOnly Day)>> ExistingVaccinationDaysAsync(UnitState unit, Guid petId, CancellationToken ct)
    {
        if (unit.VaccinationDays.TryGetValue(petId, out var days))
            return days;

        var existing = await _vaccinations.ListAsync(new ImportExistingVaccinationsSpec(unit.TenantId, [petId]), ct);
        days = existing
            .Where(v => v.AppliedAtUtc is not null)
            .Select(v => (v.VaccineDefinitionId, OperationDayBounds.ToLocalDate(v.AppliedAtUtc!.Value)))
            .ToHashSet();
        unit.VaccinationDays[petId] = days;
        return days;
    }

    // ---------------------------------------------------------------- Klinik geçmiş, hesap hareketi, randevu (ADR-048)

    /// <summary>Hasta anahtarı: birimde oluşan/eşleşen hasta (<c>file:</c>) veya mevcut hasta (<c>db:</c>).</summary>
    private static Guid? ResolvePetId(UnitState unit, string? petKey)
    {
        if (petKey is not null && unit.PetIds.TryGetValue(petKey, out var resolved))
            return resolved;

        if (ImportCommitKeys.TryParseDb(petKey, out var existingPet))
        {
            unit.ExistingPetIds.Add(existingPet);
            return existingPet;
        }

        return null;
    }

    private async Task<Result> ApplyClinicalRecordsAsync(UnitState unit, CancellationToken ct)
    {
        foreach (var (row, data) in unit.Rows<ImportNormalizedClinicalRecord>(ImportTable.ClinicalRecords))
        {
            if (data.Action != ImportRowAction.Create)
            {
                unit.Add(row, ImportRowApplyStatus.Skipped, ClinicalRecordEntity, null, matched: false);
                continue;
            }

            if (ResolvePetId(unit, data.PetKey) is not { } petId)
                return Fail(row, ImportRules.PetNotFound.Code, ImportRules.PetNotFound.Message);
            if (unit.Batch.ClinicId is not { } clinicId)
                return Fail(row, ImportRules.ClinicRequired.Code, ImportRules.ClinicRequired.Message);

            var content = new ImportedClinicalRecord.Content(
                data.RecordType, data.Complaint, data.History, data.Findings, data.Diagnosis, data.Treatment,
                data.Prescription, data.LabResults, data.Notes, data.VeterinarianName, data.WeightKg, data.TemperatureC);
            var created = await SendAsync(
                new CreateImportedClinicalRecordCommand(unit.Batch.Id, clinicId, petId, data.RecordedOn!.Value, content), ct);
            if (!created.IsSuccess)
                return Fail(row, created.Error);

            unit.Add(row, ImportRowApplyStatus.Created, ClinicalRecordEntity, created.Value, matched: false);
        }

        return Result.Success();
    }

    private async Task<Result> ApplyAccountEntriesAsync(UnitState unit, CancellationToken ct)
    {
        foreach (var (row, data) in unit.Rows<ImportNormalizedAccountEntry>(ImportTable.AccountEntries))
        {
            if (data.Action != ImportRowAction.Create)
            {
                unit.Add(row, ImportRowApplyStatus.Skipped, AccountEntryEntity, null, matched: false);
                continue;
            }

            if (unit.Batch.ClinicId is not { } clinicId)
                return Fail(row, ImportRules.ClinicRequired.Code, ImportRules.ClinicRequired.Message);

            var created = await SendAsync(
                new CreateImportedAccountEntryCommand(
                    unit.Batch.Id, clinicId, unit.ClientId!.Value, data.OccurredOn!.Value, data.Kind!.Value, data.Amount!.Value,
                    data.Description, data.PaymentMethod, data.DocumentNumber, data.PetName),
                ct);
            if (!created.IsSuccess)
                return Fail(row, created.Error);

            unit.Add(row, ImportRowApplyStatus.Created, AccountEntryEntity, created.Value, matched: false);
        }

        return Result.Success();
    }

    /// <summary>
    /// Uygulama anında yeniden çözüm: başlangıcı geçmişe düşen randevu ve arada aynı hasta + aynı başlangıçla açılmış (iptal
    /// edilmemiş) randevu atlanır; ikisi de hata değildir.
    /// </summary>
    private async Task<Result> ApplyAppointmentsAsync(UnitState unit, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var (row, data) in unit.Rows<ImportNormalizedAppointment>(ImportTable.Appointments))
        {
            if (data.Action != ImportRowAction.Create)
            {
                unit.Add(row, ImportRowApplyStatus.Skipped, AppointmentEntity, null, matched: false);
                continue;
            }

            if (ResolvePetId(unit, data.PetKey) is not { } petId)
                return Fail(row, ImportRules.PetNotFound.Code, ImportRules.PetNotFound.Message);
            if (unit.Batch.ClinicId is not { } clinicId)
                return Fail(row, ImportRules.ClinicRequired.Code, ImportRules.ClinicRequired.Message);

            var start = DateTime.SpecifyKind(data.ScheduledAtUtc!.Value, DateTimeKind.Utc);
            if (start <= now)
            {
                unit.Add(row, ImportRowApplyStatus.Skipped, AppointmentEntity, null, matched: false, ImportRules.PastAppointmentSkipped.At());
                continue;
            }

            if (unit.ExistingPetIds.Contains(petId)
                && (await ExistingAppointmentStartsAsync(unit, petId, now, ct)).Contains(start))
            {
                unit.Add(row, ImportRowApplyStatus.Skipped, AppointmentEntity, null, matched: false, ImportRules.DuplicateAppointment.At());
                continue;
            }

            var created = await SendAsync(
                new CreateImportedAppointmentCommand(
                    unit.Batch.Id, clinicId, petId, start, data.DurationMinutes, data.AppointmentType, data.Notes,
                    data.ReasonId, data.VeterinarianUserId),
                ct);
            if (!created.IsSuccess)
                return Fail(row, created.Error);

            unit.Add(row, ImportRowApplyStatus.Created, AppointmentEntity, created.Value, matched: false);
        }

        return Result.Success();
    }

    /// <summary>Mevcut hastanın gelecekteki iptal edilmemiş randevu başlangıçları; hasta başına bir sorgu.</summary>
    private async Task<HashSet<DateTime>> ExistingAppointmentStartsAsync(UnitState unit, Guid petId, DateTime now, CancellationToken ct)
    {
        if (unit.AppointmentStarts.TryGetValue(petId, out var starts))
            return starts;

        var existing = await _appointments.ListAsync(new ImportExistingAppointmentsSpec(unit.TenantId, [petId], now), ct);
        starts = existing.Select(a => DateTime.SpecifyKind(a.ScheduledAtUtc, DateTimeKind.Utc)).ToHashSet();
        unit.AppointmentStarts[petId] = starts;
        return starts;
    }

    // ---------------------------------------------------------------- Ürün (ADR-048 T7)

    /// <summary>
    /// Ürün birimi: kanonik satır (oluşacak veya eşleşen) ve dosya içi mükerrerleri. Oluşturmadan önce barkod, sonra ad + tür
    /// ile mevcut ürün yeniden aranır (K4 ilkesi; mevcut alanlar güncellenmez). Stok, parti ve narkotik sınıfı yazılmaz.
    /// </summary>
    private async Task<Result> ApplyProductsAsync(UnitState unit, CancellationToken ct)
    {
        Guid? productId = null;
        var products = unit.Rows<ImportNormalizedProduct>(ImportTable.Products);
        foreach (var (row, data) in products.Where(p => p.Data.Action is ImportRowAction.Create or ImportRowAction.Match))
        {
            if (data.Action == ImportRowAction.Match && data.TargetId is { } targetId)
            {
                if (await _products.FirstOrDefaultAsync(new ProductByIdSpec(unit.TenantId, targetId), ct) is null)
                    return Fail(row, "Products.NotFound", "Eşleşen ürün artık bulunamadı.");
                productId = targetId;
                unit.Add(row, ImportRowApplyStatus.Matched, ProductEntity, targetId, matched: true);
                continue;
            }

            if (await FindExistingProductAsync(unit.TenantId, data, ct) is { } existing)
            {
                productId = existing.Id;
                unit.Add(row, ImportRowApplyStatus.Matched, ProductEntity, existing.Id, matched: true, ImportRules.MatchedAtCommit.At());
                continue;
            }

            var created = await SendAsync(
                new CreateProductCommand(
                    data.CategoryId, data.Name!, data.Sku, data.Barcode, data.Description, data.Unit, data.UnitPrice,
                    Charge.DefaultCurrency, data.Kind, ControlledSubstanceClass.None, Manufacturer: data.Manufacturer),
                ct);
            if (!created.IsSuccess)
                return Fail(row, created.Error);

            productId = created.Value!.Id;
            unit.Add(row, ImportRowApplyStatus.Created, ProductEntity, productId, matched: false);
        }

        // Dosya içi mükerrer: ilk satırın ürününe işaret eder.
        foreach (var (row, _) in products.Where(p => p.Data.Action == ImportRowAction.Skip))
            unit.Add(row, ImportRowApplyStatus.Skipped, ProductEntity, productId, matched: false);

        return Result.Success();
    }

    private async Task<Product?> FindExistingProductAsync(Guid tenantId, ImportNormalizedProduct data, CancellationToken ct)
    {
        if (data.Barcode is { } barcode
            && await _products.FirstOrDefaultAsync(new ProductByTenantAndBarcodeSpec(tenantId, barcode), ct) is { } byBarcode)
        {
            return byBarcode;
        }

        var nameKey = ImportTextFolding.Fold(data.Name);
        var sameName = await _products.ListAsync(new ImportProductsByNameSpec(tenantId, data.Name!, data.Kind), ct);
        return sameName.FirstOrDefault(p => ImportTextFolding.Fold(p.Name) == nameKey);
    }

    // ---------------------------------------------------------------- Ortak

    /// <summary>Komut doğrulaması (FluentValidation) istisnası satır hatasına çevrilir; mesajlar değer içermez.</summary>
    private async Task<Result<T>> SendAsync<T>(IRequest<Result<T>> command, CancellationToken ct)
    {
        try
        {
            return await _sender.Send(command, ct);
        }
        catch (ValidationException ex)
        {
            return Result<T>.Failure("Validation.FluentValidation", string.Join(" ", ex.Errors.Select(e => e.ErrorMessage).Distinct()));
        }
    }

    private static Result Fail(ImportBatchRow row, string code, string message)
        => Fail(row, new Error(code, message));

    private static Result Fail(ImportBatchRow row, Error error)
        => Result.Failure(ImportOwnerUnitErrors.ForRow(row.Id, error));

    private static Result<T> FailOf<T>(ImportBatchRow row, string code, string message)
        => Result<T>.Failure(ImportOwnerUnitErrors.ForRow(row.Id, new Error(code, message)));

    private sealed record Outcome(
        ImportBatchRow Row,
        ImportRowApplyStatus Status,
        string? TargetType,
        Guid? TargetId,
        bool Matched,
        ImportIssue? Issue);

    /// <summary>Birim boyunca çözülen kimlikler ve satır sonuçları (yazım en sonda, tek seferde).</summary>
    private sealed class UnitState
    {
        private readonly IReadOnlyList<ImportBatchRow> _rows;

        public UnitState(Guid tenantId, ImportBatch batch, IReadOnlyList<ImportBatchRow> rows, ImportOptionsDocument options)
        {
            TenantId = tenantId;
            Batch = batch;
            Options = options;
            _rows = rows.OrderBy(r => r.Table).ThenBy(r => r.RowNumber).ToList();
        }

        public Guid TenantId { get; }
        public ImportBatch Batch { get; }
        public ImportOptionsDocument Options { get; }
        public Guid? ClientId { get; set; }
        public Dictionary<string, Guid> PetIds { get; } = new(StringComparer.Ordinal);
        public HashSet<Guid> ExistingPetIds { get; } = new();
        public Dictionary<Guid, HashSet<(Guid DefinitionId, DateOnly Day)>> VaccinationDays { get; } = new();
        public Dictionary<Guid, HashSet<DateTime>> AppointmentStarts { get; } = new();

        /// <summary>Birimde oluşturulan ve vefat olarak işaretlenecek hastalar (birimin son adımı).</summary>
        public List<(ImportBatchRow Row, Guid PetId, DateOnly? DeceasedOn)> DeceasedPets { get; } = new();
        public List<Outcome> Outcomes { get; } = new();
        public ImportBatchRow FirstRow => _rows[0];

        public IReadOnlyList<(ImportBatchRow Row, T Data)> Rows<T>(ImportTable table)
            where T : ImportNormalizedRow
            => _rows
                .Where(r => r.Table == table)
                .Select(r => (Row: r, Data: ImportNormalizedJson.Parse(table, r.NormalizedJson) as T))
                .Where(x => x.Data is not null)
                .Select(x => (x.Row, x.Data!))
                .ToList();

        public void Add(
            ImportBatchRow row, ImportRowApplyStatus status, string targetType, Guid? targetId, bool matched, ImportIssue? issue = null)
            => Outcomes.Add(new Outcome(row, status, targetType, targetId, matched, issue));

        /// <summary>Normalize verisi okunamayan satır (savunma): bekler halde kalmasın, atlanır.</summary>
        public void SkipUnresolvedRows()
        {
            var done = Outcomes.Select(o => o.Row.Id).ToHashSet();
            foreach (var row in _rows.Where(r => !done.Contains(r.Id)))
                Outcomes.Add(new Outcome(row, ImportRowApplyStatus.Skipped, null, null, false, null));
        }
    }
}
