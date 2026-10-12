# Aşama 8 kod keşfi — menü/bilgi mimarisi, rapor merkezi, bildirim ve açık işler, örnek klinik/onboarding, trial politikası

Salt okunur keşif (ürün kodu değişmedi, migration yok, test/derleme çalıştırılmadı, DB'ye bağlanılmadı, HTTP ile uç denenmedi, tarayıcıda ekran açılmadı). Taban: backend yerel `main` `1eab64a`, frontend (`C:\FrontEnd\veteriner-frontend`) yerel `main` `652e6a7`. Kaynaklar: ana plan §13 (Aşama 8) ve §21, `roadmap.md`, `AJAN-KUYRUGU.md`, `KOORDINATOR-DEVIR.md`, ADR-001..004 ve ADR-013, `ux/navigation.md`, önceki `ASAMA5/6/7_DISCOVERY.md`. Çıkarım olan her şey "çıkarım" diye işaretli; son bölümde toplandı.

## 1. Mevcut durum

### 1.1 Menü (`src/app/layout/component/app.menu.ts`, frontend)

Menü statik bir TypeScript listesi; izin süzmesi `AuthService.hasOperationClaim(...)` ile **tek tek öğe** bazında yapılıyor (grup, içinde görünür öğe yoksa hiç eklenmiyor; "Panel" ve "Ayarlar" her zaman var). Tek bir "öğe, rota, izin" kaydı (registry) yok: aynı rota/izin bilgisi menüde, `panel.routes.ts`'te, her özelliğin kendi rota dosyasında ve dashboard/Bugün içindeki bağlantılarda ayrı ayrı tekrarlanıyor.

| Grup | Öğeler (izin) | Sayı |
|---|---|---|
| **Panel** | Özet (`/panel/dashboard`), Bugün (`Visits.Read`, `/panel/today`) | 2 |
| **Hasta yönetimi** | Müşteriler, Hayvanlar, **Türler** (`Species.Create` veya `Update`), **Irklar** (`Breeds.Create` veya `Update`), Randevular, Randevu Takvimi, Muayeneler, Tedaviler, Reçeteler, Lab sonuçları, Yatışlar, Aşılar, Ücretler, Ödemeler | 14 |
| **Ürün ve Stok** | Ürünler, Stok Durumu (`Products.Read`), Stok hareketleri, **Kategoriler** (`ProductCategories.Read`) | 4 |
| **Raporlar** | Ödeme, Randevu, Muayene, Aşı Raporu (her biri kendi `*.Read` izni) | 4 |
| **Ayarlar** | Hesabım, Abonelik, Klinikler, Hatırlatmalar, Hatırlatma Geçmişi, Organizasyon Ayarları, Kurum üyeleri, Davetler, Davet oluştur, **Giriş** (`/auth/login`) | 10 |

Toplam: **34 öğe, 5 grup** (en geniş izin kümesinde). Gözlemler:

- Giriş sonrası varsayılan sayfa `/panel/dashboard` (`DEFAULT_PANEL_AFTER_AUTH`); Bugün ikinci sırada. Bugün günlük iş yüzeyi, ama açılış ekranı değil.
- "Hasta yönetimi" başlığı altında klinik akış (randevu, muayene, tedavi, reçete, lab, yatış, aşı), para (ücret, ödeme) ve tanımlar (tür, ırk) karışık; **"Hasta yönetimi" adı 14 öğeyi anlatmıyor**.
- Tür/Irk menüde yalnız **Create/Update** izniyle görünüyor (Read yetkisi ayrı anahtar değil); `Ürün ve Stok > Kategoriler` ise Read ile. Tutarsız izin kuralı, taşırken tek kurala indirilmeli.
- "Ayarlar" altında **"Giriş"** (`/auth/login`) öğesi var: oturum açıkken giriş sayfasına götürüyor; çıkış zaten topbar kullanıcı menüsünde (çıkarım: şablondan kalma artık).
- Davetler iki öğe (liste + oluştur) ve tenant yönetimi için üç öğe daha: Ayarlar 10 öğeye şişmiş; ADR-004 "Ayarlar'da keşfedilebilirlik" riskini kendisi söylüyor.
- Menüde **olmayan** ama rotası/servisi olan ekranlar: `administrations` (Uygulama kayıtları; rota var, menüde yok), `users` (özellik klasörü var, `panel.routes.ts`'te rota yok), Aşı tanımları ve Hayvan renkleri (yalnız servis/seçenek kaynağı, **yönetim ekranı yok**).
- Üst çubuk (`app.topbar.ts`): logo, klinik seçici, koyu tema, kullanıcı menüsü. **Bildirim zili, hızlı oluştur (+) ve global arama yok.**
- `NotificationsWidget` (`pages/dashboard/components`) PrimeNG şablonundan kalan **yer tutucu** ("Richard Jones purchased a blue t-shirt…"); `pages/dashboard/dashboard.ts` (şablon dashboard'u) kullanıyor, gerçek dashboard (`features/dashboard`) kullanmıyor (çıkarım: ölü şablon kodu, rota bağlantısı doğrulanmadı).
- Menü öğeleri `routerLink` ile veriliyor; `routerLinkActive` yalnız **tam eşleşme** (`exact`): `/panel/pets/123` açıkken "Hayvanlar" aktif görünmez (çıkarım, davranış tarayıcıda denenmedi). Menü yeniden düzenlenirken "alt sayfada üst öğe aktif" davranışı tasarlanmalı.

### 1.2 Rotalar (`features/panel/panel.routes.ts`)

Tüm `/panel/*` rotaları düz ve tek seviye: `dashboard`, `today`, `clients`, `pets`, `species`, `breeds`, `appointments`, `examinations`, `vaccinations`, `payments`, `charges`, `administrations`, `products`, `stocks`, `product-categories`, `stock-movements`, `reports`, `settings/{subscription,organization,clinics,reminders,account,members,invites}`, `treatments`, `prescriptions`, `lab-results`, `hospitalizations`, `**` → panel 404. Her özellik kendi `canActivate` izin korumasını taşıyor. Önemli: **rotalar menüye değil modüle bağlı**; menü değişince rota değişmek zorunda değil. Bu, "eski URL'ler korunur" şartını büyük ölçüde ücretsiz yapıyor (bkz. §3.1).

Mevcut `/panel/reports` kökü `redirectTo: 'payments'`: yani **Rapor Merkezi/hub yok**; menüdeki tek rapor girişi bile doğrudan ödeme raporuna düşer.

### 1.3 Rapor sayfaları

| Rapor | Frontend rota | Backend uç | İzin | Not |
|---|---|---|---|---|
| Ödeme | `/panel/reports/payments` | `GET /reports/payments` (+ `/export` CSV, `/export-xlsx`) | `Payments.Read` | `Payment.PaidAtUtc` [from,to]; yöntem, müşteri, hayvan, arama |
| Randevu | `/panel/reports/appointments` | `GET /reports/appointments` (+ export) | `Appointments.Read` | durum kırılımı okuyucusu |
| Muayene | `/panel/reports/examinations` | `GET /reports/examinations` (+ export) | `Examinations.Read` | |
| Aşı | `/panel/reports/vaccinations` | `GET /reports/vaccinations` (+ export) | `Vaccinations.Read` | etkin tarih kuralı (`VaccinationsReportEffectiveDate`) |

- Her rapor için JSON + CSV (`;` ayırıcı, UTF-8 BOM, Europe/Istanbul) + XLSX var; **liste ve export aynı filtre semantiğini kullanıyor** (kod yorumları; ana plan kabulü "aynı filtre ekran ve export'ta aynı kaydı seçer" için zemin hazır, eşitliği kanıtlayan test bu keşifte aranmadı).
- Ortak sınırlar `ReportsSharedLimits`: aralık en çok **93 gün**, sayfa en çok 200, export en çok 50.000 satır.
- Dört sayfa ~750–790 satırlık, **birbirinin kopyası** bileşenler (toplam ~3.100 satır); ortak filtre çubuğu bileşeni yok (REPORT-005 backlog). Yalnız `features/reports/shared` içinde klinik seçeneği yardımcıları var.
- **Yok olanlar** (ana plan §13 "ilk rapor seti"): **borç/açık bakiye raporu**, **stok raporu**, ücret (Charge) raporu. Ödeme raporu yalnız tahsilat; ücret/tahsis/kalan (Aşama 3) rapora yansımıyor. Müşteri bakiyesi için `GET /clients/{id}/balance` ve Bugün alanları var, **liste/toplu borç sorgusu yok** (Aşama 3 kararı: müşteri listesinde "borcu olanlar" filtresi yok). Dashboard'da `finance-summary` ucu var (Payments.Read, ayrı yüzey; tutar semantiği ödeme raporuyla aynı mı doğrulanmadı).
- Rapor görünürlüğü **menüde yalnız izin**; ADR-003'ün "claim bazlı rapor görünürlüğü" (REPORT-006) gelecek maddesi bu; ilk sürüm için mevcut model yeterli (rapor = ilgili modülün Read izni).
- Klinik kapsamı: sorgular `clinicId?` alıyor, erişilebilir klinikler kapsamı backend'de uygulanıyor (Dashboard örneğinde `accessibleClinicIds`; raporlarda aynı kural çıkarım).

### 1.4 Bildirim ve "açık işler" — var olan parçalar

| Parça | Durum |
|---|---|
| **Bildirim varlığı/ucu (uygulama içi bildirim merkezi)** | **Yok.** Backend'de `Notification` varlığı, tablo veya uç bulunamadı (`grep` yalnız `UserCreatedDomainEventHandler` ve `IDomainEvent` adlarında). Frontend'de zil/okunmadı sayacı yok; şablon `NotificationsWidget` sahte. |
| **Dashboard operasyonel uyarılar** | `GET /dashboard/operational-alerts` (Dashboard.Read): yalnız **sayılar** — gecikmiş `Scheduled` randevu, 24 saat içi randevu, bugün iptal, **gecikmiş aşı**, 7 gün içi aşı. Frontend dashboard bu uç ve "aksiyon maddeleri" (`actionItems`) ile kullanıyor. Liste/satır yok, kişiye atama yok, "okundu/çözüldü" yok. Spec'ler tenant + klinik kapsamlı. |
| **Bugün** (`GET /visits/today`) | Gün içi iş: durum, acil, geç kalan, ücret/ödenen/kalan + müşteri toplam borcu, aktif yatışlar (frontend'de yatış bloğu kullanılmıyor — Aşama 7 keşfi), açık iş göstergesi **yok**. |
| **Bekleyen lab sonucu** | `LabResult.Status = Requested` (istendi, sonuç yok), `AwaitingInterpretation` (sonuç var, yorum yok). `GET /lab-results?status=Requested` ile **liste süzülebilir**; `RequestedAtUtc`, `RequestedByUserId` var. Klinik geneli sayı ucu, Bugün'e/dashboard'a bağlantı ve "kaç gündür bekliyor" eşiği **yok**. |
| **Borç** | `GET /clients/{id}/balance` (tek müşteri), Bugün satırında `OutstandingAmount` / `ClientOutstandingAmount`. **Klinik geneli "borcu olanlar" listesi/sayı yok.** |
| **Gecikmiş aşı** | Dashboard sayısı var (`Scheduled` ve `DueAtUtc < şimdi`); hayvan özetinde `IsOverdue`; **liste ucunda "gecikmiş" filtresi doğrulanmadı**, Aşı Raporu etkin tarih kuralıyla filtrelenebilir (çıkarım, uç parametreleri bakılmadı). |
| **Sonradan eklenen kayıt → hekim bildirimi (ADR-013 backlog)** | **Kodda yok.** `Administration` varlığında "kesinleştirmeden sonra eklendi" işareti/gerekçe alanı yok (alanlar: `VoidedAtUtc`, `VoidReason`, `CreatedByUserId`, `RequestId` vb.); ADR-013 uygulaması (Aşama 5 Dilim 2'nin açık noktaları) bu keşif tabanında **görünmüyor** (çıkarım: ya henüz birleşmedi ya başka dalda). Bildirim kısmı zaten Aşama 8'e ertelendi. |
| **E-posta hatırlatma hattı** | Randevu/aşı hatırlatması e-posta ile: `TenantReminderSettings` (kiracı bazlı, varsayılan kapalı), `ReminderDispatchLog`, işleyici 5 dk'da bir, `Sent` = SMTP kabulü (Aşama 7 keşfi; yeniden doğrulanmadı). Bu **hasta sahibine** giden dış kanal; klinik içi bildirim değil. |
| **Başarısız önemli işlem** | Outbox `DeadLetterAtUtc` ve `OutboxAdminController` var (yönetici uçları); kullanıcıya görünür "başarısız işlem" yüzeyi yok. Hatırlatma geçmişi sayfasında `Failed` satırları görülebiliyor. |
| **Atanmış iş / takip** | Görev (task) varlığı yok; `FollowUpDateUtc` yalnız tarih (Aşama 7 keşfi D4: hafif "Takip" kaydı önerildi). |

Sonuç: ana planın hedefi ("atanmış iş, zamanı gelen/gecikmiş takip, başarısız önemli işlem görünür olsun") için **bugün sıfırdan bir "Açık işler" okuma modeli** gerekiyor; ama ilk sürüm kaynak kayıtlardan **türetilen** (kalıcı bildirim tablosu olmayan) bir liste ile çok büyük ölçüde karşılanabilir (bkz. D-N1).

### 1.5 Abonelik, trial ve kısıtlama kodu

- **Varlık:** `TenantSubscription` (kiracı başına 1:1): `PlanCode` (Basic/Pro/Premium; `MaxUsers` 3/10/50), `Status` (Trialing=0, Active=1, ReadOnly=2, Cancelled=3), `TrialStartsAtUtc/EndsAtUtc`, `ActivatedAtUtc`, `CancelledAtUtc`. Yeni kiracı: `StartTrial(... SubscriptionTrialDefaults.TrialDays = 14)` — ADR-001'in 14 günü kodda sabit.
- **Self-service kayıt var:** `POST /public/owner-signup` (AllowAnonymous): tek istekte kullanıcı + kiracı + klinik + trial abonelik + **admin** rolü; `planCode` zorunlu; e-posta tekrarı ve kiracı adı tekrarı kontrolü. Frontend: `/auth/...` owner-signup sayfası, `/pricing`, `/join/:token`. Kayıt yanıtının son alanı `"login"` (giriş ekranına yönlendirme; alan adı doğrulanmadı). **Kayıt sırasında örnek veri seçeneği, onboarding adımı, kullanıcıya ilk-kullanım rehberi yok** (`features/onboarding/pages/tenant-setup-page` klasörü **boş**; `features/onboarding/services` boş).
- **E-posta doğrulama:** `User.EmailConfirmed` alanı var; owner-signup'ın doğrulama gerektirip gerektirmediği, hesapla giriş sırasında kontrol edilip edilmediği bu keşifte **doğrulanmadı** (aşağıda §6).
- **Trial sonu davranışı:** `TenantSubscriptionEffectiveWriteEvaluator.GetEffectiveStatus`: `Trialing` + `TrialEndsAtUtc <= şimdi` → **ReadOnly**; `Active` ve `ActivatedAtUtc + 1 ay <= şimdi` → ReadOnly. `TenantSubscriptionWriteGuardBehavior` (MediatR pipeline) adı `*Command` ile biten **bütün** komutları ReadOnly/Cancelled kiracıda `Subscriptions.TenantReadOnly` / `TenantCancelled` ile reddediyor; `Application.Auth.*` komutları hariç. Yani **ReadOnly = okuma serbest, yazma kapalı**, veri silinmiyor. `GET /tenants/{id}/access-state` + frontend `AppReadOnlyBanner` ("Salt okunur mod" şeridi, abonelik sayfasına bağlantı) bunu kullanıcıya gösteriyor.
- **Trial kısıtlama politikası kodda yok:** `Trialing` durumu yalnız (a) plan değişimi ödemesiz (`ChangePlanDuringTrial`, "Model A"), (b) checkout başlatma özel durumu (`StartSubscriptionCheckoutCommandHandler`) ve (c) kullanıcı koltuk sayımı için ayrı ele alınıyor. **Trial'da SMS/e-Fatura/POS/API anahtarı/toplu dışa aktarma sınırlaması, kayıt sayısı/depolama kotası, dış e-posta (hatırlatma, davet) sınırı yok.** Zaten SMS/e-Fatura/POS ürünü yok (Aşama 11); trial'da fiilen **dışarı çıkan tek şey e-postadır** (davet, şifre sıfırlama, randevu/aşı hatırlatması) ve **export** (CSV/XLSX rapor).
- **Ücretli plana dönüşüm kodu var:** `POST /tenants/{id}/subscription-checkout` (+ `GET`, `.../finalize`), iyzico geri çağrısı (`/billing/iyzico/callback`), iyzico/Stripe webhook uçları, plan düşürme/bekleyen değişiklik uçları, `SubscriptionCheckoutActivationService`, `ActivatePaidPlan` (aylık dönem, yenilenmezse ReadOnly). Frontend: Abonelik sayfası (`subscription-page`, ~1.000 satır), checkout dönüş/durum yardımcıları, `subscription-manage-page` (klasör var). Plan farkı/orantılama (`SubscriptionProrationCalculator`) var. **Gerçek ödeme sağlayıcısı ile uçtan uca çalıştığı doğrulanmadı** (çıkarım: sandbox/test anahtarı gerekiyor; bu keşifte denenmedi). Ödeme sağlayıcısının canlı hesap/kimlik durumu **bilinmiyor**.
- **Trial bitiş uyarısı:** Abonelik sayfasında kalan gün gösteriliyor (`DaysRemaining`); trial bitmeden **e-posta/zil/şerit uyarısı** (örn. 3 gün kala) yok (`ReminderType` yalnız Appointment/Vaccination; read-only şeridi yalnız bittikten sonra). Dönüşüm metrikleri (TRIAL-008) toplanmıyor.
- **Plan farkı:** `Basic/Pro/Premium` arasındaki tek farkı kod `MaxUsers`; özellik bayrağı ("plan X şunu açar") **yok**. Ana plan "özellik farkı" tanımlamıyor.

### 1.6 Örnek klinik

- **Üretimde kullanılabilir örnek klinik yok.** Var olan tek şey `DevSampleDataSeeder` (`DbMigrator seed-dev-sample-data`): **yalnız Development ortamı** (`environment.IsDevelopment()` kontrolü), sabit "YağmurVet" dev kiracısı/kliniği (`DevTestVeterinarianSeeder`), "ÖRNEK" ön ekli randevu/ücret/ödeme; gerçek handler'larla yazılıyor (iyi: sayaç, tahsis, outbox aynı yolla), idempotent. Kapsamı: randevu, ücret, ödeme. **Yok:** müşteri/hayvan üretimi (mevcut kişileri kullanıyor — "Persona" yardımcısı var, `Persona(Client, Pet)`; ayrıntı ve veri kaynağı bakılmadı), muayene (kesinleşmiş/taslak), tedavi, reçete, aşı, lab (bekleyen), yatış, stok/ürün hareketleri.
- **Örnek veriyi gerçek veriden ayıran işaret yok:** `Tenant`'ta `IsDemo`/`IsSample` bayrağı yok; kayıtlarda `IsSample` alanı yok; ayrım yalnız dev seeder'ın metin ön eki ("ÖRNEK"). Ana planın "örnek ve gerçek veriler karıştırılmaz / geçiş açık olur" şartı için **veri modeli kararı gerekiyor** (D-O1).
- **Temizleme/geçiş:** "Örnek veriyi sil" işlemi yok; çoğu varlıkta zaten sert silme yerine iptal/pasif kuralı var (ücret iptali, ödeme iptali, muayene kilidi, stok hareketi geri alınamaz). Örnek klinik temizliği bu kurallarla çelişir: örnek kiracıyı bütün olarak silmek veya ayrı kiracıda tutmak gerekir.
- **Dış etki riski:** Örnek veriyle oluşan randevu/aşı kayıtları gerçek **hatırlatma e-postası** üretebilir (kiracı ayarı açıksa; alıcı e-postası örnek müşteride yoksa `Skipped`). Örnek kiracıda hatırlatma işleyicisi/`Skipped` davranışı **elle doğrulanmadı**.

### 1.7 Karşılaştırma zemini (rakip/en iyi uygulama; ana plandan, yeniden doğrulanmadı)

- **E-Vet:** sol menüde ~20+ üst alan (Stok 12, Finansal 7, Ürün 6, Müşteri 6, Hasta 8, Muayene 13, Laboratuvar 7, Genel 12…), yoğun, modül listesi gibi; plan "rakibin yoğun sidebar'ı alınmaz" diyor.
- **DaySmart:** rapor kategorileri alanlara göre hazır katalog (builder değil), arama yapılabilir Yapılandırmalar listesi; Vetinity'ye uygun: Rapor Merkezi kart/kategori + Ayarlar'da aranabilir Tanımlar.
- Bu karşılaştırmalar yalnız ürün belgelerinden alındı; bu keşifte rakip ürünler yeniden gezilmedi.

## 2. Ana plan §13 kabul kriterlerine göre eksikler ve riskler

| # | Plan maddesi | Durum | Eksik / risk |
|---|---|---|---|
| 1 | Ana menü günlük işe odaklı; Tür/Irk/ürün kategorisi Ayarlar > Tanımlar | **Yok** | Menü 34 öğe, "Hasta yönetimi" 14 öğeli; Tanımlar merkezi sayfası ve rotası yok. |
| 2 | Tek Raporlar girişi, kategori/kart ile rapora gidiş | **Kısmen** | Menüde "Raporlar" grubu 4 alt öğeyle; hub sayfası yok, `/panel/reports` doğrudan ödeme raporuna yönlendiriyor. |
| 3 | Mevcut route/bookmark korunur | **Zemin var** | Rotalar modüle bağlı, menüden bağımsız. Risk: taşınacak olan yalnız **menü konumu**; rota taşımaya karar verilirse (örn. `/panel/settings/definitions/species`) eski yol için yönlendirme gerekir (§3.1, D-M3). |
| 4 | İlk rapor seti: randevu, muayene, aşı, tahsilat/temel bakiye, stok | **Kısmen** | Randevu/muayene/aşı/ödeme var; **borç/bakiye ve stok raporları yok**. |
| 5 | Rapor klinik/tarih kapsamı, tarih semantiği, para birimi, toplamlar açık; liste ve export aynı filtre | **Kısmen** | Export aynı filtre semantiğiyle yazılmış; toplam/para birimi/tarih semantiğinin ekranda açıkça yazıldığı **doğrulanmadı**; 93 gün sınırı kullanıcıya açıklanıyor mu bilinmiyor; ücret/tahsis alanları rapora girmedi. |
| 6 | Bugün/bekleyen sonuç/takip listesi günlük aksiyon yüzeyi, rapordan ayrı | **Kısmen** | Bugün var; bekleyen lab ve takip bu yüzeyde yok (lab listesi süzülebilir ama Bugün'e bağlı değil). |
| 7 | Bildirim/açık işler gerçek kaynak kayıtlarına bağlı; atanmış iş, gecikmiş takip, başarısız önemli işlem | **Yok** | Bildirim modeli yok; dashboard uyarıları yalnız sayı. Takip/görev varlığı Aşama 7 D4'e bağlı. |
| 8 | Ortak boş durum/hata/yeniden deneme/kaydetme/dar ekran | **Kısmen** | `app-page-header` gibi paylaşılan parçalar var; menü/rapor sayfalarında ortak boş/hata deseni uygulanıyor mu tek tek bakılmadı. |
| 9 | Bir rapor/tanım yer değişince eski URL çalışır; yetkisiz rapor görünmez; aynı filtre ekran=export | **Kısmen** | URL: korunur (rota değişmezse otomatik). Yetki: rota guard + menü süzmesi var. Ekran=export eşitliği için **test ölçütü** yazılmalı. |
| 10 | Trial: boş klinikle başla / örnek kliniği yükle seçimi | **Yok** | Onboarding klasörü boş, kayıt akışında seçim yok, üretimde örnek klinik üretimi yok. |
| 11 | Örnek veri sentetik ve bağlı (gelen hasta, muayene, ücret/kısmi tahsilat, bekleyen lab, aşı, yatış/takip) | **Yok** | Dev seeder yalnız randevu+ücret+ödeme, yalnız Development. |
| 12 | Örnek→gerçek geçiş açık, karışmaz | **Yok** | İşaret/bayrak ve temizleme yok. |
| 13 | İlk kullanımda kısa görevlerle ana akış; bağlamsal yardım | **Yok** | Ürün turu/yardım bileşeni yok. |
| 14 | Aktivasyon olayları ölçülür (ilk muayene, ilk ücret/tahsilat, örnek seçimi) | **Yok** | Ölçüm/analytics altyapısı bulunamadı (çıkarım: yalnız log). |
| 15 | Abonelik bitince ReadOnly ve yenileme davranışı kullanıcıya açıklanır, güncel kodla doğrulanır | **Büyük ölçüde var** | ReadOnly mantığı + şerit + abonelik sayfası + checkout kodu var; **gerçek ödeme/yenileme akışı ve veri korunumu elle doğrulanmadı**; trial bitiminden önce uyarı yok. |
| 16 | Demo kaynaklı dış mesaj/gerçek finans işlemleri politikaya uygun ayrılır | **Yok** | Trial/örnek kiracı için dış e-posta/export sınırı yok. |

### Riskler

- **R1 Menü yeniden düzenlemesi yetki regresyonu üretebilir.** Menü izin kuralları tek tek yazılı; Tür/Irk "Create veya Update" kuralı taşınırken Read izniyle yazılırsa mevcut roller için görünürlük değişir. Çözüm: izin kuralı taşınmadan **önce** rol bazlı görünürlük tablosu çıkar (backend `RolePermissionBindings`), değişen varsa kullanıcıya göster.
- **R2 Menü registry yok → tekrar.** Menü, dashboard hızlı bağlantıları, Bugün eylemleri ve Rapor Merkezi kartları aynı "rota+izin+ad" bilgisini ayrı yazıyor. Rapor Merkezi eklenirse **kartlar için dördüncü bir kopya** olur. Öneri: tek küçük kayıt (frontend `navigation` sabiti) — "kural tek yerde yaşasın".
- **R3 `routerLinkActive` tam eşleşme.** Üst öğe, detay/düzenle sayfalarında aktif görünmez; yeni menüde gruplu/alt sayfalı yapıya geçince kullanıcı "neredeyim" bağlamını kaybeder. Küçük ama menü işinin parçası.
- **R4 Rapor sayfası kopyalama borcu.** Dört ~770 satırlık sayfa; Rapor Merkezi'ne borç raporu eklemek (ve ücret/stok) beşinci/altıncı kopya demek. Ortak filtre/sonuç iskeleti (REPORT-005) çıkarılmadan yeni rapor eklemek maliyetli ve tutarsızlık üretir.
- **R5 Borç raporu için veri modeli.** Müşteri bazlı kalan borç `GET /clients/{id}/balance` ile tek tek; toplu "borcu olanlar" sorgusu performans ve doğruluk (Charge − PaymentAllocation, iptal/ters kayıt kuralı) ister. Aşama 3 kararı "müşteri listesinde borcu olanlar filtresi yok" **Rapor Merkezi'ndeki borç raporunu engellemez** ama aynı kural tek yerde (bakiye hesabı) yaşamalı; ikinci bir hesap yolu açılmamalı. Query DB/projeksiyon yok; komut DB'den okuma çıkarım.
- **R6 Bildirim modeli seçimi geri alınması zor.** Kalıcı "Notification" tablosu (okundu/çözüldü/kime) ile **türetilmiş açık iş listesi** arasındaki seçim migration ve davranış farkı yaratır; yanlış başlangıç sonradan maliyetli. Karar D-N1.
- **R7 ADR-013 hekim bildirimi bağımlılığı.** Bildirim, "sonradan eklendi" işareti ve eklemeyi yapan/muayene hekimi bilgisini ister; işaret alanı henüz kodda yok (Aşama 5 Dilim 2'ye bağlı). Aşama 8 bildirimi bu işareti **kendisi icat etmemeli**; Aşama 5 Dilim 2 çıktısını beklemeli.
- **R8 Açık iş "çözüldü" tanımı.** Bekleyen lab (sonuç girilince biter), borç (ödenince biter), gecikmiş aşı (uygulanınca/iptal edilince biter) kaynak kayıttan **türetilirse** kapanış otomatik ve çelişmez. Kalıcı bildirimde ise kaynak ile bildirim durumu ayrışabilir (tutarsızlık riski). Türetme tutarlılık için daha güvenli.
- **R9 Örnek veri ↔ hatırlatma e-postası.** Örnek müşteri e-postası gerçek bir adres olursa üçüncü kişiye gerçek mesaj gider (KVKK/itibar). Örnek e-postalar ayrılmış, geçersiz alan adı (örn. `example.invalid`) ve örnek kiracıda dış gönderim kapalı olmalı.
- **R10 Örnek klinik bakım yükü.** Model eklendikçe (Aşama 5/6/7) örnek veri üreticisi güncellenmezse kırılır (ADR-002 de riski kabul etmiş). Üretimde gerçek handler'larla üretmek (dev seeder'ın yaklaşımı) bu riski azaltır ama modüller değiştikçe test gerektirir.
- **R11 Örnek veri KVKK ve gerçek müşteri verisi.** Aynı kiracıda örnek+gerçek veri karışırsa raporlar, borç ve stok toplamları bozulur ("örnek stok" gerçek stok sayılır). Karar D-O1 (ayrı kiracı vs bayrak) buna bağlı.
- **R12 Trial kötüye kullanım.** Kayıt anonim ve ücretsiz; e-posta doğrulama, bot koruması (CAPTCHA/oran sınırı), aynı kişinin tekrar tekrar trial açması için kontrol **doğrulanmadı**. `rate-limiting-and-security-hardening.md` dokümanı var (okunmadı).
- **R13 Trial → ReadOnly sert geçiş.** Gün 14'te yazma tamamen kapanıyor; kullanıcı **uyarı almadan** (zil/e-posta yok) çalışırken kesilebilir. Klinik ortamda "muayene kaydedemiyorum" anı kritik; hafif tolerans (ör. kısa ek süre) veya erken uyarı ürün kararı (D-T3).
- **R14 ReadOnly'de bilgi erişimi.** Raporlar/export ReadOnly'de çalışıyor (okuma); bu, ödeme yapmadan veri çıkarmaya izin verir. Bilinçli seçim olabilir (veri esareti hissi vermemek, KVKK taşınabilirlik); karar D-T2.
- **R15 Plan–özellik ilişkisi tanımsız.** Basic/Pro/Premium yalnız koltuk sayısı; "ücretli plana dönüşüm"de neyin ücretli olduğu ürün olarak tanımlı değil (fiyat, plan içerikleri `/pricing` sayfasında yazılı olabilir, okunmadı).
- **R16 Bildirim çok-kiracılı/çok-klinikli kapsam.** Bildirim/açık iş kime ve hangi kliniğe görünür (Sekreter vs Veteriner vs Yönetici) belirsiz; `accessibleClinicIds` kuralı uygulanmalı.

## 3. Menü ve bilgi mimarisi — somut öneri taslağı

### 3.1 İlkeler (ADR-003/004 + ana plan §13)

1. Ana menü **günlük iş akışı sırasıyla** (bugünden → kayıt → para → stok), 2–3 etkileşimle ulaşım.
2. **Tanımlar** (tür, ırk, ürün kategorisi, ileride aşı tanımı/hayvan rengi) **Ayarlar > Tanımlar** merkezi sayfasında, kart listesi olarak; sidebar'da tek "Tanımlar" girişi.
3. **Rotalar taşınmaz** ilk adımda (modül rotaları aynen kalır). Tanımlar merkezi yeni bir rota (`/panel/settings/definitions`) ekler ve kartlar mevcut `/panel/species`, `/panel/breeds`, `/panel/product-categories` rotalarına gider. Böylece **eski URL'lerin korunması bedava**; derin link riski sıfır. (Sonraki ve isteğe bağlı adım: rotaları `settings/definitions/...` altına taşıyıp eskilerden `redirectTo` vermek — yalnız tutarlı adres istenirse; bu adım ADR-004'ü "URL de Ayarlar altında" yönünde sıkılaştırır, ayrı karar D-M3.)
4. **Tek Raporlar girişi** `/panel/reports` Rapor Merkezi sayfası olur (§4); mevcut rapor rotaları (`/panel/reports/payments` vb.) olduğu gibi kalır.
5. Menü öğesi, rota ve izin bilgisi **tek yerde** (frontend `navigation` kayıt dosyası); menü, Rapor Merkezi kartları ve (isteğe bağlı) dashboard bağlantıları bunu okur.
6. Yeni sayfa menüye eklenmez; mevcut bir akışa/sekmeye/iç navigasyona girer (ADR-004 madde 2). Menü öğe tavanı belgede yazılsın (öneri: ≤ 14 öğe, ≤ 6 grup).

### 3.2 Önerilen menü

Öğe sayısı **34 → ~16** (en geniş izin kümesinde; her satırın izni mevcut anahtarla aynı kalır).

| Grup | Öğe | Rota | İzin (mevcut) | Not |
|---|---|---|---|---|
| **Bugün** (grup başlığı yok, üstte tek öğeler) | Bugün | `/panel/today` | `Visits.Read` | **Giriş sonrası varsayılan sayfa olması D-M2** |
| | Özet | `/panel/dashboard` | (yok, herkes) | Bugün'ün yanında; ayrı yüzey kalır |
| **Klinik** | Randevular | `/panel/appointments` | `Appointments.Read` | **Takvim**, Randevular sayfasında görünüm sekmesi/anahtarı (rota `/panel/appointments/calendar` kalır) → menüden "Randevu Takvimi" kalkar |
| | Müşteriler | `/panel/clients` | `Clients.Read` | |
| | Hayvanlar | `/panel/pets` | `Pets.Read` | Hasta kartı timeline/özetle çalışma alanı |
| | Muayeneler | `/panel/examinations` | `Examinations.Read` | |
| | Yatışlar | `/panel/hospitalizations` | `Hospitalizations.Read` | |
| | Aşılar | `/panel/vaccinations` | `Vaccinations.Read` | |
| | Lab sonuçları | `/panel/lab-results` | `LabResults.Read` | |
| **Tedavi ve reçete** (önerilen birleşme: **D-M4**) | Tedaviler | `/panel/treatments` | `Treatments.Read` | |
| | Reçeteler | `/panel/prescriptions` | `Prescriptions.Read` | |
| **Para** | Ücretler | `/panel/charges` | `Charges.Read` | |
| | Ödemeler | `/panel/payments` | `Payments.Read` | |
| **Stok** | Ürünler | `/panel/products` | `Products.Read` | **Stok Durumu ve Stok hareketleri**, Ürünler sayfasında sekmeler; rotalar (`/panel/stocks`, `/panel/stock-movements`) kalır |
| **Raporlar** | Raporlar | `/panel/reports` | herhangi bir rapor izni | Tek giriş (Rapor Merkezi) |
| **Ayarlar** | Ayarlar | `/panel/settings` (yeni giriş sayfası) | herkes (en az Hesabım) | Ayrıntı aşağıda |

Önerilen gruplama etiketleri ve sırası: **(üst) Bugün · Özet** → **Klinik** → **Para** → **Stok** → **Raporlar** → **Ayarlar**. "Tedaviler/Reçeteler" Klinik grubunun içinde tutulabilir (öğe sayısı 16 civarı kalır); ayrı grup önerisi D-M4'te sorulacak.

**Menüden çıkanlar ve nereye gidenler**

| Eski menü öğesi | Yeni yeri | Rota değişir mi? |
|---|---|---|
| Türler | Ayarlar > Tanımlar > Türler | Hayır (`/panel/species`) |
| Irklar | Ayarlar > Tanımlar > Irklar | Hayır (`/panel/breeds`) |
| Kategoriler (ürün) | Ayarlar > Tanımlar > Ürün kategorileri | Hayır (`/panel/product-categories`) |
| Randevu Takvimi | Randevular içi görünüm anahtarı | Hayır (`/panel/appointments/calendar`) |
| Stok Durumu, Stok hareketleri | Ürünler içi sekmeler (Ürünler/Stok durumu/Hareketler) | Hayır |
| Ödeme/Randevu/Muayene/Aşı Raporu | Rapor Merkezi kartları | Hayır (`/panel/reports/...`) |
| Hatırlatmalar, Hatırlatma Geçmişi | Ayarlar > İletişim > Hatırlatmalar (Geçmiş: aynı sayfada sekme) | Hayır |
| Organizasyon, Kurum üyeleri, Davetler, Davet oluştur | Ayarlar > Kurum (üyeler sayfasında "Davet et" düğmesi) | Hayır |
| "Giriş" | Kaldırılır (çıkış topbar menüsünde) | Hayır (`/auth/login` kalır) |

**Ayarlar iç yapısı** (yeni `/panel/settings` giriş sayfası; ADR-004'ün "keşfedilebilirlik" riskine karşı kart + açıklama + arama):

- **Hesabım** · **Abonelik** (izin: `Subscriptions.*`)
- **Klinik ve kurum**: Klinikler, Organizasyon ayarları, Kurum üyeleri, Davetler
- **İletişim**: Hatırlatmalar (+ geçmiş)
- **Tanımlar**: Türler, Irklar, Ürün kategorileri; **ileride** Aşı tanımları, Hayvan renkleri (şimdilik yönetim ekranı yok; Tanımlar sayfasında "yakında" göstermeyin, ekran gelince ekleyin)

Her kart ilgili izin yoksa **hiç görünmez** (kart da menü gibi aynı izin kayıt dosyasından süzülür).

### 3.3 Eski URL'lerin korunması — mekanizma

1. İlk adımda **hiçbir rota silinmez/taşınmaz**: menü yalnız giriş noktası değiştirir. Yer imi, e-posta bağlantısı, Bugün/hayvan özeti derin bağlantıları (ör. `/panel/hospitalizations/:id`, `/panel/payments/new?clientId=`) aynen çalışır.
2. Yeni rotalar yalnız **ekleme**: `/panel/settings` (giriş), `/panel/settings/definitions` (Tanımlar), `/panel/reports` (Rapor Merkezi; şimdiki `redirectTo: 'payments'` yerine bileşen).
3. Rota **taşıma kararı çıkarsa** (D-M3): `panel.routes.ts`'e eski yoldan yeniye `redirectTo` + alt yol/sorgu parametresi koruyan yönlendirme; her taşınan rota için bir test (eski URL → yeni sayfa). `returnUrl` kuralı (`/panel/...` içi) korunur.
4. Menü kaydı kullanıyorsa `routerLinkActive` seçeneği "alt yollarda aktif" (`paths: 'subset'`) olarak üst öğede ayarlanır; `/panel/pets/:id` açıkken Hayvanlar vurgulu.

### 3.4 Menü işinin riskleri ve sınırları

- Rol bazlı görünürlük tablosu (R1) önce çıkarılmalı.
- Sidebar sayısı azalırken "2–3 etkileşim" bozulmamalı: Tanımlar (Menü > Ayarlar > Tanımlar > Türler = 3 tık) kabul; hasta formundaki mevcut hızlı oluştur (`QuickSpeciesDialog`) tür ekleme için zaten kısa yol.
- Arama: global arama bu aşamada yok (ana plan "tüm modülleri kapsayan arama sonraki kapsam"); Ayarlar kartlarında basit süzme yeterli.

## 4. Rapor Merkezi önerisi

**Yapı:** `/panel/reports` bir **kategori/kart sayfası**; kartlar kayıt dosyasından gelir ve her biri izne göre görünür. İç navigasyon: sol/üst sekme yerine kart ızgarası + sayfa içinde "Raporlara dön" (breadcrumb).

| Kategori | Kartlar | Durum |
|---|---|---|
| Randevu ve ziyaret | Randevu raporu | Var |
| Klinik | Muayene raporu · Aşı raporu | Var (Aşı: gecikmiş/planlı ayrımı rapora görünür olmalı) |
| Para | Tahsilat (ödeme) raporu · **Borç / açık bakiye** (yeni) · (ileride) Ücret raporu | Ödeme var; borç yeni, **Aşama 3 bakiye kuralını kullanır** |
| Stok | **Stok durumu / düşük stok** (yeni, mevcut `ProductStocks` verisi) | Yeni; uç gerekir |

**Ortak kurallar (ana plan §13 "açık olur" maddeleri):** her rapor sayfasının başında: klinik kapsamı, tarih alanının **ne olduğu** (örn. "ödeme tarihi", "randevu saati", "muayene tarihi", "aşı etkin tarihi"), saat dilimi (Europe/Istanbul), para birimi (TRY), toplam satır/tutar, **93 günlük aralık sınırı** uyarısı. Liste ve CSV/XLSX aynı filtre nesnesinden üretilir (zaten aynı sorgu semantiği); `mapper` ortak bir "rapor filtresi" modeli çıkarmak (REPORT-005) rapor sayısı 6'ya çıkmadan **ilk dilim** olmalı.

**Borç raporu** (backend yeni uç): `GET /reports/outstanding-balances` benzeri; ölçü = müşteri bazlı `Σ ücret − Σ tahsis` (iptal/ters kayıtlar hariç; `GetClientBalanceQueryHandler` ile **aynı kural tek yerde**), "tarih" semantiği = **bir tarih itibarıyla kalan** mı yoksa **şu anki kalan** mı (D-R1). Sıralama kalan tutara göre azalan, müşteriye bağlantı, CSV/XLSX. İzin: `Charges.Read` (önerilen) — Ödemeler.Read yetmez çünkü kalan borç ücretten gelir. Veteriner de Bugün'de müşteri toplam borcunu görüyor (Aşama 3 kararı); rapor izni ayrı karar (D-R2).

**Stok raporu:** mevcut stok durumu ekranı zaten liste; "rapor" olarak katma değeri düşük stok eşiği + hareket özeti. Aşama 5 stok güvenliği (Dilim 1) migration'ı ve `StockMovement` sözleşmesi oturunca anlamlı; ilk sürümde **yalnız** "stok durumu" kartı mevcut sayfaya bağlanabilir (yeni uç yok).

**Rapor sayısı için sınır:** Aşama 8 yeni rapor üretmek yerine **hub + borç raporu + ortak filtre iskeleti**; ücret raporu, kapanış/vardiya raporu, hekim bazlı üretkenlik Aşama 11'e (talep doğrulanınca).

## 5. Bildirim ve açık işler önerisi

### 5.1 Üç ayrı kavram (karıştırılmaması önerilir)

| Kavram | Ne | Nerede yaşar | Kalıcı kayıt? |
|---|---|---|---|
| **Açık işler** | Şu an dikkat isteyen kaynak kayıtlar: bekleyen lab, borçlu müşteri, gecikmiş aşı, gecikmiş randevu (Scheduled), sonradan eklenen kayıt incelemesi, atanmış takip | Bugün'de bir şerit/panel + ayrı **"Açık işler"** sayfası (menü öğesi değil: Bugün içinde veya topbar zilinden) | **Hayır, türetilir** |
| **Bildirim (zil)** | "Birisi senin için bir şey yaptı / zamanı geldi" olayı: örn. "Dr. X kesinleşmiş muayenene yeni uygulama ekledi", "lab sonucu geldi" | Topbar zili + okundu durumu | **Evet, ama yalnız olay tabanlı olanlar için** (türetilemeyen: ADR-013 hekim bildirimi) |
| **Hasta sahibine hatırlatma** | E-posta (randevu/aşı) | Mevcut hat | Var (`ReminderDispatchLog`) |

### 5.2 İlk sürüm kapsamı (öneri)

- **Açık işler = türetilmiş okuma modeli**, migration yok: tek uç (örn. `GET /open-work`) sayılar + her tür için ilk N satır (tür, hayvan/müşteri, neden, ne kadar süredir, bağlantı). Kaynaklar: `LabResult.Status == Requested` (ve `AwaitingInterpretation`), `Charge − Allocation > 0`, `Vaccination.Scheduled && DueAtUtc < now`, `Appointment.Scheduled && ScheduledAtUtc < now`. Her satırın "çözüm" eylemi mevcut sayfaya derin bağlantı (lab sonucu gir, ödeme al, aşıyı işaretle). Kapanış otomatik (kaynak değişince kaybolur).
- Bugün'e **salt sayı çipleri** ("2 bekleyen sonuç · 3 borçlu · 1 gecikmiş aşı") + "Açık işler" bağlantısı; dashboard'daki mevcut `operational-alerts` aynı kaynak tanımlarını kullanır (**ikinci tanım yazılmaz**; mevcut spec'ler yeniden kullanılır/genişletilir).
- **Sorumlu hekim/kullanıcı süzgeci:** "benim bekleyen sonuçlarım" (`RequestedByUserId`), "benim hastalarım" (CHECKIN-014 deseni).
- **Bildirim (zil) ilk sürümde yalnız ADR-013 olayı** (sonradan eklenen kayıt → muayene hekimine) ve belki lab sonucu geldi; küçük bir `UserNotification` tablosu (tenant, alıcı kullanıcı, tür, kaynak kimliği, oluşma zamanı, okundu zamanı). Bu tablo **migration** ister ve Aşama 5 Dilim 2'nin "sonradan eklendi" işaretine bağlıdır (R7).
- **Başarısız önemli işlem** (hatırlatma `Failed`, outbox dead-letter): yönetici izinli yüzeyde sayı + Hatırlatma Geçmişi bağlantısı; ilk sürümde ayrı bildirim üretilmez.
- Kanallar: uygulama içi. E-posta/SMS/push bildirimi ilk sürümde yok (Aşama 11).

### 5.3 Gerçek zamanlılık

Yoklama (polling, ör. 60 sn) yeterli; WebSocket/SignalR ilk sürümde gereksiz (çıkarım: altyapıda SignalR bulunmadı, `grep` yapılmadı → doğrulanmadı).

## 6. Örnek klinik ve onboarding önerisi

### 6.1 Kayıt → ilk açılış akışı

1. `owner-signup` aynı (backend değişmez) + yanıtta mevcut "login" yönlendirmesi.
2. İlk girişte **"Başlangıç" ekranı** (kiracıda ilk giriş bayrağı gerekir — tenant veya kullanıcı bazlı; D-O3): **Boş klinikle başla** · **Örnek veteriner kliniğini yükle (önerilen)**. Seçim kaydı = ADR-002 metriği "örnek klinik seçme oranı".
3. Seçim "Örnek yükle" ise: arka planda örnek veri üretimi (≤ birkaç saniye hedef; uzun sürerse ilerleme göstergesi), bitince **Bugün** açılır ve 3–4 adımlı görev şeridi ("Bekleyen hastayı muayeneye al → kaydet → ücret ekle → kısmi tahsilat al").
4. Her zaman görünen küçük "Örnek veri" rozeti (topbar/şerit) + "Örnek veriyi temizle ve gerçek klinikle başla" eylemi.

### 6.2 Veri modeli seçenekleri (D-O1)

| Seçenek | Nasıl | Artı | Eksi |
|---|---|---|---|
| **A. Ayrı örnek kiracı** | Kullanıcıya ikinci kiracı (örnek) açılır; gerçek kiracı boş kalır; geçişte kullanıcı gerçek kiracıya döner, örnek kiracı kapatılır/silinir | Karışma imkânsız; rapor/borç/stok toplamları temiz; temizleme = kiracıyı pasifleştir/sil; sert silme kuralları ihlal edilmez | Çoklu kiracı geçişi mevcut yapıda var (kullanıcı birden çok kiracıya üye olabilir mi? doğrulanmadı, `UserTenant` var); abonelik/koltuk kuralları iki kiracıda ne olur belirsiz; kullanıcı "gerçek kliniğime ne zaman geçtim?" kafası |
| **B. Aynı kiracıda `IsSample` bayrağı** | Her örnek kayıt (müşteri, hayvan, randevu, muayene… ~15 tablo) işaretli; liste/rapor/borç/stok sorguları işaretli kaydı gerçekten ayıklar; "Temizle" bayraklı kayıtları siler/iptal eder | Tek kiracı, tek oturum, kesintisiz geçiş | **Çok sayıda tabloda migration ve her sorguda süzme**; unutulan bir sorgu örnek veriyi gerçeğe karıştırır; sert silme/iptal kurallarıyla (ücret/ödeme/stok geri alınamaz) çelişir; en riskli |
| **C. Yalnız boş kiracı + rehberli ekran görüntüsü/video** | Örnek veri üretilmez | En ucuz | ADR-002'ye ters (yaşayan ürün hissi yok) |

**Öneri: A (ayrı örnek kiracı)**, kullanıcıya seçenek olarak "Örnek kliniği keşfet" ve gerçek kiracıdan **tek tıkla geçiş**; ya da, A teknik olarak zorsa, aynı kullanıcıya bağlı **salt "demo kiracı"** (Tenant'a `IsDemo` bayrağı + zamanlanmış otomatik temizlik). Gerekçe: sektörde örnek/demo veri genelde ayrı çalışma alanıdır (karışma riski ürünü bozmaz); Vetinity'de ücret/ödeme/stok **geri alınamaz kayıt kuralları** B'yi güvensiz yapıyor.

> Karar bekliyor çünkü A, "kullanıcı birden çok kiracı" ve "kiracı oluştur/pasifleştir" akışına bağlı; backend'de `POST /tenants` (CreateTenant) ve `UserTenant` var ama **çoklu kiracı seçimi/geçişi arayüzü** (`tenant switcher`) bu keşifte doğrulanmadı.

### 6.3 Örnek veri içeriği (ADR-002 + ana plan, bağlı senaryo)

Üretimde **mevcut uygulama komutlarıyla** (dev seeder'ın yöntemi) üretilen tutarlı küçük set (≈10–15 müşteri/hayvan): bugün gelen hasta (Bekliyor/Muayenede), kesinleşmiş ve taslak muayene, ücret + kısmi tahsilat (kalan borç), açık lab istemi (bekleyen sonuç) ve sonuçlanmış biri, planlı ve gecikmiş aşı, aktif yatış, tedavi/reçete, birkaç ürün + stok hareketi, geçmiş randevular (gelmedi dahil). Hepsi sentetik: ad/telefon/e-posta ayrılmış sahte (R9), mikroçip sahte. Üretici Aşama 5/6/7 modelleri bittikçe genişler (R10); Aşama 8 dilimi yalnız **o gün bitmiş modelleri** kapsar, kalanı eklenir.

### 6.4 Rehberli ilk kullanım ve yardım

İlk sürüm: sabit sırada 4 görevlik şerit (kapatılabilir, kiracı bazlı ilerleme), bağlamsal "bu ekran ne işe yarar / sonraki adım" bilgi kutusu (metin, AI değil). Aktivasyon olayları (ilk muayene, ilk ücret, ilk tahsilat, örnek seçimi): ilk sürümde sunucu tarafı basit olay kaydı (tablo veya yapılandırılmış log) — **ölçüm aracı ürün kararı** (D-O5).

## 7. Trial kısıtlama ve ücretli plana dönüşüm — açık sorular

Mevcut davranış §1.5'te; ADR-001 kısıtlamayı "gerçek dış sistem işlemleri kısıtlanabilir (detay: trial kısıtlama politikası)" diye bıraktı. Önerilen politika çerçevesi (karar için D-T1…D-T6):

| Alan | Öneri |
|---|---|
| Süre | 14 gün, tam özellik, tüm klinik akışlar açık (ADR-001). Kısa uzatma: yönetici/destek elle (kodda `TrialEndsAtUtc` güncelleme ucu yok → ihtiyaç doğarsa küçük yönetici komutu) |
| Dış e-posta | Trial'da hatırlatma e-postaları **kiracı ayarıyla zaten varsayılan kapalı**; trial süresince **günlük gönderim sınırı** (örn. 20) ve yalnız doğrulanmış kayıt e-postası alıcı olarak: kötüye kullanım + SMTP maliyeti sınırı. Davet e-postası serbest (zaten plan koltuk sayısıyla, `MaxUsers`, sınırlı) |
| Export | CSV/XLSX trial'da serbest (veri taşınabilirliği güven verir) ya da "satır sınırı" — D-T2 |
| Kota | Başlangıçta **yok**; kötüye kullanım gözlemlenirse (TRIAL-006) kayıt sayısı/depolama sınırı. Dosya yükleme (Aşama 6 imaging) başladığında depolama kotası zorunlu olur → o zaman ele alınır |
| Trial bitişi | 14. günde ReadOnly (mevcut). Öneri: bitişten **3 gün ve 1 gün önce** uygulama içi şerit + e-posta; bitişten sonra **veri 90 gün saklanır**, okuma/export açık, yazma kapalı (mevcut). Silme politikası ayrı karar (KVKK saklama) |
| Dönüşüm | Abonelik sayfası → plan seç → checkout (iyzico, mevcut). Trial'da seçilen plan zaten kayıtlı (ödemesiz `ChangePlanDuringTrial`). Eksik: trial bitmeden dönüşüm çağrısı (şerit/e-posta), ödeme başarısız/yarım kalma, dönüşüm ölçümü (TRIAL-008) |
| Örnek kiracı | Abonelik/kota dışı; 30 gün sonra otomatik silinir veya kullanıcı siler |

## 8. Dilim önerisi ve sıra

Aşama 8 bağımlılıkları: Rapor Merkezi borç raporu → Aşama 3 (hazır); açık işler lab/takip → Aşama 6 (Dilim 1 birleşti, sonuç ucu `LabResultStatus` hazır) ve Aşama 7 D4 (takip kaydı, karar bekliyor); hekim bildirimi → Aşama 5 Dilim 2 (ADR-013 işareti); örnek klinik tam kapsam → Aşama 5/6/7 sonrası.

| Dilim | Kapsam | Taraf | Migration | Paralel? | Bağımlılık |
|---|---|---|---|---|---|
| **0 — Rol görünürlük tablosu** | Mevcut roller için menü görünürlüğü çıkarımı (backend `RolePermissionBindings` ↔ menü kuralları); değişmesi gereken izin tutarsızlığı listesi (Tür/Irk Create/Update vs Read) | Doküman/küçük kod okuma | Yok | Her şeyle paralel | — |
| **1 — Menü ve gezinti kaydı (frontend)** | `navigation` kayıt dosyası (öğe, rota, izin, grup); yeni menü (§3.2); `routerLinkActive` alt yol; "Giriş" öğesi kaldırma; Ayarlar giriş sayfası + Tanımlar sayfası (yeni rota, kartlar); eski URL'ler hiç değişmez. Randevu takvimi ve stok sekmeleri **ayrı alt dilim** (sayfa içi sekme gerektirir) | **Frontend-only** | Yok | Evet (backend işlerinden bağımsız) | D-M1…D-M5 kararları, Dilim 0 |
| **2 — Rapor Merkezi hub (frontend)** | `/panel/reports` kart sayfası; `redirectTo` kaldır; kategoriler; mevcut 4 rapor kartı; ortak rapor üst bilgisi (tarih alanı, klinik, saat dilimi, 93 gün uyarısı) | Frontend-only | Yok | Dilim 1 ile aynı kayıt dosyasını kullanır; Dilim 1'den sonra ya da aynı dalda | Dilim 1 kayıt dosyası |
| **3 — Ortak rapor filtre iskeleti (frontend)** | Dört kopya sayfadan ortak filtre/sonuç bileşeni çıkarma (REPORT-005 kısmi) — yalnız yeni rapor eklemeden hemen önce | Frontend | Yok | Dilim 2'den sonra; borç raporu frontend'inden önce | — |
| **4 — Borç / açık bakiye raporu** | Backend: toplu bakiye sorgusu (tek bakiye kuralı), CSV/XLSX, izin; sözleşme dokümanı; frontend rapor sayfası | Backend + frontend | Büyük olasılıkla **yok** (okuma sorgusu; performans için indeks gerekirse evet) | Dilim 2/3 ile paralel (backend) | D-R1, D-R2; Aşama 3 bakiye kuralı |
| **5 — Açık işler okuma ucu + Bugün/dashboard bağlantısı** | `GET /open-work` (bekleyen lab, borçlu, gecikmiş aşı, gecikmiş randevu); mevcut dashboard `operational-alerts` aynı spec/kuralları kullanacak şekilde; frontend "Açık işler" sayfası + Bugün çipleri | Backend + frontend | **Yok** (türetilir) | Dilim 4 ile paralel (farklı dosyalar) | D-N1; Aşama 6 Dilim 1 (lab durumu) birleşik |
| **6 — Bildirim (zil) + ADR-013 hekim bildirimi** | `UserNotification` tablosu, okundu uçları, zil, ADR-013 "sonradan eklendi" olayı → bildirim; frontend zil/açılır liste | Backend + frontend | **Evet** (tek migration, kullanıcı onayı) | Dilim 5'ten sonra; Aşama 5 Dilim 2 bitmeden başlamaz | D-N2; **Aşama 5 Dilim 2** |
| **7 — Trial bitiş uyarısı + abonelik yüzeyi** | Bitiş öncesi şerit/e-posta (3 gün, 1 gün), ReadOnly açıklama metni, dönüşüm bağlantısı; gerekirse hatırlatma türü | Backend + frontend | Hatırlatma türü enum int: migration **gerekmeyebilir**; yeni kayıt tablosu eklenirse evet | Her şeyle paralel | D-T3, D-T4 |
| **8 — Trial kısıtlama politikası** | Dış e-posta sınırı, kota, export sınırı (kararlaştırılırsa) | Backend (+ UI mesaj) | Büyük olasılıkla evet (sayaç/kota) veya Redis/log | Dilim 7'den sonra | D-T1, D-T2, TRIAL-004/006 |
| **9 — Örnek klinik altyapısı** | Örnek kiracı veya bayrak modeli kararı sonrası: üretim güvenli örnek veri üreticisi (dev seeder'dan çıkarma, geliştirme-dışı çalışabilir), temizleme/geçiş komutu, güvenli (sahte) iletişim bilgileri | Backend | **Evet** (A: `Tenant.IsDemo`/bayrak; B: ~15 tablo) | Dilim 10'dan önce; Aşama 5/6/7 sonrası kapsam genişletilir | D-O1…D-O4 |
| **10 — Onboarding akışı (frontend)** | İlk giriş "Başlangıç" ekranı (boş/örnek seçimi), 4 görevlik şerit, bağlamsal yardım, örnek rozeti, temizle/geçiş akışı; aktivasyon olayları | Frontend + küçük backend (ilk giriş bayrağı, olay kaydı) | Küçük (bayrak/olay) | Dilim 9'dan sonra | D-O3, D-O5 |

Önerilen sıra: **0 → 1 → 2 → (3 ∥ 4 ∥ 5 ∥ 7)** → 6 (Aşama 5 Dilim 2 sonrası) → 9 → 10 → 8. Menü ve Rapor Merkezi **hemen başlayabilir** (frontend-only, migration yok, backend'den bağımsız): ürüne en görünür kazanç, risk düşük. Migration gerektirenler: 6, (7), 8, 9, (10). Birden fazla migration aynı anda yürütülmemeli; her biri için ayrı kullanıcı onayı.

Önerilen paralellik (en fazla 3 ajan): ajan 1 = frontend 1→2→3; ajan 2 = backend 4 + 5; ajan 3 = backend 7 (ve Aşama 5/6/7 işleri kendi ajanlarında). Aşama 8 frontend çalışması Aşama 5/6/7 frontend ajanlarıyla `app.menu.ts` ve `panel.routes.ts` dosyasında **çakışır**: bu iki dosya Dilim 1'e kadar yalnızca ekleme (yeni öğe) şeklinde değişmeli veya Dilim 1 önce birleşmeli.

## 9. Kullanıcıya sorulacak ürün kararları

> Sade Türkçe; her soruda öneri ve gerekçe. "Rakip/en iyi uygulama" notları ürün belgelerinden alındı, yeniden doğrulanmadı.

### Menü ve gezinti

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| **D-M1** | Ana menüde hangi gruplar olsun? | **a)** Bugün/Özet · Klinik · Para · Stok · Raporlar · Ayarlar (≈16 öğe). **b)** Mevcut "Hasta yönetimi" adı korunur, yalnız Tür/Irk/Kategori Ayarlar'a çıkar (≈30 öğe). **c)** Rol bazlı farklı menüler (Resepsiyon/Hekim/Yönetici) | **a**. E-Vet gibi 20+ üst alanlı yoğun menüyü almıyoruz (ana plan kararı); gün içi sıraya göre gruplama klinik personelin zihnine uyar. **c** daha iyi ama izin altyapısı zaten rolü yansıtıyor (Sekreter Hekim menüsünü görmez), ayrı rol menüsü ek karmaşıklık; pilot geri bildirimiyle sonra. |
| **D-M2** | Giriş sonrası ilk sayfa ne olsun? | **a)** Bugün (yetkisi olanlar için), yoksa Özet. **b)** Özet (şimdiki). **c)** Rol bazlı (Resepsiyon/Hekim → Bugün, Yönetici → Özet) | **c** (a'nın pratik hali: `Visits.Read` yoksa Özet). Gerekçe: "günlük işe odaklı" hedefi; Bugün resepsiyon/hekim için günlük çalışma ekranı. Yönetici Özet'i beklerse kullanıcı tercihi eklenebilir (ayarı sonraya). |
| **D-M3** | Tanımlar taşınırken adresler (URL) değişsin mi? | **a)** Hayır: yalnız menüdeki yeri değişir, adresler aynı kalır (`/panel/species` vb.). **b)** Evet: `/panel/settings/definitions/...` altına taşınır, eski adresler yönlendirir | **a** şimdi. Gerekçe: sıfır risk, yer imleri/bağlantılar kırılmaz; **b** sadece adres düzeni için yapılırsa değer/risk dengesi kötü. İleride isterseniz **b** küçük bir ek iş. |
| **D-M4** | Tedavi ve Reçete ayrı mı kalsın? | **a)** Klinik grubunda ayrı iki öğe. **b)** "Tedavi ve reçete" tek giriş, içinde iki sekme (Aşama 5 reçete satırları ile birlikte). **c)** Ayrı "Tedavi ve reçete" grubu | **a** şimdi, **b** Aşama 5 frontend bittikten sonra bakılır. Gerekçe: Aşama 5 bu sayfaları yeniden yazıyor; iki kere taşıma. |
| **D-M5** | Randevu Takvimi ve Stok Durumu/Hareketleri menüden kalkıp ilgili sayfanın içine sekme olarak taşınsın mı? | **a)** Evet (Randevular içinde "Liste/Takvim"; Ürünler içinde "Ürünler/Stok durumu/Hareketler"). **b)** Hayır, menüde ayrı kalsın | **a**. Gerekçe: aynı iş alanının farklı görünümleri; ADR-004 "yeni özellik = yeni menü değil". Risk: sayfa içi sekme yazımı ek frontend işi, adresler korunur. |

