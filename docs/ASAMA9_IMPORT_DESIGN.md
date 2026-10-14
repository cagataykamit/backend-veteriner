# Aşama 9 Dilim 7 — Müşteri / hasta / aşı geçmişi içe aktarma tasarımı

**Tür:** Yalnız tasarım + sözleşme. Ürün kodu, migration, seed, yapılandırma değişmedi; hiçbir ortama bağlanılmadı, derleme/test/API çağrısı yapılmadı. Bilgi kod ve belge okumasından.
**Taban:** backend `main` `40f0da3`. **Sözleşme:** `IMPORT_API_CONTRACT.md` (uç, alan, kural ayrıntısı burada tekrarlanmaz).
**Kaynak:** ADR-014 K8 (müşteri + hasta + aşı geçmişi; muayene/ödeme ilk sürümde yok), K4 (küçük, kâğıt/Excel kullanan pilot klinik, 2 hafta gölge kullanım), `ASAMA9_READINESS.md` Dilim 7 ve §7 doğrulanmayanlar.
**Gizlilik:** Gerçek müşteri/hasta verisi bu belgede ve depoda yok; test dosyaları sentetik olacak.

---

## 1) Özet (sade)

- Pilot kliniğin dosya biçimi bilinmiyor. Bu yüzden **hazır şablon + sütun eşleme** birlikte: şablona uyan dosya doğrudan geçer, uymayan dosyanın başlıkları otomatik önerilir, kullanıcı düzeltir.
- Akış: **yükle → eşle → doğrula (kuru çalıştırma) → önizleme → onayla → uygula**. Önizlemede kaç kayıt oluşur / mevcutla eşleşir / atlanır / hatalı, satır satır hata raporu ve indirilebilir hata dosyası var. Hiçbir şey onaydan önce ürün tablolarına yazılmaz.
- Her uygulama bir **parti kimliği** taşır. Yanlış gelen parti, **yalnız o partinin oluşturduğu** kayıtları silerek geri alınır; ödeme/ücret/muayene/geliş gibi bağı olan kayıt silinmez (güvenli silme kuralı, `IMPORT_API_CONTRACT.md` §9).
- Aynı dosya iki kez yüklenirse çift kayıt oluşmaz (dosya özeti + `requestId` + veri düzeyinde “mevcutla eşleş” kuralı).
- **En kritik bulgu:** Mevcut hatırlatma işçisi, `Applied` bir aşıda *sonraki aşı tarihi* yakındaysa e-posta üretir. İçe aktarılan geçmiş aşılar için bu toplu ve istemsiz gidebilir. Çözüm: içe aktarılan aşılar **varsayılan olarak hatırlatma dışıdır**; yönetici sayıyı görüp açıkça serbest bırakır (§8).
- Uygulama arka planda çalışır (iş kuyruğu = parti tablosu + küçük bir hosted service); outbox bu iş için uygun değildir (§7).
- Tahmini iş: **1 migration**, 5 backend dilimi + 1 frontend sihirbaz + test/perf provası (§11). Ürün kararı gerektiren 12 madde var (§12), hepsi önerili.

---

## 2) Mevcut kod bulguları (kanıtlı)

