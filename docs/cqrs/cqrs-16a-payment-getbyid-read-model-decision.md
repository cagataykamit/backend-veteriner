# CQRS-16A — Payment GetById Read-Model Decision Audit

**Tür:** Audit + karar dokümantasyonu. **Production kod, test, schema/migration, feature flag ve commit değişmedi.**

**Ön durum:** CQRS-15O tamamlandı kabul edilir — payment list, report JSON, export CSV/XLSX ve search parity Query DB tarafında tamam; IDOR / clinic isolation roadmap kapalı.

**İlgili dokümanlar:** [`cqrs-15a-payment-read-surface-audit.md`](cqrs-15a-payment-read-surface-audit.md) · [`cqrs-15o-payment-search-read-model-rollout-readiness.md`](cqrs-15o-payment-search-read-model-rollout-readiness.md) · [`cqrs-14b-payment-read-model-schema.md`](cqrs-14b-payment-read-model-schema.md) · [`cqrs-14d-payment-read-model-reader.md`](cqrs-14d-payment-read-model-reader.md) · [`idor-regression.md`](../security/idor-regression.md)

**Git doğrulama (2026-06-23):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** (tracked değişiklik yok) |
| CQRS-15O commit kanıtı | `7191426` — `test(cqrs): verify payment search rollout readiness` |
| Export search commit | `de383d1` — `feat(cqrs): enable payment export search through read model` |
| Son 10 commit | Payment write clinic assignment + IDOR checklist dahil; 15L–15N search route commit'leri history'de mevcut |

---

## Current Command DB behavior

### Endpoint ve handler

| Alan | Değer |
|---|---|
| **Endpoint** | `GET /api/v1/payments/{id}` |
| **Controller** | `PaymentsController.GetById` — tenant resolve (`TryGetResolvedTenant`), ardından `GetPaymentByIdQuery` |
| **Handler** | `GetPaymentByIdQueryHandler` |
| **Permission** | `PermissionCatalog.Payments.Read` |
| **DTO** | `PaymentDetailDto` |

### Okuma akışı (Command DB)

```text
1. TenantId yok → Tenants.ContextMissing
2. UserId yok → Auth.Unauthorized.UserContextMissing
3. PaymentByIdSpec(tenantId, id) → IReadRepository<Payment>
4. Satır yok → Payments.NotFound
5. Aktif clinic context varsa ve payment.ClinicId != context.ClinicId → Payments.NotFound (Forbidden değil)
6. TenantWideClaimNames.IsTenantWide(claims) değilse:
     UserClinicRepository.ExistsAsync(userId, payment.ClinicId) → false ise Payments.NotFound
7. ClientByIdSpec → ClientName (boş string fallback)
8. PetId varsa PetByIdSpec → PetName (boş string fallback)
9. PaymentDetailDto map → Success
```

### List/report ile farklar (kritik)

| Konu | GetById (bugün) | List (15L) | Report/export (15G–15N) |
|---|---|---|---|
| `IClinicReadScopeResolver` | **Kullanılmıyor** | Zorunlu | Zorunlu (`PaymentsReportQueryValidation`) |
| Aktif klinik yok | Tenant-wide okuyabilir; non-tenant-wide → UserClinic | `Payments.ClinicScopeRequired` hata | Tenant-wide veya single clinic Query; multi-clinic Command fallback |
| Yetkisiz erişim kodu | **Payments.NotFound** (IDOR maskeleme) | Scope hatalarında `Clinics.AccessDenied` / `Payments.ClinicScopeRequired` | Validation scope hataları aynı |
| Multi-clinic (ClinicAdmin, aktif klinik yok) | PK okuma + UserClinic.Exists — **desteklenir** | Query path yok → Command fallback | Query path yok → Command fallback |

GetById, ID bazlı tek satır okuma olduğu için multi-clinic scope'u sorgu filtresinde temsil etmek zorunda değildir; yükleme sonrası yetkilendirme yeterlidir.

### Mevcut test kapsamı

| Test | Kapsam |
|---|---|
| `GetPaymentByIdQueryHandlerTests` | Tenant missing, NotFound, tenant-wide (Admin/Owner/PlatformAdmin), UserClinic assignment, aktif clinic mismatch, cancellation token |
| `PaymentDetailIdorIntegrationTests` | Assigned 200, unassigned 404 + `Payments.NotFound`, foreign tenant 404, tenant admin cross-clinic 200, permission 403 |