### Raporlar

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| **D-R1** | Borç raporu "ne zaman itibarıyla" olsun? | **a)** Yalnız şu anki kalan borç. **b)** Seçilen tarih itibarıyla kalan (geçmişe dönük, "vade/yaşlandırma" mantığı) | **a** ilk sürümde. Gerekçe: Aşama 3'te tek bakiye kuralı "şu an"; geçmiş tarihe göre bakiye ayrı hesap ve test ister. Yaşlandırma (0-30/31-60 gün) ilk müşteri geri bildirimine kalsın. |
| **D-R2** | Borç raporunu kim görsün? | **a)** `Charges.Read` izni olanlar (Veteriner Bugün'de zaten borç görüyor). **b)** Yalnız yönetici ve sekreter (ayrı yeni izin) | **a**. Gerekçe: yeni izin = seed uyarısı ve rol güncellemesi; mevcut Aşama 3 kararı ("Veteriner de görür") tutarlı. |
| **D-R3** | Stok raporu ilk sürümde? | **a)** Yalnız mevcut "Stok durumu" sayfası Rapor Merkezi'nde bağlantı olarak. **b)** Yeni "düşük stok / kritik ürün" raporu | **a**. Gerekçe: Aşama 5 stok güvenliği henüz oturuyor; kritik stok eşiği kavramı (ürün başına minimum) tanımlı değil. |
| **D-R4** | Rapor aralığı 93 gün sınırı kalsın mı? | **a)** Kalsın (açıkça yazılsın). **b)** 12 aya çıkar/yıllık özet | **a**. Gerekçe: performans güvencesi; ay/çeyrek raporu klinik için yeterli; yıllık ihtiyaç pilotta çıkarsa sonra. |

