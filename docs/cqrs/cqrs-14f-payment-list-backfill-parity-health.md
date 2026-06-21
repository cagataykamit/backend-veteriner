# CQRS-14F — Payment list read-model backfill + parity + health

**Tür:** Mevcut Command DB ödeme verisinin Query DB `PaymentReadModels` (list/search read-model) tablosuna güvenli backfill'i, Command ↔ Query liste parity'si ve read-model drift health sinyali.

**Production read davranışı değişmedi:** `GetPaymentsListQueryHandler` routing'i değiştirilmedi. `QueryReadModels:PaymentsListReadEnabled` default **`false`** (tüm ortam appsettings). Bu faz yalnızca rollout altyapısı ekler; flag bu altyapı doğrulanmadan açılmamalıdır.

**Ön durum:** 14B — `PaymentReadModels` tablo/migration; 14C — projection upsert + snapshot enrichment; 14D — `IPaymentsListReadModelReader`; 14E — flag-kontrollü list routing.

**İlgili dokümanlar:**

- [`cqrs-13d-payment-finance-backfill-health-parity.md`](cqrs-13d-payment-finance-backfill-health-parity.md)
- [`cqrs-14c-payment-read-model-projection.md`](cqrs-14c-payment-read-model-projection.md)
- [`cqrs-14e-payment-list-routing.md`](cqrs-14e-payment-list-routing.md)

---

## 1. Bu fazda ne eklendi?

| Bileşen | Açıklama |
|---|---|
| `PaymentReadModelBackfillService` | Command DB `Payments` → Query DB `PaymentReadModels` idempotent upsert (CQRS-14F) |
| `PaymentReadModelBackfillPlanner` / `PaymentReadModelBackfillAction` | Saf stale-guard karar mantığı (Insert/Update/SkipStale) |
| `PaymentReadModelBackfillResult` | Backfill sonucu (sayım/parity/süre; PII yok) |
| `backfill-payment-read-models` | DbMigrator komutu (`--batch-size`, `--tenant`); parity mismatch → exit code 2 |
| `IPaymentReadModelParityReader` + `PaymentReadModelParityReader` + `PaymentReadModelParityEvaluator` | Count + row-sample + recent-ordering parity (tenant + clinic kapsamlı) |
| `PaymentReadModelHealthSignal` + `PaymentReadModelHealthReader` | `PaymentReadModels` count drift sinyali |
| `PaymentProjectionHealthEvaluator` (genişletildi) | Mevcut `payment-projection` health entry'sine read-model drift boyutu eklendi (opsiyonel sinyal) |
| Testler | Backfill idempotency/izolasyon/nullable pet/normalize; parity InSync/OutOfSync; health drift severity |
| Bu doküman | Rollout/rollback sırası + manuel test listesi |

**Yapılmadı (kapsam dışı):** `GetPaymentsListQueryHandler` routing değişikliği, `PaymentsListReadEnabled` default true, search parity için yeni kolon/lookup, report/export taşıma, dashboard recent payments taşıma, frontend, migration, `PaymentReadModel` schema değişikliği.

---

## 2. Backfill komutu ve nasıl çalıştırılacağı

```powershell
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --batch-size 500
dotnet run --project src/Backend.Veteriner.DbMigrator -- backfill-payment-read-models --tenant 00000000-0000-0000-0000-000000000000
```

Akış:

```text
backfill-payment-read-models [--tenant <guid>] [--batch-size 500]
  → PaymentReadModelBackfillService.BackfillAsync
      → EnsureDistinctDatabasesAsync (Command DB ve Query DB farklı katalog olmalı)
      → Command DB Payments batch okuma (TenantId, Id sıralı)
      → Her batch için Clients (zorunlu) + Pets (nullable) Command DB'den join
      → Query DB transaction (batch):
          PaymentReadModels upsert (PK PaymentId, stale guard)
      → Count parity (Command Payments vs Query PaymentReadModels)
  → Count parity in-sync değilse exit code 2 (operatör uyarısı)
```

