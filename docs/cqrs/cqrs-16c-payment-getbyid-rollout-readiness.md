# CQRS-16C — Payment GetById Rollout Readiness

**Tür:** Acceptance audit + rollout readiness dokümantasyonu. **Production kod, test, schema/migration, feature flag ve appsettings değişmedi.**

**Ön durum:**

- **CQRS-16A** — GetById Query DB route kararı onaylandı ([`cqrs-16a-payment-getbyid-read-model-decision.md`](cqrs-16a-payment-getbyid-read-model-decision.md)).
- **CQRS-16B** — `PaymentsGetByIdReadEnabled` flag, dedicated reader ve handler routing implementasyonu tamamlandı ([`cqrs-16b-payment-getbyid-read-model-route.md`](cqrs-16b-payment-getbyid-read-model-route.md)).

**İlgili dokümanlar:** [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md) · [`cqrs-15o-payment-search-read-model-rollout-readiness.md`](cqrs-15o-payment-search-read-model-rollout-readiness.md) · [`idor-regression.md`](../security/idor-regression.md)

**Git doğrulama (2026-06-23):**

| Kontrol | Sonuç |
|---|---|
| `git status --short` | **Temiz** (tracked değişiklik yok; bu faz yalnızca yeni doküman ekler) |
| CQRS-16B commit | `6f72702` — `feat(cqrs): route payment get by id through read model` |
| CQRS-16A commit | `70008aa` — `docs(cqrs): audit payment get by id read model route` |

---

## Current status

| Alan | Durum |
|---|---|
| Endpoint | `GET /api/v1/payments/{id}` |
| Handler | `GetPaymentByIdQueryHandler` |
| Flag | `QueryReadModels:PaymentsGetByIdReadEnabled` |
| Default | **false** (tüm ortamlar) |
| Query reader | `IPaymentGetByIdReadModelReader` → `PaymentGetByIdReadModelReader` (DI registered) |
| Schema/migration | Gerekmez (16A onaylı) |
| Targeted test set | **35/35 geçti** (27 Application + 8 Integration) |
| `dotnet build --no-restore` | **Başarılı** (0 uyarı, 0 hata) |

**16C audit sonucu:** Implementasyon rollout için teknik olarak hazır. Staging'de flag açmadan önce operasyonel gate'ler (projection, backfill, parity, health) zorunludur.

---

## Default flag state

| Dosya | `PaymentsGetByIdReadEnabled` |
|---|---|
| `appsettings.json` | **false** |
| `appsettings.Development.json` | **false** |
| `appsettings.Production.json` | **false** |
| `appsettings.Staging.json` | **false** |
| `appsettings.IntegrationTests.json` | **false** |
| `appsettings.LoadTest.json` | **false** |

**Doğrulama kanıtı:**

- Kaynak grep: 6/6 dosyada explicit `false`.
- Otomatik test: `PaymentsGetByIdReadRoutingOptionsTests` (6 theory case) — **geçti**.

**Production default davranış:** Flag kapalıyken handler `HandleCommandPathAsync` çalışır — `PaymentByIdSpec` + client/pet lookup; mevcut Command DB semantiği **değişmez**.

**Flag false iken Command DB path:** `PaymentGetByIdQueryHandlerFeatureFlagTests.WhenFlagFalse_Should_UseCommandDb_NotQueryReader` — Command repo `Once`, Query reader `Never` — **geçti**.

---

## Startup log visibility

`CqrsStartupConfigurationLogger.LogEffectiveConfiguration` startup'ta şu alanı yazar:

```text
PaymentsGetByIdReadEnabled={PaymentsGetByIdReadEnabled}
```

Tam log satırı diğer payment read flag'leriyle birlikte gelir (`PaymentsListReadEnabled`, `PaymentsReportReadEnabled`, …). PII/secret loglanmaz; `CommandDbCatalog` ve `QueryDbCatalog` ayrıca yazılır.

**Integration test kanıtı** (`PaymentDetailIdorIntegrationTests` factory startup):

```text
CQRS startup configuration. ... PaymentsGetByIdReadEnabled=False CommandDbCatalog=... QueryDbCatalog=...
```

