# CQRS-12D-2 — Client/Pet shared search lookup additive reader infrastructure

**Tür:** Additive altyapı. **Production handler davranışı değişmedi.** Yeni lookup reader metotları,
request/result modelleri, DI kaydı ve integration testleri eklendi; hiçbir handler bu metotları henüz
kullanmıyor.

## Amaç

CQRS-12D-1 audit'inde tespit edilen paylaşılan client/pet ön-arama adımları için Query DB read-model
üzerinden lookup yapılacak altyapıyı hazırlamak. Handler routing **12D-3**'te yapılacaktır.

## Seçilen interface tasarımı — Seçenek B

Liste reader interface'leri (`IClientReadModelReader`, `IPetReadModelReader`) **değiştirilmedi**.
Ayrı lookup interface'leri eklendi; mevcut reader sınıfları her iki interface'i de implement eder:

| Interface | Implementasyon | DI |
|---|---|---|
| `IClientReadModelLookupReader` | `ClientReadModelReader` | `AddScoped<IClientReadModelLookupReader, ClientReadModelReader>()` |
| `IPetReadModelLookupReader` | `PetReadModelReader` | `AddScoped<IPetReadModelLookupReader, PetReadModelReader>()` |

Gerekçe: liste contract'ı şişmez; 12D-3'te handler'lar yalnız lookup interface'ini inject edebilir;
test mock'laması kolay.

## Eklenen lookup metotları

### Client — `IClientReadModelLookupReader`

| Metot | Request | Result | Command eşdeğeri |
|---|---|---|---|
| `ResolveClientIdsByTextSearchAsync` | `ClientTextSearchLookupRequest(TenantId, SearchContainsLikePattern?)` | `ClientTextSearchLookupResult(ClientIds)` | `ClientsByTenantTextSearchSpec` |
| `GetNamesByIdsAsync` | `ClientNamesLookupRequest(TenantId, ClientIds)` | `ClientNamesLookupResult(Items: ClientNameLookupItem[])` | `ClientsByTenantIdsNameSpec` |

### Pet — `IPetReadModelLookupReader`

| Metot | Request | Result | Command eşdeğeri |
|---|---|---|---|
| `ResolvePetIdsByTextSearchAsync` | `PetTextSearchLookupRequest` | `PetTextSearchLookupResult(PetIds)` | `ListSearchPetIds` (Strateji A) |
| `ResolvePetIdsByPetTextFieldsAsync` | `PetTextFieldsSearchLookupRequest` | `PetTextFieldsSearchLookupResult(PetIds)` | `PetsByTenantTextFieldsSearchSpec` (Strateji B) |
| `ResolvePetIdsByClientIdsAsync` | `PetIdsByClientIdsLookupRequest(TenantId, ClientIds)` | `PetIdsByClientIdsLookupResult(PetIds)` | `PetsByTenantForClientIdsSpec` |
| `GetDisplayByIdsAsync` | `PetDisplayLookupRequest(TenantId, PetIds)` | `PetDisplayLookupResult(PetDisplayLookupItem[])` | `PetsByTenantIdsNameClientSpeciesSpec` (superset) |

## Search alanları

### Client text search

`FullName`, `Email`, `Phone`, `PhoneNormalized` — `ClientsByTenantTextSearchSpec` ile aynı.

### Pet text search — Strateji A (`ResolvePetIdsByTextSearchAsync`)

Pet kartı: `Name`, `Breed`, `SpeciesName`, `BreedRefName` + denormalize `ClientFullName`.
Tek sorguda `ListSearchPetIds` birleşimine eşdeğer.

### Pet text search — Strateji B (`ResolvePetIdsByPetTextFieldsAsync`)

Yalnız pet kartı alanları; **client adı genişlemesi yok** — ödeme araması (12D-4) için.

### ColorName

Command path (`PetsByTenantTextFieldsSearchSpec`, pet list spec) ColorName aramaz → lookup'a **dahil edilmedi**.

## Tenant isolation

Her metot `TenantId == request.TenantId` filtresi uygular. Toplu by-ids metotları yalnız istenen
tenant satırlarını döndürür; başka tenant id'leri sessizce yok sayılır (command path ile aynı).

## Sıralama

| Metot | Sıralama |
|---|---|
| Client id/name lookup | `FullNameNormalized`, `ClientId` |
| Pet id/display lookup | `NameNormalized`, `PetId` |
| Pet list (`GetListAsync`) | değişmedi: `Name`, `SpeciesName`, `PetId` |

## Boş/null search davranışı

`SearchContainsLikePattern` null ise boş id kümesi döner — command path arama yokken lookup
çağrılmaz. Boş `ClientIds` / `PetIds` koleksiyonları boş sonuç döner.

## Büyük sonuç seti

Command path'te `ListSearchPetIds` ve text search spec'lerinde **keyfi limit yoktur**. Lookup metotları
da limit uygulamaz; tüm eşleşen id'ler döner. Handler'a bağlanmadı; 12D-3'te parity ön-koşulu
zorunludur.

## Normalize/escape

Handler'lar `ListQueryTextSearch.Normalize` + `BuildContainsLikePattern` ile desen üretir; lookup
reader'lar hazır LIKE deseni bekler (liste reader ile aynı contract).

## Yapılmayanlar (bu faz)

- Handler routing / `ListSearchPetIds` değişikliği
- Yeni feature flag
- Health/parity/backfill/projection değişikliği
- Migration
- Otomatik fallback

## Sonraki adım — 12D-3

`GetExaminationsListQueryHandler` pilot: `SharedSearchLookupEnabled` bayrağı + `ListSearchPetIds`
Query DB lookup'a yönlendirme (bu fazda bayrak eklenmedi).

## Testler

- `ClientReadModelLookupReaderIntegrationTests`
- `PetReadModelLookupReaderIntegrationTests`
- Mevcut `ClientReadModelReaderIntegrationTests` / `PetReadModelReaderIntegrationTests` regresyon
