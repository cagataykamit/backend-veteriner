# Performans bulguları — Aşama 9 Dilim 9 (pilot hacmi ölçümü)

Tarih: 2026-10-14. Kapsam: yalnız ölçüm ve rapor. İndeks/migration **uygulanmadı ve üretilmedi**; öneriler bölüm 5'te, onay bekler.

## 1. Özet

- Pilot hacminde (5.000 müşteri, 10.000 hasta, 36.500 geliş/yıl, 72.940 ücret) ölçülen **tüm uçlar tek kullanıcıda 100 ms altında** (ortanca); istisnalar dışa aktarımlar (CSV 6 MB: ~550 ms, XLSX 90 gün: ~490 ms, ilk çağrı ~3 sn).
- **Bugün 500 satır sınırı** çalışıyor: 500 satır = 200 (ortanca 36 ms, 365 KB gövde); 501 satır = 400 `Visits.TodayLimitExceeded` (25 ms). Günlük gerçekçi hacim (100 geliş) 37 ms.
- **N+1 tespit edilmedi:** her uçta SQL komut sayısı sabit (2–10) ve aynı SQL metni istek içinde tekrar etmiyor (tek istisna: Hasta özeti, 2 tekrar — iki farklı parametreli sorgu şekli, döngü değil).
- **Ölçekle büyüyen** uçlar (1k → 10k): Borç raporu (5 → 55 ms), müşteri/hasta arama (10–24 → 34–67 ms), Defter: muayene (15 → 75 ms), CSV dışa aktarımlar. Nedenleri bölüm 4'te; eksik indeks önerileri bölüm 5'te.
- Pilot ölçeğinde acil darboğaz yok. Öneri, büyüme/çok şube (10× ve üzeri) için Charges ve Examinations indeksleri ile arama biçiminin ileride gözden geçirilmesi.

## 2. Yöntem

- **Ortam:** yerel, tek kullanıcı, Windows, LocalDB (SQL Server 2025), Debug derleme, bellek ~2 GB boşta. Uygulama süreç içi `WebApplicationFactory` ile çalıştı; gerçek HTTP yığını, JWT, yetki, EF ve SQL dahil. Ağ gecikmesi yok.
- **Veritabanı yalıtımı:** entegrasyon testi altyapısındaki koşu kimlikli izole test DB (`VetinityCommandDb_IntegrationTests_<koşu>`); `IntegrationTestDatabaseGuard` ile doğrulanır, dev/paylaşılan/üretim DB'ye ve çalışan dev API'ye dokunulmadı. Test sonunda DB silinir. Query DB kapalı (pilot kararı K5).
- **Veri üretici:** `tests/Backend.Veteriner.IntegrationTests/Performance/PerfDataGenerator.cs` (sabit tohum, domain fabrikaları, gün başına tek `SaveChanges`). Ölçek ortam değişkenleriyle: `PERF_CLIENTS`, `PERF_PETS_PER_CLIENT`, `PERF_DAYS`, `PERF_VISITS_PER_DAY`, `PERF_PRODUCTS`.
- **Ölçüm:** `PerformanceMeasurementTests` (yalnız `VETINITY_PERF=1` ile çalışır). Her uç 1 soğuk + 5 sıcak çağrı; ortanca ve en büyük süre. EF `DbCommandInterceptor` ile istek başına SQL komut sayısı, SQL süresi, tekrar eden SQL sayısı. Ölçümden önce `sp_updatestats`. Sonda DMV'ler: `sys.dm_db_missing_index_*`, `sys.dm_exec_query_stats` + plan XML (tarama/arama operatörleri).
- **Çalıştırma** (PowerShell):

```
$env:VETINITY_PERF='1'; $env:INTEGRATION_TEST_RUN_ID='Perf10'; $env:PERF_OUT="$env:TEMP\perf-10k.md"
$env:PERF_CLIENTS='5000'; $env:PERF_DAYS='365'; $env:PERF_VISITS_PER_DAY='100'
dotnet test tests\Backend.Veteriner.IntegrationTests --filter "FullyQualifiedName~PerformanceMeasurementTests"
```

## 3. Ölçülen değerler

Üretilen veri — **1k aşaması:** 1.000 müşteri, 2.000 hasta, 60 gün × 20 geliş (1.200 geliş, 2.355 ücret). **10k aşaması:** 5.000 müşteri, 10.000 hasta, 365 gün × 100 geliş (36.500 geliş, 36.433 muayene, 72.940 ücret, 33.568 ödeme, 67.253 tahsis, 10.724 reçete, 9.072 aşı, 18.250 randevu, 26.777 stok hareketi, 1.200 parti, 300 ürün). Veri üretimi 249 sn. Yaklaşık %8 geliş borçlu bırakıldı.

Ortanca süre (ms, sıcak), SQL komut sayısı 10k aşamasından. Not: 1k'da rapor pencereleri 60 gün, 10k'da 90 gündür; sütunlar tamamen karşılaştırılabilir değildir.