---

## Current PaymentReadModel capability

### Entity (`PaymentReadModel`)

Tablo: `PaymentReadModels`. PK: `PaymentId`. Projection: `PaymentProjectionProcessor` + `PaymentProjectionSnapshot`.

| Alan | Tip | Detail DTO'da kullanım |
|---|---|---|
| `PaymentId` | `Guid` | `Id` |
| `TenantId` | `Guid` | `TenantId` |
| `ClinicId` | `Guid` | `ClinicId` |
| `ClinicName` | `string` | DTO'da yok (fazladan; route blokörü değil) |
| `ClientId` | `Guid` | `ClientId` |
| `ClientName` | `string` | `ClientName` (denormalize — Command client lookup gerekmez) |
| `PetId` | `Guid?` | `PetId` |
| `PetName` | `string?` | `PetName` → `?? string.Empty` |
| `AppointmentId` | `Guid?` | `AppointmentId` |
| `ExaminationId` | `Guid?` | `ExaminationId` |
| `Amount` | `decimal` | `Amount` |
| `Currency` | `string` | `Currency` |
| `Method` | `int` | `(PaymentMethod)Method` |
| `PaidAtUtc` | `DateTime` | `PaidAtUtc` |
| `Notes` | `string?` | `Notes` |

Search/normalize ve projection metadata alanları (`*Normalized`, `LastEventId`, …) detail DTO için gerekli değildir.

### Mevcut reader altyapısı

| Reader | Amaç | GetById |
|---|---|---|
| `IPaymentsListReadModelReader` / `PaymentsListReadModelReader` | Paged list + search | PK lookup yok |
| `IPaymentsReportReadModelReader` | Report JSON aggregate + page | PK lookup yok |
| `IPaymentsReportExportReadModelReader` | Export ordered rows | PK lookup yok |
| `IClientPaymentSummaryReadModelReader` | Client aggregate + recent | PK lookup yok |
| `IPaymentReadModelParityReader` | Parity drift | Operasyonel |

**GetById için dedicated reader/interface bugün yok** — 16B'de eklenecek.

### Index yeterliliği

PK (`PaymentId`) tek satır lookup için yeterli. Ek migration/index gerekmez. Tenant izolasyonu handler/reader filtresinde `TenantId == request.TenantId` ile korunur (PK global unique).

---

## DTO field parity

### `PaymentDetailDto` ↔ `PaymentReadModel`

| `PaymentDetailDto` | `PaymentReadModel` | Parity |
|---|---|---|
| `Id` | `PaymentId` | **yes** |
| `TenantId` | `TenantId` | **yes** |
| `ClinicId` | `ClinicId` | **yes** |
| `ClientId` | `ClientId` | **yes** |
| `ClientName` | `ClientName` | **yes** (Query path'te Command lookup'tan daha iyi) |
| `PetId` | `PetId` | **yes** |
| `PetName` | `PetName` | **yes** |
| `AppointmentId` | `AppointmentId` | **yes** |
| `ExaminationId` | `ExaminationId` | **yes** |
| `Amount` | `Amount` | **yes** |
| `Currency` | `Currency` | **yes** |
| `Method` | `Method` | **yes** (enum cast) |
| `PaidAtUtc` | `PaidAtUtc` | **yes** |
| `Notes` | `Notes` | **yes** |

**Sonuç:** Eksik alan yok. CQRS-15C'deki `ClinicName` blokörü GetById için geçerli değil — `PaymentDetailDto` clinic adı taşımıyor.

**Eventual consistency notu:** Query path'te `ClientName`/`PetName` projection anlık görüntüsünden gelir; Command path anlık Command DB join'inden. Parity/backfill gate rollout ön koşulu (15O §7 ile aynı).

---

## Tenant / clinic scope analysis

### Mevcut Command DB scope matrisi

