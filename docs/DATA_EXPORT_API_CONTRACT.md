# Tüm veri dışa aktarma — API sözleşmesi

Durum: uygulandı (2026-11-08). İlgili: ürün deposu `legal/veri-isleme-sozlesmesi.md` madde 6 (süre sonunda dışa aktarma),
`operations/CANLI-ISLETIM-KILAVUZU.md` §10 (geri dönüş). Kişi başı KVKK kopyası ayrıdır: `CLIENT_DATA_EXPORT_API_CONTRACT.md`.

Kod: `Domain/DataExports/DataExportJob.cs` (durum, saklama, sınır sabitleri), `Application/DataExports/` (uçlar, biçim),
`Infrastructure/DataExports/` (tablo listesi `DataExportTables`, ZIP yazıcı, işçi). Bu belgedeki dosya ve sütun listesi
`DataExportTables.All` ile aynıdır; pakete eklenen `BENIOKU.txt` aynı tanımdan üretilir.

## 1. Amaç

Klinik (kiracı) Vetinity'yi bırakırsa ya da verisini başka bir sistemde kullanmak isterse kendi verisinin tamamını tek ZIP
pakette alır. Paket makinece okunur CSV dosyalarından oluşur; dosyalar kayıt kimlikleriyle birbirine bağlanır.

## 2. Yetki

| Kural | Değer |
|---|---|
| İzin | `DataExport.Manage` (grup `DataExport`) |
| Varsayılan roller | `Admin`, `Owner` (ClinicAdmin, Veteriner, Sekreter almaz) |
| Paket | Her pakette açık; `PlanFeature` kilidi yok |
| Abonelik | Salt okunur ve süresi dolmuş abonelikte de çalışır (`IIgnoreTenantWriteSubscriptionGuard`); veri taşınabilirliği kilitlenmez |
| Kapsam | Kiracının tamamı (tüm klinikler); klinik seçimi yok |
| İndirme | Yalnız kimlikli istekle (`Authorization: Bearer`); imzalı ya da kimliksiz bağlantı üretilmez |

## 3. İş yaşam döngüsü

| Durum | Sayı | Anlamı |
|---|---|---|
| `Queued` | 0 | Talep alındı, işçiyi bekliyor |
| `Running` | 1 | Hazırlanıyor (işçide kira var) |
| `Ready` | 2 | Hazır; `expiresAtUtc` anına kadar indirilebilir |
| `Failed` | 3 | Hazırlanamadı; yeni talep açılabilir |
| `Expired` | 4 | Saklama süresi doldu, dosya silindi |

- İş kuyruğu `DataExportJobs` tablosudur. İşçi (`DataExportHostedService`, ayar `DataExports:Worker`) açık işi kira ile alır
  (10 dk, her dosyadan sonra yenilenir; rowversion ile iki örnekten biri). Çöken iş kira dolunca devralınır; en çok 3 deneme,
  sonra `Failed`. Beklenmeyen hata işi hemen `Failed` yapar.
- Paket önce geçici dosyaya yazılır, sonra dosya deposuna (`IFileStorage`, anahtar `{kiracı}/exports/{yyyy}/{MM}/{iş}`) taşınır.
- Saklama: hazır olduktan sonra **7 gün** (`DataExportJob.Retention`, tek sabit). Bakım turu (varsayılan saatte bir) süresi
  dolan paketin dosyasını siler ve işi `Expired` yapar.

## 4. Uçlar

Kök: `/api/v1/data-exports`. Tüm uçlar `DataExport.Manage` ister; yoksa `403`.

### 4.1 `GET /api/v1/data-exports`

Son 10 iş (yeniden eskiye) ve talep kuralı.

```json
{
  "items": [
    {
      "id": "guid",
      "status": "Ready",
      "requestedAtUtc": "2026-11-08T11:30:00Z",
      "requestedByName": "Ayşe Yılmaz",
      "startedAtUtc": "2026-11-08T11:30:05Z",
      "completedAtUtc": "2026-11-08T11:31:10Z",
      "expiresAtUtc": "2026-11-15T11:31:10Z",
      "fileSizeBytes": 123456,
      "canDownload": true
    }
  ],
  "retentionDays": 7,
  "maxRequestsPerWindow": 3,
  "windowHours": 24,
  "remainingRequests": 2,
  "hasOpenJob": false
}
```

`status` metin olarak döner. Depo anahtarı hiçbir yanıtta yoktur. İstemci kuralı yeniden hesaplamaz; `remainingRequests` ve
`hasOpenJob` alanlarını kullanır.

