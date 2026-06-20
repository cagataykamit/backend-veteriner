# CQRS-12C-1 — Pet Read-Model Schema Foundation

## Kapsam

Bu faz **yalnızca** Pet CQRS read-model'i için Query DB tarafında minimal ve güvenli şema temelini hazırlar.
Yazma davranışı, handler routing, feature flag, event emission ve projection processor bu fazın **dışındadır**.

## Yapılanlar

- Query DB `PetReadModel` entity'si eklendi.
- EF configuration (`PetReadModelConfiguration`) mevcut read-model desenine (`Client`/`Appointment`) uygun eklendi.
- `QueryReadModelConstraints`'e Pet'e özgü normalize uzunlukları eklendi
  (`PetNameNormalized`, `SpeciesNameNormalized`, `PetColorName`, `PetColorNameNormalized`).
- `QueryDbContext`'e `PetReadModels` DbSet'i eklendi.
- Query DB migration oluşturuldu: `20260620000207_AddPetReadModel`.
- `QueryDbMigrationIntegrationTests` genişletildi: `PetReadModels` tablosu, beş index ve
  `BirthDate` (`date`) / nullable alan round-trip doğrulaması.

## Tablo: `PetReadModels`

| Kolon | Tip | Null | Not |
|-------|-----|------|-----|
| `PetId` | `uniqueidentifier` | Hayır | PK |
| `TenantId` | `uniqueidentifier` | Hayır | Zorunlu, tenant scope |
| `ClientId` | `uniqueidentifier` | Hayır | Sahip |
| `ClientFullName` | `nvarchar(300)` | Hayır | Denormalize sahip adı |
| `ClientFullNameNormalized` | `nvarchar(300)` | Hayır | Sahip adına göre arama/sıralama |
| `Name` | `nvarchar(200)` | Hayır | Hayvan adı |
| `NameNormalized` | `nvarchar(200)` | Hayır | Ada göre arama/sıralama |
| `SpeciesId` | `uniqueidentifier` | Hayır | |
| `SpeciesName` | `nvarchar(200)` | Hayır | Denormalize tür adı |
| `SpeciesNameNormalized` | `nvarchar(200)` | Hayır | Tür adına göre arama |
| `BreedId` | `uniqueidentifier` | Evet | Global ırk FK (domain `Pet.BreedId` nullable) |
| `Breed` | `nvarchar(150)` | Evet | Serbest metin ırk (domain `Pet.Breed`) |
| `BreedRefName` | `nvarchar(200)` | Evet | Çözümlenmiş global ırk adı (`Pet.BreedRef.Name`) |
| `ColorId` | `uniqueidentifier` | Evet | |
| `ColorName` | `nvarchar(200)` | Evet | Denormalize renk adı |
| `ColorNameNormalized` | `nvarchar(200)` | Evet | Renk adına göre arama |
| `Gender` | `int` | Evet | `PetGender` int karşılığı (1=Male, 2=Female) |
| `BirthDate` | `date` | Evet | |
| `Weight` | `decimal(6,2)` | Evet | Command `Pet.Weight` ile aynı precision |
| `LastEventId` | `uniqueidentifier` | Hayır | Projection idempotency/dedup |
| `LastEventOccurredAtUtc` | `datetime2` | Hayır | Stale/out-of-order guard ordering anahtarı (Client deseni) |
| `LastProjectedAtUtc` | `datetime2` | Hayır | Projection wall-clock izleme |

### Index'ler

- `IX_PetReadModels_TenantId_NameNormalized_PetId` — (TenantId, NameNormalized, PetId)
- `IX_PetReadModels_TenantId_ClientId` — (TenantId, ClientId)
- `IX_PetReadModels_TenantId_ClientFullNameNormalized_PetId` — (TenantId, ClientFullNameNormalized, PetId)
- `IX_PetReadModels_TenantId_SpeciesId` — (TenantId, SpeciesId)
- `IX_PetReadModels_TenantId_ColorId` — (TenantId, ColorId)

> Not: İsim/sahip-adı index'lerine sondan `PetId` eklendi. `Appointment`/`Client` desenindeki gibi PK'yi
> index'e tiebreaker olarak dahil ederek stabil sıralama/keyset paging sağlar.

## Denormalize alan kararları

- **ClientFullName / SpeciesName / ColorName** denormalize tutuluyor (okuma performansı).
  Rename propagation (Client/Species/Color adı değişince Pet satırlarını güncelleme) bu fazda **yapılmadı**;
  projection fazında ele alınacak.
- **Breed**: Domain'de hem serbest metin `Pet.Breed` hem opsiyonel `Pet.BreedId` (+ `BreedRef.Name`) var.
  `Appointment` read-model deseni (`PetBreed` + `PetBreedRefName`) izlenerek üç alan da eklendi:
  `BreedId`, `Breed`, `BreedRefName`. Böylece domain'in her iki ırk gösterimi de zorlanmadan desteklenir.
- **Normalize alanları** (`*Normalized`) şu an yalnızca şema; nasıl doldurulacağı (Türkçe culture-aware
  lower/uppercase, `ListQueryTextSearch.Normalize` ile hizalama) projection fazında netleşecek.

## `CreatedAtUtc` kararı (eklenmedi)

Pet domain entity'sinde (`Pet.cs`), `PetListItemDto`, `PetDetailDto` ve `PetListProjectionRow` içinde
**canonical `CreatedAtUtc` kaynağı yok** (Client'ta `CreatedAtUtc` vardır). İlk CQRS-12C-1 taslağında
alan eklenmişti; ancak kaynağı olmayan `NOT NULL CreatedAtUtc` projection/backfill sırasında yapay tarih
üretmeye zorlayacağından **kaldırıldı**. Oluşturulma zamanı ihtiyacı olursa önce Pet domain/command
tarafında canonical alan tanımlanmalı; ardından read-model'e eklenmelidir. Bu fazda
`LastEventOccurredAtUtc` ve `LastProjectedAtUtc` projection izleme için yeterlidir.

## Soft delete / `IsDeleted` kararı (uygulanmadı)

`Pet` domain entity'sinde soft-delete yok: `ISoftDelete`, `IsDeleted`, `DeletedAtUtc` veya EF global query
filter bulunmuyor (kod tabanı genelinde de soft-delete deseni yok). Bu nedenle `IsDeleted` **eklenmedi**.
Pet tarafında silme komutu/event'i netleştiğinde değerlendirilmelidir.

## Tenant scope / ClinicId kararı

`Pet` domain tenant-scoped (ClinicId'ye bağlı değil). Bu yüzden read-model'e `ClinicId` **eklenmedi**;
tenant scope `TenantId` ile korunur ve tüm index'ler `TenantId` ile başlar.

## Uygulama (apply) komutu

Migration veritabanına manuel uygulanmadı; DbMigrator üzerinden uygulanır:

```bash
dotnet run --project src/Backend.Veteriner.DbMigrator -- migrate-query
```

## Garanti

- Production command/read handler davranışı değişmedi (`GetPetsListQueryHandler`, command handler'lar dokunulmadı).
- Feature flag, event emission, projection processor eklenmedi.
- Client projection/event/read path'i değiştirilmedi.
- Route/auth/permission/tenant scope davranışına dokunulmadı.
- Mevcut testler kırılmadı.
