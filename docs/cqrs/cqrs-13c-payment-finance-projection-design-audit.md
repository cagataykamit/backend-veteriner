# CQRS-13C — Payment finance projection processor tasarım audit'i

**Tür:** Karar audit'i (yalnız inceleme + öneri). **Production C# / migration / flag / appsettings
DEĞİŞMEDİ.** Bu doküman yalnızca 13C projection processor'ı kodlamadan önce doğru projection
stratejisini sabitler.

**Ön durum:** 13B commit edildi (`4b11757`). Payment create/update artık `payment.created.v1` /
`payment.updated.v1` integration event yayıyor; `ClinicDailyPaymentStatsReadModel` tablosu + migration
(`20260621035135`) mevcut ama **boş** (processor yok). Dashboard finance hâlâ Command DB aggregate okuyor.

---

## 1. Kısa teşhis

13B finance projeksiyonunun **write-side event'ini** ve **günlük özet tablosunu** kurdu, ama iki kritik
şey eksik bırakıldı ve bu eksikler projection stratejisini doğrudan belirliyor:

1. **Payment event'i yalnızca `Current` snapshot taşıyor — `Previous` yok.**
   `PaymentCreatedIntegrationEvent` / `PaymentUpdatedIntegrationEvent` envelope'u
   `(EventId, OccurredAtUtc, Current)` üçlüsü. Karşılaştırma için Appointment event'leri
   `(Previous, Current)` taşır (`AppointmentUpdatedIntegrationEvent.Previous`). Bu fark, update delta'sını
   doğrudan event'ten hesaplamayı **imkânsız** kılar.

2. **Query DB'de per-payment read-model YOK.**
   `**/*PaymentReadModel*` araması sıfır sonuç verdi. Appointment projeksiyonu daily stats'ı
   `AppointmentReadModel` per-aggregate tablosundan **recompute** ederek üretir; payment tarafında bu
   ara katman hiç yok. Tek finance read-model `ClinicDailyPaymentStatsReadModel` (aggregate özet).

Bu iki gerçek, kullanıcı sorusunun çekirdeğini doğruluyor: aggregate özet tablosuna sadece `Current`
snapshot ile increment yapmak `payment.updated`'te **double count / yanlış total** üretir, çünkü eski
katkının (eski gün / eski klinik / eski tutar / eski currency) ne olduğunu bilmenin **hiçbir yolu yok**.

### Mevcut Appointment precedent'i (kanonik desen)

`AppointmentProjectionProcessor.ApplySnapshotChangeAsync`:

- Per-aggregate read-model'i (`AppointmentReadModel`) **upsert** eder.
- `GetAffectedDailyBuckets(previous, current)` ile **eski + yeni** gün bucket'larını toplar (previous
  event payload'ından gelir).
- Her etkilenen bucket için `RecalculateDailyStats` → o günün **tüm** per-aggregate satırlarını
  Query DB'den okuyup **yeniden SUM/COUNT** eder. Increment/decrement yok; **recompute** var.
- Tümü `ApplyTransactionallyAsync` içinde: `ProcessedProjectionEvents` insert + read-model yazımı tek
  Query DB transaction'ında.

