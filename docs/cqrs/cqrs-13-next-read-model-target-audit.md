# CQRS-13 — Sonraki read-model / projection hedefi audit

**Tür:** Karar audit'i (yalnız inceleme + öneri). **Production C# / migration / flag değişmedi.**

**Ön durum:** CQRS-11 (Appointment), 12B (Client), 12C (Pet) read-model'leri tamam. 12D (Client/Pet
shared search lookup) zinciri tüm klinik liste + appointment + payment list/report/export yüzeylerinde
**search id çözümlemesini** Query DB'ye taşıdı; **aggregate verisi hâlâ Command DB'den** okunuyor.

---

## 1. Kısa teşhis

12D sonrası tablo nettir:

- **Tüm liste/arama yüzeyleri** search id çözümlemesi için Query DB read-model lookup kullanabiliyor
  (flag'li), ama **count / page / amount / hydration aggregate verisi Command DB'de**.
- **Yeni bir projeksiyon** kurulabilecek tek "ucuz" yüzey kalmadı: Dashboard'un **appointment dilimi**
  zaten projeksiyonlu (`ClinicDailyAppointmentStatsReadModel` + activity read-model'leri). Geri kalan tüm
  aday projeksiyonlar (**dashboard finance/operational, examination, payment, stock**) **yeni write-path
  event emission** gerektiriyor — çünkü Payment, Vaccination, Examination çocuk aggregate'leri ve Stock
  **integration event yaymıyor** (yalnız Appointment/Client/Pet outbox event'i mevcut — doğrulandı).
- Dolayısıyla CQRS-13 seçimi aslında: **"hangi projeksiyon, birim event-emission + risk başına en çok
  kullanıcı değeri/risk azaltımı sağlar?"**

İki gerçek bunu yönlendirir:

1. **En ağır canlı hesaplama, en sık görülen yüzeyde:** Dashboard finance summary (6 SQL aggregate +
   7 günlük tüm payment satırı taraması + recent + N+1 client/pet) ve operational alerts. Dashboard
   uygulamanın ana ekranı; her açılışta çalışır.
2. **12D yeni bir bağımlılık yüzeyi açtı:** Liste/report/export artık Client/Pet projeksiyonlarına
   bağımlı, ama Client/Pet processor'ları **claim/lease içermiyor** (çok-instance'ta duplicate işleme
   riski), **parity reader var ama health'e bağlı değil**, ve config hijyeni eksik
   (`ClientProjection` appsettings'te yok, `PetProjection.Enabled=false` default). Bu, az parlak ama
   **hemen kodlanabilir ve sıfır-yeni-event** olan tek de-risk işidir.

---

## 2. Aday karşılaştırma tablosu

Ölçek: ✅ iyi / olumlu, ⚠️ orta / dikkat, ❌ kötü / yüksek risk.

| Kriter | 1. Dashboard | 2. Examination | 3. Payment | 4. Stock | 5. Hardening |
|---|---|---|---|---|---|
| Kullanıcı perf katkısı | ✅ En yüksek (ana ekran, en ağır canlı hesap) | ⚠️ Orta (detail/related düşük trafik) | ⚠️ Orta (rapor/export nadir) | ❌ Düşük (okuma zaten ucuz snapshot) | ⚠️ Dolaylı (reliability) |
| Risk seviyesi | ⚠️ Orta (yeni event emission) | ⚠️ Orta | ❌ Yüksek | ⚠️ Orta-yüksek | ✅ Düşük |
| Finansal / veri bütünlüğü riski | ⚠️ Orta (finance read-only aggregate; export değil) | ✅ Düşük | ❌ En yüksek (export = otoriter finansal) | ⚠️ Orta (stok tutarlılığı) | ✅ Düşük |
| Tenant / clinic scope riski | ⚠️ Orta (clinic-derived client/pet zaten çözüldü) | ✅ Düşük | ⚠️ Orta | ⚠️ Orta (clinic-zorunlu listeler) | ✅ Düşük |
| Backfill / projection zorluğu | ⚠️ Yeni payment/vaccination event + processor (mevcut desen) | ⚠️ Composite çok-aggregate event | ❌ Çok zor (rapor/export parity) | ❌ Sıfırdan event + idempotency (movement'larda idempotency yok) | ✅ Yok (mevcut altyapı) |
| Test edilebilirlik | ✅ İyi (mevcut parity/smoke deseni) | ✅ İyi | ⚠️ Parity testi zor | ⚠️ Idempotency testi gerekli | ✅ İyi |
| Rollback kolaylığı | ✅ Flag + restart (mevcut desen) | ✅ Flag + restart | ✅ Flag + restart | ✅ Flag + restart | ✅ İçsel, davranış nötr |
| Blast radius | ⚠️ Orta-yüksek (yüksek görünürlük) | ✅ Dar | ❌ Geniş (finansal) | ⚠️ Orta | ✅ Dar |
| Mevcut altyapıya uyum | ✅ Mükemmel (`ClinicDaily*StatsReadModel` deseni birebir) | ⚠️ İyi ama composite | ⚠️ Strateji B karmaşık | ❌ Hiç projection/event yok | ✅ Mükemmel |
| Fazlara bölünebilirlik | ✅ Net (foundation→processor→reader→flag) | ✅ İyi | ⚠️ Zor | ⚠️ Zor | ✅ İyi |
| **Hemen kodlanabilir mi?** | ❌ Önce foundation/design (yeni event) | ❌ Önce design | ❌ Önce ağır audit | ❌ Önce greenfield design | ✅ **Evet** |

---

## 3. Önerilen hedef

**Stratejik CQRS-13 hedefi: Dashboard read-model / projection** (finance summary + operational alerts;
appointment dilimi zaten projeksiyonlu).

**İlk kodlanabilir faz (13A): Projection hardening** — çünkü:

1. Dashboard finance projeksiyonu **yeni bir projeksiyon consumer'ı** ekler; Client/Pet'te zaten var olan
   çok-instance duplicate riskini artırır. Önce claim/lease + parity-in-health + config hijyeni.
2. Hardening **sıfır yeni event** içerir, **hemen kodlanabilir**, ve 12D ile yeni açılan
   liste/report/export bağımlılık yüzeyini doğrudan de-risk eder.

Yani: **kısa vadeli kodlanabilir iş = hardening (13A)**, **orta vadeli kullanıcı değeri = dashboard
finance/operational projeksiyonu (13B–13D)**. İkisi tek bir CQRS-13 zincirine sıralanır.

---

## 4. Neden bu hedef?

- **En yüksek kullanıcı perf katkısı doğru yüzeyde:** Finance summary tek istekte 6 aggregate + 7 günlük
  tüm payment satırı taraması (`PaymentsPaidAtAmountInWindowSpec` → in-memory bucket) + recent list +
  client/pet N+1 yapıyor; operational alerts 5 ayrı count. Dashboard ana ekran olduğu için bu, projeksiyona
  taşınınca en görünür kazanım.
- **Mevcut altyapıya birebir uyum:** `ClinicDailyAppointmentStatsReadModel` + `AppointmentProjectionProcessor`
  + activity read-model deseni **aynen** finance günlük istatistiğine uygulanabilir
  (`ClinicDailyPaymentStatsReadModel`). Yeni desen icat etmeye gerek yok.
- **Düşük rollback maliyeti:** Dashboard zaten `DashboardAppointmentsEnabled` flag deseniyle çalışıyor;
  finance için ayrı `DashboardFinanceReadEnabled` flag + restart ile aynı rollback hikâyesi.
- **Finansal risk sınırlı:** Dashboard finance **read-only özet aggregate**; otoriter CSV/XLSX export
  **değil**. Stale read-model en kötü ihtimalle özet sayıyı yaşlandırır; para hareketi/otorite üretmez.
- **Hardening önceliği mantıklı:** 12D zinciri liste/report/export'u Client/Pet projeksiyonlarına bağladı;
  bu projeksiyonların çok-instance güvenliği (claim/lease) ve parity gözlemlenebilirliği şu an Appointment'ın
  gerisinde. Dashboard yeni bir consumer eklemeden önce bu borcu kapatmak hem 12D'yi hem 13B+'yı korur.

---

## 5. Neden diğerlerini şimdi değil?

- **2. Examination read-model:** Detail/related-summary temiz composite read-model adayı ama düşük trafik +
  5–6 aggregate üzerinden yeni event emission gerektirir (child aggregate'ler event yaymıyor). ROI dashboard'ın
  altında; dashboard'dan sonra doğal bir 13E/14 adayı.
- **3. Payment read-model:** **En yüksek finansal risk.** Rapor `totalAmount` in-memory sum (93 güne kadar
  tüm satır) + `MaxExportRows=50.000` otoriter export; parity testi son derece zor. 12D-6/8/9 dokümanları
  bunu bilinçli erteledi ("export en kritik yüzey"). Ayrı, ağır bir parity audit fazı olmadan girilmemeli.
- **4. Stock read-model:** Okuma zaten ucuz (`QuantityOnHand` write-time snapshot, query-time sum yok). Stok
  için **hiç integration event / projection / outbox yok** ve `StockMovement`'ta **idempotency yok**
  (`ReferenceType/Id` unique değil). Sıfırdan event + idempotency + projection kurmak yüksek maliyet, düşük
  perf getirisi. En zayıf aday.
- **5. Hardening (tek başına headline olarak):** Doğru ve hemen kodlanabilir, ama tek başına kullanıcıya
  doğrudan perf getirmez. Bu yüzden **headline değil, 13A enabler** olarak önerildi — atılmıyor, öne alınıyor.

---

## 6. Önerilen CQRS-13 faz planı

| Faz | Başlık | Kapsam | Yeni event? | Hemen kodlanabilir? |
|---|---|---|---|---|
| **13A** | Projection hardening + finance read-model design | Client/Pet processor'a claim/lease (lease kolonları migration'da zaten var: `AddOutboxClaimLeaseColumns`); parity'yi health Degraded sinyaline bağla; config hijyeni (`ClientProjection` appsettings, default `Enabled` hizalama); `IAppointmentReadModelParityReader` ya da paylaşılan parity servisi; **+ finance read-model design notu** (payment event emission tasarımı) | Hayır | ✅ Evet |
| **13B** | Dashboard finance read-model foundation | `Payment` integration event emission (write-path outbox); `ClinicDailyPaymentStatsReadModel` entity + EF config + Query DB migration; idempotency (`ProcessedProjectionEvents`) | Evet (payment) | Hayır (13A sonrası) |
| **13C** | Finance projection processor + backfill + health + parity | `PaymentProjectionProcessor` (mevcut desen) + hosted service + `backfill-payment-stats-projections` DbMigrator komutu + `payment-projection` health check + parity reader + acceptance/smoke script | — | Hayır |
| **13D** | Finance reader + flag routing + rollout | `DashboardFinancePaymentAggregatesReader` Query DB yolu; `DashboardFinanceReadEnabled` flag; `GetDashboardFinanceSummaryQueryHandler` routing (flag false=Command DB, true=Query DB); rollout/acceptance/smoke; doküman | — | Hayır |
| *(13E ops.)* | Operational alerts + examination | Vaccination overdue/upcoming stats projeksiyonu; ardından examination detail read-model | Evet | Hayır |

> **Önemli:** 13B–13D **write-path event emission** içerir (payment outbox event'i). Bu, saf bir read-side
> faz değildir; blast radius'u Command DB write yoluna kadar uzatır. Bu yüzden 13A foundation/design ile
> ayrılması zorunludur.

---

## 7. İlk implementation fazı (13A) için net kapsam

**Yalnızca aşağıdakiler; production okuma davranışı değişmez (flag default'ları aynı):**

- **Client/Pet processor claim/lease:** `AddOutboxClaimLeaseColumns` migration'ı zaten lease kolonlarını
  ekledi; Appointment'taki `ProcessClaimBatchAsync` desenini `ClientProjectionProcessor` ve
  `PetProjectionProcessor`'a opt-in (`ClaimingEnabled`, default mevcut FIFO davranışı koruyacak şekilde)
  port et. **Davranış flag default'ta değişmez.**
- **Parity gözlemlenebilirliği:** `IClientReadModelParityReader` / `IPetReadModelParityReader` zaten var;
  Appointment için eşdeğer C# parity reader ekle veya `Get-CqrsStagedParityReport` mantığını paylaşılan
  servise çıkar. (Health evaluator'a parity sinyali eklemek opsiyonel; eklenirse yalnız Degraded.)
- **Config hijyeni:** `ClientProjection` bölümünü tüm `appsettings.*`'e açıkça ekle; `PetProjectionOptions`
  default `Enabled` değerini Client/Appointment ile hizala (operasyonel asimetriyi gider). **Default
  read-flag'ler `false` kalır.**
- **Naming/struct hizalama (opsiyonel, düşük öncelik):** `IAppointmentProjectionProcessor` arayüzünü
  `Application/Projections/Appointments` altına taşıma; rebuild vs backfill terminolojisini dokümante etme.
- **Finance read-model design notu:** `docs/cqrs/cqrs-13b-...` taslağı — payment integration event sözleşmesi,
  `ClinicDailyPaymentStatsReadModel` şeması, backfill stratejisi.

**13A'da YAPILMAYACAK:** Yeni read-flag default'unu `true` yapma; payment event emission (13B); dashboard
handler routing (13D); migration ekleme (lease kolonları zaten var); export/report davranışına dokunma.

---

## 8. Test / acceptance beklentileri

**13A:**

- Client/Pet claim/lease unit + integration testleri (Appointment claim testleriyle paritede:
  dedup, ordering, çok-instance drain).
- `ClaimingEnabled=false` (default) → mevcut FIFO davranışı bit-for-bit korunur (regression).
- Parity reader unit testleri (in-sync / drift senaryoları).
- Mevcut Client/Pet/Appointment projection ve 12D smoke testleri **etkilenmez**.
- CI-safe acceptance script güncellemesi (varsa) + `Test-Cqrs*RolloutAcceptance.ps1` parse/sequence yeşil.

**13B–13D (ileride):**

- Payment event emission outbox testleri (idempotency, ordering).
- `ClinicDailyPaymentStatsReadModel` projeksiyon + backfill + parity testleri.
- Dashboard finance **parity testi:** flag false (Command DB) vs true (Query DB) **aynı** today/week/month
  total + 7 günlük trend üretir (dolu read-model ile).
- Stale/empty read-model + flag true → fallback yok; özet eksik olabilir (dokümante).
- Tenant/clinic isolation; `DashboardFinanceReadEnabled` rollout/rollback smoke.

---

## 9. Riskli görülen alanlar

- **Write-path genişlemesi (13B):** Payment integration event emission Command DB write yoluna dokunur;
  finansal aggregate kaynağıdır. Outbox emission'ı atomik (aynı transaction) ve idempotent olmalı.
- **Çok-instance duplicate (mevcut):** Client/Pet bugün FIFO-only; çok-instance deployment'ta duplicate
  işleme riski var. 13A bunu kapatmadan dashboard yeni consumer eklemek riski artırır.
- **Finance parity hassasiyeti:** Dashboard total'ları kullanıcı güveni açısından kritik; stale read-model
  yanlış finansal özet gösterebilir. Fallback yok → backfill + parity + health kapısı zorunlu.
- **Payment read-model'e erken giriş:** Rapor/export parity'si (in-memory `totalAmount`, `MaxExportRows`)
  ayrı ağır audit olmadan ele alınmamalı; 13 kapsamı dışı tutuldu.

---

## 10. Değişen dosyalar

- `docs/cqrs/cqrs-13-next-read-model-target-audit.md` *(bu doküman — yeni)*

**Production C# / migration / flag / appsettings değişmedi.**

---

## 11. Commit

**Commit atılmadı.** Audit yalnız doküman ekledi; implementation fazı (13A) ayrı başlatılacak.