### Bildirim ve açık işler

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| **D-N1** | Açık işler nasıl çalışsın? | **a)** Kaynak kayıtlardan **hesaplanan liste** (bekleyen sonuç, borç, gecikmiş aşı…), kapanış otomatik, "okundu" yok. **b)** Her iş için kalıcı bildirim kaydı (okundu/çözüldü/atanan) | **a** ilk sürüm. Gerekçe: veri çelişmez (ödeme yapılınca borç kendiliğinden kapanır), migration yok, pilotta hızlı; E-Vet/DaySmart'ta da klinik işlerin çoğu listeden süzülen "bekleyen" ekranlarıdır. **b** yalnız kişiye atanmış/olay tabanlı şeyler için (D-N2). |
| **D-N2** | Hekime bildirim (sonradan eklenen kayıt) ilk sürümde olsun mu? | **a)** Evet: küçük bildirim tablosu + topbar zili, yalnız bu olay (ve lab sonucu). **b)** Hayır, sonra; ilk sürümde yalnız muayene sayfasında "Kesinleştirmeden sonra eklendi" rozeti | **a**, ama **Aşama 5 Dilim 2 işareti geldikten sonra**. Gerekçe: ADR-013 bunu bilerek Aşama 8'e bıraktı; ek kayıt yazılıp hekimin haberi olmazsa denetim zayıf kalır (tıbbi kayıt ek ilkesi: ekleyen ve gerekçe görünür, ilgilisi haberdar). Onay iş akışı (hekim "kabul etti") hâlâ kapsam dışı. |
| **D-N3** | Açık iş eşikleri | Bekleyen lab: kaç günden sonra "gecikmiş" (örn. 3 gün)? Borç: tutar/gün eşiği olsun mu? Gecikmiş aşı: kaç gün tolerans? | Öneri: lab **3 gün**, borç eşiksiz (>0), aşı eşiksiz (vade geçti). Gerekçe: basit ve öngörülebilir; pilotta ayar ihtiyacı çıkarsa Ayarlar'a eşik eklenir. |
| **D-N4** | Açık işler nerede görünsün? | **a)** Bugün'ün üstünde sayı çipleri + ayrı "Açık işler" sayfası (menüde yok, çiplerden ve zilden gidilir). **b)** Menüde ayrı "Açık işler" öğesi | **a**. Gerekçe: menü büyümez (ADR-004); günlük aksiyon yüzeyi Bugün'dür (ana plan). |