Payment için bu deseni birebir uygulamanın tek engeli: **eski bucket'ı tanımlayan bir kaynak yok**
(ne event'te `Previous`, ne de per-payment state satırı). Çözüm: per-payment contribution/state satırı
eklemek — bu satır hem "eski bucket"ı verir hem recompute için SUM kaynağı olur.

---

## 2. Kritik risk

| Risk | Açıklama | Etki |
|---|---|---|
| **Update double-count** | `payment.updated` sadece `Current` taşır; eski katkı bilinmeden aggregate'e ekleme yapılırsa eski gün/klinik/currency satırı asla düzeltilmez | Yanlış finansal total (kullanıcı güveni — dashboard ana ekran) |
| **Cross-bucket move** | Update `PaidAtUtc` (gün), `ClinicId` veya `Currency` değiştirirse katkı yeni bucket'a taşınır; eski bucket'tan **düşülmezse** her iki gün de yanlış | Hem eski hem yeni gün bozulur |
| **Stale / out-of-order** | Payment'ta per-aggregate sequence yok; ordering yalnız `OccurredAtUtc` (emit anında `DateTime.UtcNow`). Eski bir event yeni katkıyı ezebilir | Geçici/kalıcı yanlış total |
| **No fallback** | 13D'de flag true iken read-model stale/eksikse otomatik Command DB fallback yok | Dashboard eksik özet gösterir |
| **Mixed-currency** | Mevcut dashboard handler currency'yi ayırmadan `Amount` topluyor; read-model PK'de `Currency` var | Parity için toplama davranışı eşitlenmeli (§7) |

Bu risklerin tamamı, eski projected katkıyı bilen bir **per-payment contribution state** ile ortadan
kalkıyor.

---

## 3. Strateji karşılaştırması

Ölçek: ✅ iyi / ⚠️ dikkat / ❌ kötü.

| Kriter | A) Sadece aggregate increment/decrement | B) Aggregate + per-payment contribution state (recompute) | C) Her eventte Command DB'den gün recompute | D) Projection yok, backfill-only / materialized |
|---|---|---|---|---|
| **Doğruluk (create)** | ✅ | ✅ | ✅ | ✅ (run anında) |
| **Doğruluk (update: amount/date/clinic/currency)** | ❌ Eski katkı bilinmiyor (event'te `Previous` yok) → düzeltilemez | ✅ Eski bucket contribution satırından gelir, recompute kesin | ⚠️ Eski bucket'ı yine bilmek gerekir; Command DB yalnız güncel satırı tutar → eski gün bilinmez | ⚠️ Yalnız son backfill anına kadar doğru |
| **Idempotency** | ❌ Tekrar uygulama drift biriktirir | ✅ Recompute + `ProcessedProjectionEvents` → tam idempotent / self-heal | ⚠️ Recompute idempotent ama eski-bucket problemi sürer | ✅ Rerun idempotent |
| **Stale event güvenliği** | ❌ Decrement edilemez | ✅ Contribution `LastEventOccurredAtUtc` guard + recompute | ⚠️ Guard koyulabilir ama eski-bucket eksik | ✅ Snapshot, event sırası önemsiz |
| **Backfill kolaylığı** | ⚠️ Yalnız aggregate; drift'i düzeltmez | ✅ Command DB → contribution + aggregate; non-destructive upsert (12B deseni) | ✅ Zaten recompute | ✅ Doğası backfill |
| **Performans** | ✅ O(1) ama yanlış | ✅ Bucket başı SUM (Appointment ölçeği; index'li) | ❌ Her event cross-DB Command okuması; CQRS izolasyonunu kırar | ⚠️ Canlı taze değil, periyodik ağır iş |
| **Migration ihtiyacı** | Yok (drift'li) | **Evet — yeni contribution tablosu** | Yok | Yok (ama tazelik yok) |
| **Test edilebilirlik** | ⚠️ Yanlış davranış zaten | ✅ Appointment claim/parity deseni birebir | ⚠️ Cross-DB mock zor | ✅ Basit ama tazelik testi yok |
| **Mevcut altyapıya uyum** | ❌ | ✅ Appointment `Recalculate*` deseniyle birebir | ⚠️ Cross-DB her event yeni desen | ⚠️ Dashboard tazelik hedefini karşılamaz |
| **Risk** | ❌ Finansal yanlışlık | ⚠️ Yeni tablo + processor (kontrollü) | ❌ CQRS sınır ihlali | ⚠️ UX (stale) |

**Sonuç:** A finansal olarak yanlış; C CQRS Query/Command izolasyonunu her event'te kırar ve update
eski-bucket problemini yine çözmez (Command DB güncel satırı tutar, eski günü bilmez); D dashboard
tazelik hedefini (13 audit'in birincil gerekçesi) karşılamaz. **B**, Appointment'ın kanıtlanmış
recompute desenini payment'a taşır ve tüm update senaryolarını kesin/idempotent çözer.

---

## 4. Önerilen projection modeli — B (contribution state + recompute)

**Beklentiyle uyumlu:** `ClinicDailyPaymentStatsReadModel` + per-payment contribution state.

### Akış (Appointment `ApplySnapshotChangeAsync` ile paralel)

```text
PaymentProjectionProcessor.ApplyEventAsync(created|updated)
  → ApplyTransactionallyAsync (tek Query DB transaction)
      1. ProcessedProjectionEvents (EventId, ConsumerName) insert  → duplicate ise skip
      2. PaymentDailyContributionReadModel.Find(PaymentId)
           - existing varsa ve existing.LastEventOccurredAtUtc > event.OccurredAtUtc → STALE, skip
           - "eski bucket" = existing (varsa): (oldClinicId, oldLocalDate, oldCurrency)
      3. contribution satırını upsert et (yeni snapshot: clinic, localDate=ToLocalDate(PaidAtUtc), currency, amount, lastEventId, lastEventOccurredAtUtc, lastProjectedAtUtc)
      4. affectedBuckets = { eski bucket (varsa), yeni bucket }
      5. her affected bucket için RecalculateDailyStats:
           SUM(Amount), COUNT(*) over contribution rows WHERE (tenant, clinic, localDate, currency)
           → ClinicDailyPaymentStatsReadModel upsert (0 satır kalırsa Remove)
```

- **Recompute, increment değil.** Appointment'taki gibi bucket başına SUM/COUNT; drift imkânsız,
  rerun güvenli.
- **Eski bucket kaynağı = contribution satırı** (event'te `Previous` olmadığı için zorunlu).
- **Currency PK'de** olduğundan currency değişimi de "bucket move" olarak ele alınır.

### Neden delta arithmetic (B-delta) yerine recompute (B-recompute)?

Stored contribution ile O(1) decrement/increment de matematiksel olarak doğru olur. Ancak **recompute**
tercih edilir çünkü: (a) Appointment precedent'i bit-for-bit aynı; (b) tam self-healing — herhangi bir
geçmiş drift bir sonraki eventte düzelir; (c) test/parity beklentisi mevcut desende hazır. Delta yalnız
ölçek sorununda (çok yoğun klinik-gün) optimizasyon olarak düşünülür; varsayılan recompute.

---

## 5. Gerekli ek read-model — schema önerisi

**13B schema eksik kaldı mı?** Teknik olarak 13B kapsamı yalnız "daily stats foundation" idi, dolayısıyla
"plana göre eksik" değil; ama **B stratejisi için yeterli değil**. Update'i doğru projekte etmek için
per-payment contribution/state tablosu **zorunlu** ve bu tablo 13B'de yok. Appointment'ta eşdeğeri
(`AppointmentReadModel`) zaten var olduğu için Appointment ek migration gerektirmemişti; payment'ta yok.

**Karar: 13C YENİ bir Query DB migration eklemeli** (`PaymentDailyContributionReadModel`).

### `PaymentDailyContributionReadModel` (öneri)

| Alan | Tip | Açıklama |
|---|---|---|
| `PaymentId` | `Guid` (PK) | Aggregate kimliği — unique; upsert anahtarı |
| `TenantId` | `Guid` | Kiracı izolasyonu |
| `ClinicId` | `Guid` | Mevcut (son projeksiyonlu) klinik |
| `LocalDate` | `date` | `OperationDayBounds.ToLocalDate(PaidAtUtc)` (İstanbul) |
| `Currency` | `nvarchar(3)` | ISO 4217 alpha-3 |
| `Amount` | `decimal(18,2)` | Bu payment'ın katkısı |
| `LastEventId` | `Guid` | Son uygulanan event (backfill'de `Guid.Empty`) |
| `LastEventOccurredAtUtc` | `datetime2` | Stale guard ordering anahtarı |
| `LastProjectedAtUtc` | `datetime2` | Projection wall-clock |

**Index (recompute SUM için):**
`IX_PaymentDailyContributionReadModels_Tenant_Clinic_LocalDate_Currency`
→ `(TenantId, ClinicId, LocalDate, Currency)`. Bu, hem `RecalculateDailyStats` SUM/COUNT sorgusunu hem
de bucket başı taramayı karşılar. PK zaten `PaymentId` üzerinde `Find` lookup'ını verir.

> İsimlendirme: `PaymentDailyContributionReadModel` (katkı odaklı) önerilir; `PaymentFinanceProjectionStateReadModel`
> de kabul edilebilir. Tek bir satır hem "state" (son projeksiyonlu hali) hem "contribution" (aggregate'e
> katkısı) rolünü görür; iki ayrı tabloya gerek yok.

`ClinicDailyPaymentStatsReadModel` **değişmeden** kalır; `LastEventOccurredAtUtc` alanı aggregate
seviyesinde gözlem amaçlı tutulur (gerçek stale guard contribution seviyesindedir).

---

## 6. Cevaplanan kararlar (özet)

1. **`ClinicDailyPaymentStatsReadModel` tek başına yeterli mi?** Hayır. Create için yeterli; update için
   eski katkıyı bilmediğinden yetersiz. Per-payment contribution state gerekir.
2. **`payment.updated` delta'sı nasıl hesaplanmalı?** Event'te `Previous` yok → eski bucket contribution
   satırından okunur; etkilenen (eski + yeni) bucket'lar contribution rows üzerinden **recompute** edilir
   (increment/decrement değil).
3. **Per-payment tablo gerekli mi?** Evet — `PaymentDailyContributionReadModel` (PaymentId unique,
   TenantId, ClinicId, LocalDate, Currency, Amount, LastEventId, LastEventOccurredAtUtc, LastProjectedAtUtc).
4. **Migration / 13B eksikliği / 13C sorumluluğu:** 13B plan kapsamına göre "eksik" değil ama B için
   yetersiz; **13C yeni migration eklemeli** (contribution tablosu). Appointment ek migration
   gerektirmemişti çünkü `AppointmentReadModel` zaten vardı.
5. **Delete/cancel/refund write path yok:** `Payment` yalnız create + update; `Amount > 0` invariant'ı
   her zaman geçerli. Sıfırlama/silme event'i yok. Drift yalnızca backfill ile düzeltilir; processor'da
   "remove contribution" yolu **gerekmez** (ama recompute, contribution 0 satıra düşerse aggregate satırını
   `Remove` eder — Appointment `totalCount == 0` davranışıyla aynı; bu yol pratikte tetiklenmez).
6. **Idempotency:** `ProcessedProjectionEvents (EventId, ConsumerName)` yeterli **ve** contribution upsert
   + aggregate recompute **aynı Query DB transaction'ında** olmalı (Appointment `ApplyTransactionallyAsync`
   deseni). İkisi ayrı commit'lenirse kısmi state riski doğar.
7. **Ordering / stale:** `LastEventOccurredAtUtc` contribution satırında tutulur; gelen event'in
   `OccurredAtUtc` değeri mevcut satırın değerinden küçükse (out-of-order) **skip**. Uyarı: payment'ta
   per-aggregate sequence olmadığından ordering wall-clock'a (`DateTime.UtcNow` emit) dayanır; saat kayması
   teorik kenar durum — tek instance ve tek currency akışında pratik risk düşük, dokümante edilir.
8. **Backfill:** Command DB `Payments` → her satır için contribution upsert (12B non-destructive +
   stale-guard deseni), ardından etkilenen bucket'ları recompute → aggregate. **Hem contribution hem
   aggregate doldurulmalı**; aggregate'i doğrudan Command DB `GROUP BY` ile yazmak contribution'ı boş
   bırakır ve sonraki update'leri yine bozar. Backfill `LastEventId = Guid.Empty` işaretler,
   `ProcessedProjectionEvents`'e dokunmaz.
9. **Dashboard read index'leri:** clinic-scoped → `ClinicDailyPaymentStatsReadModel` PK
   `(TenantId, ClinicId, LocalDate, Currency)` zaten karşılar; tenant-wide → mevcut
   `IX_..._TenantId_LocalDate` yeterli. Ek index gerekmez. Contribution tablosu yalnız projection-internal
   recompute içindir (dashboard ondan okumaz).
10. **Rollback:** Read tarafı için `DashboardFinanceReadEnabled=false` + restart yeterli (handler Command DB
    fallback'e döner — 13E). Projection processor **ayrı** bir `Enabled` flag'le yönetilir (default false,
    Client/Pet/Appointment deseni); read flag kapalıyken projection açık kalabilir (zararsız, read-model'i
    sıcak tutar). Projection'ın kendisi sorun çıkarırsa kendi `Enabled=false` ile durdurulur. İki bağımsız
    flag, otomatik fallback yok.

---

## 7. 13C implementation kapsamı (gelecek faz — bu audit'te kodlanmadı)

- `PaymentDailyContributionReadModel` entity + EF configuration + **yeni Query DB migration** (PK
  `PaymentId`, recompute index).
- `PaymentProjectionProcessor` (Appointment deseni):
  - FIFO + opt-in claim/lease (`PaymentProjectionOptions: Enabled` default `false`, `ClaimingEnabled`
    default `false`).
  - `OutboxMessageQueryFilters` payment event tiplerini consume edecek şekilde (generic processor zaten
    atlıyor); gerekirse `SqlPaymentOutboxClaimRepository`.
  - `ApplyTransactionallyAsync`: `ProcessedProjectionEvents` insert + contribution upsert + affected bucket
    recompute, tek transaction.
  - `ApplyEventAsync`: `payment.created.v1` / `payment.updated.v1` → her ikisi de aynı upsert+recompute
    (created'de eski bucket yok; updated'de contribution satırından gelir).
  - Stale guard: contribution `LastEventOccurredAtUtc`.
- Hosted service kaydı + `PaymentProjectionMetrics`.

**13C'de YAPILMAYACAK:** dashboard handler routing, read flag default `true`, payment write path /
event contract değişikliği.

---

## 8. 13D backfill / health / parity kapsamı

- `PaymentFinanceReadModelBackfillService`: Command DB `Payments` → contribution + aggregate
  (non-destructive upsert, `Guid.Empty` event id, stale-guard; 12B/12C servis deseni).
- `backfill-payment-finance-projections` DbMigrator komutu (mevcut backfill komut deseni).
- `PaymentProjectionHealthCheck` (Client/Pet health deseni; `claimingEnabled`, lag, dead-letter).
- `IPaymentFinanceReadModelParityReader`: Command DB günlük `GROUP BY (clinic, localDate, currency)`
  SUM/COUNT vs `ClinicDailyPaymentStatsReadModel` — drift sinyali (Degraded).
- Acceptance / smoke script (12D/12C deseni).

## 9. 13E dashboard routing kapsamı

- `DashboardFinancePaymentAggregatesReader`'a Query DB yolu (read-model'den window SUM/COUNT okuma) —
  **mixed-currency davranışı korunmalı**: mevcut handler currency ayırmadan `Amount` topluyor; read-model
  PK'de `Currency` ayrı satır tuttuğundan, window için currency satırları **toplanarak** aynı sonuç
  üretilmeli (parity için kritik).
- 7 günlük trend: read-model `LocalDate` bucket'larından doğrudan; mevcut UTC-window + in-memory bucket
  taraması yerine.
- Recent payments + client/pet hydration: kapsam dışı (per-payment read-model değil; ayrı karar — bugün
  Command DB).
- `DashboardFinanceReadEnabled` flag (default `false`); `GetDashboardFinanceSummaryQueryHandler` routing
  (false=Command DB, true=Query DB).
- Rollout / rollback / acceptance dokümanı + smoke.

---

## 10. Test / acceptance beklentileri

| Faz | Test odağı |
|---|---|
| **13C** | created→contribution+aggregate; updated amount/date/clinic/currency değişimi → eski bucket düşer, yeni bucket artar; duplicate event idempotent; stale event skip; claim path two-worker no-duplicate; `Enabled=false` (default) → projection no-op (regression) |
| **13D** | backfill idempotent (rerun aynı sonuç) + contribution & aggregate doldurur; parity reader in-sync / drift; health Degraded sinyali |
| **13E** | flag false (Command DB) vs true (Query DB) **aynı** today/week/month total + 7 günlük trend (dolu read-model, mixed-currency dahil); stale/empty read-model + flag true → fallback yok (dokümante); tenant/clinic isolation; rollback smoke |

Manuel (kullanıcı çalıştıracak; bu fazda zorunlu değil):

```powershell
dotnet build --no-restore
dotnet test --no-restore --filter "Payment"
dotnet test --no-restore --filter "Projection"
```

---

## 11. Değişen dosyalar

- `docs/cqrs/cqrs-13c-payment-finance-projection-design-audit.md` *(bu doküman — yeni)*

**Production C# / migration / flag / appsettings / handler / processor DEĞİŞMEDİ.** Yalnızca audit
dokümanı eklendi.

---

## 12. Commit

**Commit atılmadı.** Audit yalnız bu dokümanı ekledi; 13C implementation ayrı bir fazda başlatılacak.

---

## 13. İlgili dokümanlar

- [`cqrs-13b-payment-finance-read-model-foundation.md`](cqrs-13b-payment-finance-read-model-foundation.md)
- [`cqrs-13a-projection-hardening-dashboard-finance-design.md`](cqrs-13a-projection-hardening-dashboard-finance-design.md)
- [`cqrs-13-next-read-model-target-audit.md`](cqrs-13-next-read-model-target-audit.md)
- Appointment recompute precedent: `AppointmentProjectionProcessor.ApplySnapshotChangeAsync` /
  `RecalculateDailyStats`
- Backfill precedent: `ClientReadModelBackfillService`