Operasyon ekibi deploy/restart sonrası log satırından flag durumunu doğrudan okuyabilir.

---

## Query path acceptance

| Kontrol | Beklenen | Kanıt |
|---|---|---|
| Flag true → Query path | `HandleQueryPathAsync` seçilir | `PaymentGetByIdQueryHandlerFeatureFlagTests.WhenFlagTrue_AndReaderReturnsRow_*` |
| Command DB fallback yok | `IReadRepository<Payment>` çağrılmaz | Feature flag testleri + handler kodu (`HandleQueryPathAsync` Command repo kullanmaz) |
| Query row yok → NotFound | `Payments.NotFound` | `WhenFlagTrue_AndQueryDbEmpty_Should_ReturnNotFound_WithoutCommandFallback` |
| Reader exception propagate | Command fallback yok | `WhenFlagTrue_AndReaderThrows_Should_PropagateException_WithoutCommandFallback` |
| Cross-flag isolation | List/report/export flag'leri GetById'yi etkilemez | `PaymentGetByIdReadFlagIsolationTests` (2 test) |
| Reader tenant filter | `TenantId + PaymentId` | `PaymentGetByIdReadModelReader` + `PaymentReadModelReaderIntegrationTests.GetById_*` (3 test) |
| Structured log (Query hit) | PII yok | Handler: `"Payment detail generated from Query DB read model"` |

Query path'te `ClientName`/`PetName` projection'dan gelir; Command client/pet lookup **yapılmaz** (feature flag testinde `_clients`/`_pets` `Never`).

---

## IDOR / tenant-clinic scope acceptance

Auth mantığı Command ve Query path'te **paylaşılan** `TryGetClinicAccessFailureAsync` ile uygulanır:

| Kontrol | Davranış | Kanıt |
|---|---|---|
| TenantId + PaymentId lookup | Reader: `WHERE TenantId = @tenant AND PaymentId = @id` | `PaymentGetByIdReadModelReader` |
| Satır sonrası clinic access | Aktif clinic mismatch → `Payments.NotFound` | Feature flag + unit auth testleri |
| Tenant-wide kullanıcı | `UserClinic.Exists` atlanır | `WhenFlagTrue_AndTenantWideUser_*` |
| Non-tenant-wide atanmış | `UserClinic.Exists` true → 200 | `WhenFlagTrue_AndNonTenantWideAssignedClinic_*` |
| Non-tenant-wide atanmamış | `Payments.NotFound` (Forbidden değil) | `WhenFlagTrue_AndNonTenantWideUnassignedClinic_*` |
| Aktif clinic mismatch | `Payments.NotFound`; UserClinic çağrılmaz | `WhenFlagTrue_AndActiveClinicMismatch_*` |
| Cross-tenant | Reader null → `Payments.NotFound` | `GetById_Should_ReturnNull_When_CrossTenantPaymentId` |
| Permission layer | Handler öncesi policy → 403 | `PaymentDetailIdorIntegrationTests.GetById_Should_Return403_*` |

**Integration IDOR baseline (flag false, Command path):** `PaymentDetailIdorIntegrationTests` — 5 senaryo **geçti**:

- Assigned clinic → 200
- Unassigned same-tenant → 404 + `Payments.NotFound`
- Foreign tenant → 404 + `Payments.NotFound`
- Tenant admin cross-clinic → 200
- No `Payments.Read` permission → 403

**Bilinen test gap (kabul edildi):** HTTP integration testleri varsayılan `PaymentsGetByIdReadEnabled=false` ile Command path çalışır. Flag=true + projected row ile tam HTTP IDOR smoke **staging manuel adımı** olarak zorunlu (16A §Required tests önerisi).

Unit seviyesinde Query path auth parity feature flag testleriyle kapsanmıştır.

---

## No-fallback policy

Query path seçildiğinde (flag true):

```text
1. IPaymentGetByIdReadModelReader.GetByIdAsync(tenantId, paymentId)
2. null → Payments.NotFound (Command DB'ye düşülmez)
3. Reader throw → exception propagate (Command fallback yok)
4. Auth geçerse → PaymentDetailDto döner
5. IReadRepository<Payment> / PaymentByIdSpec / client/pet lookup ÇAĞRILMAZ
```