### Örnek klinik ve onboarding

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| **D-O1** | Örnek veri nerede yaşasın? | **a)** Ayrı örnek kiracı (gerçek klinik boş kalır, geçiş = örneği kapat). **b)** Aynı klinikte "örnek" işaretli kayıtlar, temizle düğmesi. **c)** Örnek veri yok, yalnız anlatım | **a**. Gerekçe: karışma imkânsız; ücret/ödeme/stok kayıtları silinemeyen (iptal/ters kayıt) kurallara tabi, **b** bunları temizlemekte güvensiz; sektörde demo verisi genelde ayrı çalışma alanıdır. Teknik doğrulama gerekir (çoklu kiracı geçişi). |
| **D-O2** | Örnek veri ne kadar zengin olsun? | **a)** Sadece bugün bitmiş modeller (muayene, ücret/tahsilat, randevu, aşı); lab/yatış Aşama 6/7 bitince eklenir. **b)** Aşama 5/6/7 bitene kadar örnek klinik yayına alınmaz | **a**. Gerekçe: ADR-002 hedefi 5–10 dk'da değer; modüller geldikçe örnek zenginleşir. |
| **D-O3** | İlk giriş seçim ekranı kime ve ne zaman? | **a)** Yalnız ilk kayıt olan yönetici, kiracı ilk girişinde bir kez; sonra Ayarlar'dan "örnek kliniği yükle" yok. **b)** Her zaman Ayarlar'dan da erişilebilir | **a** + Yönetici için Ayarlar'da küçük "Örnek kliniği keşfet" bağlantısı. Gerekçe: boş ekranda kalan kullanıcıya yol göstermek. |
| **D-O4** | Örnek verinin dış iletişimi | **a)** Örnek kiracıda hiçbir dış e-posta gitmez, sahte alan adı. **b)** Kullanıcı kendi e-postasını girerse gerçek test hatırlatması gider | **a**. Gerekçe: KVKK/itibar riski; örnek müşteriye yanlışlıkla gerçek mesaj gitmesin. |
| **D-O5** | Aktivasyon ölçümü nasıl olsun? | **a)** Basit sunucu tarafı olay kaydı (ilk muayene, ilk ücret, ilk tahsilat, örnek seçimi), panel yok, sorguyla bakılır. **b)** Üçüncü taraf ürün analitiği (tarayıcı izleme) | **a**. Gerekçe: KVKK + sade; **b** çerez/izin ve veri aktarımı konuları getirir, sonra. |