### 4.2 `POST /api/v1/data-exports`

Gövdesiz. Başarıda `202 Accepted` ve kuyruktaki iş (4.1 satırı, `status: "Queued"`).

### 4.3 `GET /api/v1/data-exports/{id}/download`

Hazır paket: `200`, `Content-Type: application/zip`, `Content-Disposition: attachment; filename=vetinity-veri-yyyyMMdd-HHmm.zip`
(İstanbul saati), `Cache-Control: private, no-store`.

## 5. Kurallar ve hatalar

| Kod | HTTP | Ne zaman |
|---|---|---|
| `DataExports.AnotherJobRunning` | 409 | Kiracıda açık (`Queued`/`Running`) iş var. Tek açık iş filtreli benzersiz indeksle (`UX_DataExportJobs_TenantId_OpenJob`) güvence altında; eşzamanlı ikinci talep de 409 alır |
| `DataExports.LimitReached` | 409 | Son 24 saatte 3 talep açılmış (`Failed` işler sayılmaz) |
| `DataExports.NotReady` | 409 | İndirme: iş hazır değil |
| `DataExports.Expired` | 410 | İndirme: saklama süresi doldu |
| `DataExports.NotFound` | 404 | İş yok ya da başka kiracının |
| `Files.StorageUnavailable` | 503 | Dosya deposu yapılandırılmamış ya da erişilemiyor |

## 6. Paket biçimi (sürüm 1)

### 6.1 Genel kurallar

- ZIP içinde her tablo bir `.csv` dosyası ve `BENIOKU.txt`.
- CSV: UTF-8 **BOM'lu**, ayırıcı `;` (Türkiye Excel; rapor CSV'leriyle aynı kural), satır sonu CRLF, ilk satır başlık.
  Ayırıcı, tırnak ya da satır sonu içeren hücre çift tırnakla sarılır (RFC 4180); başka değişiklik yapılmaz.
- Her dosyanın **ilk sütunu `Id`**: kaydın kalıcı kimliği (GUID, `D` biçimi). Diğer dosyalardaki `...Id` sütunları bu kimliklere
  bağlanır (ör. `hastalar.csv MusteriId` → `musteriler.csv Id`).
- Zaman (an) sütunları iki sütundur: `{Ad}Utc` = ISO 8601 UTC (`2026-11-08T11:30:00Z`), `{Ad}Istanbul` = ISO 8601 İstanbul saati
  (`2026-11-08T14:30:00+03:00`).
- Saatsiz tarih (doğum tarihi, son kullanma): `yyyy-MM-dd`.
- Sayı ve tutar: ondalık nokta (`1250.50`), binlik ayırıcı yok; para birimi ayrı sütunda.
- Evet/hayır: `Evet` / `Hayır`. Boş hücre: değer yok.
- Kod sütunları (durum, tür) sabit İngilizce enum adlarıdır; olası değerler `BENIOKU.txt` sütun açıklamasında listelenir.
  Birden çok bayrak (hasta uyarıları) virgülle ayrılır.
- İptal edilmiş/hatalı işaretlenmiş kayıtlar da verilir; iptal sütunlarına bakılır.
- Sütun eklemek geriye uyumludur; sütun kaldırma ya da anlam değişikliğinde `DataExportFormat.Version` artar.

### 6.2 `BENIOKU.txt`

Biçim sürümü, oluşturma zamanı (UTC ve İstanbul), genel kurallar, pakette olmayanlar, dosya listesi (satır sayısıyla) ve her
dosyanın sütun açıklamaları. CRLF, UTF-8 BOM'lu.

### 6.3 Dosyalar