- **Batch:** `DefaultBatchSize = 500`; offset pagination, her batch ayrı Query DB transaction; `ChangeTracker.Clear()`.
- **Snapshot:** list projection ile birebir aynı zenginleştirilmiş snapshot — `PaymentProjectionSnapshotFactory.Create(payment, client.FullName, pet?.Name)`. Denormalize alan eşlemesi `PaymentProjectionProcessor.ApplyReadModelChange` ile aynı kurallarla yapılır (trim + invariant lower; defensive fallback dahil).
- **Tenant izolasyonu:** `--tenant` verilirse yalnızca o tenant; aksi halde tüm tenant'lar. TenantId/ClinicId Command DB truth'tan korunur.
- **Nullable pet:** `PetId` null ise pet join atlanır; `PetName`/`PetNameNormalized` null kalır.
- **Client zorunlu:** Payment.ClientId daima geçerli bir client'a işaret eder (write path doğrular). Backfill client bulunamazsa `InvalidOperationException` ile durur (veri bütünlüğü guard'ı).
- **Finance backfill bozulmadı:** `backfill-payment-finance-projections` ayrı komut/servis olarak değişmeden kalır; bu faz yalnızca yeni `PaymentReadModels` backfill ekler.

---

## 3. Idempotency davranışı

| Durum | Davranış |
|---|---|
| Backfill tekrar çalıştırma | PK `PaymentId` upsert; ilk run Insert, ikinci run Update; **duplicate satır yok** |
| Mevcut read-model satırı | Command DB truth ile update edilir (tüm alanlar overwrite) |
| Backfill + daha yeni projection event'i | Stale guard → satır korunur (SkipStale), backfill ezmez |
| Duplicate event replay | `ProcessedProjectionEvents` (processor) tarafından ele alınır; backfill bu tabloya **dokunmaz** |

Backfill ordering anahtarı: `DateTime.MinValue` (UTC sentinel, `PaymentReadModelBackfillPlanner.BackfillBaselineOccurredAtUtc`). Payment domain'de mutasyon timestamp'i yoktur; gerçek `payment.created.v1` / `payment.updated.v1` event'leri handler'da `DateTime.UtcNow` ile gelir → her zaman backfill sentinel'inden yenidir.

---

## 4. LastEventId / backfill marker kararı

`PaymentReadModels` satırında projection metadata üç alandır: `LastEventId`, `LastEventOccurredAtUtc`, `LastProjectedAtUtc`.