Bu politika list/report/export Query path sözleşmesiyle (15O) hizalıdır; tek fark GetById'de boş sonuç **boş liste değil**, tek kayıt için **NotFound**'dur.

---

## Required pre-rollout gates

Flag staging/production'da **true** yapılmadan önce tüm gate'ler geçmelidir:

| # | Gate | Doğrulama | Referans |
|---|---|---|---|
| 1 | **Payment projection enabled** | `PaymentProjection:Enabled=true`; outbox/processor çalışıyor | Staging appsettings + health |
| 2 | **Backfill tamam** | `backfill-payment-read-models` exit 0; count parity InSync | [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md) |
| 3 | **Parity temiz** | `PaymentReadModelParityEvaluator` InSync (count + sample + recent ordering) | 14F parity reader/evaluator |
| 4 | **Health temiz** | `PaymentProjectionHealth` / read-model drift degraded veya unhealthy **değil** | `PaymentReadModelHealthReader` |
| 5 | **Client/Pet projection** | Detail DTO `ClientName`/`PetName` projection'dan gelir; client/pet rename lag kabul edilir ama backfill/parity gate geçmeli | 15O §6 |
| 6 | **IDOR smoke (flag true)** | Staging'de projected payment ile assigned/unassigned/foreign tenant/cross-clinic senaryoları | Bu doküman §Smoke test checklist |

**Projection kapalı + flag açık riski:** Her GetById → 404 (Query row yok). Rollout checklist'te projection gate **blokör**.

**Drift tespit edilirse:** Flag açma; backfill yeniden çalıştır; parity InSync olana kadar bekle; gerekirse projection processor lag/incident çöz.

---

## Staging rollout steps

1. **Gate doğrula** — Projection ON, backfill InSync, parity InSync, health green.
2. **Mevcut davranış baseline** — Flag false iken staging smoke: bilinen payment ID → 200, detail alanları doğru.
3. **Config değiştir** — `QueryReadModels:PaymentsGetByIdReadEnabled=true` (Staging appsettings veya secret/config overlay).
4. **Deploy/restart** — API instance'ları yeniden başlat.
5. **Startup log doğrula** — `PaymentsGetByIdReadEnabled=True` satırını kontrol et.
6. **Smoke checklist** — Aşağıdaki maddeleri çalıştır.
7. **Gözlem** — 5–15 dk error rate, Query DB latency, `"Payment detail generated from Query DB read model"` log hacmi.
8. **Production** — Staging smoke temizse kademeli production aç (aynı gate + smoke).

**Not:** GetById flag'i list/report/export flag'lerinden bağımsızdır; yalnızca detail yüzeyini etkiler.

---

## Smoke test checklist

### A. Happy path (flag true, projected row mevcut)

- [ ] Tenant-wide admin, aktif klinik yok → tenant içi payment → **200**, DTO alanları (amount, currency, method, clientName, petName, notes) dolu
- [ ] Non-tenant-wide, atanmış klinik payment → **200**
- [ ] Tenant admin, farklı klinik payment (atanmamış olsa bile tenant-wide) → **200**

### B. IDOR / scope (flag true)

- [ ] Non-tenant-wide, atanmamış klinik payment → **404**, code `Payments.NotFound`
- [ ] Cross-tenant payment ID → **404**, code `Payments.NotFound`
- [ ] Aktif clinic context ≠ payment.ClinicId → **404**, code `Payments.NotFound`
- [ ] `Payments.Read` permission yok → **403**

### C. No-fallback / empty Query DB

- [ ] Command DB'de var, Query DB'de yok payment ID → **404** (Command fallback olmamalı)
- [ ] Bilinen projected payment → **200** (Query path aktif kanıtı)

### D. Detail parity (örneklem)

- [ ] Aynı payment ID için flag false (Command) vs flag true (Query) DTO alan karşılaştırması — en az 3 payment örneği
- [ ] Nullable pet, notes boş, farklı currency/method kombinasyonları

### E. Rollback doğrulama

