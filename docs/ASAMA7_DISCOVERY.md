# Aşama 7 kod keşfi — yatış görünürlüğü, bakım tamamlama, taburcu talimatı ve takip, aşı/kontrol hatırlatmaları

Salt okunur keşif (ürün kodu değişmedi, migration yok, test/derleme çalıştırılmadı, DB'ye bağlanılmadı, HTTP ile uç denenmedi, e-posta gönderilmedi). Taban: yerel `main` `6d2f10e`. Kaynaklar: ana plan §12 (Aşama 7), `roadmap.md`, `AJAN-KUYRUGU.md`, `KOORDINATOR-DEVIR.md`, `ASAMA4/5_DISCOVERY.md`. Backend + frontend (`C:\FrontEnd\veteriner-frontend`, `main` `0ce6172`) okundu. Aşama 5 (stok/Uygulama/reçete) ve Aşama 6 (lab) alanlarına yalnız bakıldı. Doğrulanmayan her şey "çıkarım" diye işaretli; son bölümde toplandı.

## 1. Mevcut durum

### 1.1 Yatış (`Hospitalization`)

| Alan | Değer |
|---|---|
| Alanlar | `AdmittedAtUtc`, `PlannedDischargeAtUtc?`, `DischargedAtUtc?`, `Reason` (≤2000, zorunlu), `Notes?` (≤4000), `CreatedAtUtc/UpdatedAtUtc` |
| Bağlar | `TenantId`, `ClinicId`, `PetId`, `ExaminationId?` (var ise klinik+hayvan uyumu doğrulanır) |
| **Olmayanlar** | `VisitId`, sorumlu hekim, konum (oda/kafes), ayrı "bakım talimatı/taburcu talimatı" alanı, taburcu nedeni/sonucu, `RowVersion`, iptal/yanlış kayıt kavramı |
| Durum modeli | Açık durum alanı yok; **türetilir**: `DischargedAtUtc == null` → aktif (`IsActive`). Giriş / planlanan çıkış / gerçek çıkış zaten üç ayrı alan (plan #1 bu kısmı karşılıyor). |
| Kurallar | Hayvan başına klinikte **tek aktif yatış** (filtreli benzersiz indeks `IX ... [DischargedAtUtc] IS NULL` + handler `ActiveHospitalizationExists`). Taburcu edilmiş yatış güncellenemez (`AlreadyDischarged`). Taburcu zamanı girişten önce olamaz. Taburcuda `Notes` yalnız body'de gönderilirse **üzerine yazılır** (kısa not ile talimat aynı alan). Giriş zamanı penceresi `AdmittedAtUtcWindow`. |
| Yazma güvenliği | Tedavi/stok gibi `ITransactionalRequest`/`IAuditableRequest` işaretleri yatış komutlarında **yok** (grep: `Hospitalizations/` altında geçmiyor). Concurrency token yok: iki kullanıcı aynı anda güncelleyince son yazan kazanır (çıkarım, handler/entity okundu). Taburcu zaten tekrarda `AlreadyDischarged` döner (idempotent değil, hata). |

### 1.2 Uçlar ve izinler

| Uç | İzin |
|---|---|
| `POST /hospitalizations` | `Hospitalizations.Create` |
| `PUT /hospitalizations/{id}` | `Hospitalizations.Update` |
| `POST /hospitalizations/{id}/discharge` (`dischargedAtUtc`, `notes?`) | `Hospitalizations.Discharge` |
| `GET /hospitalizations/{id}`, `GET /hospitalizations` (`petId`, `clinicId`, `activeOnly`, tarih, arama) | `Hospitalizations.Read` |

Roller (`RolePermissionBindings`, satır aralıklarından): Admin, Owner, ClinicAdmin, Veteriner → Read/Create/Update/Discharge; **Sekreter yalnız Read** (satır 440). Taburcu için ayrı izin var (iyi), ama "talimat yaz" için ayrı izin yok.

### 1.3 Yatışın diğer yüzeylerle bağlantısı (backend)

| Yüzey | Durum |
|---|---|
| **Bugün** (`GET /visits/today`) | `TodayDto.ActiveHospitalizations` (liste: id, hayvan, sahip, giriş, planlanan çıkış; klinik bazlı, `DischargedAtUtc == null`) ve satır bazlı `HasActiveHospitalization` var (`VisitTodayReader`). **Notlar/konum/talimat alanı yok.** |
| **Hayvan özeti** (`/pets/{id}/summary`) | `ActiveHospitalization` bölümü: id, klinik adı, giriş, planlanan çıkış, `Reason`, `ExaminationId`. **`Notes` yok.** |
| **Hayvan geçmişi özeti / timeline** | Yatış tek olay (kabul tarihinde), `Status` = `Active`/`Discharged`; `RelatedVisitId: null` (bağ yok), `RelatedExaminationId` dolu. Taburcu tarihi timeline'da ayrı olay değil. |
| **Muayene ilişkili özet** | `Examination` → bağlı yatışlar listesi (`HospitalizationsForExaminationRelatedSpec`). |
| **Visit** | **Doğrudan bağ yok.** Yatış → Examination (opsiyonel) → `Examination.VisitId` zinciriyle dolaylı. Muayenesiz açılmış yatış hiçbir Visit'e bağlanamaz. Visit tamamlandığında yatışa bakılmaz; yatışta olan hastanın Visit'i tamamlanabilir ve tersi. |
| **Query DB / projeksiyon** | Yatış için read-model yok (`Projections/` altında yalnız Appointments, Clients, Payments, Pets; çıkarım: yatış doğrudan command DB'den okunuyor). |

### 1.4 Frontend yatış durumu

- Rotalar: liste, yeni, düzenle, detay (`features/hospitalizations`; menü "Yatışlar", `HOSPITALIZATIONS_READ_CLAIM`). Detayda "Taburcu et" diyaloğu var (salt okunur kipte devre dışı varyantı da var).
- **Bugün**: satırda "Yatışta" rozeti var; **`activeHospitalizations` listesi frontend'de hiç kullanılmıyor** (grep: yalnız pet-summary tarafında benzer ad). Yani backend'in döndürdüğü "aktif yatışlar" bloğu Bugün'de gösterilmiyor (plan #1 "klinik operasyon yüzeyi" açığı).
- Hayvan özeti kartı "Aktif yatış" ve detay sayfası "Son yatışlar" gösteriyor; muayene detayında "Yatış Başlat" ve "Bağlı yatışlar" var.
- Talimat/yazdırma/dışa aktarma: yatış, muayene ve genel alanlarda `print` kullanımı bulunamadı (grep boş; yalnız `examinations` ve `hospitalizations` altında arandı). Taburcu talimatı yüzeyi yok.

### 1.5 Bakım tamamlama (Visit)

- `VisitCareStatus`: Waiting → InProgress → Completed (+ düzeltme, yanlış geliş/geri alma, acil). `TransitionVisitCommandHandler`: yalnız durum geçişi; **tamamlanma kontrolü yok** (muayene kesinleşti mi, reçete, bekleyen sonuç, kısmi borç hiçbiri sorulmaz). `Visit` entity yorumu: "Ödeme ve açık iş göstergeleri bu durumdan ayrıdır" (plan #9 ile uyumlu bir zemin).
- İzinler: `Visits.Update` (ilerletme), `Visits.Correct` (gerekçeli düzeltme).
- Bugün DTO'su ücret/ödenen/kalan alanlarını (`OutstandingAmount`, `ClientOutstandingAmount`) taşıyor (Aşama 3) ama **bekleyen lab / açık takip / kontrol göstergesi yok**; frontend notu: "açık iş göstergesi backend'de yok".
- Hazır malzeme: `Examination` durumu (Taslak/Kesinleşmiş, Aşama 4 özet ucu bölüm bazlı durum veriyor), `Charge`/bakiye (Aşama 3), `LabResults` domain klasörü mevcut (Aşama 6 keşfi başka ajanda; bu keşifte okunmadı), tedavi/reçete (`FollowUpDateUtc`).

### 1.6 Takip / kontrol ve talimat alanları

- `Treatment.FollowUpDateUtc` ve `Prescription.FollowUpDateUtc`: yalnız tarih. Durum yok (açık/tamamlandı/iptal), atanan kişi yok, **hatırlatmaya veya randevuya bağlanmıyor** (`ReminderProcessorService` yalnız Appointment ve Vaccination okur). Görev (task) varlığı yok.
- Hasta sahibine evde bakım/taburcu talimatı için varlık, alan veya uç **yok**. En yakın alan: `Hospitalization.Notes` (≤4000, taburcuda üzerine yazılır) ve `Examination` serbest metinleri; hiçbiri "hasta sahibine verilecek metin" olarak ayrılmamış, üretim/yazdırma izi tutulmuyor (plan #6 "kaynağı izlenir").
- Kontrol randevusu önerisi: randevu oluşturma ayrı; muayene/tedaviden "kontrol randevusu oluştur" bağlantısı yok (çıkarım: ilgili komutlarda randevu üretimi görülmedi).

### 1.7 Aşı ve hatırlatma altyapısı

**Aşı (`Vaccination`)**: `Status` = Scheduled/Applied/Cancelled; `AppliedAtUtc`, `DueAtUtc`, `ExaminationId?`, `VaccineDefinitionId`. Uçlar `/vaccinations` (`Vaccinations.Read/Create/Update`). Plan #8 "planlı / yapılmış / gecikmiş ayrımı": hayvan özetinde son uygulanan + en yakın planlı ayrı, `IsOverdue` bayrağı var (`PetSummaryNextVaccinationDto`). Klinik genelinde "gecikmiş aşılar" listesi/uç gözlenmedi (çıkarım: liste ucunda filtre var mı bakılmadı).

**Hatırlatma hattı**:

- Varlıklar: `TenantReminderSettings` (kiracı bazlı: randevu hatırlatma açık/saat 1–168, aşı hatırlatma açık/gün 1–30, e-posta kanalı; **varsayılan hepsi kapalı**), `ReminderDispatchLog` (tür Appointment/Vaccination, kaynak, alıcı e-posta/ad, `ScheduledForUtc`, `ReminderDueAtUtc`, `Status` Pending/Enqueued/Sent/Failed/Skipped, `DedupeKey` (kiracı içinde benzersiz), `OutboxMessageId`, hata).
- Uçlar: `GET/PUT /reminders/settings` (`Reminders.Read` / `Reminders.Manage`), `GET /reminders/logs` (`Reminders.Read`, filtre: tür, durum, tarih, klinik). Manage: Admin/Owner/ClinicAdmin; Veteriner ve Sekreter yalnız Read.
- İşleyici: `ReminderProcessorHostedService` (varsayılan 5 dk) → `ReminderProcessorService.ProcessOnceAsync`. Randevu: `Scheduled` ve `ScheduledAtUtc` gelecekte ve `≤ şimdi + saat`. Aşı: `Scheduled` **veya** `Applied` ve `DueAtUtc` gelecekte ve `≤ şimdi + gün` (Applied → "sonraki uygulama tarihi" metni). Tekilleştirme: `appointment:{id}:hours-before:{n}` / `vaccination:{id}:days-before:{n}`; e-posta yoksa `Skipped` ("Recipient email is missing"). İçerik düz metin, sabit şablon, Türkiye saati biçimi.
- Gönderim: `ReminderEmailOutboxEnqueuer` outbox'a e-posta yazar; `OutboxProcessor` `IEmailSenderImmediate` (MailKit SMTP) ile gönderir; işleyici sonraki turda outbox `ProcessedAtUtc` doluysa log'u **`Sent`** yapar, `DeadLetterAtUtc` doluysa `Failed`. **`Sent` = SMTP'ye teslim edildi, alıcıya ulaştı değil** (bounce/teslim geri bildirimi yok; çıkarım: kodda webhook/bounce işleme görülmedi).
- Testler: `ReminderProcessorServiceTests` (entegrasyon, **sahte** outbox enqueuer ile), `RemindersEndpointTests`, log sorgu testleri. Gerçek SMTP ile uçtan uca test **yok**; bu keşifte de yapılmadı.
- Yapılandırma: `appsettings.json` Smtp bölümü `smtp.gmail.com:587` + kullanıcı/parola alanları (değerler bu belgeye yazılmadı); `Reminders:Processor:Enabled=true`. Gerçek ortamda (dev/pilot) SMTP kimliğinin dolu olup olmadığı bilinmiyor.
- Frontend: "Hatırlatmalar" (ayarlar) ve "Hatırlatma Geçmişi" (log listesi) sayfaları var (`features/reminders`, ayarlar menüsü altında). Hasta/aşı/randevu bağlamından hatırlatma görme, tek tek yeniden gönderme yok.
- Kontrol/takip, yatış ve taburcu için hatırlatma türü **yok** (`ReminderType` yalnız Appointment=0, Vaccination=1).

## 2. Ana plan §12 kabul kriterlerine göre eksikler ve riskler

| # | Plan maddesi | Durum | Eksik / risk |
|---|---|---|---|
| 1 | Aktif yatış hasta özetinde ve klinik operasyon yüzeyinde görünür; giriş/planlanan/gerçek çıkış ayrı | **Kısmen** | Backend alanları ve Bugün/özet verisi var. Eksik: Bugün'de aktif yatış listesi **frontend'de gösterilmiyor**; özet ve Bugün'de `Notes`/talimat yok. Timeline'da taburcu ayrı olay değil. |
| 2 | Konum/kısa bakım notu ve talimat görünürlüğü | **Eksik** | Konum alanı yok (backlog HOSP-002: kaynak hiyerarşisi TBD); "kısa bakım notu" = `Notes` (taburcuda ezilebilir, risk R1). |
| 3 | Ayaktan bakım tamamlama + yatış taburcusu bağlama, terminoloji zorunlu değil | **Eksik** | Visit ile yatış arasında bağ yok (R2); ayaktan "bakım tamamlandı" yalnız `Completed` durum geçişi. |
| 4 | Kısa tamamlanma kontrolü (klinik kayıt, işlem, reçete, takip, bekleyen sonuç, ücret/açık hesap) | **Yok** | `TransitionVisit` hiçbir kontrol yapmıyor. Lab sonucu ve takip bağımlılıkları Aşama 5/6 çıktısına bağlı. |
| 5 | Eksiği nedenle açıkla; yetkili gerekçeyle devam etsin; yetki/bütünlük bypass edilmez | **Yok** | Gerekçeli bypass için desen mevcut (`Visit.ValidateReason`, düzeltme) → yeniden kullanılabilir. |
| 6 | Hasta sahibine manuel evde bakım/takip talimatı; yazdırma/dışa aktarma kaynağı izli | **Yok** | Varlık, uç, şablon, yazdırma yok; "kaynak izi" için ne tutulacağı ürün kararı (D3). |
| 7 | Kontrol gereken vakada görev/randevu seçeneği | **Yok** | Görev varlığı yok (backlog: unified task worklist v1 dışı aday). `FollowUpDateUtc` yalnız tarih. |
| 8 | Planlı/yapılmış/gecikmiş aşı ayrı; e-posta hatırlatma uçtan uca doğrulanır; "ulaştı" denmez | **Kısmen** | Ayrım hayvan özetinde var, klinik listesi gözlenmedi. Hatırlatma hattı kodda tam ama **gerçek SMTP ile hiç doğrulanmadı**, `Sent` anlamı yanıltıcı olabilir (R5). |
| 9 | Bakım bitmiş, ödeme/lab takibi açık kalabilir; sorumlu ve sonraki aksiyon görünür | **Kısmen** | Ödeme göstergesi Bugün'de var (Aşama 3). Lab bekleyen ve "sorumlu kişi + sonraki aksiyon" yok. |

### Riskler

- **R1** Taburcuda `Notes` üzerine yazılması: talimat tek alanda tutulursa taburcu notu giriş notunu siler. Ayrı alan/varlık kararı gerekir.
- **R2** Visit–yatış bağı yok: muayenesiz yatış hiçbir geliş ile ilişkilenmez; "ayaktan tamamla / yatışa çevir" akışı kurulamaz. Visit tamamlanınca aktif yatış uyarısı yok.
- **R3** Yatış yazma yolunda concurrency token ve audit işareti yok; taburcu/güncelleme eşzamanlı çakışmada son yazan kazanır (çıkarım). Talimat metni eklenince veri kaybı riski büyür.
- **R4** Aşama 4 D1 (kesinleşmiş muayene kilidi) yatışa uygulanmıyor: kesinleşmiş muayeneye bağlı yatış `Update` ile değişebilir (çıkarım: `Hospitalizations/` altında `Finalized` kontrolü yok; HTTP ile denenmedi).
- **R5** `Sent` anlamı: yalnız SMTP kabulü. Arayüzde "Gönderildi" yazılırsa plan #8 ("ulaştı" demeyiz) çiğnenir; metin "E-posta gönderim sırasına alındı / SMTP'ye iletildi" düzeyinde kalmalı. Gmail SMTP günlük limit/uygulama parolası bağımlılığı pilot için kırılgan (çıkarım).
- **R6** Hatırlatma ayarları varsayılan kapalı ve klinik değil kiracı bazlı: pilot kliniği ayarı açmazsa hiç mail gitmez; ayrıca randevu hatırlatması `Scheduled` randevu için çalışıyor, ADR-009/010 ile `Visit`/`NoShow` durumları işleyicide ele alınmıyor (çıkarım: yalnız `AppointmentStatus.Scheduled` filtresi var, geliş oluşmuş randevu için de hatırlatma gidebilir).
- **R7** Aşı hatırlatması `Applied` kaydın `DueAtUtc`'unu da kapsar; aynı aşı için sonradan `Scheduled` yeni kayıt açılırsa iki ayrı hatırlatma gidebilir (çıkarım, dedupe kaydı bazlı).
- **R8** Alıcı e-postası yoksa `Skipped`; müşteriye "e-posta yok" geri bildirimi arayüzde yalnız log sayfasında (görünürlük düşük).
- **R9** İzin ayrımı: Veteriner `Reminders.Manage` yok; Sekreter yatışı yazamaz. Talimat yazma/yazdırma için yeni izin gerekirse seed uyarısı (yalnız birleşik main'den çalıştır) geçerli.
- **R10** Aşama 5/6 bağımlılığı: tamamlanma kontrolü (reçete, uygulama, bekleyen lab) o aşamaların çıktısını gerektirir; paralel yürütülürse sözleşmeler çakışır.

## 3. Önerilen dilimler

| Dilim | Kapsam | Migration | Paralel? |
|---|---|---|---|
| **0 — Doğrulama** | Gerçek SMTP ile test (pilot/dev hesabı, kullanıcı onayıyla): randevu + aşı hatırlatması uçtan uca; `Sent` anlamının sözleşmeye yazılması; `Skipped`/`Failed` mesajları. Kod değişikliği küçük (isteğe bağlı: `NoShow`/geliş oluşmuş randevuyu atlama düzeltmesi, R6). | Yok | Evet, her şeyden bağımsız |
| **1 — Yatış görünürlüğü (frontend ağırlıklı)** | Bugün'de aktif yatış bloğu (mevcut `activeHospitalizations`), özet/Bugün'de not görünürlüğü, timeline'da taburcu bilgisi (`Status` zaten var, taburcu zamanı eklenebilir). Backend: özete `Notes`/`DischargedAtUtc` alanları (DTO eklemesi). | Yok | Evet; sözleşme eklemesi çok küçük, Dilim 0 ile paralel |
| **2 — Yatış veri modeli** | Taburcu/evde bakım talimatı için ayrı alan(lar) (D2), isteğe bağlı konum (D5), `RowVersion` + `ITransactionalRequest`/audit, Visit ilişkisi (D1), kesinleşmiş muayene kilidi uyumu (R4). Taburcuda not ezme sorununu giderir. | **Evet** (tek migration, kullanıcı onayı) | Dilim 3'ten önce; Dilim 1'le dosya çakışması az |
| **3 — Taburcu/evde bakım talimatı çıktısı** | Hasta sahibine metin hazırlama, yazdırma/PDF veya e-posta, "hazırlandı/yazdırıldı" izi (D3). Yatışsız ayaktan bakım için de çalışır (D1 kararına göre muayene/Visit'e bağlı). | Büyük olasılıkla evet (talimat/çıktı kaydı); yalnız yazdırma ve iz yoksa hayır | Dilim 2'den sonra |
| **4 — Bakım tamamlama kontrolü** | Tamamlanma kontrol ucu (muayene, reçete, uygulama, takip, bekleyen lab, ücret/açık hesap) — salt okunur "eksikler + neden" yanıtı; gerekçeli devam (`Visits.Update` + gerekçe, `Visit.ValidateReason` deseni); Bugün'de açık iş göstergesi. **Aşama 5 ve 6 çıktısına bağlı** (reçete/uygulama/lab kısımları sonradan eklenebilir; ilk sürüm muayene+ücret+takip ile çıkabilir). | Gerekçe/çıktı kaydı tutulacaksa evet, yalnız hesap-okuma ise hayır | Dilim 2'den bağımsız başlayabilir; lab/reçete parçaları Aşama 5/6 sonrası |
| **5 — Kontrol/takip görevi** | `FollowUpDateUtc` + durum/atanan; "kontrol randevusu oluştur" kısayolu; zamanı gelen/gecikmiş takip listesi (Aşama 8 "açık işler" ile birleşir). | Evet (görev/takip varlığı) | Ürün kararı D4'e bağlı; sona bırak |
| **6 — Aşı/kontrol hatırlatma genişletmesi** | Klinik genelinde "gecikmiş/planlı aşı" listesi ve hatırlatma gönderim izi arayüzü (hayvan/aşı bağlamında "hatırlatma gönderildi"), kontrol hatırlatma türü (`ReminderType`). | Yeni `ReminderType` değerleri enum int olduğu için **migration gerekmeyebilir**; yeni kaynak varlık türü/alan eklenirse evet | Dilim 0'dan sonra, Dilim 5 ile birlikte |

Önerilen sıra: **0 ∥ 1 → 2 → 3 ∥ 4 (Aşama 5/6 durumuna göre) → 6 → 5**. Migration gerektirenler: 2, (3), (4), 5, (6). Her migration için kullanıcıdan ayrı onay gerekir (paylaşılan/yerel DB kuralı); migrationlı iki dilim aynı anda yürütülmemeli.

## 4. Kullanıcıya sorulacak ürün kararları

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| D1 | Bakım tamamlama ve yatış nasıl bağlansın? | **a)** Yatış doğrudan `VisitId` taşır (açarken seçilir; Visit tamamlanırken aktif yatış uyarısı). **b)** Bağ yalnız `ExaminationId` üzerinden kalır. **c)** Yatış kendi başına, Visit'ten bağımsız | **a**, `VisitId` opsiyonel. Gerekçe: "yatış taburcusu bağlama" (#3) ve Bugün/kontrol listesi için tek anahtar gerekir; b muayenesiz yatışı bağlayamaz. Eski kayıtlar null kalır. |
| D2 | Talimat nerede yaşasın? | **a)** `Hospitalization`'a ayrı `DischargeInstructions` alanı (+ ayaktan için muayene/Visit'e eş alan). **b)** Ayrı "Talimat" varlığı (yatış/muayene/Visit'e opsiyonel bağlı, sürüm/yazdırma izi). **c)** Mevcut `Notes` kullanılmaya devam | **b**. Gerekçe: ayaktan hasta için de lazım (#3), yazdırma izi (#6) ve tekrar düzenleme geçmişi ayrı kayıt ister; c talimatı not ezmeye açık bırakır (R1). Daha hızlı alternatif a; ama ayaktan akış yine sonra ayrı iş olur. |
| D3 | Talimat çıktısı ve "kaynak izi" | **a)** Yalnız ekranda yazdırma, iz yok. **b)** Yazdırma/PDF + "hazırlandı/yazdırıldı" kaydı (kim, ne zaman). **c)** E-postayla gönderim de (hatırlatma altyapısıyla) | **b** ilk sürüm. E-posta gönderimi SMTP doğrulaması (Dilim 0) bitince ayrı karar. |
| D4 | Kontrol/takip nasıl temsil edilsin? | **a)** Yalnız tarih (`FollowUpDateUtc`) + listede gösterim. **b)** Hafif "Takip" kaydı (durum, atanan, bağ: muayene/yatış/Visit; randevuya çevirme). **c)** Genel görev sistemi | **b**. Plan #7/#9 sorumlu ve sonraki aksiyonu ister, c kapsam dışı (backlog v1 sonrası), a bu ikisini karşılamaz. Aşama 8 "açık işler" bu kayıtları okur. |
| D5 | Yatış konumu ilk sürümde? | **a)** Serbest metin alan (ör. "Kafes 3"). **b)** Klinik tanımlı konum listesi (Ayarlar). **c)** Yok | **a**. Plan "yatak/oda kapasite yönetimi sonraki aşama" diyor; b tanım ekranı ve IA kararı ister (HOSP-002 TBD). |
| D6 | Tamamlanma kontrolü kapsamı ve engelleme | **a)** Tümü uyarı, hiçbiri engel değil, gerekçe istenmez. **b)** Uyarı + eksik varsa yetkili kullanıcı gerekçeyle devam eder; yetki/bütünlük hataları bypass edilmez. **c)** Bazı eksikler sert engel (ör. kesinleşmemiş muayene) | **b** (plan #5). Hangi eksiklerin gerekçe istediği: muayene taslak, ücret açık, bekleyen lab → öneri: hepsi gerekçeli devam; sert engel yok. Gerekçe kaydı (Visit düzeltme gerekçesiyle aynı 5–500 kural) kalıcı tutulur → migration. |
| D7 | Yatış kesinleşmiş muayene kilidine tabi mi? (R4) | **a)** Evet, Aşama 5 D3 gibi (kesinleşmişe bağlı yatış değişmez; düzeltme iptal+yeni). **b)** Hayır, yatış zaten canlı kayıt (taburcu bunu değiştirir) | **b** ama kesinleşmiş muayene bağını (ExaminationId) değiştirmeyi engelle; yatış notu/talimat serbest, audit'li. |
| D8 | Hatırlatma kapsamı | **a)** Yalnız mevcut randevu + aşı e-postası doğrulansın. **b)** Kontrol/takip hatırlatması da eklensin. **c)** SMS/WhatsApp de | **a** şimdi, **b** Dilim 5/6 ile. SMS ayrı aşama (backlog). |
| D9 | Hatırlatma ayarı varsayılanı ve kapsamı | **a)** Kiracı bazlı, varsayılan kapalı (mevcut). **b)** Pilotta yeni kiracı için aşı hatırlatması varsayılan açık. **c)** Klinik bazlı ayar | **a** kalsın, onboarding'de (Aşama 8) açma adımı. Klinik bazlı ayar ihtiyacı pilot geri bildirimine. |
| D10 | Gerçek gönderim testi | **a)** Kullanıcı kendi e-postasıyla, dev ortamında bir test hatırlatması; hangi SMTP hesabı kullanılacak. **b)** Yalnız sahte gönderici (mevcut test) | **a**, kullanıcı onayı ve hesap bilgisi gerekir (parola sohbete yazılmaz; user-secrets). Bu karar Dilim 0'ı açar. |
| D11 | "Gönderildi" metni (R5) | **a)** Arayüz `Sent` için "SMTP'ye iletildi" der, "ulaştı" demez. **b)** Teslim geri bildirimi (bounce/webhook) sağlayıcı değişikliğiyle sonra | **a**; **b** ESP seçimine bağlı, ayrı iş. |

