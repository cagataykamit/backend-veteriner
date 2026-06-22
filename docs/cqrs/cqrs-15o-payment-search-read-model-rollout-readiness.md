# CQRS-15O — Payment search Query DB rollout final readiness

**Tür:** Audit + test hardening + rollout readiness dokümantasyonu. **Production behavior, schema/migration, projection/backfill/parity değişmedi.**

**Ön durum:**

- **CQRS-15L** — Payment list search dolu + `PaymentsListReadEnabled=true` + single clinic → Query DB; multi-clinic → Command DB fallback; Query path no fallback.
- **CQRS-15M** — Payment report JSON search dolu + `PaymentsReportReadEnabled=true` + single clinic veya tenant-wide → Query DB; multi-clinic → Command DB fallback; Query path no fallback.
- **CQRS-15N** — Payment export CSV/XLSX search dolu + `PaymentsReportExportReadEnabled=true` + single clinic veya tenant-wide → Query DB; multi-clinic → Command DB fallback; Query path no fallback; 50k limit search-filtered count üzerinden korunuyor.

**İlgili dokümanlar:** [`cqrs-15l-payment-list-search-read-model-route.md`](cqrs-15l-payment-list-search-read-model-route.md) · [`cqrs-15m-payment-report-search-read-model-route.md`](cqrs-15m-payment-report-search-read-model-route.md) · [`cqrs-15n-payment-export-search-read-model-route.md`](cqrs-15n-payment-export-search-read-model-route.md) · [`cqrs-15k-payment-search-parity-gap-audit.md`](cqrs-15k-payment-search-parity-gap-audit.md)

---

## 1. Özet

Payment list, report JSON ve export CSV/XLSX yüzeylerinde search destekli Query DB routing tamamlandı. Üç bağımsız feature flag ile kademeli rollout mümkün. Tüm ortam `appsettings*.json` dosyalarında flag'ler **explicit false**; startup log üç flag'i de yazar. Cross-flag interference yok — her yüzey yalnızca kendi flag'ini okur.

**15O audit sonucu:** Rollout için teknik hazırlık yeterli. Ön-koşul: Payment + Client + Pet projection backfill/parity doğrulaması.

---

## 2. Kapsanan surfaces

| Surface | Route | Handler | Flag |
|---|---|---|---|
| Payment list | `GET /api/v1/payments` | `GetPaymentsListQueryHandler` | `PaymentsListReadEnabled` |
| Report JSON | `GET /api/v1/reports/payments` | `GetPaymentsReportQueryHandler` | `PaymentsReportReadEnabled` |
| Export CSV | `GET /api/v1/reports/payments/export` | `ExportPaymentsReportQueryHandler` | `PaymentsReportExportReadEnabled` |
| Export XLSX | `GET /api/v1/reports/payments/export/xlsx` | `ExportPaymentsReportXlsxQueryHandler` | `PaymentsReportExportReadEnabled` |

Export CSV/XLSX ortak `PaymentsReportExportPipeline` kullanır — routing davranışı birebir aynıdır.

---

## 3. Flag matrix

| Flag | Default | Etkilediği yüzey | Etkilemediği yüzeyler |
|---|---|---|---|
| `PaymentsListReadEnabled` | **false** | Payment list | Report JSON, export CSV/XLSX |
| `PaymentsReportReadEnabled` | **false** | Report JSON | Payment list, export CSV/XLSX |
| `PaymentsReportExportReadEnabled` | **false** | Export CSV/XLSX | Payment list, report JSON |