| Kullanıcı / context | Davranış | Query path'te korunabilir mi? |
|---|---|---|
| Tenant-wide (Admin/Owner/PlatformAdmin), aktif klinik yok | Tenant içi herhangi payment | **yes** — `TenantId + PaymentId` lookup, UserClinic atlanır |
| Tenant-wide, aktif klinik A | Payment clinic A → 200; clinic B → **NotFound** | **yes** — adım 5 aynı |
| Non-tenant-wide, atanmış klinik | UserClinic.Exists true → 200 | **yes** — satır yüklendikten sonra Exists kontrolü |
| Non-tenant-wide, atanmamış klinik | **NotFound** (Forbidden değil) | **yes** |
| Non-tenant-wide, aktif klinik yok, atanmadığı klinik payment'ı | **NotFound** (test: `Handle_Should_ReturnNotFound_When_NonTenantWideUserNotAssignedToPaymentClinic_WithoutActiveClinicContext`) | **yes** — UserClinic.Exists false |
| Cross-tenant ID | Spec/reader tenant filtresi → **NotFound** | **yes** — `TenantId` filtresi zorunlu |
| Permission yok | Controller policy → **403** | **yes** — routing handler öncesi |

### List/report'tan farklı güvenli scope

GetById Query path **multi-clinic non-tenant-wide** senaryosunda da güvenli olabilir — list/report'taki `TryGetRepresentableQueryClinicScope` / Command fallback **gerekmez**. Sebep: tek ID lookup; erişim kararı satırın `ClinicId` değerine göre post-load verilir.

### `ClinicReadScopeResolver` kullanılmıyor — risk değerlendirmesi

GetById bugün resolver kullanmaz; bunun yerine inline `TenantWideClaimNames` + `UserClinic.Exists` + aktif clinic eşleşmesi vardır. IDOR integration testleri bu semantiği doğrular. 16B'de Query route eklerken **mevcut inline kontroller korunmalı**; resolver'a geçiş zorunlu değil (scope refactor non-goal).

---

## IDOR risk analysis

### Kapalı pattern referansı

| Bileşen | GetById'deki rol |
|---|---|
| `TenantWideClaimNames` | Handler içinde inline — tenant-wide bypass |
| `ClinicAssignmentAccessGuard` | Doğrudan kullanılmıyor; semantik `UserClinic.Exists` ile eşdeğer |
| `ClinicReadScopeResolver` | Kullanılmıyor — GetById list scope modelinden farklı |

### IDOR vektörleri (Query path)

| Vektör | Mitigasyon |
|---|---|
| Cross-tenant ID enumeration | Reader: `WHERE TenantId = @tenant AND PaymentId = @id` |
| Cross-clinic (atanmamış) | Post-load `UserClinic.Exists` → NotFound |
| Aktif clinic mismatch | Post-load `clinicContext.ClinicId` karşılaştırması → NotFound |
| Tenant-wide cross-clinic (aktif klinik varken) | Post-load clinic context mismatch → NotFound |
| Information disclosure via 403 vs 404 | Mevcut: yetkisiz kaynak **404 + Payments.NotFound** — korunmalı |
| Stale projection ile var olan payment | Query row yok → NotFound (aşağıda kabul edilir) |
| Reader exception mid-request | Exception propagate, Command fallback yok (15O policy) |

### Mevcut testlerin Query flag sonrası geçerliliği

`PaymentDetailIdorIntegrationTests` integration DB'de Command path ile çalışır. 16B'de flag=true + projected row senaryoları **ek** test gerektirir; mevcut IDOR testleri regression baseline olarak korunur (flag default false).

---

## Feature flag decision

### Karar: **Ayrı flag gerekli — `PaymentsGetByIdReadEnabled`**

| Gerekçe | Açıklama |
|---|---|
| Mevcut pattern | Her payment read yüzeyinin bağımsız flag'i var (`PaymentsListReadEnabled`, `PaymentsReportReadEnabled`, `PaymentsReportExportReadEnabled`, …) |
| Cross-flag isolation | List flag açıkken GetById Command'da kalabilmeli; tersi de geçerli |
| Kademeli rollout | Detail eventual consistency riski list/report'tan bağımsız değerlendirilmeli |
| Startup observability | `CqrsStartupConfigurationLogger`'a yeni alan eklenmeli (16B) |

**Kullanılmaması gereken flag'ler:** `PaymentsListReadEnabled`, `PaymentsReportReadEnabled`, `PaymentsReportExportReadEnabled` — GetById routing'i bunları okumamalı.

**Default:** `false` (6× `appsettings*.json` explicit false — 16B).

