# CQRS-15D — PaymentReadModel ClinicName enrichment

**Tür:** Schema enrichment + projection / backfill / parity hazırlığı. **Runtime routing değişmedi, flag eklenmedi.**

**Amaç:** `PaymentReadModels` read-model'ini `ClinicName` ile zenginleştirmek; böylece client payment summary ve ileride report/export için Query DB contract parity mümkün hale gelsin. 15C'de blokör olan tek alan (`ClinicName`) bu fazda kapatıldı.

**İlgili dokümanlar:** [`cqrs-15c-client-payment-summary-read-model-decision.md`](cqrs-15c-client-payment-summary-read-model-decision.md) · [`cqrs-15b-dashboard-recent-payments-read-model.md`](cqrs-15b-dashboard-recent-payments-read-model.md) · [`cqrs-14f-payment-list-backfill-parity-health.md`](cqrs-14f-payment-list-backfill-parity-health.md) · [`cqrs-14b-payment-read-model-schema.md`](cqrs-14b-payment-read-model-schema.md)

---

## 1. Bu fazda ne değişti

`PaymentReadModel` denormalize bir `ClinicName` alanı kazandı. Alan, write-path snapshot'ından (create/update projection) ve backfill'de Command DB truth'tan doldurulur. Parity karşılaştırması artık `ClinicName` farkını da yakalar.

- **Eklenen kolon:** `PaymentReadModels.ClinicName`
- **Eklenmeyen:** `ClinicNameNormalized` — bilinçli karar (bkz. §2).
- **Değişmeyen:** payment list routing, dashboard recent routing, GetById, client summary route, report/export, search parity, herhangi bir flag, herhangi bir DTO.

---

## 2. Eklenen kolon

| Kolon | Tip | Null | Karar gerekçesi |
|---|---|---|---|
| `ClinicName` | `nvarchar(300)` | **NOT NULL** | `ClientName` pattern'i ile aynı: display için zorunlu. Payment domain'de her ödeme geçerli bir kliniğe bağlıdır (`Clinic.Name` boş olamaz), bu yüzden client gibi clinic de zorunludur. |

**`ClinicNameNormalized` neden yok:** Normalize alanları yalnızca filtre/search için anlamlı. PaymentReadModel zaten tenant + clinic kapsamlı okunuyor; klinik adına göre arama/filtre yüzeyi yok. `ClientName + ClientNameNormalized` ve `PetName + PetNameNormalized` pattern'i search içindir; `ClinicName` salt display olduğu için normalize alan eklenmedi (gereksiz kolon + index'ten kaçınıldı).

**Uzunluk:** `QueryReadModelConstraints.ClinicName = 300` (zaten mevcuttu; AppointmentReadModel.ClinicName ile hizalı).

---

## 3. Projection enrichment davranışı

- `PaymentProjectionSnapshot`'a nullable `ClinicName` eklendi (event contract; geriye uyumlu — eski payload'larda `null`).
- `PaymentProjectionSnapshotFactory.Create(payment, clientName, clinicName, petName?)`: `clinicName` artık **zorunlu** parametre (`ArgumentException.ThrowIfNullOrWhiteSpace`), `ClientName` ile aynı kontrat. Finance-only `Create(payment)` overload'ı değişmedi (`ClinicName` null kalır; list yüzeyini beslemez).
- Create/Update command handler'ları snapshot'a `clinic.Name` geçer (handler clinic kaydını zaten doğruluyordu; ek DB erişimi yok).
- `PaymentProjectionProcessor.ApplyReadModelChange`: insert ve update'te `ClinicName` yazılır. Çözümleme `ClientName` ile birebir aynı kural:
  - dolu → `Trim()`
  - **null/empty (eski payload) → boş string** (defensive fallback; NOT NULL kolon korunur, sessiz veri bozulması yok)
- Stale guard, idempotency, finance gate ile lockstep davranış değişmedi.

---

## 4. Backfill davranışı

`backfill-payment-read-models` komutu `ClinicName`'i de doldurur.

- `LoadPaymentBatchAsync` batch'i için `Clinics` Command DB'den hydrate edilir (clientIds/petIds ile aynı toplu-yükleme deseni). Clinic bulunamazsa `InvalidOperationException` ile durur — `Client` guard'ı ile aynı veri bütünlüğü davranışı (payment daima geçerli kliniğe işaret eder).
- Snapshot list projection ile birebir aynı: `PaymentProjectionSnapshotFactory.Create(payment, client.FullName, clinic.Name, pet?.Name)`.
- `MapToReadModel` (insert) ve `ApplyUpdate` (update) `ClinicName` yazar; çözümleme `ResolveDenormalizedFields` içinde projection ile aynı (trim, null/empty → boş string).
- **Korunan davranışlar:** batch mantığı (offset pagination, `DefaultBatchSize=500`), idempotent non-destructive upsert, sentinel (`LastEventId=Guid.Empty`, `LastEventOccurredAtUtc=DateTime.MinValue`), stale guard (yeni gerçek event satırını downgrade etmez), `ProcessedProjectionEvents`'e dokunmama, count parity hesabı.

---

## 5. Parity davranışı

- `PaymentReadModelParityEvaluator.RowSnapshot`'a `ClinicName` eklendi; `FirstFieldDifference` `ClinicName` farkını da kontrol eder (ClientName'den hemen sonra).
- `PaymentReadModelParityReader`:
  - Query tarafı: read-model'den `x.ClinicName` seçilir.
  - Command tarafı: recent örnek tek clinic kapsamlı olduğundan klinik adı `Clinics`'ten bir kez okunur ve projection/backfill ile **aynı kuralla** (trim, null → boş string) üretilir; backfill sonrası karşılaştırma adil olur.