- [ ] Flag false + restart → Command path; aynı payment ID **200** (Query row olsa bile Command'dan okunur)

---

## Rollback plan

| Adım | Aksiyon |
|---|---|
| 1 | `QueryReadModels:PaymentsGetByIdReadEnabled=false` yap |
| 2 | Deploy/restart |
| 3 | Startup log: `PaymentsGetByIdReadEnabled=False` doğrula |
| 4 | Smoke: bilinen payment → 200 (Command DB path) |

**Anında etki:** Handler `HandleCommandPathAsync`'e döner. Kod geri alımı veya migration gerekmez.

**Query DB boş/stale iken rollback sonrası:** Command DB truth'tan okuma devam eder; kullanıcı etkisi giderilir.

**Kısmi rollback:** Yalnız GetById flag'ini kapatmak list/report/export Query path'ini **etkilemez**.

---

## Known risks

| Risk | Etki | Azaltma |
|---|---|---|
| Projection lag / missing row | Flag açıkken kayıt Command'da var, Query'de yok → **404** | Parity gate + backfill; health monitoring |
| Eventual consistency on names | Client/pet rename projection gecikmesi | Parity monitoring; kabul edilen CQRS trade-off |
| Flag açık + projection kapalı | Her GetById NotFound | Rollout checklist gate #1 |
| HTTP IDOR test gap (flag true) | CI'da Command path baseline; Query path HTTP IDOR otomatik değil | Staging smoke §B zorunlu |
| Query DB outage | Reader exception → 5xx; Command fallback yok | Operasyonel alert; rollback flag false |
| GetById vs list scope farkı | List multi-clinic → Command fallback; GetById PK + post-load auth | Mevcut contract; dokümante, değiştirilmez |

---

## Final decision

| Soru | Karar |
|---|---|
| 16B implementasyonu rollout-ready mi? | **Evet** — flag, reader, handler, startup log, targeted tests doğrulandı |
| Production davranış bugün değişti mi? | **Hayır** — default false |
| Staging'de flag açılabilir mi? | **Evet — operasyonel gate'ler geçildikten sonra** |
| Production'da flag açılabilir mi? | **Staging smoke + parity/health gate sonrası** |

**Koşullu onay:** `PaymentsGetByIdReadEnabled=true` staging'e **gate checklist tamamlandığında** açılabilir. Gate'ler geçilmeden flag açmak, mevcut ödemeler için false-negative 404 riski taşır (no-fallback policy).

---

## Test acceptance (16C audit çalıştırması)

### Application.Tests

```powershell
dotnet test tests/Backend.Veteriner.Application.Tests --no-restore --filter "FullyQualifiedName~GetPaymentById|FullyQualifiedName~PaymentGetById|FullyQualifiedName~PaymentsGetById"
```

**Sonuç:** 27/27 geçti.

| Sınıf | Test sayısı |
|---|---|
| `PaymentsGetByIdReadRoutingOptionsTests` | 6 |
| `GetPaymentByIdQueryHandlerTests` | 11 |
| `PaymentGetByIdQueryHandlerFeatureFlagTests` | 8 |
| `PaymentGetByIdReadFlagIsolationTests` | 2 |

Filtre beklenen dışı ek test **yakalamadı**.

### Integration.Tests

```powershell
dotnet test tests/Backend.Veteriner.IntegrationTests --no-restore --filter "FullyQualifiedName~PaymentDetailIdorIntegrationTests|FullyQualifiedName~PaymentReadModelReaderIntegrationTests.GetById"
```

**Sonuç:** 8/8 geçti.

| Sınıf | Test sayısı |
|---|---|
| `PaymentDetailIdorIntegrationTests` | 5 |
| `PaymentReadModelReaderIntegrationTests.GetById_*` | 3 |

Filtre beklenen dışı ek test **yakalamadı**.

### Build

```powershell
dotnet build --no-restore
```

**Sonuç:** Başarılı — 0 uyarı, 0 hata.

---

## İlgili commit'ler

| Commit | Mesaj |
|---|---|
| `70008aa` | `docs(cqrs): audit payment get by id read model route` (16A) |
| `6f72702` | `feat(cqrs): route payment get by id through read model` (16B) |