| # | Bulgu | Kanıt |
|---|-------|-------|
| 1 | Müşteri telefonu opsiyonel; doluysa **yalnız Türkiye cep standardı** `905XXXXXXXXX` (12 hane). `05XXXXXXXXX`, `5XXXXXXXXX`, `+90…` kabul; sabit hat geçersiz. | `Domain/Clients/TurkishMobilePhone.cs`, `Client.cs` |
| 2 | Müşteri mükerrer kuralı: kiracıda **aynı ad (trim + `ToLowerInvariant`) + aynı e-posta** veya **aynı ad + aynı telefon** → `Clients.DuplicateClient`. Yalnız telefonun aynı olması engel değil (aile). Veritabanı benzersiz indeksi yalnız `(TenantId, Email, PhoneNormalized)` ve ikisi de doluyken. | `CreateClientCommandHandler.cs`, `ClientConfiguration.cs:43-45`, `CLIENTS_QUICK_REGISTER_API_CONTRACT.md` §4 |
| 3 | Hayvan mükerrer kuralı: aynı müşteri + küçük harf ad + aynı tür. **Mikroçip için benzersizlik kuralı/indeksi yok**; mikroçip yalnız ≤ 50 karakter, serbest metin. | `CreatePetCommandHandler.cs`, `PetConfiguration.cs:41` (indeks listesinde mikroçip yok) |
| 4 | Hayvan: tür zorunlu (aktif `Species`), ırk hem `BreedId` (türle uyumlu) hem serbest metin; renk yalnız `ColorId` (serbest metin alanı **yok**); cinsiyet yalnız `Male=1/Female=2`; doğum tarihi gelecekte olamaz. `Pet` varlığında **`UpdatedAtUtc` yok**. `CreatePet` ayrıca abonelik yazma koruması uygular. | `Pet.cs`, `PetGender.cs`, `CreatePetCommandHandler.cs` |
| 5 | **Aşı kaydı (`Vaccination`)**: `ClinicId` zorunlu, `VaccineDefinitionId` + ad anlık görüntüsü zorunlu, `Status` Scheduled/Applied/Cancelled, `AppliedAtUtc`/`DueAtUtc` (UTC). Seri/lot/uygulayan alanı **yok** (yalnız `Notes`). | `Vaccination.cs` |
| 6 | Günlük aşı girişi (`CreateVaccinationCommandHandler`) uygulama zamanını **en fazla 7 gün öncesine** sınırlar; bu yüzden eski geçmiş mevcut uçtan **girilemez**. Sınırlar handler yardımcılarında (`internal static`), `Vaccination` kurucusunda değil. `Applied` için `DueAtUtc > AppliedAtUtc`, gelecek uygulama tarihi yok; `DueAtUtc` penceresi −10/+5 yıl. | `VaccinationAppliedAtWindow.cs`, `VaccinationDueAtWindow.cs`, `VaccinationStatusDateRules.cs` |
| 7 | **Hatırlatma işçisi**: tenant ayarında aşı hatırlatması **varsayılan kapalı** (`VaccinationRemindersEnabled=false`, 3 gün önce). Açıksa aday: `Scheduled` **veya `Applied`**, `DueAtUtc` ∈ (şimdi, şimdi + `daysBefore`]; sahibin e-postası yoksa kayıt `Skipped` günlüğüne düşer; tekrar önleme anahtarı `(aşıId, daysBefore)`; tur başına `BatchSize` (1–500) sınırı. “Kaçırılan pencere” de dahildir (hatırlatma zamanı geçmiş, vade gelecekte). | `ReminderProcessorService.cs:223-340`, `TenantReminderSettings.cs` |
| 8 | Arama/Türkçe harf: tek normalizasyon noktası `DailySearchTerm` (`Fold`: ı→i, İ→I; kolonda `Latin1_General_100_CI_AI`); telefon/mikroçip rakam dizisi toleransı. Mikroçip aramada **boşluksuz** saklanan değer bulunur. | `DailySearchTerm.cs`, `SEARCH_API_CONTRACT.md` |
| 9 | Müşteri ve hasta **kiracı geneli** (klinik filtresi yok); `Vaccination.ClinicId` var ve yazma sırasında klinik erişimi denetlenir. | `SEARCH_API_CONTRACT.md` “Değişmeyenler”, `VaccinationClinicWriteScope` |
| 10 | Hızlı kayıt: `ITransactionalRollbackOnFailureRequest` ile mevcut komutları sırayla çalıştırır; başarısız ikinci adım birincisini geri alır. Aynı desen sahip-bazlı işlem birimi için kullanılabilir. | `QuickRegisterClientCommandHandler.cs`, `ITransactionBehavior.cs` |
| 11 | İdempotency deseni: `RequestId` + `...ByRequestIdSpec` + `DbUpdateException` yakalama (Charge). | `CreateChargeCommandHandler.cs:106-196` |
| 12 | `Client`/`Pet` oluşturma outbox olayı üretir (`client.created.v1`, `pet.created.v1`); **silme olayı tipi yok** (yalnız Created/Updated). `Client` ve `Appointment` projeksiyon işleyicileri `Enabled: true`, `Pet`/`Payment` `false`; okuma bayrakları kapalı. | `ClientIntegrationEventTypes.cs`, `PetIntegrationEventTypes.cs`, `ASAMA9_READINESS.md` §2.2 |
| 13 | `/health/ready` outbox bekleyen **> 500** ise Degraded ve ready ucu Degraded'ı **503** sayar (eşik sabit). 10 bin kayıtlık içe aktarma bunu tetikleyebilir. | `ASAMA9_READINESS.md` §2.3 (`OutboxHealthCheck`; bu işte kod yeniden okunmadı) |
| 14 | `ClosedXML` zaten `Application` projesinde (rapor dışa aktarma). CSV okuma için paket yok. Yükleme (`IFormFile`) ve istek boyutu sınırı (`RequestSizeLimit`) kodda **yok**; dosya deposu kodu yok. | `Application.csproj`; `grep` sonuçları boş |
| 15 | Güvenlik/yetki: izinler `PermissionCatalog`; seed izin siler (yalnız birleşik main'den çalıştırılır, çıkış-giriş gerekir). `AuditBehavior` `IAuditableRequest` ile komut payload'ını yazar (hassas anahtarlar maskeli). | `PermissionCatalog.cs`, `ASAMA9_READINESS.md` §2.2, `AuditBehavior.cs` |
| 16 | `Administration` (uygulama kaydı) stok düşer; **içe aktarma kapsamı dışı** (aşı geçmişi `Vaccination` varlığıdır, stok hareketi üretmez). | `Administration.cs` |

---

## 3) Girdi biçimi yaklaşımı

**Neden şablon + eşleme?** Pilot klinik kâğıt/Excel kullanıyor (K4); dosyası büyük olasılıkla klinikçe düzenlenmiş serbest bir Excel olacak. Yalnız katı şablon, ilk gün klinikten “bu şablona çevirin” istemek demek ve hatayı kliniğe yıkar. Yalnız serbest eşleme ise doğrulanmayan, her klinikte farklı kural riski. İkisi birlikte: şablon *kanonik sözleşme* (alanlar, kurallar sabit), eşleme yalnızca “sizin sütun adınız → kanonik alan” çevirisi.

- **Üç mantıksal tablo** (müşteri / hasta / aşı) bire çok ilişkiyle bağlanır. Klinikler genelde tek düz liste tutar (satır = hayvan, sahip sütunları tekrarlı). Bunu ilk sürümde **doğrudan desteklemiyoruz**: hasta tablosunda `SahipAdSoyad + SahipTelefon` ile bağlanma izin verilir (müşteri tablosu olmadan müşteri satırı hasta satırından türetilmez). Düz liste gelirse kullanıcı aynı dosyayı hem müşteri hem hasta tablosu rolüyle eşleyebilmelidir; bu “tek dosya, çok rol” senaryosu **ilk dilimde açık soru** (`§12 K1` notu, frontend sihirbazı karmaşıklaştırır).
- **Tür/ırk/aşı adları**: kâğıt dünyasında serbest yazılır (“Kopek”, “KÖPEK”, “golden”, “Karma aşı”). Eşleme anahtarı katlanmış metin; eşlenemeyenler önizlemede benzersiz değer listesi olarak çıkar, kullanıcı bir kez eşler (`valueMappings`), doğrulama tekrarında uygulanır. Katalogu otomatik genişletmiyoruz (§12 K6).
- **Telefon**: kliniklerde sabit hat, “0532 123 4567”, “532-1234567”, Excel'de baştaki 0 kaybı yaygın. Normalizasyon mevcut `TurkishMobilePhone` ile; geçersiz telefon satırı bloklamaz (önerilen, §12 K5) ama özgün değer raporlanır.
- **Mikroçip**: Excel 15 haneli sayıyı bilimsel gösterime çevirip basamak kaybettirir; bu bilinen tuzak için ayrı hata kodu ve şablonda metin biçimli sütun. Çip kimlik bilgisidir, sessizce bozulmaz.
- **Tarih**: Türkiye biçimi (`gg.aa.yyyy`) ve ISO; belirsiz biçimler reddedilir (yanlış yorumlanıp 3 Mayıs yerine 5 Mart yazılması geri dönüşsüz klinik veri hatasıdır).

---

## 4) Akış

```
Yükle ──► Eşle ──► Doğrula ──► Önizleme ──► Onayla ──► Uygula ──► (Hatırlatmayı aç?) ──► Rapor
(Uploaded) (Mapped) (Validating  (Validated) (Committing) (Committed)
                     → Validated)                              └─► Geri al (RollingBack → RolledBack)
```

1. **Yükle** (`POST /imports/batches`): boyut/imza denetimi, içerik özeti, başlık okuma, otomatik eşleme önerisi. Satırlar hazırlık tablosuna normalize biçimde yazılır (ürün tablolarına dokunulmaz).
2. **Eşle** (`PUT .../mapping`): sütun eşleme + değer eşleme + seçenekler. Tekrarlanabilir.
3. **Doğrula** (`POST .../validate`, arka plan): her satır için mevcut kurallar çalışır; veritabanındaki mevcut müşteri/hasta/aşı sorguları **toplu** (telefon/ad kümeleriyle), satır başına değil. Hiçbir ürün kaydı yazılmaz.
4. **Önizleme** (`GET .../preview`, `.../rows`, `.../errors`): oluşacak/eşleşecek/atlanacak/hatalı sayıları, kaynak değer listesi, uyarı kodu dağılımı, **hatırlatma etkisi**, satır tablosu, hata raporu indirme.
5. **Onayla** (`POST .../commit`): `validationId` ile — kullanıcı tam olarak önizlediğini onaylar.
6. **Uygula** (arka plan, ilerleme yoklanır): sahip bazlı işlem birimi, satır sonucu kayıtla aynı transaction'da.
7. **Rapor**: nihai sayılar + hata raporu. Düzeltilmiş hata dosyası **yeni parti** olarak yüklenebilir; önceki başarılar `Matched` olduğundan çift kayıt oluşmaz.

---

## 5) Veri modeli ve migration ihtiyacı

**Evet, 1 migration** (kullanıcı onayıyla, yerel dev DB bile onay ister):

| Nesne | Alanlar (özet) |
|-------|----------------|
| `ImportBatches` | `Id`, `TenantId`, `ClinicId?`, `RequestId`, `ContentHash` (SHA-256), `Name?`, `Status`, `MappingJson`, `OptionsJson`, `ValidationId?`, sayaç sütunları (tablo başına toplam/geçerli/uyarı/hatalı/oluşan/eşleşen/atlanan), `CreatedByUserId`, `CreatedAtUtc`, `ValidatedAtUtc?`, `CommittedAtUtc?`, `RolledBackAtUtc?`, `RemindersReleasedAtUtc?`, `PayloadPurgedAtUtc?`, `LeaseOwner?`, `LeaseUntilUtc?`, `RowVersion` |
| `ImportBatchRows` | `Id`, `BatchId`, `Table` (Customer/Pet/Vaccination), `RowNumber`, `SourceJson`, `NormalizedJson`, `ValidationStatus`, `IssuesJson`, `ApplyStatus`, `TargetEntityType?`, `TargetEntityId?`, `Matched`, `AppliedAtUtc?` |
| `Vaccinations.ImportBatchId` | nullable `Guid`, indeksli (hatırlatma dışlaması ve “içe aktarıldı” etiketi için) |

İndeksler / kısıtlar (idempotency ve eşzamanlılığın **veritabanı düzeyi** güvencesi): `ImportBatches (TenantId, RequestId)` benzersiz; `(TenantId, ContentHash)` benzersiz **filtreli** (`Status` ∉ Discarded/Expired/RolledBack/CommitFailed); `(TenantId)` benzersiz filtreli `Status ∈ (Validating, Committing, RollingBack)` (kiracı başına tek aktif iş); `ImportBatchRows (BatchId, Table, RowNumber)` benzersiz; `ImportBatchRows (TargetEntityId)` (geri alma). `Client`/`Pet` tablosuna **sütun eklenmez** (okuma modeli ve projeksiyonlar etkilenmesin): oluşturulan kayıt kimlikleri satır sonucundadır.

Hatırlatma dışlaması için `Vaccinations.ImportBatchId` alternatifi (hazırlık satırlarına join) hatırlatma sorgusunu ağırlaştırır; reddedildi. Alternatif “ReminderDispatchLog'a önceden `Skipped` yazmak” da `daysBefore` ayarı değişince anahtar değiştiği için kırılgan; reddedildi.

Üretim kuralı: migration ve seed (izinler) kullanıcı onayıyla, birleşik main'den (seed uyarısı).

---

## 6) Eşzamanlılık, idempotency, sınırlar

**Idempotency (dört katman):**
1. `requestId` (Charge deseni): aynı kiracı + aynı `requestId` → aynı parti.
2. Dosya **içerik özeti** (SHA-256, kiracı bazlı): aynı dosya ikinci kez `409 Imports.DuplicateFile` (geri alınmışsa tekrar yüklenebilir).
3. `commit`/`rollback` tekrarında yeni iş başlamaz, durum özeti döner.
4. **Veri düzeyi:** müşteri/hasta/aşı satırı mevcut kayıtla eşleşirse oluşturulmaz (`Matched`/`DuplicateVaccination`). Dosya biraz değiştirilip yeniden yüklense bile (özet farklı) çift kayıt oluşmaz. Bu, “yanlış dosyayı yükledim, düzeltilmişini yükleyeyim” akışını da güvenli yapar.

**Eşzamanlılık:**
- Kiracı başına tek aktif iş (filtreli benzersiz indeks); ikinci çağrı `409`, sırada bekletme yok (basit ve öngörülebilir).
- Parti durum geçişleri `RowVersion` ile (iki sekme aynı partide commit/discard dener → biri `409 Imports.InvalidState`).
- İşçi **kira (lease)** alır (mevcut CQRS “claim-lease” deseni, `docs/cqrs/cqrs-11d-claim-lease.md`; ayrıntısı bu işte okunmadı); süresi dolan kira başka örnek/yeniden başlatma sonrası devralınır; satır sonucu ile kayıt aynı transaction'da olduğundan kaldığı yerden devam eder, çift oluşmaz.
- İçe aktarma sürerken kullanıcılar elle müşteri/hasta ekleyebilir: eşleşme kuralları uygulama anında yeniden çözülür; veritabanı benzersiz indeksi (`Email+Phone`) yarışta ihlal verirse satır bir kez yeniden çözülüp `Matched` olarak işaretlenir.
- Önizleme–uygulama sapması (arada elle eklenen kayıt) beklenen davranış; nihai rapor esas, `validationId` yalnız “önizlenen eşleme/kural seti” güvencesidir.

**Sınırlar (varsayılan, yapılandırılabilir):** dosya ≤ 10 MB; tablo başına ≤ 10.000, toplam ≤ 25.000 satır (pilot 5–10 bin hasta hacmi; ölçülmedi). Yükleme ucuna açık `RequestSizeLimit` konur (kodda bugün yok). XLSX tamamen belleğe yüklenir (ClosedXML); bu yüzden dosya boyutu ve satır sınırı **okumadan önce** sıkı denetlenir, açılmış (zip içi) boyut tavanı konur (zip bombası). CSV akışla okunur. Taslak partiler 7 gün sonra `Expired`; kiracı başına ≤ 5 açık taslak.

---

## 7) Sürekli iş (arka plan) gerekir mi?

**Evet.** Gerekçe: 10–25 bin satırın doğrulaması ve özellikle uygulaması istek zaman aşımını aşabilir (mevcut işleyiciler satır başına ~3–5 sorgu yapar; süre ölçülmedi), kullanıcı sekmeyi kapatabilir, yönetilen platformda uygulama yeniden başlayabilir (K1).

- **Mekanizma:** `ImportBatches` tablosu iş kuyruğudur (durum + kira); küçük bir hosted service (`ReminderProcessorHostedService` / `ScheduledPlanChangeProcessorHostedService` desenindeki gibi `AddHostedService`) durumu `Validating/Committing/RollingBack` olan partiyi alır ve parçalar (örn. 50 sahip birimi) halinde işler. İlerleme `progress` ile okunur.
- **Outbox uygun değil:** outbox “entegrasyon olayı → yan etki (e-posta/projeksiyon)” içindir, kullanıcıya görünen ilerlemeli iş yönetimi sunmaz.
- **Alternatif (reddedildi):** istemcinin art arda “sonraki parçayı uygula” çağırması. Altyapı gerektirmez ama sekme kapanınca durur, kısmi uygulama kullanıcıya kalır. Kullanıcı kararı §12 K11.
- **Dikkat – sağlık ucu:** uygulama binlerce `client.created`/`pet.created` olayı üretir; outbox bekleyeni 500'ü aşınca `/health/ready` 503 verir (bulgu 13). İşçi her parça öncesi outbox bekleyen sayısına bakıp **geri basınç** uygular (eşik ~300 altına düşene kadar bekler). Pet projeksiyonu kapalıyken `pet.created` olaylarının nasıl tüketildiği/birikip birikmediği bu işte **doğrulanmadı**; koordinatör notundaki “pet projeksiyonunda bekleyen olaylar” `/health` 503'ü ve onun düzeltmesi (`main`'e birleşti) hacimli yükte yeniden doğrulanmalı (Dilim B4 kabul koşulu).
- **Dikkat – abonelik:** `CreatePet` abonelik yazma korumasına bakar; salt-okunur kiracıda commit başlamadan `Imports` bunu bildirir.

---

## 8) Aşı geçmişi ve hatırlatma etkisi

**Veri kuralı:** geçmiş tarihli `Applied` aşı; `Scheduled` içe aktarılmaz (planlı gelecek aşı klinikte zaten yeniden planlanır; planlı satırlar yanlışlıkla hatırlatma tetikler). “Sonraki aşı tarihi” `DueAtUtc` olarak saklanır (mevcut modelin tek “vade” alanı).

**Neden açık kural gerekiyor?** Bulgu 7: işçi `Applied` + `DueAtUtc` ∈ (şimdi, şimdi+3 gün] olanları aday sayar ve “kaçırılan pencere”yi de içerir. Pilot kliniğin eski kayıtlarında gelecek vadeli yüzlerce “sonraki aşı” olacaktır; tenant hatırlatmayı açtığı anda bir turda `BatchSize` kadar (≤ 500), sonraki turlarda kalanı e-posta olarak gider — müşteriler beklemedikleri bir sistemden, klinik henüz canlıya geçmeden mail alır. Ayrıca KVKK açısından (K3) içe aktarılan müşterilerin e-posta iletişimine uygunluğu danışman değerlendirmesi gerektirir (hukuki tavsiye değildir).

**Çözüm (sözleşme §6.3):**
1. `Vaccination.ImportBatchId` + `ImportBatch.RemindersReleasedAtUtc`.
2. Hatırlatma aday sorgusu serbest bırakılmamış içe aktarma kayıtlarını **dışlar** (tek yer: `ReminderProcessorService`).
3. Önizleme `reminderImpact` verir: gelecek vadeli aşı sayısı, e-postalı müşteri sayısı, hatırlatma penceresine düşen tahmini e-posta sayısı.
4. `release-reminders` yönetici onaylı, tek yönlü; onay anındaki sayıyı doğrular (`confirmEmailCount`). Pilot planında (K4) öneri: ilk 2 haftalık gölge kullanım bitip klinik/KVKK teyidi alındıktan sonra.
5. Varsayılan hiçbir şey gitmez; tenant ayarı zaten kapalıysa serbest bırakma yine de bir şey göndermez (yanıtta not).

**Aşı eşleme:** `AsiAdi` → kiracıya görünür aktif `VaccineDefinition` (global kodlar: Kuduz, Karma, Bronşin, Lösemi, FIV, FIP, İç/Dış Parazit, Corona, Lyme; `VaccineDefinitionSeeder`). Tanımın türü hasta türüyle uyumlu olmalı (`VaccinationCatalogResolver` kuralı). “Karma 2. doz”, “Kuduz-2023” gibi serbest yazımlar değer eşlemeyle bir tanıma bağlanır; doz/seri bilgisi `Notes`'a düşer (alan yok, bulgu 5). Klinik kayıt: partide tek `clinicId`.

**Neden dar pencere yerine ayrı kural?** Günlük giriş penceresi (7 gün) klinik güvenlik içindir (geçmişe dönük sahte kayıt engeli); onu gevşetmek tüm uygulamaya yayılır. İçe aktarma kuralı yalnız içe aktarma kodunda yaşar, yerine ≥1990 / gelecek yok / doğum tarihi tutarlılığı konur.

---

## 9) Geri alma

### 9.1 Neden güvenli silme mümkün
Pilot ilk sürümde yalnız müşteri+hasta+aşı içeri alınır (K8); içe aktarma ödeme, ücret, muayene, geliş **yaratmaz**. Dolayısıyla taze bir partinin kayıtları ürünün başka hiçbir kaydıyla bağlı değildir. Kural bunu *varsaymaz, denetler* (sözleşme §9).

### 9.2 Gerçekçi kullanım penceresi
K4 gölge kullanım: klinik içe aktarmanın hemen ardından aynı hastalarla geliş/ücret açmaya başlar; o andan sonra “tam geri alma” anlamsızlaşır. Bu yüzden:
- Varsayılan `Fail`: engelli tek kayıt varsa **hiçbir şey silinmez**, liste döner (kullanıcı yanlış sürümü ilk günlerde, kullanım başlamadan fark eder).
- `KeepBlocked` kullanım başlamış durumlar için kısmi geri alma.
- Tavsiye: gerçek içe aktarmadan **önce** staging'de deneme partisi + geri al provası (Dilim 10 senaryosuna eklenir).

### 9.3 Düzenleme tespiti
`Client.UpdatedAtUtc` doluysa müşteri “değiştirilmiş” sayılır ve silinmez. `Pet`'te `UpdatedAtUtc` yok (bulgu 4): sonradan hayvan düzenlemeleri tespit edilemez; hayvan bağımlılığı yoksa silinir ve düzenleme kaybolur. Kabul edilebilir mi? Öneri: evet, `rollback-preview` ve onay metni “değişiklik kaybolabilir” uyarısını taşır; kalıcı çözüm `Pet.UpdatedAtUtc` eklemek (ayrı küçük iş, bu dilime bağlanmadı).

### 9.4 Query DB / okuma modeli
Silme olayı tipi yok; `Client` ve `Appointment` projeksiyon işleyicileri açık. Geri alma silinen müşteri/hasta için okuma modelinde **hayalet kayıt** bırakır (okuma bayrakları kapalı olduğundan pilotta görünmez, ama bayrak açılınca bozuk veri). Seçenekler: (a) `client.deleted.v1`/`pet.deleted.v1` olayı + projeksiyon işleyici desteği (önerilen, B5 kapsamında; projeksiyon işleyicisinin silmeyi nasıl işleyeceği bu işte **okunmadı**); (b) geri alma sonrası backfill (`backfill-client/pet`) ile Query DB yenilemesi — backfill'in fazla kayıtları silip silmediği **doğrulanmadı**. Dilim B5 başında kod okunup seçilir.

**B5 kararı (uygulandı):** (a) seçildi. `client.deleted.v1` / `pet.deleted.v1` olayları silmeyle aynı işlemde outbox'a yazılır; müşteri ve hasta projeksiyon işleyicileri olayda okuma modeli satırını siler (olay son uygulanan olaydan eskiyse yok sayılır). Pilot ayarında hasta projeksiyonu kapalıdır; olaylar kuyrukta bekler ve açıldığında sırayla işlenir. Ayrıntı ve kesin silinebilirlik tablosu: `IMPORT_API_CONTRACT.md` §7.11–7.12, §9. Geri alma isteği (`requestId`, politika) migration gerektirmemek için parti seçenek belgesinde (`OptionsJson`) tutulur; K7 opsiyonel politikanın adı `DeleteUnblockedOnly` oldu (taslakta `KeepBlocked`).

### 9.5 Sıra ve atomiklik
Geri alma sahip bazlı birimlerle işlenir (aşı → hasta → müşteri). Birimde bağımlılık denetimi silmeyle aynı transaction'da yapılır (denetim ile silme arasında yeni geliş açılması yarışına karşı). `Fail` politikasında önce planlama (hiçbir şey silmeden) yapılır; engelli varsa durur.

---

## 10) Yetki, audit, KVKK

- **İzinler:** `Imports.Read`, `Imports.Manage`, `Imports.Commit` + commit'te `Clients.Create`/`Pets.Create`/`Vaccinations.Create` ek denetimi (sözleşme §2). Yalnız Yönetici rolüne seed edilir (FIN düzeltme kararındaki “yönetici düzeyi” ile hizalı). Seed çalıştırma kullanıcı onayı ister; yalnız birleşik main'den (izin silme riski), sonrasında çıkış-giriş.
- **Audit:** sözleşme §10. Satır verisi audit payload'ına yazılmaz (kişisel veri); `Password/Token` maskesi bu veriyi korumaz, bu yüzden payload tasarımı bilinçli dar tutulur.
- **Veri minimizasyonu:** satır verisi hazırlık tablosunda geçici; 30 gün sonra temizlenir (K9: süre KVKK danışmanıyla teyit). Ham dosya saklanmaz. Hata raporu indirme audit'li, önbelleksiz.
- **Kapsam dışı kalan KVKK işleri:** aydınlatma metni, açık rıza/e-posta uygunluğu, veri işleyen sözleşmesi, silme/anonimleştirme Dilim 8 ve K3 danışman çıktısıdır; bu belge hukuki tavsiye vermez. İçe aktarma, sahiplerin verisini kliniğin yüklemesiyle işler (klinik = veri sorumlusu varsayımı **doğrulanmadı**, hukuk konusu).
- **Güvenlik:** dosya imzası, uzantı beyaz listesi, makro/parola reddi, boyut/satır tavanı, CSV formül enjeksiyonu (indirme), günlükte kişisel veri yok, başka kiracı partisi `404`.

---

## 11) Tahmini dilimler

Her dilim ayrı dal ve commit; sözleşme dosyası dilim başında güncel tutulur.

| # | Dilim | İçerik | Migration | Tahmin* |
|---|-------|--------|-----------|---------|
| B1 | Temel | `ImportBatch`/`ImportBatchRow` varlıkları, yapılandırmalar, `Vaccination.ImportBatchId`, durum makinesi, izinler (+ seed sınıfı), **migration** (onay), parti oluştur/listele/oku/sil iskeleti | **1** | M |
| B2 | Okuma ve eşleme | XLSX/CSV okuyucu (kodlama, ayırıcı, imza, tavanlar), kanonik başlık/takma ad eşleme, şablon üretimi (`ClosedXML`), `PUT mapping`, değer eşleme çözücü (katlama) | — | M |
| B3 | Doğrulama / kuru çalıştırma | Satır kuralları (telefon, e-posta, tarih, çip, cinsiyet…), toplu eşleşme sorguları, mükerrer kuralları, `reminderImpact`, preview/rows/errors uçları, hata raporu dosyası | — | L |
| B4 | Uygulama | Hosted service + kira, sahip bazlı işlem birimi, mevcut komutlar üzerinden yazma, içe aktarma aşı yolu, geri basınç, `ReminderProcessorService` dışlaması, `release-reminders`, idempotency testleri | — | L |
| B5 | Geri alma ve temizlik | Bağımlılık denetimi, `rollback-preview/rollback`, silme olayı tipleri + projeksiyon, 30 gün veri temizleme, `Expired` temizliği (**uygulandı**, migration yok) | (olay tipleri için yok) | M–L |
| F1 | Frontend sihirbaz | Adımlı ekran: şablon indir → yükle → eşle → doğrula/önizleme → onay → ilerleme → rapor; parti geçmişi, hata dosyası indirme, hatırlatma serbest bırakma, geri alma onayı; **ancak B2–B3 sözleşmesi sabitlendikten sonra** | — | L |
| T | Deneme/perf | Sentetik pilot benzeri veri (5–10 bin hasta), staging'de uçtan uca + geri alma provası, süre ve `health/ready` gözlemi, entegrasyon testleri | — | M |

*S/M/L göreli büyüklük; gün tahmini verilmedi (ölçülmüş hız verisi yok). Önerilen sıra: B1 → B2 → B3 (frontend F1 bunlarla paralel başlayabilir) → B4 → B5 → T. Migration yalnız B1'de; uygulama kullanıcı onayıyla.

**Bağımlılıklar:** Pilot klinik ve gerçek veri biçimi (K4) — B2'nin eşleme önerileri ve “düz liste” desteği kararı buna bağlı; B1–B3 klinik dosyası olmadan başlayabilir. İzin seed'i birleşik main'den. Staging ortamı (Dilim 3) T için.

---

## 12) Kullanıcıya sorulacak kararlar (öneriyle, sade Türkçe)

Bu kararlar ürün davranışını belirler; kullanıcı yanıtlayınca ADR-015 (içe aktarma) yazılması önerilir.

**K1 — Klinik dosyası nasıl gelecek?**
- **a)** Yalnız bizim hazır şablonumuz (klinik kendi Excel'ini şablona taşır).
- **b)** Şablon + **sütun eşleme**: klinik kendi Excel'ini yükler, sistem başlıkları tahmin eder, kullanıcı düzeltir. *(öneri)*
- **c)** Her kliniğe özel dönüştürme betiği.
- Öneri **b**. Ek soru: düz liste (satırda hem sahip hem hayvan) ilk sürümde desteklensin mi? Öneri: ilk sürümde şablonun üç sayfalı biçimi; düz liste için klinik dosyası görüldükten sonra karar.