| Uç | 1k ms | 10k ms | SQL komut (10k) |
|---|---|---|---|
| Bugün (günlük, 100 geliş) | 30 | 37 | 8 |
| Bugün (tam 500 satır) | 15 | 36 | 8 |
| Bugün (501 satır → 400 hata) | 14 | 25 | 7 |
| Müşteri listesi (sayfa 1) | 3 | 2 | 2 |
| Müşteri arama (ad / telefon) | 10 / 11 | 34 / 37 | 2 |
| Hasta listesi (sayfa 1) | 4 | 13 | 2 |
| Hasta arama (ad / mikroçip) | 24 / 24 | 66 / 67 | 3 |
| Hasta timeline (yoğun / tipik) | 10 / 8 | 11 / 10 | 10 |
| Hasta özeti | 7 | 11 | 10 |
| Müşteri bakiyesi | 4 | 4 | 7 |
| Stok durumu (liste / minimum altı) | 5 / 4 | 4 / 3 | 5 / 4 |
| Stok hareketleri (sayfa / ürün / tarih) | 9 / 4 / 6 | 7 / 4 / 7 | 5 |
| Stok partileri | 5 | 4 | 5 |
| Borç raporu (sayfa / arama) | 5 / 19 | 55 / 96 | 5 |
| Tahsilat raporu (90 gün) | 15 | 23 | 8 |
| Randevu / Muayene / Aşı raporu (90 gün) | 8 / 10 / 6 | 7 / 16 / 17 | 7 |
| Defter: muayene | 15 | 75 | 8 |
| Defter: reçete / kontrollü madde | 7 / 6 | 16 / 22 | 5 / 4 |
| Defter: aşı / ilaç (**boş sonuç**, bkz. bölüm 6) | 3 / 3 | 8 / 9 | 3 |
| CSV: borç (tüm satırlar, 130 KB) | 5 | 61 | 5 |
| CSV: muayene defteri (1 yıl, 6 MB) | 36 | 546 | 8 |
| XLSX: tahsilat (90 gün, 412 KB) | 260 | 492 (maks 1.145) | 7 |

Soğuk (ilk) çağrı: Bugün 655 ms, XLSX 3.020 ms (JIT/plan derleme; uygulama ısındıktan sonra görülmez). Her istekte 2 sabit yetki sorgusu (UserOperationClaims, UserClinics) vardır ve süreye dahildir.

Ham çıktılar ve istek başına SQL metinleri `PERF_OUT` yolunda (`.md` ve `.sql.txt`) üretilir; depoda tutulmaz.

## 4. Sorgu incelemesi (plan önbelleği, 10k)

| Uç / sorgu | Gözlem (10k) | Not |
|---|---|---|
| Bugün — ücret tutarları (`Charges`, müşteri kümesi için) | ortalama ~8.850 mantıksal okuma/yürütme; en çok okuma yapan uygulama sorgularından | Müşterilerin **tüm geçmiş** ücretlerini okuyup bellekte toplar (`ChargeAmountsForClientsSpec`); büyüyen geçmişle artar. Eksik indeks DMV'sinde Charges ilk sırada. |
| Borç raporu (sayım, toplam, sayfa: 3 sorgu) | her biri `Charges` kümelenmiş tarama, ~3.600 okuma | Aynı filtre 3 kez yürür; ücret sayısıyla doğrusal büyür (1k'da 5 ms → 10k'da 55 ms). |
| Defter: muayene (+ CSV) | `Examinations` kümelenmiş tarama; ~14.500 okuma/yürütme | `(TenantId, ExaminedAtUtc)` indeksi var ama kapsayıcı değil; plan taramayı seçiyor. |
| Müşteri / hasta arama | `Clients` / `Pets` kümelenmiş tarama | Aramada `REPLACE(...) COLLATE ...` ve `LIKE` kullanılıyor (Türkçe harf toleransı); indeksle desteklenemez. Süre kayıt sayısıyla doğrusal: müşteride 1k→5k kayıtta 10→34 ms. |
| Hasta timeline / özeti / son geliş | `Visits` taraması (~1.300 okuma) | `Visits` üzerindeki `(TenantId, PetId)` indeksi yalnız aktif gelişleri kapsıyor (filtreli); hastanın geçmiş gelişleri için kapsayan indeks yok. |
| Bugün / randevu — ödemeler (`Payments`, randevu/muayene kimlikleriyle) | kümelenmiş tarama, ~1.400 okuma | `Payments`'ta `(TenantId, VoidedAtUtc)` kapsayan indeks yok. |
| Stok uçları | 549 okuma, indeks araması | Sorun yok (ürün/klinik/tarih indeksleri kullanılıyor). |

**N+1:** hiçbir uçta komut sayısı sonuç satırı sayısına bağlı değil (Bugün 100 ve 500 satırda aynı 8 komut). Hasta özeti'ndeki 2 tekrar, aynı şekle sahip iki ayrı filtreli sorgudur.