**Mevcut startup log (GetById flag yok):** `PaymentsListReadEnabled`, `PaymentsReportReadEnabled`, `PaymentsReportExportReadEnabled` loglanıyor; GetById flag'i henüz tanımlı değil.

---

## Schema / migration decision

### Karar: **Schema/migration gerekmez**

| Kontrol | Sonuç |
|---|---|
| Detail DTO alanları tabloda var mı? | **Evet** (§DTO field parity) |
| PK lookup index | `PaymentId` PK mevcut |
| Yeni kolon / normalize alan | Gerek yok |
| ClinicName enrichment (15D) | Zaten projection'da; DTO kullanmıyor |

16A fazında migration oluşturulmadı (kısıt).

---

## Query DB route feasibility

### Karar soruları — cevaplar

| # | Soru | Cevap |
|---|---|---|
| 1 | PaymentReadModel GetById detail DTO için yeterli mi? | **Evet** |
| 2 | Eksik alan var mı? | **Hayır** |
| 3 | Schema/migration gerekiyor mu? | **Hayır** |
| 4 | Ayrı `PaymentsGetByIdReadEnabled` flag gerekli mi? | **Evet** |
| 5 | Query path hangi scope'larda güvenli? | **Tüm mevcut GetById scope'ları** — PK lookup + mevcut post-load auth (tenant-wide, aktif clinic, UserClinic, cross-tenant). Multi-clinic fallback **gerekmez**. |
| 6 | Normal kullanıcı aktif clinic yokken atanmadığı clinic payment'ını okuyabilir mi? | **Hayır** — `UserClinic.Exists` false → NotFound; Query path'te aynı kontrol korunmalı. |
| 7 | Yetkisiz/cross-clinic/cross-tenant NotFound/Forbidden semantiği korunabilir mi? | **Evet** — `Payments.NotFound` + permission 403; `Clinics.AccessDenied` GetById'de bugün yok, eklenmemeli. |
| 8 | Query path seçildiğinde fallback olmaması kabul edilebilir mi? | **Evet** — 15O list/report/export ile aynı operasyonel sözleşme; parity/backfill gate ile yönetilir. |
| 9 | Query DB empty olduğunda NotFound doğru mu? | **Evet** — tek kayıt yüzeyi; boş liste değil. Projection lag'de kullanıcı kaydı bulamaz (eventual consistency). Rollout öncesi parity zorunlu. |
| 10 | Reader exception → fallback olmadan propagate? | **Evet** — list/report/export Query path policy ile hizalı. |

### Önerilen Query path akışı (16B tasarım — implementasyon değil)

```text
PaymentsGetByIdReadEnabled == false → mevcut Command DB path (değişmez)

PaymentsGetByIdReadEnabled == true:
  1. TenantId / UserId doğrulama (aynı)
  2. IPaymentGetByIdReadModelReader.GetByIdAsync(tenantId, paymentId)
  3. null → Payments.NotFound
  4. Mevcut adım 5–6 auth (clinic context + TenantWide + UserClinic) — satır ClinicId üzerinden
  5. PaymentReadModel → PaymentDetailDto map (Command client/pet lookup YOK)
  6. Command IReadRepository<Payment> çağrılmaz
```

---

## Required implementation plan if approved

**Onay koşulu:** Payment + Client + Pet projection backfill/parity InSync; `PaymentProjectionHealth` degraded değil (15O §7).

### 16B kapsamı (önerilen)

| # | Bileşen | Dosya / konum |
|---|---|---|
| 1 | `QueryReadModelsOptions.PaymentsGetByIdReadEnabled` | `QueryReadModelsOptions.cs` |
| 2 | 6× appsettings explicit `false` | `appsettings*.json` |
| 3 | Startup log alanı | `CqrsStartupConfigurationLogger.cs` |
| 4 | `IPaymentGetByIdReadModelReader` + `PaymentGetByIdReadRequest`/`Result` | `Application/Payments/ReadModels/` |
| 5 | `PaymentGetByIdReadModelReader` | `Infrastructure/Query/Payments/` |
| 6 | DI registration | `Infrastructure/DependencyInjection.cs` |
| 7 | `GetPaymentByIdQueryHandler` routing + map | Mevcut auth blokları **aynen**; yalnız veri kaynağı değişir |
| 8 | Structured log | `"Payment detail generated from Query DB read model"` (PII yok) |

