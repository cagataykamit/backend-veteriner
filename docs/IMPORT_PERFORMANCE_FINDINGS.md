# İçe aktarma deneme ve performans bulguları — Aşama 9 Dilim T

Tarih: 2026-10-14. Dal: `test/import-deneme` (taban `main` `228e383`). Kapsam: sentetik pilot benzeri dosyayla uçtan uca deneme ve ölçüm (`ASAMA9_IMPORT_DESIGN.md` §11 T, §13 R1–R4). Ürün kodu yalnız bulunan iki gerçek hata için değişti (bölüm 5); migration, seed, yapılandırma değişmedi; paylaşılan/dev veritabanına ve çalışan API'ye dokunulmadı.

## 1. Özet

- **Uçtan uca akış pilot hacminde çalıştı:** şablon → yükle → eşle → doğrula → önizleme → değer eşleme → commit (gerçek arka plan işi) → ilerleme → sonuç → hatırlatma dışlaması → serbest bırakma → ikinci parti → geri alma önizleme / `Fail` / `DeleteUnblockedOnly` → 30 gün temizliği. 36 denetimin hepsi geçti (tam ölçek, iki koşu).
- **Pilot hacmi tek partiye sığmıyor:** 20.000 aşı satırı tablo sınırını (10.000) aşıyor; tek dosya `400 Imports.TooManyRows`. Dosya bölünmek zorunda; ikinci aşı dosyası hastaya yalnız *sahip adı + cep + hayvan adı + tür* ile bağlanabildiğinden geçerli cep telefonu olmayan sahiplerin aşıları bağlanamadı (**%21 `Imports.PetNotFound`**, bölüm 4.4).
- **Süreler (LocalDB, Debug, tek kullanıcı):** 23.000 satırlık partinin doğrulaması **~38 sn senkron HTTP isteği**; uygulaması **2,5–3,6 dk** (4.799 sahip birimi); geri alması **1,2–1,8 dk**; 30 gün temizliği 42 sn.
- **İki gerçek hata bulundu ve düzeltildi** (ayrı commit + regresyon testi): (1) `/health/ready` yoğun yazımda **sahte 503** (projeksiyon durum okuyucusu yeni olayı "yeniden deneme bekleyen" sayıyordu; R4'te görülen 503'lerin tek nedeni buydu); (2) hatırlatma işçisi pencerede `BatchSize`'dan fazla aday olunca **kalanları hiç değerlendirmiyordu** (serbest bırakılan içe aktarma partisinde e-posta kaybı riski).
- **R4 (outbox / health):** düzeltmeden sonra tam ölçekte ~460 örnekte **0 × 503**; müşteri projeksiyonu bekleyeni en çok 182, en eski olay yaşı en çok 2 sn; genel outbox bekleyeni en çok 3. Geri basınç bu makinede **hiç devreye girmedi** (eşiğe yaklaşılmadı).
- **Hatırlatma dışlaması gerçekten çalıştı (R1):** pencerede 32 içe aktarılmış aşı varken serbest bırakmadan önce **0** gönderim günlüğü (aynı turda elle girilmiş kontrol aşısı için 1); serbest bırakma sonrası 32 günlük (1 e-posta, 31 e-postasız `Skipped`); serbest bırakılmamış ikinci parti için 0.
- **Darboğazlar (öneri, uygulanmadı):** ikinci partinin doğrulamasında mevcut kayıt sorguları karesel büyüyor (22 sn / tur); işçi SQL komutlarının **~%29'u** her komutta tekrar eden kiracı/abonelik okuması; doğrulama sonucu yazımı satır başına ~1,5 ms; MARS açıkken bir pilot döngüsünde **73.179** "Savepoints are disabled" uyarı günlüğü.

## 2. Yöntem