**K2 — Hatalı satırlar ne olsun?**
- **a)** Doğru satırlar aktarılsın, hatalılar atlanıp raporlansın. *(öneri)*
- **b)** Tek hata varsa hiçbir şey aktarılmasın.
- Öneri **a** (geri alma var, hata dosyası düzeltilip tekrar yüklenebilir).

**K3 — Eski aşıların “sonraki aşı” hatırlatma e-postaları?**
- **a)** Hiçbiri otomatik gitmesin; yönetici sayıyı görüp “hatırlatmaları aç” derse gitsin. *(öneri)*
- **b)** Hiç gitmesin (içe aktarılan kayıtlar için hatırlatma kalıcı kapalı).
- **c)** Normal sistem gibi hemen gitsin (**önermiyoruz**: yüzlerce istemsiz e-posta riski).
- Öneri **a**; zamanlaması: gölge kullanım sonrası ve KVKK teyidiyle.

**K4 — Aynı müşteri zaten kayıtlıysa (aynı ad + telefon)?**
- **a)** Mevcut müşteriyle eşleşsin, yeni oluşmasın; hayvanlar ona bağlansın. *(öneri)*
- **b)** O satır atlansın.
- **c)** Hata olsun.
- Öneri **a** (tekrar yükleme güvenli olur, hiçbir mevcut bilgi ezilmez).