| Dosya | İçerik | Bilerek verilmeyen alanlar |
|---|---|---|
| `klinikler.csv` | Klinikler: ad, il/ilçe, iletişim, adres, etkin | — |
| `kullanicilar.csv` | Kurum kullanıcıları: kimlik, görünen ad, katılma zamanı | E-posta, parola özeti, kilit/deneme sayacı, roller |
| `musteriler.csv` | Müşteriler: ad, iletişim, adres, tercih kanalı, hatırlatma tercihi, KVKK rıza durumu/tarihi/sürümü, fatura kimliği (TCKN, VKN, vergi dairesi, unvan, fatura adresi) | Normalize telefon, rıza formu özeti (hash) |
| `hastalar.csv` | Hastalar: sahip, ad, tür, ırk, renk, cinsiyet, doğum tarihi, kilo, kısırlık, çip/pasaport/protokol/HBS no, uyarılar ve notu, not, durum, vefat | — |
| `randevu_nedenleri.csv` | Randevu nedenleri | Hekim eşleşmeleri |
| `randevular.csv` | Randevular: hasta, zaman, süre, tür, durum, neden, hekim, not, sahip onayı | Değişiklik sayacı |
| `gelisler.csv` | Gelişler: hasta, randevu, hekim, zamanlar, durum, acil, neden, hatalı işaretleme | Değişiklik sayacı |
| `muayeneler.csv` | Muayeneler: tüm metin alanları (şikâyet, anamnez, bulgular, değerlendirme, plan, not), vital bulgular, durum, kesinleştirme zamanı ve kişi | — |
| `muayene_ek_notlari.csv` | Kesinleşmiş muayene ek notları: gerekçe, metin, ekleyen | — |
| `tedaviler.csv` | Tedaviler | — |
| `receteler.csv` | Reçeteler: içerik, HBS durumu, iptal, geç giriş | HBS işaretleyen kişi |
| `recete_satirlari.csv` | Reçete satırları: ilaç, ürün, miktar, doz, birim, kullanım, seri no | — |
| `asilar.csv` | Aşılar (uygulanan ve planlanan): ad, durum, zamanlar, yol/bölge, not, HBS | İçe aktarma partisi kimliği |
| `eski-program-kayitlari.csv` | Eski program kayıtları (içe aktarılmış salt okunur klinik geçmiş, ADR-048): tarih, kayıt türü, şikâyet, anamnez, bulgular, tanı, tedavi, reçete ve lab metni, not, hekim adı, kilo, ateş | İçe aktarma partisi kimliği |
| `uygulamalar.csv` | İlaç/malzeme uygulamaları: ürün, parti, miktar, yol/bölge, iptal | İstek anahtarı, paket önerisi bağı |
| `lab_sonuclari.csv` | Lab istek ve sonuçları: test, durum, zamanlar, sonuç metni, yorum, iptal | Ekli dosyaların içeriği |
| `goruntuleme.csv` | Görüntüleme kayıtları | Görüntü dosyaları |
| `dosyalar.csv` | Yüklenen dosyaların kaydı: bağlı kayıt, dosya adı, tür, boyut, durum | Dosya içeriği, depo anahtarı, SHA-256 |
| `yatislar.csv` | Yatışlar: zamanlar, neden, konum, not, taburcu notu | Oda kimliği |
| `bakim_talimatlari.csv` | Bakım talimatları | — |
| `ucretler.csv` | Ücretler: açıklama, tür, miktar, birim fiyat, tutar, KDV, para birimi, kapanan tutar, iptal; devir bakiyesinde içe aktarma partisi (`IceAktarmaPartisiId`, ADR-048) | İstek anahtarı, değişiklik sayacı |
| `eski-program-hareketleri.csv` | Eski program hesap hareketleri (içe aktarılmış, salt okunur; bakiyeyi etkilemez, ADR-048): tarih, borç/tahsilat, tutar, açıklama, ödeme şekli, belge no, hasta adı | İçe aktarma partisi kimliği |
| `odemeler.csv` | Ödemeler: tutar, yöntem, nakdin kasası (`KasaId`; nakitte boşsa Ana kasa), zaman, dağıtılan ve krediye kalan, not, iptal | İstek anahtarı ve özeti, değişiklik sayacı |
| `odeme_dagitimlari.csv` | Ödemenin ücretlere dağıtımı | — |
| `krediler.csv` | Müşteri kredileri ve **iadeleri** (iade zamanı, tutarı, yöntemi, nakit iadede kasası, gerekçesi, iade eden); devir alacağında kaynak ödeme boş, içe aktarma partisi dolu (`IceAktarmaPartisiId`, ADR-048 T3) | Değişiklik sayacı |
| `kredi_kullanimlari.csv` | Kredinin ücretlere uygulanması | İstek anahtarı |
| `kasalar.csv` | Kasalar (TIER-008): klinik, kod, ad, Ana kasa mı, etkin | — |
| `kasa_hareket_turleri.csv` | Kasa giriş/çıkışının gelir/gider türleri | — |
| `kasa_hareketleri.csv` | Kasa giriş/çıkışları ve kasalar arası aktarım bacakları: kasa, tür, gün, tutar, gelir/gider türü, açıklama, aktarım bağı ve karşı kasa, kaydeden, iptal | Değişiklik sayacı |
| `kasa_sayimlari.csv` | Günlük kasa sayımları: kasa, gün, açılış, beklenen, sayılan, fark, açıklama, düzeltme bağı, kaydeden, iptal | Değişiklik sayacı |
| `urunler.csv` | Ürünler: ad, kategori, tür, SKU, barkod, birim, fiyat, kontrollü madde sınıfı, form, üretici | — |
| `stok_durumu.csv` | Klinik bazında eldeki miktar ve asgari stok | — |
| `stok_partileri.csv` | Stok partileri: seri, son kullanma, miktarlar, tedarikçi, fatura, teslim/sevk, taşıma, soğuk zincir | — |
| `stok_hareketleri.csv` | Stok hareketleri: tür, miktar, maliyet, neden, kaynak kayıt, ters hareket, parti | İstek anahtarı |
| `satislar.csv` | Satışlar: müşteri, zaman, toplam, ücret/ödeme bağı, iptal | İstek anahtarı ve özeti |
| `satis_satirlari.csv` | Satış satırları | — |
| `hatirlatma_gunlugu.csv` | Hatırlatma gönderimleri: tür, kaynak kayıt, planlanan/hatırlatılan zaman, durum, gönderim/hata zamanı | **İleti içeriği**, alıcı adı ve adresi, hata metni, tekrar anahtarı, outbox kimliği |
| `portal_baglari.csv` | Hasta sahibi portalı bağları: müşteri, durum, kurulma/kapanma, aydınlatma ve ticari ileti izni zamanları | **Portal hesabı kimliği ve giriş bilgileri** |