### Trial ve dönüşüm

| # | Soru | Seçenekler | Öneri ve gerekçe |
|---|---|---|---|
| **D-T1** | Trial'da neyi sınırlayalım? | **a)** Hiçbir klinik işlevi değil; yalnız dış e-posta günlük sınırı (ve ileride SMS/POS yoksa/trial'da kapalı). **b)** Kayıt sayısı kotası da koy (örn. 200 müşteri). **c)** Tam özellik, sınır yok | **a**. Gerekçe: ADR-001 "günlük kullanımın büyük bölümü deneyimlenmeli"; kötüye kullanım riski şimdilik e-posta (SMTP maliyeti/itibar). Kota pilotta kötüye kullanım gözlenirse. |
| **D-T2** | Trial'da/ReadOnly'de dışa aktarma (CSV/XLSX) serbest mi? | **a)** Serbest (veri taşınabilirliği; güven verir). **b)** Trial'da satır sınırlı (örn. 100), ücretli planda tam | **a**. Gerekçe: Vetinity pozisyonu "gizlenmeyen, esaret yaratmayan ürün" (ADR-001); satış tarafında **b** yaygın ama veri rehin alma hissi yaratır. Ödemeden veri çıkarma riski kabul edilebilir (müşteri verisi zaten onların). |
| **D-T3** | Trial bitişi nasıl olsun? | **a)** 14. günde ReadOnly (şimdiki), öncesinde 3 gün/1 gün uyarı (şerit+e-posta). **b)** a + 3 günlük "yumuşak" ek süre (yazma açık, şerit kırmızı) | **b** önerilir. Gerekçe: klinikte "muayene kaydedemiyorum" anı kritik; hafif tolerans dönüşüm sürtünmesini azaltır. Ek süre için kodda alan yok (`TrialEndsAtUtc` +3 gün basit); ürün kararı. |
| **D-T4** | Ücretli plana dönüşüm akışı | **a)** Mevcut Abonelik sayfası + iyzico checkout; trial bitmeden yönlendirme eklenir. **b)** Satış temsilcisi/fatura ile (manuel) | **a** (kod var). Önce **gerçek ödeme sağlayıcısı hesabı ve test ödemesi** doğrulanmalı; bu keşifte doğrulanamadı. |
| **D-T5** | Plan farkı | **a)** Şimdilik yalnız koltuk sayısı (3/10/50). **b)** Plan başına özellik bayrakları (örn. çoklu klinik, raporlar) | **a**. Gerekçe: ana plan plan-özellik farkı tanımlamıyor; pilot öncesi karmaşa. Fiyat/özet tablosu `/pricing` ile tutarlı mı ayrıca bakılmalı. |
| **D-T6** | Kayıt kötüye kullanımı | **a)** E-posta doğrulama zorunlu + istek oranı sınırı + bot koruması. **b)** Yalnız mevcut durum | **a**, mevcut durum doğrulandıktan sonra (§10). Pilot öncesi kapı. |