**K5 — Telefonu geçersiz/sabit hat olan müşteri?**
- **a)** Telefonsuz aktarılsın, raporda uyarı + özgün numara görünsün. *(öneri)*
- **b)** Satır reddedilsin.
- Öneri **a**: sistem yalnız cep numarası kabul ediyor; klinik sabit hatlı müşteriyi kaybetmesin. Telefonsuz kayıtlarda yinelenen tespiti zayıflar, bu uyarıyla bildirilir.

**K6 — Dosyada olup sistemde olmayan tür/aşı adı?**
- **a)** İlk sürümde yeni tanım oluşturulmasın; klinik önce Ayarlar'dan ekler, sonra yeniden doğrular, ya da satır atlanır. *(öneri)*
- **b)** İçe aktarma yeni tanımı kendi oluştursun.
- Öneri **a** (katalog kirlenmesin; ürün kararı gerektirir).

**K7 — Geri alma kuralı?**
- **a)** Bağı olan (geliş/ücret/muayene…) kayıt varsa **hiçbir şey silinmesin**; kullanıcıya liste gösterilsin. *(öneri, varsayılan)* İsteğe bağlı: “bağı olanlar kalsın, gerisi silinsin” seçeneği.
- **b)** Hep kısmi geri alma.
- Öneri **a** + opsiyonel b. Not: hayvan düzenlemeleri izlenemediği için bağı olmayan hayvanlardaki düzenleme geri almada kaybolur (§9.3).

