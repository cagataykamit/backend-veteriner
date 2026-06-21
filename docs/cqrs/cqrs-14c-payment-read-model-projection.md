# CQRS-14C — Payment snapshot enrichment + PaymentReadModel projection upsert

**Tür:** Projection write path + read-model upsert. **Production read davranışı değişmedi** (list routing/flag yok).

**Ön durum:** 14B — `PaymentReadModels` tablosu + migration var; projection/routing/backfill/health yoktu. Finance bloğu (13C–13F) `PaymentDailyContributionReadModel` + `ClinicDailyPaymentStatsReadModel` üzerinden çalışıyor.

---

## 1. Bu fazda ne eklendi?

| Bileşen | Değişiklik |
|---|---|
| `PaymentProjectionSnapshot` | `ClientName`, `ClientNameNormalized`, `PetName?`, `PetNameNormalized?`, `Notes?`, `NotesNormalized?` alanları **nullable + default null** olarak eklendi (geriye uyumlu). |
| `PaymentProjectionSnapshotFactory` | Yeni enrich eden overload `Create(payment, clientName, petName?)`; eski finance-only `Create(payment)` overload backfill için korundu. |
| `CreatePaymentCommandHandler` / `UpdatePaymentCommandHandler` | Doğrulanmış `client.FullName` + (varsa) `pet.Name` snapshot'a aktarılıyor. Ek DB sorgusu yok (mevcut validation lookup'ları kullanılıyor). |
| `PaymentProjectionProcessor` | Aynı Query DB transaction'ı içinde `PaymentReadModel` upsert (`ApplyReadModelChange`). |
| Testler | Factory unit testleri + projection integration testleri (insert/update/duplicate/nullable pet/legacy payload/stale). |

**Yapılmadı:** reader, list routing, `PaymentsListEnabled` flag, backfill/parity/health, dashboard recent, report/export, migration, frontend.

---

## 2. Snapshot enrichment davranışı

- Write path'te `ClientName` **zorunludur**: factory `ArgumentException.ThrowIfNullOrWhiteSpace(clientName)` ile garanti eder. `client.FullName` her zaman dolu olduğundan create/update'te boş gelmez.
- `PetName` payment'a pet bağlı değilse `null`.
- Normalize kuralları command-side ile hizalı:
  - `ClientNameNormalized` = `Client.NormalizeFullNameForDuplicateCheck` (trim + invariant lower).
  - `PetNameNormalized` / `NotesNormalized` = trim + invariant lower; boşsa `null`.
- `AppointmentId` / `ExaminationId` zaten domain + snapshot içinde mevcut; doğrudan `PaymentReadModel`'e map edilir.
- Tenant/clinic izolasyonu korunur: tüm alanlar snapshot üzerinden taşınır, processor cross-tenant okuma yapmaz.
- N+1 yok: handler'lar zaten client ve (PetId varsa) pet kaydını validation için yüklüyor; isim bu kayıtlardan alınır.

---

## 3. PaymentReadModel upsert davranışı

- `PaymentProjectionProcessor.ApplyTransactionallyAsync` finance contribution + daily stats recompute işlemini yaptıktan **sonra, aynı transaction içinde** `ApplyReadModelChange` çağrılır.
- Created event → satır **insert**. Updated event → aynı `PaymentId` satırı **overwrite (upsert)**.
- Increment mantığı **yok**; her event satırı tam olarak snapshot değerleriyle yazar.
- `LastEventId`, `LastEventOccurredAtUtc`, `LastProjectedAtUtc` her yazımda doldurulur.
- Finance contribution / daily stats transaction bütünlüğü değişmedi; read-model aynı `SaveChanges` + `Commit` sınırında kalıcı olur. Hata durumunda tüm değişiklikler birlikte rollback olur.

---

## 4. Idempotency / replay / stale davranışı

- Idempotency anahtarı değişmedi: `ProcessedProjectionEvents (EventId, ConsumerName)`. Fast-path duplicate check + transaction içi unique-insert (SQL 2601/2627) korunur.
- Replay (aynı `EventId`) → duplicate; `PaymentReadModel` ikinci kez yazılmaz (event zaten işlenmiş sayılır).
- Stale guard tek otoritedir: finance `PaymentDailyContributionReadModel.LastEventOccurredAtUtc`. Event stale ise (`occurredAtUtc < existing`), finance korunur **ve** read-model upsert atlanır (`if (!stale)`).
- Ek savunma: `ApplyReadModelChange` içinde `occurredAtUtc < existing.LastEventOccurredAtUtc` kontrolü; finance gate ile lockstep çalıştığı için normalde tetiklenmez, divergence'e karşı güvenlik kemeridir.

---

## 5. Geriye uyumluluk notu

- `PaymentProjectionSnapshot`'a eklenen alanlar nullable + default null; outbox payload contract'ı geriye uyumludur (JSON property-adı tabanlı, eksik alanlar `null` deserialize olur).
- **Eski (14C öncesi) payload fallback** — yeni enrichment alanları yoksa `ApplyReadModelChange` patlamaz:
  - `ClientName` / `ClientNameNormalized` → boş string (`""`). Kolon `NOT NULL` olduğu için `null` yerine boş string yazılır.
  - `PetName` / `PetNameNormalized` / `Notes` / `NotesNormalized` → `null`.
- Bu satırlar 14D backfill veya yeni bir enriched update event ile düzeltilebilir. Finance projection eski payload ile regresyona girmez.

---

## 6. Kapsam dışı

- `IPaymentReadModelReader` ve query reader.
- `GetPaymentsListQueryHandler` routing / `PaymentsListEnabled` flag.
- Backfill / parity / health (`PaymentReadModel` için).
- Dashboard recent payments taşıma.
- Report / export taşıma.
- Frontend, migration oluşturma.

---

## 7. Sonraki faz — 14D notu

- `PaymentReadModel` backfill servisi (mevcut command Payments → read-model), parity ve health check.
- Eski payload ile yazılmış boş `ClientName` satırlarının backfill ile doldurulması.
- Sonrasında reader + list routing + `PaymentsListEnabled` flag (14E+).

---

## 8. Riskler

- Eski payload satırlarında `ClientName` boş kalır; list/search bu satırları ada göre bulamaz (14D backfill ile çözülür).
- `ClientName`/`Notes` uzunluğu command-side validation'a güvenir; processor truncate etmez (finance projection deseniyle tutarlı). Aşırı uzun değer teorik olarak kolon limitinde hata verebilir — command validator'ları bunu önler.
- Read-model upsert finance transaction'ına eklendiği için transaction biraz büyüdü; yük profili değişmedi (tek ek upsert).

---

## 9. Manuel test listesi

```powershell
dotnet build --no-restore

dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjection"

dotnet test --no-restore --filter "FullyQualifiedName~PaymentReadModel"

dotnet test --no-restore --filter "FullyQualifiedName~PaymentProjectionSnapshotFactory"
```

Opsiyonel tam suite:

```powershell
dotnet test --no-restore
```

> Integration testleri Query/Command DB gerektirir (mevcut `PaymentProjectionWebApplicationFactory` altyapısı).

---

## 10. Commit

**Commit atılmadı.** Kullanıcı onayı sonrası ayrı commit.