- **Ortam:** yerel, Windows 11, i7-13650HX (20 mantıksal işlemci), 16 GB RAM (**ölçüm sırasında boşta ~1,8 GB**), LocalDB, Debug derleme. Uygulama süreç içi `WebApplicationFactory` ile; gerçek HTTP yığını, JWT, yetki, MediatR hattı, EF ve SQL dahil.
- **Veritabanı yalıtımı:** koşu kimlikli izole test DB (`VetinityCommandDb_IntegrationTests_<koşu>`), `IntegrationTestDatabaseGuard` ile doğrulanır; koşu sonunda silinir. Query DB ayrı test veritabanı.
- **Arka plan işleri gerçekten çalıştı:** uygulama ve geri alma işleri üretimdeki `ImportCommitHostedService` / `ImportRollbackHostedService` sınıflarıyla, `PollIntervalSeconds = 1` ve **varsayılan geri basınçla**; test, işi `progress` ucundan 2 sn aralıkla yoklar (arayüz gibi). Hosted service'ler test tarafından başlatılır (SQL'i işçiye atfetmek için `AsyncLocal` etiketi). Müşteri projeksiyonu üretim döngüsüyle açık (2 sn, 50'lik parti), hasta projeksiyonu kapalı (pilot ayarı), genel outbox 5 sn; e-posta gönderici no-op. Hatırlatma işçisi doğrudan çağrılır (`ReminderProcessorService.ProcessOnceAsync`), kiracıda aşı hatırlatması açık (3 gün). Bakım işi `FakeTimeProvider` ile 30 gün ileri alınarak çağrılır.
- **Veri üretici:** `tests/Backend.Veteriner.IntegrationTests/Performance/ImportPilotFileGenerator.cs` — sabit tohum, gerçek kişisel veri yok (ortak ad listeleri, sıra numarasından telefon, `example.com`). Klinik başlık yazımları (takma ad: `Ad Soyad`, `Cep Telefonu`, `Hasta No`, `Aşı`…). Kirli veri karışımı ve enjekte edilen sayılar raporda; tam ölçekte örn. 734 büyük harfli ad, 377 sabit hat + 158 geçersiz + 237 boş telefon, 259 baştaki 0'ı kaybolmuş, 100 dosya içi mükerrer müşteri, 215 sistemde zaten kayıtlı müşteri, 50 dosya içi + 36 sistemle çakışan çip, 32 bilimsel gösterimli çip, 161 bilinmeyen tür, 441 bilinmeyen aşı, ~450 bozuk/belirsiz tarih. Varyantlar: XLSX (üç sayfa) ve Windows-1254 `;` CSV (BOM yok).
- **Partiler:** A = müşteri + hasta + aşıların ilk yarısı (XLSX; tam ölçekte 5.000 + 8.000 + 10.000 = 23.000 satır, 858 KB); B = aşıların ikinci yarısı (CSV-1254, `HayvanNo` yok, sahip + hayvan adı + tür ile bağlanır; 10.000 satır). A'nın CSV-1254 varyantı ayrıca doğrulanıp sayılar karşılaştırıldı, sonra silindi. Bilinmeyen tür/aşı değerleri ADR-019 K6 varsayılanıyla **atlandı** (`targetId: null`; katalog genişletilmedi).
- **Ölçüm:** aşama başına duvar saati süresi; EF `DbCommandInterceptor` ile komut sayısı ve süresi kaynağa göre (HTTP isteği / işçi / `progress` / sağlık / diğer arka plan), okuyucu ömrü (`DataReaderDisposing`: akışla okunan büyük sonuçların süresi), `GC.GetTotalAllocatedBytes` farkı, GC 0/1/2 sayısı, çalışma kümesi. Saniyede bir `/health/ready` + geri basınç ölçümü (`IImportBackpressureProbe`) + kapalı hasta projeksiyonunun bekleyeni; 500 ms'de bir `sys.dm_exec_requests` kilit beklemesi.
- **Kademeli ölçek:** müşteri 500 (A 2.300 satır), 1.100 (A 5.060 satır), 5.000 (A 23.000 satır, tam; iki koşu).
- **Çalıştırma** (PowerShell; normal test koşusunda `[PerfFact]` atlanır):

```
$env:VETINITY_PERF='1'; $env:INTEGRATION_TEST_RUN_ID='ImpTFull'; $env:PERF_OUT="$env:TEMP\import-perf.md"
$env:IMPORT_PERF_CUSTOMERS='5000'
dotnet test tests\Backend.Veteriner.IntegrationTests --filter "FullyQualifiedName~ImportPerformanceTests"
```

## 3. Ölçülen değerler

### 3.1 Aşama süreleri (ms; tam ölçekte iki koşu "1. / 2.")

| Aşama | 500 | 1.100 | 5.000 (1. / 2.) | Not (tam ölçek) |
|---|---|---|---|---|
| Tek dosya (20.000 aşı) yükle | — | — | 2.648 / 7.824 | `400 Imports.TooManyRows`; ret öncesi ~510 MB ayrıldı |
| A yükle (XLSX) | 1.422 | 2.049 | 5.051 / 7.631 | 558 SQL (toplu ekleme, ~42 satır/komut) |
| A doğrula (1. tur) | 3.355 | 7.055 | 37.634 / 38.692 | **senkron istek**; SQL yürütme 35,4 sn (sonuç yazımı) |
| A doğrula (2. tur, değer eşleme sonrası) | 212 | 586 | 2.143 / 2.042 | yalnız değişen satırlar yazılır |
| A önizleme / satırlar / hata raporu | 37 / 58 / 68 | 60 / 61 / 83 | 141 / 115 / 230 | sabit SQL: 6 / 5 / 5 |
| A CSV-1254 varyantı doğrula | 2.514 | 5.153 | 37.821 / 29.876 | sayılar XLSX ile **birebir aynı** |
| **A commit + arka plan** | 12.180 | 22.262 | **215.755 / 150.214** | 4.799 birim; 42,7 SQL/birim; geri basınç bekleme 0 |
| B yükle (CSV-1254) | 125 | 229 | 1.856 / 1.369 | kodlama otomatik algılandı, uyarı yok |
| B doğrula (1. / 2. tur) | 1.248 / 268 | 3.140 / 1.093 | 35.947 / 35.838 — 19.453 / 22.922 | 2. turda SQL okuma 21,8 sn (bölüm 4.1) |
| **B commit + arka plan** | 6.048 | 10.169 | **63.205 / 36.648** | 3.089 birim; 24,1 SQL/birim |
| Hatırlatma serbest bırakma | 99 | 46 | 228 / 139 | |
| Geri alma önizleme | 106–294 | 78–276 | 467–1.195 | sabit 24 SQL |
| A geri al (`Fail`) → 409 | 98 | 76 | 566 / 457 | `Imports.RollbackBlocked` |
| B geri al (`Fail`) + arka plan | 4.110 | 6.142 | 23.055 / 18.659 | 10.000 aşı satırı |
| **A geri al (`DeleteUnblockedOnly`) + arka plan** | 10.200 | 16.248 | **109.932 / 74.402** | 4.539 müşteri + 7.155 hasta + 8.636 aşı silindi |
| Bakım: 30 gün satır verisi temizliği | 3.478 | 7.116 | 41.840 / 41.692 | 33.000 satır boşaltıldı |

Tam ölçek iki koşu arasındaki fark (commit 216 / 150 sn) aynı makinede; LocalDB ve boş belleğin (~1,8 GB) değişkenliği. Mutlak değerler üretime taşınamaz (bölüm 6).

### 3.2 Uygulama sonuçları (tam ölçek)

| Tablo | Toplam | Hata (doğrulama) | Oluştu | Eşleşti | Atlandı | Başarısız |
|---|---|---|---|---|---|---|
| A müşteri | 5.000 | 101 | 4.584 | 215 | 201 | 0 |
| A hasta | 8.000 | 557 | 7.191 | 0 | 809 | 0 |
| A aşı | 10.000 | 989 | 8.636 | 0 | 1.364 | 0 |
| B aşı | 10.000 | 2.347 | 7.341 | 0 | 2.659 | 0 |

Değişmezler her tabloda tuttu (`valid + warning + error = total`, `willCreate + willMatch + willSkip = valid + warning`, `created + matched + skipped + failed = total`). Enjekte edilen kirli veriyle doğrulama kodları uyumlu (örn. 535 `PhoneInvalid` = 377 sabit hat + 158 geçersiz; 215 `MatchedExisting` = 215 sistemde kayıtlı; 32 `MicrochipScientificNotation`). `AppliedBeforeBirth` uyarısı (1.679) üreticinin rastgele doğum/aşı tarihlerinden; kural doğru çalışıyor.

### 3.3 EF komutları ve N+1

| Uç / iş | SQL komutu | Ölçekle davranış |
|---|---|---|
| Önizleme / satırlar / hata raporu / progress | 6 / 5 / 5 / ~4 | sabit (500 → 23.000 satır) |
| Geri alma önizleme / istek | 24 / 28–32 | sabit |
| Yükleme / doğrulama (1. tur) | 558 / 566 | satır sayısıyla doğrusal — EF toplu yazma (~42 satır/komut); satır başına komut **yok** |
| Uygulama işçisi | 43,1 / 42,9 / 42,7 SQL/birim (A) | birim başına sabit; **N+1 yok** (komut sayısı birim içi satırla doğrusal, ölçekle artmıyor) |
| Geri alma işçisi | ~7 SQL / silinen kayıt | sabit |

İşçinin tüm koşudaki (iki commit + iki geri alma, ~450.000 komut) en sık komutları: `Tenants` TOP(1) **78.435** ve `TenantSubscriptions` TOP(1) **50.683** (birlikte **~%29**). Kaynak: her komutta (`ApplyImportOwnerUnit` / `RollbackImportOwnerUnit` + içinden çağrılan `CreateClient` / `CreatePet` / `CreateImportedVaccination`) `TenantSubscriptionWriteGuardBehavior` (kiracı + abonelik) ve işleyicinin kendi kiracı okuması; A uygulamasında birim başına tahmini ~10 kiracı + ~5 abonelik okuması. 1. koşuda bu iki sorgunun yürütme süresi toplamı 29,5 + 18,0 sn (tüm işçi işleri için).

### 3.4 Bellek

Süreç tepe çalışma kümesi 506–513 MB (test sunucusu + tüm hosted service'ler dahil). Yönetilen yığın 82–189 MB. Ayrılan bellek: A XLSX yükleme ~760 MB (GC2: 8–9), A doğrulama ~524 MB, A commit ~9 GB (150–216 sn boyunca; GC0 ~750, GC2 3–4), A geri alma ~6,6 GB. 20.000 aşılı tek dosya reddedilmeden önce ~510 MB ayırdı (ClosedXML çalışma kitabını tümüyle açıyor; satır tavanı okuma sırasında denetleniyor).

### 3.5 R4 — outbox, geri basınç, `/health/ready`

| Aşama (tam ölçek, 2. koşu) | Örnek | 503 | Müşteri proj. bekleyen (maks) | En eski olay yaşı (maks) | Genel outbox (maks) | Hasta olayı bekleyen | `/health/ready` ms (ortanca/maks) |
|---|---|---|---|---|---|---|---|
| A commit | 145 | 0 | 97 | 2 sn | 0 | 0 → 7.191 | 14 / 73 |
| B commit | 35 | 0 | 0 | 0 | 0 | 7.191 | 18 / 36 |
| A geri alma | 71 | 0 | 182 | 2 sn | 0 | 7.191 → 14.346 | 24 / 84 |

- Düzeltmeden **önce**: 500 ölçekli ilk koşuda A geri alması sırasında 6 × 503 (müşteri projeksiyonu Degraded) ve bir `progress` isteği `500` döndü; 1.100 ölçekte 2 × 503, günlük nedeni `Client projection retry-waiting count: 1` (projeksiyonda hata/yeniden deneme günlüğü **yok**). Kök neden ve düzeltme bölüm 5.1. Düzeltmeden **sonra** iki tam ölçek koşusunda 0 × 503.
- Geri basınç: işçi (A'da ~22–32 müşteri/sn) müşteri projeksiyonunun tüketim hızının altında kaldı; eşikler (bekleyen ≥ 200 veya yaş ≥ 6 sn; genel ≥ 300) hiç aşılmadı, bekleme sayısı 0. Eşiklerin "yavaş tüketici" altındaki davranışı bu makinede **ölçülemedi**; mekanizmanın kendisi B4 entegrasyon testinde doğrulanmıştı.
- Hasta projeksiyonu kapalıyken `pet.created.v1` / `pet.deleted.v1` olayları **birikiyor** (bir pilot döngüsünde 14.346) ve sağlığı etkilemiyor (tasarım §14'teki "doğrulanmadı" maddesi doğrulandı). Hasta projeksiyonu açıldığı anda bu birikimin yaşı Degraded eşiğini (10 sn) aşacağından `/health/ready` tüketim bitene kadar 503 verecektir (öneri 7).
- Genel outbox (`OutboxHealthCheck`, eşik 500) içe aktarmadan etkilenmiyor: müşteri/hasta olayları projeksiyon olayı olarak bu sayımın dışında; yalnız hatırlatma e-postaları girer (en çok 3).
- Kilit beklemesi (DMV, 500 ms) tam ölçekte gözlenmedi.

### 3.6 Hatırlatma dışlaması (R1, K3)

Tam ölçek: A'da vadesi pencerede (3 gün) 32 içe aktarılmış aşı (sahibi e-postalı: 1). Serbest bırakmadan önceki işçi turunda içe aktarılan aşılar için **0** günlük; aynı turda elle girilmiş kontrol aşısı için 1 günlük (işçi çalışıyor). Serbest bırakma (`confirmEmailCount = 1`) sonrası 5 turda 32 günlük: 1 `Enqueued`, 31 `Skipped` (e-postasız). Serbest bırakılmamış B partisinin aşıları için 0.

### 3.7 Geri alma (bağı olan / olmayan karışımı)

Engel: oluşturulan hastaların %0,5'ine geliş (36), müşterilerin %0,2'si düzenlendi (10).
- B commit'liyken A önizlemesi: silinebilir 1.652 / 2.952 / 8.636, **engelli 2.932 / 4.239** (neden `HasOtherVaccinations`: B'nin aşıları A'nın hastalarına bağlı). Partiler **ters sırayla** (önce B) geri alınmalı.
- B geri alındıktan sonra: engelli 45 / 36 / 0 (`HasVisits` 36, `HasKeptPets` 35, `ClientModified` 10); `Fail` → `409 Imports.RollbackBlocked`, hiçbir şey silinmedi; `DeleteUnblockedOnly` → `PartiallyRolledBack`. Kalan kayıt (262 / 73 / 1) = mevcut veri (217 müşteri, 37 hasta, 1 aşı) + engelliler (45 / 36 / 0); eşleşen 215 mevcut müşteri **silinmedi**. Uyarılar `PetEditsNotTracked`, `RemindersAlreadyReleased` döndü.

## 4. Darboğazlar ve gözlemler (öneri; uygulanmadı)

### 4.1 İkinci partide doğrulama: mevcut kayıt sorguları karesel (en büyük)
B (10.000 aşı, kiracıda 4.800 müşteri / 7.200 hasta) her doğrulama turunda **~22 sn**, bunun 21,8 sn'si iki sorgunun **okunması**: `ImportExistingClientsSpec` (en uzun 10,9 sn) ve `ImportExistingPetsSpec` (en uzun 10,7 sn). Yürütme süresi kısa, satırlar yavaş akıyor: `OR` ile bağlı `IN (OPENJSON …)` kümeleri ve harmanlanmış ad ifadesi index aramasını engelliyor; plan her satır için JSON kümesini tarıyor (kayıt × anahtar). Ölçekle: B 2. tur 268 ms → 1.093 ms → ~20 sn (satır ×4,5, kiracı ×4,5 → süre ×20). Senkron istek olduğu için büyüyen kiracıda zaman aşımı riski.
Seçenekler: (a) sorguları ayırmak (müşteri: telefon / e-posta / ad; hasta: `ClientId` / çip) ve sonuçları bellekte birleştirmek — sorgu sayısı sabit kalır, `(TenantId, ClientId)` indeksi kullanılabilir; (b) `(TenantId, PhoneNormalized)` ve `(TenantId, MicrochipNumber)` indeksleri (migration, onay gerekir); (c) anahtar kümesi büyükse kiracının izdüşümünü (kimlik, ad, telefon, e-posta) tek taramayla belleğe almak. Her seçenek planla yeniden ölçülmeli.

### 4.2 Uygulamada tekrar eden kiracı/abonelik okuması
İşçi komutlarının ~%29'u (`Tenants` + `TenantSubscriptions` TOP(1)); A uygulamasında birim başına tahmini ~15 okuma. Öneri: kapsam (DI scope) başına bellek içi önbellek (`TenantSubscriptionEffectiveWriteEvaluator` ve işleyicilerin kiracı okuması) — tüm komut hattını etkilediği için ayrı iş. Kazanç tahmini commit süresinin ~%10–20'si (ölçülmedi).

### 4.3 Commit'te mikroçip denetimi indeks olmadan
Uygulama anındaki K10 denetimi (`ImportExistingPetsSpec` çip başına) Pets'te çip indeksi olmadığından kiracı hastalarını tarıyor: 1. tam koşuda 2.965 çağrı, toplam 21,5 sn, ortalama 7 ms (kiracıdaki hasta sayısıyla büyür). Birim süresi 1.100 ölçekte 21 ms, 5.000'de 31–45 ms. Öneri 4.1(b) ile aynı indeks.

### 4.4 Tablo sınırı ve ikinci dosyanın bağlanması
Pilot 20.000 aşı > `MaxRowsPerTable` 10.000. Bölünen ikinci aşı dosyası `HayvanNo` ile önceki partiye bağlanamıyor (yalnız aynı dosyadaki hasta tablosu); sahip + cep + hayvan adı + tür ile bağlanma, cebi olmayan/sabit hatlı sahipler için kiracıda yapılmıyor → B'de **2.123 / 10.000 `PetNotFound`**. ADR-019 varsayılanı (sınır 10.000, `MusteriNo`/`HayvanNo` yalnız dosya içi) korundu. Seçenekler: aşı tablosu sınırını yapılandırmayla 20.000'e çıkarmak (`Imports:Limits`; bellek/süre bu raporda ölçülmedi: 33.000 satırlık tek dosya yalnız reddedildi), ya da önceki partilerin `HayvanNo` eşlemesini kullanmak (ürün kararı). Mevcut kurallarla pilot için güvenli yol (**denenmedi**): dosyayı **sahip bazında** bölmek — her dosyada bir müşteri grubu + onların hastaları + o hastaların aşıları; bağlantılar dosya içi `MusteriNo`/`HayvanNo` ile kalır, aynı müşteri iki dosyada yer almadığından telefonsuz müşteri de çift oluşmaz.

### 4.5 Doğrulama sonucu yazımı ve senkron istek süresi
23.000 satırın ilk doğrulaması 38 sn; bunun 35 sn'si ~550 güncelleme komutu (~62 ms/komut, ~1,5 ms/satır). B3 sözleşmesindeki ölçüm (10.200 satır ≈ 1,3 sn) burada tekrarlanmadı; fark LocalDB G/Ç ve düşük boş bellekle açıklanabilir (**doğrulanmadı**). Karar notu (`IMPORT_API_CONTRACT.md` §7.4 "süre ölçümü ve gerekirse arka plana taşıma kararı T diliminde"): pilot dosyası tablo başına ≤ 10.000 satırla senkron kalabilir ama ön yüz zaman aşımı ≥ 120 sn olmalı ve staging SQL'de yeniden ölçülmeli; staging'de 60 sn'yi aşarsa doğrulama uygulama işiyle aynı desende arka plana alınmalı. Yazımı hızlandırmak için küme tabanlı güncelleme (TVP / `SqlBulkCopy` + `MERGE`) seçeneği.

### 4.6 MARS açıkken uyarı seli
`MultipleActiveResultSets=True` (staging belgelerindeki bağlantı dizesi) ile her işlem içi `SaveChanges` EF uyarısı yazıyor: tam ölçek bir döngüde **73.179** `Savepoints are disabled…` satırı (tüm uyarıların %99,9'u). Öneri: MARS gerekmiyorsa kapatmak ya da `SqlServerEventId.SavepointsDisabledBecauseOfMARS` uyarısını yapılandırmada susturmak.

### 4.7 Diğer
- Müşteri projeksiyonunun FIFO sorgusu (`OutboxMessages` TOP(n)) 1. koşuda 1.449 çağrı, ortalama 36 ms, en çok 152 ms; hasta olayı birikimi aynı tabloda büyüdükçe artması beklenir (ölçülmedi).
- `progress` ucu 23.000 satırda ortalama ~25 ms (en çok 117 ms); satır sayımı `(BatchId, Table, RowNumber)` indeks önekiyle.
- Randevu projeksiyon işleyicisinde aynı zaman damgası yarışı (yeni olayın `NextAttemptAtUtc`'si okuma anından sonra) yalnız işlemeyi bir döngü geciktirir; düzeltilmedi.
- 500 ölçekli ilk koşuda (sağlık düzeltmesinden önce) A geri alması sırasında bir `progress` isteği `500` döndü ve bir `/health/ready` 9,4 sn sürdü; ayrıntılı günlük o koşuda tutulmadı, sonraki 4 koşuda (kilit örneklemesi açıkken) tekrarlanmadı. Neden **bilinmiyor**.

## 5. Düzeltilen hatalar (ayrı commit + test)

### 5.1 `46feff2` — `/health/ready` sahte 503 (projeksiyon durum okuyucuları)
Durum okuyucuları (randevu, müşteri, hasta, ödeme) `utcNow`'ı sorgudan önce alıyor; outbox kesişimi (`OutboxSaveChangesInterceptor`) yeni olaya `NextAttemptAtUtc = yazım anı` yazıyor. Okuma anından hemen sonra yazılan olay `NextAttemptAtUtc > utcNow` olduğundan "yeniden deneme bekleyen" sayılıyor → değerlendirici Degraded → `/health/ready` 503. Üretimde açık olan randevu projeksiyonu için de geçerli (yoğun randevu yazımında). Düzeltme: yeniden deneme bekleyen = `RetryCount > 0` ve sonraki deneme gelecekte; diğerleri bekleyen sayılır. Test: dört okuyucuya "okuma anından sonra yazılmış yeni olay" senaryosu (düzeltmeden önce 4 test kırmızı, sonra yeşil); projeksiyon/outbox/sağlık testleri 290/290 geçti.

### 5.2 `361c427` — hatırlatma işçisi kalan adayları değerlendirmiyordu
Aday sorgusu `Take(BatchSize)` ile alınıyor, günlüğü zaten yazılmış adaylar yalnız döngüde atlanıyordu. Pencerede `BatchSize`'dan (varsayılan 100) fazla aday varken her tur aynı ilk adayları seçiyor, kalanları vade geçene kadar hiç değerlendirmiyordu; içe aktarılan aşıların vadesi aynı ana (İstanbul 12:00) düştüğünden serbest bırakılan partide kalan hatırlatmalar **kaybolabiliyordu** (sözleşme §7.6 "BatchSize turlarına bölünür" ifadesi fiilen doğru değildi). Düzeltme: aşı ve randevu aday sorgusu aynı tekrar önleme anahtarlı günlüğü olan adayları dışarıda bırakır (anahtar biçimi tek yerde). Test: `BatchSize = 2`, 5 aday, 3 tur → 5 günlük (düzeltmeden önce 2); hatırlatma testleri 30/30 geçti.

## 6. Doğrulanmayanlar / sınırlar

- **Ortam:** LocalDB + Debug + süreç içi host, ağ gecikmesi yok, boş bellek ~1,8 GB. Yönetilen bulut SQL, Release derleme ve ayrı Query DB sunucusu süreleri değiştirir; yalnız oranlar ve eğilimler anlamlıdır. İki tam koşu arasında %30–45 sapma görüldü.
- **Staging uçtan uca prova** (tasarım §11 T) yapılmadı; staging ortamı yok.
- **Eşzamanlılık:** tek kullanıcı, tek parti; içe aktarma sürerken klinik trafiği (gelişler, randevular) yoktu. Çok örnekli kira devri bu denemede yok (B4 testlerinde var).
- **Geri basınç** yavaş tüketici altında tetiklenmedi; eşiklerin yeterliliği yalnız "tüketici işçiden hızlı" durumunda gösterildi.
- **İstek gövdesi sınırı** (`RequestSizeLimit`, 413) test sunucusunda uygulanamıyor (TestServer günlük uyarısı); ölçülmedi.
- Hata raporu dosyalarının içeriği, rows ucunun tüm sayfaları ve ön yüz sihirbazı denenmedi; yalnız ilk sayfa ve indirme boyutu.
- 4.1–4.6 önerilerinin etkisi **ölçülmedi**; tam test paketi çalıştırılmadı (yalnız içe aktarma + hatırlatma 156, projeksiyon/outbox/sağlık 290 test; birim testleri çalıştırılmadı).
- 500 ölçekli ilk koşudaki `progress` 500 hatasının nedeni bulunamadı (bölüm 4.7).
- Sentetik veri: düzgün rastgele dağılım; gerçek klinik dosyasının biçimi (R3) bilinmiyor.

## 7. Sonradan uygulanan düzeltmeler (dal `perf/ice-aktarma-dogrulama`, 2026-10-15)

Aynı makine, aynı test (`ImportPerformanceTests`, 5.000 müşteri ölçeği), değişiklikten önce ve sonra birer koşu; mutlak değerler bölüm 6'daki sapmaya tabidir.

- **4.1 uygulandı (seçenek a):** `ImportExistingClientsSpec` / `ImportExistingPetsSpec` anahtar türü başına ayrı sorgu (müşteri: telefon / e-posta / ad; hasta: müşteri / çip); `ImportExistingDataLoader` sonuçları kimliğe göre bellekte birleştirir (en çok 6 sorgu, sonuç kümesi birleşik `OR` koşuluyla aynı). Kural, anahtar ve Türkçe/aksan duyarsız ad karşılaştırması değişmedi; iki koşuda doğrulama sayıları, sorun dağılımları ve uygulama sonuçları birebir aynı.

| Aşama | Önce (ms) | Sonra (ms) | Not |
|---|---|---|---|
| B doğrula (2. tur) | 21.941 | 1.449 | HTTP okuyucu ömrü 20.785 → 338 ms |
| B doğrula (1. tur) | 36.641 | 15.037 | kalan süre sonuç yazımı (4.5) |
| A doğrula (2. tur) | 1.734 | 2.219 | küçük, gürültü aralığında |
| A commit + arka plan | 168.993 | 115.568 | çip denetimi dahil; koşu sapması da var |
| B commit + arka plan | 71.974 | 50.928 | |

- **4.3 (indeks) uygulanmadı:** commit'teki K10 çip denetimi artık yalnız çip sorgusunu çalıştırıyor (boş `ClientId IN OPENJSON` kolu kalktı). Önce 2.965 çağrı toplam 17.911 ms (işçinin süre sıralamasında 2.); sonra işçinin en yüksek 8 komutu arasına girmiyor (< 5.645 ms; tam değer raporda yok). Kalan maliyet commit süresinin < %5'i olduğundan `(TenantId, MicrochipNumber)` indeksi ve migration ölçülmüş bir fayda göstermiyor; kiracı hasta sayısı çok büyürse yeniden ölçülmeli.
- **3.7 geri alma sırası:** kod değişmedi; sözleşmeye (`IMPORT_API_CONTRACT.md` §9) "bağlı partiler ters sırayla geri alınır" notu eklendi.
- **4.4 tablo sınırı:** değiştirilmedi (öneri raporda).

## 8. Doğrulama arka plan işine alındı (ADR-019 K13, dal `feat/ice-aktarma-dogrulama-arka-plan`, 2026-10-15)

`POST .../validate` artık işi kuyruğa alıp `202` döner; satırları `ImportValidationProcessor` işler (sözleşme `IMPORT_API_CONTRACT.md` §7.4). Bu §4.5'teki "staging'de 60 sn aşılırsa arka plana al" önerisinin ölçümden bağımsız olarak uygulanmasıdır (kullanıcı kararı). Ölçüm: aynı makine, aynı oturum, `ImportPerformanceTests` (`VETINITY_PERF=1`, `IMPORT_PERF_CUSTOMERS=5000`, izole koşu kimlikli LocalDB), önce = `main` (`d055927`, senkron istek süresi), sonra = bu dal (POST + işin bitişine kadar 250 ms aralıklı `progress` yoklaması; perf işçisi `PollIntervalSeconds = 1`). Diğer ajanların süreçleri aynı anda çalışıyordu; mutlak değerler bölüm 6'daki sapmaya tabidir.

| Aşama | Önce: senkron istek (ms) | Sonra: POST yanıtı (ms) | Sonra: bitişe kadar (ms) |
|---|---|---|---|
| A doğrula (1. tur) | 25.509 | 20 | 25.891 |
| A doğrula (2. tur) | 2.342 | 11 | 4.080 |
| A CSV-1254 varyantı doğrula | 23.285 | 10 | 26.963 |
| B doğrula (1. tur) | 10.482 | 12 | 11.521 |
| B doğrula (2. tur) | 836 | 8 | 2.455 |

- İstek artık anlık (8–20 ms); toplam iş süresi pratikte aynı. Fark: işçinin işi alma gecikmesi (boşta yoklama aralığı, üretimde en çok 2 sn), yoklama tanesi (≤ 250 ms) ve önceki sonuçların sıfırlanıp parça parça yazılması. Sonuç sayıları, sorun dağılımları ve tüm denetimler (`[x]`) önceki koşuyla aynı.
- İlk ölçümde yeniden doğrulama (2. tur) belirgin yavaştı (A 11.933 ms, B 7.129 ms): sıfırlanan durum yüzünden her satır ayrı `UPDATE` ile yazılıyordu. Sonucu değişmeyen satırların yalnız durumu parça başına küme güncellemesiyle yazılınca yukarıdaki değerlere indi (commit `perf(imports): yeniden dogrulamada ...`).
- Sorgu sayısı (`ImportValidationQueryCountTests`, istek + işçi): 9 / 10.200 / 24.000 satırda 21 SELECT (önceki yalnız istek: 16); 10.200 satır ≈ 2,4 sn, 24.000 satır ≈ 2,3 sn; POST yanıtı 8–33 ms.
- **Doğrulanmadı:** staging SQL'de süre; çok örnekli kurulumda iki işçinin gerçekten yarıştığı senaryo (kira/rowversion birim ve entegrasyon testleriyle sınandı, gerçek iki süreçle değil).