**K8 — Kimler kullanabilsin?**
- **a)** Yalnız Yönetici (`Imports.Read/Manage/Commit`). *(öneri)*
- **b)** Sekreter de yükleyip doğrulayabilsin, onay/geri alma yalnız Yönetici.
- Öneri **a** (toplu işlem, düzeltilmesi zor).

**K9 — Önizleme/hata dosyası verisi ne kadar saklansın?** İçe aktarılan satırlar kişisel veri içerir. Öneri: işlem bittikten **30 gün** sonra silinsin (sayılar ve özet kalır). KVKK danışmanı farklı süre söylerse değişir.

**K10 — Aynı mikroçip iki hayvanda çıkarsa?**
- **a)** Hata (satır aktarılmaz). *(öneri)*
- **b)** Uyarı (aktarılır).
- Öneri **a** (mikroçip tek hayvana ait olmalı; sistemde bugün benzersizlik denetimi yok, içe aktarma kopyayı yaymasın).

**K11 — Arka planda mı çalışsın?**
- **a)** Arka plan işi: sekme kapansa da sürer, ilerleme ekranda izlenir. *(öneri)*
- **b)** Kullanıcı ekranı açık tutup parça parça ilerlesin (altyapısı basit, ama sekme kapanınca durur).
- Öneri **a**.

