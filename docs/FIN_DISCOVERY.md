# FIN kod keşfi (Aşama 3: temel ücret, tahsilat, bakiye)

Salt okunur keşif; kod, migration veya sözleşme değişikliği yok. Taban: `main` @ `8e4b0f8` (2026-10-10). Girdi: ADR-012 (taslak), ana plan bölüm 8, FIN-001..004, ADR-009. Doğrulanmayan yerler "bulunamadı" veya "doğrulanmadı" diye işaretlidir.

## Özet

- **Var:** `Payment` yalnızca tahsilat kaydıdır (tutar, para birimi, yöntem, tarih, not, opsiyonel pet/randevu/muayene). Create/Update/GetById/List uçları, rapor/export, dashboard finans, Query DB projeksiyonu (hepsi bayrakla, varsayılan kapalı) mevcut.
- **Yok:** ücret/borç kaynağı, bakiye, ödeme–ücret ilişkisi, ödeme durumu (iptal/iade), silme ucu, `RowVersion`/eşzamanlılık belirteci, ödeme audit'i, tekrar-istek (idempotency) koruması, `VisitId` bağı. Klinik kayıtlarda (Tedavi/Reçete/Aşı/Lab/Yatış/Muayene) fiyat alanı yok.
- **Riskler:** (1) `PUT /payments/{id}` tutarı/müşteriyi gerekçe ve audit olmadan değiştirir; ilişkilendirme gelince bu bütünlüğü bozar. (2) Ödeme okuyan yüzey çok (liste, rapor, export, dashboard, özet, Bugün, Query DB üç tablo); iptal/iade eklemek hepsine dokunur. (3) Üç "bilinen başarısız" test gerçek ve `main`'de de kırık (bölüm 2).

## 1. Payment modeli