## 5. Eksik indeks önerileri (UYGULANMADI — onay gerekir)

Kaynak: `sys.dm_db_missing_index_*` (10k koşusu, "improvement" sırasıyla). Bunlar yalnız bu ölçüm iş yükünden çıkan **adaylardır**; her biri yazma maliyeti ve mevcut indekslerle çakışma açısından gözden geçirilmeli, uygulandıktan sonra yeniden ölçülmelidir (etkisi **ölçülmedi**).

| # | Tablo | Anahtar / INCLUDE (DMV önerisi) | Yarar |
|---|---|---|---|
| 1 | Charges | `(TenantId, VoidedAtUtc)` INCLUDE `(ClinicId, ClientId, Amount, AllocatedAmount, CreatedAtUtc)` | Bugün ücret tutarları, Borç raporu (3 sorgu), bakiye. DMV'nin iki ayrı önerisini birleştirir. En yüksek etki (skor 7.756 + 3.764). |
| 2 | Examinations | `(TenantId, ExaminedAtUtc)` INCLUDE `(ClinicId, CreatedAtUtc)` | Muayene raporu/defteri ve CSV (skor 2.187 + 1.572). Mevcut `(TenantId, ExaminedAtUtc)` indeksinin kapsayıcı hale getirilmesi anlamına gelir (yeni indeks yerine genişletme tercih edilebilir). |
| 3 | Visits | DMV: `(TenantId, PetId, VoidedAtUtc)`; muhtemelen `ArrivedAtUtc` ile birlikte daha uygun | Hasta timeline/özet/son geliş (skor 1.890). Anahtar seçimi **doğrulanmadı**; sorgunun sıralama sütunu belirlenip ayrıca denenmeli. |
| 4 | Payments | `(TenantId, VoidedAtUtc)` INCLUDE `(AppointmentId, ExaminationId)` | Randevu/Bugün ödeme eşleştirmesi (skor 1.603). |
| 5 | StockMovements | `(TenantId, MovementType, OccurredAtUtc)` INCLUDE `(ClinicId, ProductId)` | Defter: ilaç/aşı stok girişleri (skor 804). Düşük öncelik. |
| 6 | Prescriptions, Pets, StockBatches | DMV düşük skorlu (143, 112, 16) | Şimdilik gerekmez. |

Kod tarafı (migration gerektirmez, ürün/tasarım kararı gerekebilir): Bugün ücret tutarlarının yalnız açık ücretleri okuması; Borç raporunun 3 sorguyu tek geçişe indirmesi. Bunlar öneridir, uygulanmadı.

## 6. Doğrulanmayanlar / sınırlar

- **Eşzamanlılık ölçülmedi:** tek kullanıcı, tek istek. Çok kullanıcılı yük (mevcut `tests/load/*` k6 betikleri) çalıştırılmadı.
- **Gerçek ortam farkı:** LocalDB + Debug derleme + süreç içi host; yönetilen bulut SQL, ağ gecikmesi ve Release derlemesi süreleri değiştirir. Mutlak ms değerleri pilot ortamına taşınamaz; yalnız oranlar/eğilimler anlamlıdır.
- **Sentetik veri:** düzgün rastgele dağılım; gerçek klinikte yoğunlaşma (az sayıda yoğun hasta/müşteri, tatil günleri, uzun açık borç geçmişi) farklıdır. Yalnız "yoğun hasta" için tek örnek ölçüldü (yıl boyunca ~120+ geliş).
- **Kapsam dışı kalan veri/uç:** Yatış, laboratuvar sonucu, tedavi, uygulama (Administration) kayıtları, hatırlatıcılar, Dashboard uçları ölçülmedi/üretilmedi. **Defter: aşı ve defter: ilaç boş yanıt (27 bayt) döndü** — bu defterler stok *giriş* hareketlerini okur ve üretici girişleri ölçüm penceresinin dışına (veri başlangıcından önce) yazdı; bu iki satırın süreleri boş sonuç içindir, anlamlı ölçüm sayılmaz. Kontrollü madde defteri (çıkış hareketleri) dolu döndü.
- **Outbox:** `SaveChanges` kesişim noktası outbox satırlarını yazdı; işleyici (arka plan) çalıştırılmadı, dolayısıyla projeksiyon/outbox yükü ölçüme dahil değil.
- **İndeks etkisi** uygulanmadı, dolayısıyla "iyileşir" iddiası ölçülmüş değildir.
- Soğuk başlangıç (ilk çağrı) etkisi tek kez gözlendi; derinlemesine incelenmedi.
- Rapor uçlarının tarih penceresi en çok 93 gündür (`ReportsSharedLimits.MaxRangeDays`), defterler 366 gün; daha uzun aralık 400 döner (ölçüm sırasında ilk denemede görüldü).
- Test sınırı: `PerformanceMeasurementTests` normal koşularda atlanır; tam test paketi çalıştırılmadı.