**K12 — Yaş sütunu (“2 yaşında”)?** Doğum tarihi yerine yaş yazan klinikler olur. Öneri: ilk sürümde **desteklenmesin** (tahmini doğum tarihi uydurmak klinik veriyi bozar); dosyada yaş varsa `Not`'a düşer.

---

## 13) Riskler

| # | Risk | Etki | Önlem |
|---|------|------|-------|
| R1 | Toplu hatırlatma e-postası | Müşteri şikâyeti, itibar, KVKK | §8: varsayılan dışla + onaylı serbest bırakma |
| R2 | Kirli kaynak veri (yanlış telefon, tür yazımı, çip bozulması) | Hatalı veya eksik kayıt | Kuru çalıştırma, değer eşleme, hata raporu, geri alma, staging denemesi |
| R3 | Pilot dosya biçimi tahminden farklı (düz liste, çok sayfa, birleşik hücre) | Eşleme yetersiz kalır, B2/F1 yeniden iş | Klinik dosyası örneği (kişisel veri çıkarılmış) erken istenir; K1 notu |
| R4 | Outbox birikimi → `/health/ready` 503 | Yönetilen platformda trafik kesilmesi/yeniden başlatma | Geri basınç, T diliminde ölçüm |
| R5 | Silme olayı olmadan geri alma → Query DB hayalet kayıt | Bayrak açılınca tutarsız okuma | §9.4 B5 kapsamı |
| R6 | Geri alma `Pet` düzenlemelerini kaybettirir | Küçük veri kaybı | Uyarı metni, ileride `Pet.UpdatedAtUtc` |
| R7 | XLSX bellek tüketimi / zip bombası | Hizmet kesintisi | Boyut ve satır tavanı okumadan önce, açılmış boyut tavanı |
| R8 | Hazırlık tablosunda kişisel veri | KVKK | 30 gün temizleme, günlük/audit'e yazmama, yetki sınırı |
| R9 | Mevcut oluşturma yolları satır başına yavaş | Uzun uygulama süresi | Arka plan, parça parça, ölçüm (T); gerekirse toplu hazırlık sorguları |
| R10 | Seed izni yanlış daldan çalışırsa izinler silinir | Yetki kaybı | Yalnız birleşik main'den seed (mevcut kural) |
| R11 | Mevcut `Clients.DuplicateClient` ile `Matched` anlamı farkı | Beklenti karışıklığı | Sözleşme ve arayüzde “mevcutla eşleşti” net etiketi |
| R12 | Saat dilimi (Europe/Istanbul 12:00) ile ekran tarih tutarsızlığı geçmişte yaşandı (muayene tarihi hatası) | Aşı tarihi bir gün kayık görünür | 12:00 seçimi; frontend gösteriminde aynı kural; T'de doğrulama |