## 5. Doğrulanmayanlar

- Derleme, test, API çağrısı, DB bağlantısı, e-posta gönderimi **yapılmadı**; bütün bilgi kod okumasından.
- Yatış komutlarında `ITransactionalRequest`/`IAuditableRequest` ve concurrency token olmadığı grep ile çıkarıldı (`Hospitalizations/` klasörü); pipeline davranışlarının başka bir kayıttan (genel behavior) uygulanıp uygulanmadığı bakılmadı.
- R4 (kesinleşmiş muayeneye bağlı yatış değişebilir) handler/grep çıkarımı; HTTP ile denenmedi.
- Rol/izin dağılımı `RolePermissionBindings.cs` satırlarından okundu (Sekreter yatış yalnız Read, Veteriner Reminders.Read); ilgili testler çalıştırılmadı.
- `ReminderProcessorService` içindeki randevu filtresi yalnız `Scheduled` randevuyu alıyor; geliş (Visit) oluşmuş randevuya hatırlatma gidip gitmediği çalıştırılarak denenmedi (R6).
- SMTP: `appsettings.json`'da Gmail ayarı var; kullanıcı/parola dev/pilot ortamında dolu mu, uygulama parolası geçerli mi **bilinmiyor**. Outbox'ın canlı çalıştığı ve `Sent` durumuna geçişi gerçek ortamda görülmedi. Teslim/bounce işleme olmadığı kod okumasından çıkarım.
- Aşı liste ucunun "gecikmiş" filtresi sunup sunmadığı bakılmadı; klinik genelinde gecikmiş aşı listesi olmadığı çıkarım.
- Frontend: Bugün'de `activeHospitalizations` kullanılmadığı grep ile (yalnız `features/visits` içinde) çıkarıldı; tarayıcıda görülmedi. Yazdırma yokluğu yalnız `examinations` ve `hospitalizations` klasörlerinde arandı. Frontend testleri çalıştırılmadı.
- Lab (`LabResults` domain) ve Aşama 5 Uygulama/reçete sözleşmeleri okunmadı; Dilim 4'ün bu parçaları onların son hâline bağlı.
- Query DB'de yatış read-model olmadığı `Projections/` dizin adlarından çıkarım; bu keşifte başka ayrıntı doğrulanmadı.
- Rakip davranışları (E-Vet, Provet, DaySmart) ana plandan alındı; yeniden doğrulanmadı. Pilot kliniğin talimat/taburcu şablon ihtiyacı ve gerçek e-posta toplama oranı bilinmiyor.