| Konu | Bulgu |
|---|---|
| Alanlar | `Id, TenantId, ClinicId, ClientId (zorunlu), PetId?, AppointmentId?, ExaminationId?, Amount decimal(18,2) > 0, Currency (3 harf), Method (Cash=0/Card=1/Transfer=2), PaidAtUtc, Notes ≤4000` (`Payment.cs`, `PaymentConfiguration.cs`) |
| Durum / iptal | **Yok.** Status, void, iade alanı yok; silme ucu yok. Tek değiştirme yolu `PUT` (tüm alanları üzerine yazar) |
| Kurallar | Tutar > 0; `PaidAtUtc` en fazla 7 gün geçmiş, 2 yıl ileri (`PaymentPaidAtWindow`); pet müşteriye, randevu/muayene kliniğe ve müşteriye ait olmalı; klinik yazma kapsamı `PaymentClinicWriteScope` |
| Uçlar | `POST /payments` (201), `PUT /payments/{id}` (204), `GET /payments/{id}`, `GET /payments` (sayfalı, filtre+arama). `DELETE` yok |
| Yetkiler | `Payments.Read/Create/Update` (`PermissionCatalog.cs`) |
| `appointmentId` / `examinationId` | İkisi de nullable, FK/benzersizlik yok; yalnızca doğrulama ve raporlama amaçlı. Bir randevu/muayeneye birden fazla ödeme bağlanabilir. `VisitId` yok; Bugün'deki `PaymentRecorded` göstergesi Visit'in randevusu veya Visit'e bağlı muayenelerin ödemelerinden türetilir (`VisitTodayReader`) |
| Para birimi | Zorunlu alan, ISO 3 harf, **herhangi bir kod kabul edilir** (TRY'ye kilitli değil; domain yorumu "varsayılan TRY" der ama komut zorunlu ister). Kur/dönüşüm yok |
| Audit | `IAuditableRequest` **yok** (Visit düzeltme komutlarında var). Ödeme değişikliği için kalıcı iz yok; yalnızca outbox olayı (`payment.created.v1`, `payment.updated.v1`) |
| Eşzamanlılık | Payment'ta `RowVersion`/concurrency token yok. Aynı `POST` iki kez gönderilirse iki ödeme oluşur (idempotency anahtarı bulunamadı) |

## 2. Query DB ve finans okuma yolları

Tüm `QueryReadModels:*` bayrakları ve `PaymentProjection:Enabled` varsayılan `false` (Development ve IntegrationTests appsettings'te doğrulandı); varsayılan yol Command DB.

| Yüzey | Command DB yolu | Query DB (bayrak) |
|---|---|---|
| Liste / arama | `PaymentsListFilteredPagedSpec` | `PaymentsListReadEnabled`, `PaymentsSearchLookupEnabled` |
| Detay | `PaymentByIdSpec` | `PaymentsGetByIdReadEnabled` |
| Rapor / export | `PaymentsFiltered*Spec` | `PaymentsReportReadEnabled`, `PaymentsReportExportReadEnabled` |
| Dashboard finans | `DashboardFinancePaymentAggregatesReader` | `DashboardFinanceReadEnabled` (`ClinicDailyPaymentStats`, `PaymentDailyContribution`) |
| Dashboard son ödemeler | `PaymentsForDashboardRecentSpec` | `DashboardRecentPaymentsReadEnabled` |
| Müşteri ödeme özeti | `PaymentsForClientSummaryRowsSpec` | `ClientPaymentSummaryReadEnabled` |

Projeksiyon: outbox → `PaymentProjectionProcessor` → `PaymentReadModels` + iki günlük özet tablosu; backfill, parity ve health servisleri var. **Bugün sorgusu Query DB kullanmaz**, Command DB'den okur (`docs/VISITS_API_CONTRACT.md`).

### Bilinen başarısız testler (teşhis; düzeltme yapılmadı)

Çalıştırma: yalnızca filtreli; Application ve IntegrationTests projeleri. `main` ile bu dal aynı commit (`git rev-parse` doğrulandı), yani **`main`'de de kırık**.

| Test | Sonuç | Kök neden | Tür |
|---|---|---|---|
| `PaymentCommandHandlerOutboxEmissionTests` | 2/4 kırık (`Create_..._CorrectPayload`, `Update_..._CorrectPayload`) | Test sabit tarih kullanır (`2026-06-20` / `2026-06-21`). `PaymentPaidAtWindow.Validate` 7 günden eski ödemeyi reddeder; bugün 2026-10-10 olduğu için handler `Payments.PaidTooFarInPast` döner. Not: hata kodunu çalıştırıp yazdırmadım, çıkarım kod okumasıdır | Test hatası (zamana bağımlı sabit tarih), gerçek ürün hatası değil |
| `PaymentFinanceRolloutAcceptanceIntegrationTests` | 4/8 kırık (diğer 4 geçti) | Dashboard finans handler'ı test içinde gerçek `IClientContext` ile kurulur; HTTP bağlamı olmadığı için `UserId` boş, `ClinicReadScopeResolver` `Auth.Unauthorized.UserContextMissing` döner. Geçici tanı çıktısıyla doğrulandı (değişiklik geri alındı, `git status` temiz). Bir test buna bağlı `NullReferenceException` verir (`Value` null) | Test altyapısı: handler'a kullanıcı bağlamı verilmiyor |
| `DashboardFinanceQueryParityIntegrationTests` | 4/4 kırık | Aynı neden (`DashboardFinanceQueryParityTestSupport.InvokeFinanceHandlerAsync`). Randevu testleri bu yüzden geçiyor: `FixedClientContext` kullanıyorlar | Aynı |

Not: Kullanıcı bağlamı koşulu `832da6f` (2026-05-07, klinik okuma kapsamı) ile resolver'a girdi; finans testlerinin hangi tarihten beri kırık olduğunu doğrulamadım. Ortam sorunu değil (LocalDB çalıştı; çalıştırma sonrası `Vetinity%` veritabanı kalmadı, sorguyla doğrulandı). **FIN öncesi bu üç testin onarımı gerekir**, çünkü finans yazma/okuma yolunu koruyan parity ağı şu an kör.

## 3. `ClientPaymentSummary`

`GET /clients/{id}/payment-summary`: ödeme sayısı, para birimine göre toplam tahsilat, tek birimse toplam, son ödeme zamanı, son N ödeme. Klinik kapsamı `IClinicReadScopeResolver` ile; Query DB ayrımı bayrakla. **Borç veya ücret kavramı yok.**

Bakiye için yeniden kullanım: yalnızca desen (kapsam çözümü, para birimine göre gruplama, çoklu birimde tek toplam vermeme). `totalPaidAmount` anlamı değiştirilmemeli; bakiye **ayrı uç** olmalı (sözleşme kırılmaz). Bakiye parayı etkilediği için eventually-consistent Query DB projeksiyonundan değil, ücret ve ilişkilendirmenin yazıldığı Command DB'den okunmalı (Bugün ile aynı karar).

## 4. Ücret bağı ve mevcut kayıtlar

| Varlık | Bağ | Fiyat/ücret alanı |
|---|---|---|
| `Visit` | `PetId`, `ClinicId`, `AppointmentId?`; **ClientId yok** (pet üzerinden); `MutationSequence` concurrency token; `VoidedAtUtc/VoidReason` deseni | Yok |
| `Examination` | `PetId`, `ClinicId`, `AppointmentId?`, `VisitId?` (eski ve gelişsiz muayenede null), `RowVersion` | Yok |
| `Treatment`, `Prescription`, `Vaccination`, `LabResult`, `Hospitalization` | `PetId`, `ClinicId`, `ExaminationId?` (Hospitalization/Lab/Vaccination dahil) | **Yok** (başlık/açıklama/not alanları var, tutar yok) |
| `Product` (stok) | — | `UnitPrice decimal`, `Currency`; hizmet kataloğu değil, klinik işlemlere bağlı değil. `VaccineDefinition`'da fiyat yok |

Öneri: `Charge` varlığı `TenantId, ClinicId, ClientId (zorunlu, Payment ile aynı), PetId?, VisitId?, ExaminationId?` taşır. Gerekçe: Visit ana akış ama muayenesiz/gelişsiz ücret (ürün satışı, eski kayıt, telefon) mümkün olmalı; ClientId Visit'ten türetilemez (pet üzerinden), bakiye sahip bazında olduğu için Charge'a yazılmalı. Visit ve Muayene'nin tutarlılığı (aynı pet, aynı klinik) Payment'taki doğrulama desenindedir. "Muayenesiz ücret gerekli mi" ürün sorusudur (karar 1).

## 5. Tahsilat–ücret ilişkilendirme: en küçük temiz tasarım

| Seçenek | Değerlendirme |
|---|---|
| `Payment.ChargeId` (genişletme) | En küçük, ama bir ödeme yalnızca tek ücreti karşılar; "tek tahsilat, iki ücret" ve "ücretin bir kısmı" ifade edilemez. Bölme için ödeme parçalamak gerekir |
| **`PaymentAllocation` tablosu (öneri)** | `Id, TenantId, PaymentId, ChargeId, Amount, CreatedAtUtc, CreatedByUserId, VoidedAtUtc?`. Kısmi ödeme, çok ücretli tahsilat ve "aynı para iki borcu kapatamaz" doğal ifade edilir. Eski ödemeler kayıtsız kalır (hayali borç türetilmez) |

Değişmezler (domain + veritabanı):
- Aynı para birimi: `payment.Currency == charge.Currency`.
- `Σ allocation(payment) ≤ payment.Amount` ve `Σ allocation(charge) ≤ charge.Amount`. Her iki toplam için `AllocatedAmount` sayacı + `CHECK (AllocatedAmount <= Amount)`; sayaç `Charge` ve `Payment` üzerinde concurrency token ile korunur.
- Eşzamanlılık: Visit desenini izle (`MutationSequence` + `IsConcurrencyToken`, `DbUpdateConcurrencyException` → `*.ConcurrencyConflict` → 409). İki kullanıcı aynı ücrete yazarsa biri 409 alır; tekrar denendiğinde güncel kalanı görür.
- Çift istek: `POST` için istemci `Idempotency-Key` (veya komutta `ClientRequestId`) + `(TenantId, ClientRequestId)` benzersiz indeksi önerilir; aynı anahtar aynı sonucu döner. Yeni tahsilat+tahsis tek komut ve tek `SaveChanges` olmalı. Allocation için `(PaymentId, ChargeId)` benzersiz indeksi ikinci savunmadır.
- `Payments.Update` ile tutar düşürme/müşteri değiştirme, tahsis varsa reddedilmeli ya da yalnızca düzeltme yetkisi + gerekçe ile yapılmalı (bölüm 6).

Uçlar (taslak): `POST /charges`, `GET /charges?visitId|clientId`, `POST /payments` opsiyonel `allocations[]` (geriye uyumlu), `POST /payments/{id}/allocations`, `GET /clients/{id}/balance`.

## 6. İzinler

| Rol | Mevcut Payments | Visits.Correct |
|---|---|---|
| Admin, Owner, ClinicAdmin | Read, Create, Update | var |
| Sekreter | Read, Create, Update | yok |
| Veteriner | **yalnız Read** | var (`RolePermissionBindings.cs`) |

Önerilen yeni izinler (Visits desenine uygun; ürün onayı gerekir):

| İzin | Anlam | Roller (öneri) |
|---|---|---|
| `Charges.Read` | Ücret ve bakiye görme | Admin, Owner, ClinicAdmin, Sekreter, Veteriner |
| `Charges.Create` | Manuel ücret satırı ekleme | Admin, Owner, ClinicAdmin, Sekreter, Veteriner (hekim muayene sonunda ücret ekler) |
| `Charges.Correct` | Ücret tutarı değiştirme/iptal (gerekçe + audit) | Admin, Owner, ClinicAdmin |
| `Payments.Correct` | Tahsilat iptal/iade/tutar düzeltme (gerekçe + audit) | Admin, Owner, ClinicAdmin |

Mevcut `Payments.Update` bugün gerekçesiz tutar değiştirir; FIN-003 sonrasında yalnızca yetki dışı alanlarda (not, yöntem) kalmalı, tutar/müşteri/klinik değişimi `Payments.Correct`'e geçmeli. Veteriner'in tahsilat (`Payments.Create`) alması ayrı karardır; ADR-012 bunu söylemiyor.

## 7. Öneriler (ADR-012 karar noktalarıyla eşleşme)

| Konu | Öneri | ADR-012 maddesi |
|---|---|---|
| Fazla ödeme | İlk dilimde **reddet** (`Σ allocation ≤ charge.Amount`; tahsis edilmeyen kısım ödemede "bağlanmamış" kalır, kredi olarak sayılmaz) | Karar 4 |
| Kısmi ödeme | Allocation ile doğal; kalan = `charge.Amount − allocated` | — (karar yok) |
| İptal/iade | Silme yok. Ücret: `VoidedAtUtc/VoidReason` + `Charges.Correct`. Ödeme: aynı desen + `Payments.Correct`; tahsisler birlikte iptal edilir. Negatif tutarlı ters kayıt **önerilmez** (`Amount > 0` kuralı ve beş okuma yüzeyi bozulur). Bedeli: tüm ödeme okuma yolları ve projeksiyon (`payment.voided.v1`) iptal edileni dışlamalı | Karar 2 |
| Para birimi | İlk dilimde TRY'ye kilitle (validator), alan kalsın; bakiye para birimine göre ayrı hesaplansın, çok birimde tek toplam yok (mevcut özet davranışı) | Karar 3 |
| Yuvarlama / vergi | `decimal(18,2)`, `MidpointRounding.AwayFromZero`; KDV modellenmez (fiyat tek tutar). Belge/vergi alanları ADR gereği kapsam dışı; kesinleşmeden e-belgeye taşınmamalı | Belge satırı |

## 8. Teslim dilimleri

| # | Dilim | Bağımlılık | Test odağı | Karar gerekir mi |
|---|---|---|---|---|
| 0 | Üç kırık testi onar (sabit tarihi göreli yap; finans testlerine `FixedClientContext`) | — | Parity ağının yeşil dönmesi | Hayır (bakım) |
| 1 | `Charge` varlığı + migration (uygulama onayı ayrı) + `Charges.*` izinleri + `POST/GET /charges` (ücret satırı, TRY, Visit/Muayene bağı, iptal yok) | 0, karar 1–2 | Müşteri/pet/klinik/visit tutarlılığı, yetki, klinik kapsamı, tenant izolasyonu | Evet (1) |
| 2 | `PaymentAllocation` + sayaçlar + `CHECK` + concurrency; `POST /payments` ile tahsis, idempotency anahtarı | 1 | 1000/400/600 kabul senaryosu; çift istek; iki kullanıcı (409); fazla ödeme reddi; para birimi uyumsuzluğu; `PUT /payments` kısıtı | Evet (fazla ödeme, para birimi) |
| 3 | `GET /clients/{id}/balance` (Command DB; ücret, ödenen, kalan; Visit/pet kırılımı) | 2 | Para birimine göre ayrı hesap; klinik kapsamı; bağlanmamış tahsilat ayrı satır | Hayır |
| 4 | Bugün satırına ücret/ödenen/kalan alanları (`PaymentIndicator` korunur, ek alan) | 3 | Bakım durumunu etkilememe; eski göstergenin geriye uyumu | Hayır |
| 5 | Düzeltme: ücret/ödeme iptal, tutar değiştirme, `Payments.Correct`/`Charges.Correct`, audit, gerekçe; okuma yollarının iptal edileni dışlaması; `payment.voided.v1` | 2, 3 | Dashboard/rapor/export/özet/Query DB parity (iptal sonrası); tahsisin geri düşmesi; audit kaydı | Evet (rol) |

FIN-004 (arayüz) dilim 3 sonrası başlayabilir. Query DB projeksiyonu bakiye için kullanılmaz; yalnızca dilim 5'te mevcut ödeme projeksiyonu iptali tanıyacak şekilde genişler.

## Kullanıcıdan karar gereken noktalar

1. **Ücret bağı:** `ClientId` zorunlu, `VisitId` ve `ExaminationId` opsiyonel (öneri) mi, yoksa Visit zorunlu mu? Muayenesiz/gelişsiz ücret (ürün satışı vb.) ilk dilimde gerekli mi?
2. **Düzeltme yetkisi:** `Payments.Correct`/`Charges.Correct` yalnızca Admin/Owner/ClinicAdmin (öneri) mi; Sekreter mevcut `Payments.Update` ile tutar değiştirebilmeye devam edecek mi? Veteriner `Charges.Create` alsın mı?
3. **Para birimi:** İlk dilimde yalnız TRY (öneri) yeterli mi?
4. **Fazla ödeme:** Reddet ve tahsis edilmeyen kısmı "bağlanmamış tahsilat" olarak tut (öneri), yoksa hiç kabul etme?
5. **Mevcut tahsilatlar:** Eski `Payment` kayıtları ücretsiz "bağlanmamış" kalır (hayali borç yok, ADR'ye uygun). Bunlar bakiyede ayrı satır olarak mı gösterilsin, yoksa hiç gösterilmesin mi?