**Bağımsız lookup flag (Query path'i etkilemez):**

| Flag | Query path'te rol |
|---|---|
| `PaymentsSearchLookupEnabled` | Yalnız Command DB path'te lookup kaynağını seçer. Query path her zaman Query DB lookup reader kullanır. |
| `SharedSearchLookupEnabled` | Client/pet list search lookup; payment yüzeylerinden bağımsız. |

**Explicit false olan ortamlar (6 dosya):**

- `appsettings.json`
- `appsettings.Development.json`
- `appsettings.Production.json`
- `appsettings.Staging.json`
- `appsettings.IntegrationTests.json`
- `appsettings.LoadTest.json`

**Startup log:** `CqrsStartupConfigurationLogger` → `PaymentsListReadEnabled={...}`, `PaymentsReportReadEnabled={...}`, `PaymentsReportExportReadEnabled={...}` (PII/secret loglanmaz).

---

## 4. Search behavior matrix

### 4.1 Payment list (`PaymentsListReadEnabled`)

| Search | Scope | Kaynak |
|---|---|---|
| boş/whitespace | single clinic | **Query DB** (flag true) |
| dolu | single clinic | **Query DB** (15L) |
| boş veya dolu | multi-clinic | **Command DB** fallback |
| herhangi | tenant-wide (clinic yok) | **Hata** (`Payments.ClinicScopeRequired`) — değişmedi |

### 4.2 Report JSON (`PaymentsReportReadEnabled`)

| Search | Scope | Kaynak |
|---|---|---|
| boş/whitespace | single clinic veya tenant-wide | **Query DB** (15G) |
| dolu | single clinic veya tenant-wide | **Query DB** (15M) |
| boş veya dolu | multi-clinic | **Command DB** fallback |

### 4.3 Export CSV/XLSX (`PaymentsReportExportReadEnabled`)

| Search | Scope | Kaynak |
|---|---|---|
| boş/whitespace | single clinic veya tenant-wide | **Query DB** (15J) |
| dolu | single clinic veya tenant-wide | **Query DB** (15N) |
| boş veya dolu | multi-clinic | **Command DB** fallback |

### 4.4 Query path ortak kurallar (tüm yüzeyler)

- Command DB Count/List/SearchResolution **çağrılmaz**.
- Query lookup/reader throw → exception propagate, **fallback yok**.
- Query DB no matches → boş sonuç (liste/rapor/export).
- `PaymentsSearchLookupEnabled` Query path'i **etkilemez**.

---

## 5. Search parity

Query DB search OR mantığı (15L/15M/15N ortak):

| Kaynak | Alanlar | Parity notu |
|---|---|---|
| **Direct** (`PaymentReadModels`) | `ClientNameNormalized`, `PetNameNormalized`, `NotesNormalized`, `Currency` | Client/pet name, notes, currency doğrudan LIKE |
| **Client lookup** → `ClientId IN (...)` | FullName, Email, Phone, PhoneNormalized | Email/phone PaymentReadModel'e eklenmedi; lookup ID filtre |
| **Pet lookup** → `PetId IN (...)` | Name, Breed, SpeciesName, BreedRefName | Species/breed PaymentReadModel'e eklenmedi; lookup ID filtre |

Search resolution: `PaymentsListQuerySearchResolution.ResolveSearchIdsAsync` → `IClientReadModelLookupReader` + `IPetReadModelLookupReader` (tenant-only; clinic izolasyonu reader filtrelerinde).

Command path (`flag=false`): mevcut `PaymentsListSearchResolution` / `PaymentsReportSearchResolution`; isteğe bağlı `PaymentsSearchLookupEnabled` ile Query lookup.

---

## 6. Known risks

| Risk | Etki | Azaltma |
|---|---|---|
| Client/Pet lookup read model stale | Eksik search sonucu (email/phone/species/breed) | Client/Pet projection parity + health; rollout öncesi backfill doğrula |
| Query path no fallback | Query DB boş/stale → boş sonuç veya hata; Command DB'ye dönmez | Parity gate; health evaluator; kademeli flag açma |
| Multi-clinic Query search yok | ClinicAdmin (aktif klinik yok) → Command DB fallback | Bilinçli tasarım; sonraki faz |
| Export memory/streaming | Tüm satırlar belleğe; 50k tavan korunuyor ama büyük export OOM riski | Operasyonel limit; streaming/paged export sonraki faz |
| Payment projection kapalı (`PaymentProjection:Enabled=false`) | Query path boş döner | Rollout öncesi projection + backfill + parity |

---

## 7. Rollout önerisi

1. **Backfill/parity doğrula** — Payment + Client + Pet read model count drift InSync; `PaymentProjectionHealth` degraded/unhealthy değil.
2. **`PaymentsListReadEnabled=true`** — Staging'de single-clinic search smoke (direct + lookup alanları); production'da aynı sıra.
3. **`PaymentsReportReadEnabled=true`** — Report JSON totalCount/totalAmount + search smoke; finansal toplam parity kritik.
4. **`PaymentsReportExportReadEnabled=true`** — CSV/XLSX export search smoke; 50k limit senaryosu.
5. **Manuel/API smoke** — Aşağıdaki search senaryolarını tenant/clinic izolasyonu ile doğrula.

Her adımda startup log'dan flag değerlerini doğrula.

---

## 8. Rollback

| Adım | Aksiyon |
|---|---|
| 1 | İlgili flag'i `false` yap (`QueryReadModels` section) |
| 2 | Deploy/restart |
| 3 | Startup log'da `Payments*ReadEnabled=False` doğrula |
| 4 | İstekler anında Command DB path'e döner — kod geri alımı gerekmez |

Kısmi rollback mümkün: örn. yalnız export flag'ini kapatmak list/report Query path'ini etkilemez.

---

## 9. Test coverage özeti (15O hardening)

| Alan | Test sınıfları |
|---|---|
| Flag default false | `PaymentsListReadRoutingOptionsTests`, `PaymentsReportReadRoutingOptionsTests`, `PaymentsReportExportReadRoutingOptionsTests`, `PaymentListRolloutAcceptanceIntegrationTests` |
| List routing + search | `PaymentListQueryHandlerFeatureFlagTests`, `PaymentReadModelReaderIntegrationTests` |
| Report routing + search | `PaymentsReportReadRoutingTests` |
| Export routing + search + 50k | `PaymentsReportExportReadRoutingTests`, `PaymentsReportExportPipelineTests` |
| Cross-flag isolation | `PaymentSearchQueryReadFlagIsolationTests`, `PaymentsReportExportScopeGuardTests`, `PaymentsReportExportReadRoutingTests` |
| Lookup resolution | `PaymentsListQuerySearchResolutionTests`, `PaymentsListSearchResolutionTests` |
| Command path lookup flag | `GetPaymentsListQueryHandlerPaymentsSearchLookupFeatureFlagTests`, `GetPaymentsReportQueryHandlerPaymentsSearchLookupFeatureFlagTests`, `ExportPaymentsReportPaymentsSearchLookupFeatureFlagTests` |

---

## 10. Opsiyonel sonraki fazlar

- Shared resolver rename (`PaymentsListQuerySearchResolution` → `PaymentsQuerySearchResolution`) — ayrı küçük refactor
- Multi-clinic Query search tasarımı
- Export streaming/paged writer tasarımı
- Search lookup freshness/health metric

---

## 11. Manuel test (referans)

```bash
dotnet build --no-restore
dotnet test --no-restore --filter "FullyQualifiedName~PaymentList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsList"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReport"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportRead"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportExport"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsReportExportRead"
dotnet test --no-restore --filter "FullyQualifiedName~ExportPaymentsReport"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentsSearchLookup"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentSearchQueryReadFlagIsolation"
```

---

## 12. Garanti

Production behavior değişmedi. Schema/migration/projection/backfill/parity değişmedi. Commit/test bu fazda çalıştırılmadı.