- **Gerçek event id yoktur** (backfill bir event değil, Command DB snapshot'ıdır). Bu yüzden:
  - `LastEventId = Guid.Empty` (`PaymentReadModelBackfillService.BackfillEventId`) — finance backfill ile aynı marker.
  - `LastEventOccurredAtUtc = DateTime.MinValue (UTC sentinel)` — stale-guard ordering anahtarı.
  - `LastProjectedAtUtc = backfill wall-clock` (gözlem amaçlı).
- **Projection ordering/idempotency ile çakışma yok:** Stale guard `occurredAtUtc < existing.LastEventOccurredAtUtc` kuralını kullanır. Backfill sentinel en küçük değer olduğundan:
  - Daha yeni gerçek projection event'i her zaman backfill satırını ezer (doğru).
  - Backfill, daha yeni gerçek event ile yazılmış satırı **geriye düşürmez** (SkipStale) — eski Command snapshot ile regresyon riski engellenir.
- **Risk değerlendirmesi:** Backfill sırasında aynı anda gelen yeni payment event'i upsert idempotent olduğundan sonraki batch'te veya event uygulanırken yakalanır; double-row üretmez.

---

## 5. Parity kapsamı

`IPaymentReadModelParityReader.GetClinicParityAsync(tenantId, clinicId, recentSampleSize = 50)` → `PaymentReadModelParityResult`. Tenant + clinic kapsamlıdır (list read yüzeyi ile aynı kapsam). Üç boyut:

1. **Count parity** — tenant+clinic toplam ödeme sayısı: `Payments` vs `PaymentReadModels` (`CountInSync`).
2. **Row sample parity** — recent top-N örneğinde ortak `PaymentId`'ler için kritik alan karşılaştırması: `Amount`, `Currency`, `Method`, `PaidAtUtc`, `ClientId`, `PetId`, `ClientName`, `PetName`, `Notes` (`RowSampleParityInSync`, `RowSampleMismatches` — PII yok, yalnızca PaymentId + farklı alan adı).
3. **Recent top-N ordering parity** — `PaidAtUtc DESC, PaymentId DESC` sıralı ilk N kaydın Command ve Query tarafında aynı sırada olması (`RecentOrderingInSync`).

`InSync = CountInSync && RowSampleParityInSync && RecentOrderingInSync`. OutOfSync = `!InSync`.

**ClientName/PetName denormalizasyon notu:** Bu alanlar payment event'i ile snapshot'lanır. Backfill hemen sonrası Command truth ile birebir eşittir (parity InSync). Sonradan bir client/pet rename'i, ilgili payment'a ait yeni bir event tetiklemeden read-model'e yansımaz; bu beklenen denormalizasyon davranışıdır ve parity'nin "backfill sonrası doğrulama" amacıyla çakışmaz.

**Report/export parity kapsam dışı.**

---

## 6. Search parity neden kapsam dışı?

14E'de Query DB list yolu **yalnızca arama boşken** seçilir; arama dolu iken bilinçli olarak Command DB path kullanılır (documented "unsupported search route guard"). 14D reader'ın arama yüzeyi (denormalize `ClientNameNormalized / PetNameNormalized / NotesNormalized / Currency` üzerinden OR) Command DB'nin client email/telefon veya pet breed gibi genişlikleriyle tam parity değildir. Search varken Query path kullanılmadığından, search parity bu fazda gerekli değildir ve kapsam dışıdır. Search parity genişletmesi ayrı bir faza bırakılmıştır.

---

## 7. Health kapsamı

Read-model durumu mevcut `payment-projection` health entry'sine ek bir boyut olarak eklendi (`/health/ready`). Finance kuyruk değerlendirmesi (`PaymentProjectionHealthEvaluator`) 13D ile birebir korunur; read-model drift ayrı bir boyuttur ve nihai seviye iki boyutun **en kötüsüdür**.

### Gate ve kurallar

- **Gate:** `PaymentProjection:Enabled` **ve** `PaymentsListReadEnabled` ikisi de kapalıysa read-model drift hiç değerlendirilmez (sinyal hesaplanmaz; production default'ta `/health/ready` ekstra sorgu yapmaz). Backfill yapılmamış boş Query DB, flag kapalıyken sistemi **Unhealthy yapmaz** (Healthy kalır).
- **Drift = Command Payments sayısı − Query PaymentReadModels sayısı** (global count).
- Gate açıkken:
  - Drift yok → **Healthy**.
  - Drift var + `PaymentsListReadEnabled = true` → **Unhealthy** (kullanıcı eksik/yanlış liste görür).
  - Drift var + yalnızca `PaymentProjection:Enabled = true` (list read flag kapalı) → **Degraded** (backfill/catch-up penceresi).

### `data` alanları (sinyal hesaplandığında eklenir)

`paymentsListReadEnabled`, `readModelCommandPaymentCount`, `readModelCount`, `readModelCountDrift`, `readModelCountInSync` — mevcut `pendingCount`/`deadLetterCount`/`projectionEnabled` vb. alanlara ek.

### Korunan davranışlar

- Projection disabled + flag kapalı → Healthy (finance rollout güvenliği).
- Dead-letter / pending age / retry-waiting severity kuralları değişmedi.
- Dashboard finance health davranışı etkilenmedi (ayrı kavram).

> Not: Recent/field-level drift, her zaman çalışan health check'i ucuz tutmak için health'te değil **parity reader**'da (klinik kapsamlı) değerlendirilir; rollout runbook'u flag açmadan önce parity InSync doğrulamasını zorunlu kılar.

---

## 8. Rollout öncesi önerilen sıra

```text
migrate-query
  → PaymentProjection:Enabled=true (+ restart) — projection açık, outbox tüketimi başlar
  → backfill-payment-read-models — Command DB ödemelerini PaymentReadModels'e doldur
  → parity InSync doğrula (IPaymentReadModelParityReader.GetClinicParityAsync)
  → health Healthy / read-model InSync doğrula (/health/ready payment-projection)
  → QueryReadModels:PaymentsListReadEnabled=true (+ restart) — list read path Query DB'ye geçer
  → list smoke (flag true + boş arama → Query DB log satırı)
```

| # | Adım | Doğrulama |
|---|---|---|
| 1 | `migrate-query` | `PaymentReadModels` tablo + indexler mevcut |
| 2 | `PaymentProjection:Enabled=true` + restart | Startup log; outbox tüketimi |
| 3 | `backfill-payment-read-models` | Success; count parity in-sync (mismatch → exit 2) |
| 4 | Parity | `GetClinicParityAsync` → `InSync=true` (count + row sample + recent ordering) |
| 5 | Health | `/health/ready` → `payment-projection` Healthy; `readModelCountInSync=true` |
| 6 | `PaymentsListReadEnabled=true` + restart | Startup log `PaymentsListReadEnabled=True` |
| 7 | Smoke | Flag true + boş arama → "Payments list generated from Query DB read model" |

> **Altın kural:** `PaymentsListReadEnabled` açmadan önce backfill + parity zorunludur. Query DB boşken flag true → boş liste (fallback yok).

---

## 9. Rollback

```text
QueryReadModels:PaymentsListReadEnabled=false → restart → handler anında Command DB path'ine döner
```

- Kod geri alımı gerekmez. Startup logunda `PaymentsListReadEnabled=False` ile doğrulanır.
- `PaymentProjection:Enabled` opsiyonel olarak açık bırakılabilir (read-model sıcak tutma); açık kalması list routing kapalıyken zararsızdır.
- Read flag kapalıyken read-model drift health sinyali en fazla Degraded üretir (projection açıksa) ve `/health/ready`'yi Unhealthy yapmaz.

---

## 10. Bilinen riskler

| Risk | Azaltma |
|---|---|
| Flag true + Query DB boş/eksik | Backfill + parity önce; read flag açıkken count drift → health Unhealthy |
| Backfill sırasında yeni payment | Upsert idempotent; sonraki batch veya projection event'inde yakalanır |
| Daha yeni event'i backfill ezmesi | Stale guard (MinValue sentinel) → SkipStale |
| Client/pet rename denormalizasyon drift'i | Beklenen davranış; parity backfill sonrası InSync, rename payment event'i ile yansır (dokümante) |
| Search parity eksikliği | 14E search route guard ile Query path search'te kullanılmaz (dokümante) |
| `/health/ready` ekstra maliyet | Read-model sinyali yalnızca gate açıkken hesaplanır (production default kapalı) |

---

## 11. Manuel test listesi

Test bu fazda **çalıştırılmadı**. Visual Studio veya terminalde:

```powershell
dotnet build --no-restore

dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModelBackfill"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModelParity"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentFinanceBackfill"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentFinanceParity"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjectionHealth"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModelReader"
dotnet test --no-restore --filter "FullyQualifiedName~PaymentList"
```

Ek manuel doğrulama:

- `backfill-payment-read-models` çalıştır → çıktıda count parity in-sync olduğunu doğrula.
- `IPaymentReadModelParityReader.GetClinicParityAsync` → backfill sonrası `InSync=true`.
- `/health/ready` → `payment-projection` Healthy; flag/projection kapalıyken `readModel*` alanları yok (gate kapalı).
- Finance health/parity/backfill davranışının değişmediğini regression filtreleriyle doğrula.

---

## 12. Commit

**Commit atılmadı.** Kullanıcı onayı sonrası ayrı commit.