## 10. Doğrulanmayanlar

- Derleme, test, API çağrısı, DB bağlantısı, e-posta gönderimi, tarayıcıda ekran açma **yapılmadı**; bilgi kod ve belge okumasından. Yerel `main` taban SHA'ları yukarıda; çalışma başlarken backend `main` `7d84586` iken `1eab64a`'ya ilerlemiş çıktı, belge `1eab64a` üzerinden yazıldı.
- Menü öğe sayısı ve izin kuralları `app.menu.ts` okumasından (34 = en geniş küme); rol bazlı gerçek görünürlük (`RolePermissionBindings` ↔ menü) çıkarılmadı (Dilim 0).
- `routerLinkActive` "exact" davranışı şablon kodundan çıkarım; tarayıcıda alt sayfada aktiflik denenmedi.
- `NotificationsWidget` ve `pages/dashboard/dashboard.ts` şablon artığı; gerçek dashboard'un bunları kullanmadığı import taramasından (rota bağlantısı ayrıca bakılmadı).
- `features/users` klasörünün rota/menü bağlantısı yok olduğu `panel.routes.ts` taramasından; ölü kod olup olmadığı doğrulanmadı.
- Backend'de bildirim modeli yokluğu `grep` ile (`Notification` adı yalnız domain event adlarında); SignalR/WebSocket altyapısı olup olmadığı aranmadı.
- ADR-013 uygulamasının kodda olmaması: `Administration` varlığında işaret/gerekçe alanı bulunamadı (çıkarım). Aşama 5 Dilim 2 başka dalda sürüyor olabilir.
- Rapor ucu parametreleri/davranışları yalnız controller imzasından; liste=export eşitliği, toplamlar, saat dilimi, klinik kapsam (erişilebilir klinikler) raporlarda kod seviyesinde doğrulanmadı. 93 gün sınırının ekranda kullanıcıya anlatılıp anlatılmadığı bakılmadı.
- Borç raporu için toplu bakiye sorgusu, Query DB/projeksiyon durumu ve performans: çalıştırılmadı.
- Aşı liste ucunun gecikmiş filtresi, Aşı Raporu etkin tarih kuralının gecikmiş tanımıyla uyumu bakılmadı.
- `owner-signup` e-posta doğrulaması, giriş sırasında `EmailConfirmed` kontrolü, bot koruması, istek oranı sınırı ve aynı kişinin tekrar trial açması **doğrulanmadı**. `docs/rate-limiting-and-security-hardening.md` okunmadı.
- Çoklu kiracı üyeliği ve kiracı geçişi arayüzünün durumu (örnek kiracı seçeneği A için kritik) doğrulanmadı.
- iyzico/Stripe checkout, webhook ve geri çağrı akışlarının gerçek hesapla çalıştığı, kimlik bilgilerinin dolu olup olmadığı **bilinmiyor**; `SubscriptionCheckoutActivationService`/`StartSubscriptionCheckoutCommandHandler` yalnız başlık düzeyinde okundu. `/pricing` sayfasındaki plan/fiyat metni okunmadı.
- `DevSampleDataSeeder` yalnız üst kısmı ve özet imzası okundu; hangi müşteri/hayvan kaynağını kullandığı, idempotans ayrıntısı, Development dışı çalışmaya uygunluğu doğrulanmadı.
- Hatırlatma işleyicisinin örnek/trial kiracıda davranışı (örn. `Skipped`) çalıştırılarak denenmedi; Aşama 7 keşfindeki hatırlatma bulguları yeniden doğrulanmadı.
- Frontend testleri (`ng test`), `ng build` ve ESLint çalıştırılmadı.
- Rakip menü/rapor karşılaştırmaları (E-Vet, DaySmart) ürün belgelerinden alındı; yeniden doğrulanmadı. Pilot kliniğin gerçek menü/rapor/bildirim ihtiyacı ve trial dönüşüm hedefi bilinmiyor.
- Kullanıcı sayılarındaki koltuk sınırı (3/10/50) ile örnek kiracı/trial arasındaki ilişki, aynı kullanıcının iki kiracıda koltuk saymasının nasıl olduğu bakılmadı.