- **Row mismatch raporu PII-free kaldı:** `PaymentReadModelRowMismatch(PaymentId, Field)` — field adı `"ClinicName"`, değer loglanmaz.
- **Değişmeyen parity:** Amount/Currency/Method/PaidAtUtc/ClientId/PetId/ClientName/PetName/Notes karşılaştırmaları aynı; recent ordering parity (`PaidAtUtc DESC, PaymentId DESC`) ve count parity aynı.

---

## 6. Health davranışı (neden değişmedi)

`PaymentProjection` health yalnızca **global count drift** değerlendirir; row-field parity health evaluator kapsamında değil. `ClinicName` bir field-level denormalize alan olduğundan health'e yeni sinyal **eklenmedi** — count drift davranışı bozulmadan korundu. `ClinicName` drift'i parity reader/test kapsamı tarafından yakalanır (row mismatch). Health evaluator zaten row parity kullanmadığından gereksiz genişletme yapılmadı.

---

## 7. Rollout sırası

1. **Query DB migration uygula** — `20260622050000_AddPaymentReadModelClinicName` (mevcut satırlar `ClinicName=''` default alır).
2. **`PaymentProjection:Enabled=true`** — yeni create/update event'leri `ClinicName`'i dolu yazar.
3. **`backfill-payment-read-models` çalıştır** — mevcut satırların `ClinicName`'i Command DB truth'tan dolar (idempotent).
4. **PaymentReadModel parity InSync doğrula** — `ClinicName` dahil row sample parity + count parity.
5. **PaymentProjection health Healthy/InSync doğrula** — count drift yok.

---

## 8. Rollback notu

- **Runtime route rollback gerekmiyor:** Bu faz routing/flag değiştirmedi; production okuma yolları (payment list, dashboard recent, client summary) aynı kaldı.
- **Schema rollback:** `ClinicName` non-null kolon. Migration `Down` kolonu drop eder; ayrı schema rollback migration politikası genel CQRS rollback prosedürüne tabidir (veri kaybı: denormalize alan, Command DB'den yeniden türetilebilir). Geri alma gerekirse backfill ile yeniden doldurulabilir.

---

## 9. Riskler

| Risk | Açıklama / Azaltma |
|---|---|
| ClinicName drift | Klinik rename ayrı bir payment event tetiklemez (ClientName/PetName ile aynı bilinen denormalizasyon davranışı). Backfill ile yeniden hizalanır. |
| Migration öncesi backfill | Backfill migration'dan önce çalışırsa kolon yok → hata. Rollout sırası (§7) bunu engeller. |
| Boş ClinicName (eski payload) | Defensive fallback boş string yazar; parity bunu mismatch olarak işaretler → operatör backfill çalıştırır. Sessiz bozulma yok. |
| Backfill clinic eksik | `InvalidOperationException` ile durur (veri bütünlüğü guard'ı) — Client guard'ı ile aynı. |

---

## 10. Değişen / eklenen dosyalar

**Schema / entity**
- `src/.../Persistence/Query/Models/PaymentReadModel.cs` — `ClinicName` property
- `src/.../Persistence/Query/Configurations/PaymentReadModelConfiguration.cs` — `ClinicName` config
- `src/.../Persistence/Migrations/Query/20260622050000_AddPaymentReadModelClinicName.cs` (+ `.Designer.cs`)
- `src/.../Persistence/Migrations/Query/QueryDbContextModelSnapshot.cs` — `ClinicName` snapshot

**Projection**
- `src/.../Payments/IntegrationEvents/PaymentProjectionSnapshot.cs` — nullable `ClinicName`
- `src/.../Payments/IntegrationEvents/PaymentProjectionSnapshotFactory.cs` — zorunlu `clinicName`
- `src/.../Payments/Commands/Create/CreatePaymentCommandHandler.cs` — `clinic.Name`
- `src/.../Payments/Commands/Update/UpdatePaymentCommandHandler.cs` — `clinic.Name`
- `src/.../Projections/Payments/PaymentProjectionProcessor.cs` — upsert `ClinicName`

**Backfill**
- `src/.../Projections/Payments/PaymentReadModelBackfillService.cs` — clinic hydrate + map

**Parity**
- `src/.../Payments/ReadModels/PaymentReadModelParityEvaluator.cs` — RowSnapshot + field diff
- `src/.../Query/Payments/PaymentReadModelParityReader.cs` — clinic name okuma

**Testler**
- `tests/.../Payments/IntegrationEvents/PaymentProjectionSnapshotFactoryTests.cs`
- `tests/.../Payments/ReadModels/PaymentReadModelParityEvaluatorTests.cs`
- `tests/.../Projections/Payments/PaymentProjectionTestSupport.cs`
- `tests/.../Projections/Payments/PaymentProjectionIntegrationTests.cs`
- `tests/.../Projections/Payments/PaymentReadModelBackfillIntegrationTests.cs`
- `tests/.../Query/Payments/PaymentReadModelParityIntegrationTests.cs`
- `tests/.../Query/Payments/PaymentReadModelReaderIntegrationTests.cs` (helper: NOT NULL koruması)
- `tests/.../Query/Dashboard/DashboardRecentPaymentsReadModelReaderIntegrationTests.cs` (helper: NOT NULL koruması)
- `tests/.../Infrastructure/QueryDbMigrationIntegrationTests.cs` (ClinicName kolon assert)

---

## 11. Kapsam dışı

- `ClientPaymentSummaryReadEnabled` flag, client payment summary route
- Dashboard recent routing, payment list routing değişikliği
- Report/export taşıma, GetById taşıma
- Search parity genişletme, `ClinicNameNormalized`
- Frontend değişikliği, DTO değişikliği
- Test çalıştırma, commit