### Bilinçli non-goals (16B)

- `ClinicReadScopeResolver`'a GetById refactor
- `PaymentDetailDto` contract değişikliği (ClinicName ekleme)
- Command fallback Query path'te
- Mevcut payment flag'lerinin GetById routing'ine bağlanması
- Schema/migration

### Rollout / rollback

1. Parity gate geç
2. Staging'de `PaymentsGetByIdReadEnabled=true` — IDOR smoke + detail parity
3. Production kademeli aç
4. Rollback: flag `false` + restart → anında Command DB (15O rollback modeli)

---

## Required tests for CQRS-16B

| Alan | Önerilen test sınıfı / senaryo |
|---|---|
| Flag default false | `PaymentsGetByIdReadRoutingOptionsTests` (list routing test pattern) |
| Flag true → Query reader | `PaymentGetByIdQueryHandlerFeatureFlagTests` — Command repo Never, reader Once |
| Flag false → Command repo | reader Never |
| Auth parity (unit) | Mevcut `GetPaymentByIdQueryHandlerTests` genişlet — Query path mock reader ile tenant-wide, UserClinic, clinic mismatch, NotFound |
| Cross-flag isolation | `PaymentGetByIdReadFlagIsolationTests` — list/report flag true iken GetById Command; GetById flag yalnız kendi option'ını okur |
| Reader integration | `PaymentGetByIdReadModelReaderIntegrationTests` — tenant filter, PK hit/miss |
| IDOR (flag true) | `PaymentDetailIdorIntegrationTests` duplicate veya factory flag override — projected payment ile assigned/unassigned/foreign tenant |
| Query exception propagate | Reader throw → handler exception, Command repo Never |
| Projection empty | Flag true, row yok → NotFound + `Payments.NotFound` |
| Regression | Mevcut `PaymentDetailIdorIntegrationTests` flag false ile green kalmalı |

**Mevcut testler 16A'da değiştirilmedi.**

---

## Risks and non-goals

### Riskler

| Risk | Etki | Azaltma |
|---|---|---|
| Projection lag / missing row | Flag açıkken kayıt Command'da var, Query'de yok → **404** | Parity evaluator + backfill; health gate; kademeli rollout |
| Eventual consistency on names | Client/pet rename projection gecikmesi | Parity monitoring; kabul edilen CQRS trade-off |
| Flag açık + projection kapalı | Her GetById NotFound | Rollout checklist; startup log |
| GetById list scope farkı | Operatörler list'te `ClinicScopeRequired`, GetById'de tenant-wide OK | Mevcut contract; dokümante edildi, değiştirilmez |
| Inline auth vs resolver drift | Gelecekte resolver değişirse GetById farklı kalabilir | IDOR testleri; isteğe bağlı future align (P3) |

### Non-goals (16A + önerilen 16B)

- Production/test kod değişikliği (16A)
- GetById için search routing (N/A)
- Multi-clinic Query list/report tasarımı (ayrı faz)
- Strong consistency garantisi Query path'te
- `PaymentDetailDto`'ya `ClinicName` ekleme

---

## Final decision

| Soru | Karar |
|---|---|
| Query DB GetById route yapılabilir mi? | **Evet — onaylandı (16B implementasyon)** |
| PaymentReadModel yeterli mi? | **Evet — tam DTO parity** |
| Schema/migration? | **Gerekmez** |
| Ayrı flag? | **Evet — `PaymentsGetByIdReadEnabled`** |
| Güvenlik | Mevcut inline auth + IDOR semantiği Query path'te korunursa **güvenli**; multi-clinic fallback **gerekmez** |
| Fallback policy | Query seçilince **fallback yok**; empty → **NotFound**; reader throw → **propagate** |

**CQRS-16B'ye geçiş:** **Onaylı.** Ön koşul: 15O parity/backfill operasyonel gate (değişmedi).

**Önerilen 16B scope özeti:** Yeni flag + dedicated GetById reader + handler routing + test hardening + startup log; auth mantığı ve API contract değişmez; schema yok.

---

## Doğrulama (16A faz sonu)

```bash
git status --short
dotnet build --no-restore
```

Sonuçlar çıktı raporunda özetlenir.