---

## 14) Doğrulanmayanlar

- Bu iş **yalnız okuma**: derleme, test, API, veritabanı, migration, yük ölçümü yok. Tüm “var/yok” ifadeleri dosya okumasından.
- Pilot kliniğin gerçek veri biçimi, hacmi ve temizliği **bilinmiyor**; sütun adları ve kural varsayımları tahmindir.
- `OutboxHealthCheck` ve outbox/pet projeksiyon davranışı bu işte yeniden okunmadı; 500 eşiği ve ready=503 bilgisi `ASAMA9_READINESS.md`'den. Pet projeksiyonu kapalıyken olay tüketimi ve birikimi doğrulanmadı.
- Client/Pet projeksiyon işleyicilerinin silme olayını nasıl işleyeceği, `backfill-client/pet`'in fazla kayıtları temizleyip temizlemediği, `Client→Pet` ve diğer FK silme davranışları (Restrict/Cascade) **okunmadı**.
- Kira (lease) deseninin ayrıntısı (`cqrs-11d-claim-lease.md`) okunmadı; hosted service deseni yalnız `AddHostedService` kayıtlarından.
- `ToLowerInvariant` ile `İ` harfinin mükerrer karşılaştırmasındaki davranışı çalıştırılarak doğrulanmadı; içe aktarma kuralı mevcut domain fonksiyonunu aynen kullanacağı için DB ile tutarlı kalır ama Türkçe `İ/ı` farkları kaçırılabilir (`PossibleDuplicate` uyarısı bunu telafi etmeyi amaçlar).
- `TimeZoneInfo` kimliği Windows/Linux farkı (`Turkey Standard Time` / `Europe/Istanbul`) yalnız mevcut randevu kodundaki kullanımla gerekçelendi, bu içe aktarma için denenmedi.
- Satır başına işlem süresi, ClosedXML bellek tüketimi, `BatchSize` varsayılanı (ayardan; değer okunmadı, kod `Clamp(1,500)`), Kestrel/IIS istek gövde varsayılanı ölçülmedi/okunmadı.
- `Imports.*` izin adları ve `Imports.*` hata kodları öneridir; ürün sahibi onaylamadı. ADR-015 gerekir mi (hatırlatma, geri alma, izin kararları) kullanıcıya bırakıldı.
- KVKK konusunda (veri sorumlusu/işleyen rolü, e-posta uygunluğu, saklama süresi) bu belge hukuki tavsiye değildir.