Kayıt defterleri (muayene, reçete vb.) bu tablolardan türetildiği için ayrı dosya değildir; rapor merkezinden alınmaya devam eder.

### 6.4 Pakette olmayanlar

| Veri | Neden |
|---|---|
| Parola özetleri, oturum ve yenileme belirteçleri, e-posta doğrulama ve portal kodları, davet belirteçleri | Kimlik bilgisi; taşınabilir veri değil |
| API anahtarı özetleri ve ön ekleri | Kimlik bilgisi |
| Portal hesapları (`OwnerAccount`) ve push cihazları | Hasta sahibinin kendi hesabı; kliniğe ait değil (yalnız bağ durumu verilir) |
| E-posta/bildirim içerikleri, outbox | İleti içeriği verilmez |
| Dosya içerikleri (görüntü, lab ekleri) | Sürüm 1 dışında; `dosyalar.csv` kayıt ve ad verir (çalışma varsayımı) |
| Denetim kayıtları, kayıt geçmişi, içe aktarma partileri | İç işletim kaydı |
| Abonelik, ödeme oturumu, fatura profili, ayarlar (randevu, online randevu, portal, web sayfası, hatırlatma kuralları) | Klinik işletim verisi değil ya da Vetinity'ye özgü yapılandırma |
| Başka kiracıların verisi | Her sorgu kiracıyı açıkça süzer; kiracı kapsamı filtresine ek |

## 7. Bildirim

İş hazır olunca talep edene bildirim merkezi kaydı: `UserNotificationType.DataExportReady = 6`; başarısız olursa
`DataExportFailed = 7`. `targetType = "DataExport"`, `targetId` = iş kimliği, `clinicId = null`.

## 8. Denetim

| Eylem | `Action` | `TargetId` |
|---|---|---|
| Talep | `DataExport.Request` | — |
| İndirme | `DataExport.Download` | `DataExportJobId={id}` |

Başarılı ve başarısız denemeler `AuditBehavior` ile yazılır; paket içeriği denetime yazılmaz. Günlüklere yalnız iş kimliği,
sayı ve istisna türü yazılır.

## 9. Çalışma varsayımları

- Saklama 7 gün; talep sınırı kayan 24 saatte 3 (başarısız işler sayılmaz); kiracıda tek açık iş.
- Ayırıcı `;` ve UTF-8 BOM mevcut rapor CSV kuralıyla aynı; sayılar ondalık noktalı (rapor CSV'sinden farklı, makinece okunur).
- Hücreler değiştirilmez (formül önekine tırnak eklenmez); `BENIOKU.txt` Excel'de metin olarak açmayı önerir.
- Paket kiracının tamamıdır; tüm klinikler tek pakette.

## 10. Testler

- Domain: `DataExportJobTests` (kira, deneme sınırı, saklama, süre dolumu, anahtar).
- Application: `DataExportFormatTests`, `DataExportAccessRulesTests` (biçim, BENIOKU, izin bağları, abonelik muafiyeti, sınır).
- Entegrasyon: `DataExportIntegrationTests` (ZIP dosya listesi ve içerik, hassas alan ve başka kiracı yokluğu, salt okunur
  abonelik, 403, tek açık iş ve sınır, süre dolumu ve 410, denetim kaydı, bildirim).
