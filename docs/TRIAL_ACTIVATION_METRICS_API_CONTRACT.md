# Deneme (trial) aktivasyon ölçümleri API sözleşmesi — TRIAL-008

Backend tek doğruluk kaynağıdır. Kaynak: ADR-001 (self-service deneme), ADR-002 (örnek klinik), backlog `TRIAL-008` ("Trial başlatma, örnek klinik seçimi, ilk 10 dk aksiyon, aktivasyon, dönüşüm metrikleri"), `roadmap/release-plan.md` "Genel metrik referansı". İlgili: `EXAMPLE_CLINIC_API_CONTRACT.md`, `TRIAL_LIMITS_API_CONTRACT.md`, `SALES_PARTNER_DISCOUNT_API_CONTRACT.md` §5 (platform yöneticisi yetki modeli), `AUTH_TENANT_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme (uygulanıyor, `feat/trial-008-activation-metrics`). **Migration var** (`TenantActivations` tablosu), yeni izin yok (platform yöneticisi claim'i), **1 yeni uç**. Frontend platform yöneticisi alanında (Satış ortakları yanında) "Deneme ölçümleri" sayfasını bu belgeye göre yapar.

---

## 1) Amaç ve sınırlar

Ürün ekibi denemenin işe yaradığını ölçmeden optimize edemez. Bu sözleşme beş ölçümü tanımlar ve tek bir özet ucuyla verir:

| Metrik (release plan) | Burada | Nasıl ölçülür |
|---|---|---|
| Trial başlatma oranı | `trialStarts` (yalnız sayı) | Dönemde başlayan deneme abonelikleri. Ziyaretçi sayısı üründe yok; **oran verilmez** (tanıtım sitesi analitiğiyle dışarıda birleştirilir). |
| Örnek klinik seçme oranı | `exampleClinic` | Deneme başlatanların kaçı örnek kliniği yükledi (kayıtta ya da sonradan). |
| İlk 10 dakikada anlamlı aksiyon | `activation.within10Minutes*` | Deneme başlangıcından ilk anlamlı aksiyona ≤ 10 dk. |
| Trial aktivasyon oranı | `activation` | Deneme başlatanların kaçı en az bir anlamlı aksiyon yaptı. |
| İlk değer anına ulaşma süresi | `activation.medianMinutesToFirstAction` | Aktive olanlarda başlangıç → ilk aksiyon süresinin medyanı (dakika). |
| Trial-to-paid dönüşüm | `conversion` | Deneme başlatanların kaçı ücretli plana geçti. |

Sınırlar:
- Yalnız **platform yöneticisi** okur (JWT `platform_admin = true`; `AuthorizationPolicyNames.PlatformAdmin`). Klinik rolleri izinleri ne olursa olsun **403**.
- Kohort = **dönemde deneme başlatan kiracılar**; tüm oranların paydası `trialStarts`. Aktivasyon/dönüşüm tarihi dönem dışında olsa da sayılır (kohort bakışı).
- Kiracı bazlı liste **yoktur** (kişisel veri değil ama gerek de yok); yalnız toplamlar.
- Zaman serisi (günlük) ilk sürümde **yok** (§7).

## 2) Tanımlar (tek kaynak: kod)

### 2.1 Anlamlı aksiyon ve aktivasyon

**Anlamlı aksiyon**, kiracı kullanıcısının deneme boyunca yaptığı, release planındaki "Muayene, randevu, hayvan ekleme vb." listesine giren **başarılı** yazma komutudur. Kural tek yerde yaşar: komut `IActivationSignalRequest` işaretini taşır, `ActivationSignalBehavior` komut **başarıyla** bittiğinde kiracının **ilk** aksiyonunu kaydeder (sonrakiler yazmaz).

| `ActivationSignalKind` | Komut |
|---|---|
| `ClientCreated` | `CreateClientCommand`, `QuickRegisterClientCommand` |
| `PetCreated` | `CreatePetCommand` |
| `AppointmentCreated` | `CreateAppointmentCommand` |
| `ExaminationCreated` | `CreateExaminationCommand` |
| `VaccinationCreated` | `CreateVaccinationCommand` |
| `ChargeCreated` | `CreateChargeCommand` |
| `PaymentCreated` | `CreatePaymentCommand` |
| `SaleCreated` | `CreateSaleCommand` |
| `ProductCreated` | `CreateProductCommand` |

- Örnek klinik yüklemesi komut pipeline'ından **geçmez** (doğrudan yazar); bu yüzden **aksiyon sayılmaz**. Böylece örnek klinikle başlayan kiracının aktivasyonu da kullanıcının kendi ilk işlemiyle ölçülür.
- Kayıt **kiracı başına tektir** (`TenantActivations`, PK `TenantId`): `ExampleClinicLoadedAtUtc?`, `FirstActionAtUtc?`, `FirstActionKind?`, `FirstActionUserId?`. İlk aksiyon bir kez yazılır; yarış durumunda (iki eş zamanlı ilk komut) kazanan kalır, kaybeden sessizce atlanır.
- Ölçüm kaydı **iş akışını asla bozmaz**: behavior hatayı yutar ve `LogError` yazar (denetim kaydıyla aynı ilke). İşlem (transaction) dışında, komut commit'inden sonra yazılır.
- Deneme dışı (ücretli) kiracıda da ilk aksiyon kaydedilir (ucuzdur, başka ölçümlere açık kapı); özet yalnız deneme kohortunu okur.

### 2.2 Örnek klinik seçimi

`IExampleClinicLoader.LoadAsync` (tek yükleyici; kayıt ve sonradan yükleme ikisi de buradan geçer) başarıyla biterse aynı işlemde `TenantActivations.ExampleClinicLoadedAtUtc` yazılır. "Seçim", yüklemenin gerçekleşmesidir; kayıtta `startWithExampleClinic: true` deyip yükleme geri alınırsa seçim sayılmaz.

### 2.3 Deneme başlangıcı ve dönüşüm

- **Deneme başlangıcı:** `TenantSubscriptions.TrialStartsAtUtc` (kayıt anı; `StartTrial`). Dönem filtresi bu alana uygulanır: `fromUtc ≤ TrialStartsAtUtc < toUtc`.
- **Dönüşüm:** aynı abonelikte `ActivatedAtUtc` dolu (`ActivatePaidPlan`). Deneme süresi içinde plan değiştirme (`ChangePlanDuringTrial`) dönüşüm **değildir**.
- Platform kiracısının (`Tenant.PlatformTenantName`) deneme aboneliği yoktur; kohorta girmez.

## 3) `GET /api/v1/admin/trial-metrics` — platform yöneticisi

| Sorgu parametresi | Tip | Varsayılan | Kural |
|---|---|---|---|
| `fromUtc` | ISO-8601 UTC | `toUtc − 30 gün` | `fromUtc < toUtc`, aksi 400 |
| `toUtc` | ISO-8601 UTC | istek anı | Aralık en çok **366 gün**, aksi 400 |

Başarı **200**:

```json
{
  "fromUtc": "2026-09-25T00:00:00Z",
  "toUtc": "2026-10-25T00:00:00Z",
  "trialStarts": 40,
  "exampleClinic": { "count": 28, "rate": 0.7 },
  "activation": {
    "count": 25, "rate": 0.625,
    "within10MinutesCount": 12, "within10MinutesRate": 0.3,
    "medianMinutesToFirstAction": 7.5
  },
  "conversion": { "count": 3, "rate": 0.075 },
  "firstActionBreakdown": [
    { "kind": "ClientCreated", "count": 10 },
    { "kind": "AppointmentCreated", "count": 8 },
    { "kind": "ExaminationCreated", "count": 7 }
  ],
  "byOnboarding": [
    { "onboarding": "ExampleClinic", "trialStarts": 28, "activationRate": 0.75, "within10MinutesRate": 0.39, "conversionRate": 0.11 },
    { "onboarding": "EmptyClinic",   "trialStarts": 12, "activationRate": 0.33, "within10MinutesRate": 0.08, "conversionRate": 0.0 }
  ]
}
```

Alan kuralları:
- Oranlar **0–1 arası ondalık**; payda sıfırsa `null` (frontend "—" gösterir).
- `medianMinutesToFirstAction`: aktive olan kiracılarda `(FirstActionAtUtc − TrialStartsAtUtc)` dakikalarının medyanı, 1 ondalık; aktive olan yoksa `null`. Çift sayıda eleman: ortadaki ikisinin ortalaması.
- `within10Minutes*`: `FirstActionAtUtc − TrialStartsAtUtc ≤ 10 dk` (eşitlik dahil).
- `firstActionBreakdown`: yalnız sayısı > 0 olan türler, sayıya göre azalan; `kind` §2.1 enum adı (string).
- `byOnboarding`: sabit iki satır (`ExampleClinic`, `EmptyClinic`), sıfır kohortta da gelir (oranlar `null`).
- `fromUtc`/`toUtc` yanıtta **etkin** değerlerdir (varsayılan uygulandıysa onlar döner).

| Kod | HTTP | Durum |
|---|---|---|
| `TrialMetrics.InvalidRange` | 400 | `fromUtc ≥ toUtc` veya aralık > 366 gün |
| — | 401 | Kimlik yok |
| — | 403 | `platform_admin` claim'i yok (klinik rolleri) |

Sorgu tek SELECT'e yakın çalışır (kohort abonelikleri + `TenantActivations` sol birleşim); pilot ölçeğinde (yüzlerce kiracı) ek indeks gerekmez. `TenantSubscriptions.TrialStartsAtUtc` üzerinde indeks eklendi (dönem filtresi).

## 4) Kod yerleri

- Domain: `Tenants/TenantActivation.cs` (`MarkExampleClinicLoaded(utcNow)`, `MarkFirstAction(kind, userId, utcNow)` — ikisi de ilk yazımdan sonra no-op), `Tenants/ActivationSignalKind.cs`.
- Application: `Common/Abstractions/IActivationSignalRequest.cs`, `Common/Behaviors/ActivationSignalBehavior.cs`, port `Tenants/Activation/ITenantActivationRecorder.cs`, okuyucu `Tenants/Activation/ITrialActivationMetricsReader.cs`, sorgu `Tenants/Queries/GetTrialActivationMetrics/*`, DTO `Tenants/Contracts/TrialActivationMetricsDto.cs`.
- Infrastructure: `Persistence/Configurations/TenantActivationConfiguration.cs`, `Tenants/TenantActivationRecorder.cs`, `Tenants/TrialActivationMetricsReader.cs`, `ExampleClinicDataGenerator.LoadAsync` sonunda işaret, migration `AddTenantActivations`.
- Api: `TrialMetricsAdminController` (`admin/trial-metrics`, `PlatformAdmin` policy).
- Pipeline sırası: `… → Audit → ActivationSignal → Performance → Logging → Transaction`.

## 5) Testler

- Birim: `TransactionalCommandMarkerTests` benzeri `ActivationSignalMarkerTests` (9 komut işareti taşır, tür eşlemesi doğru); `ActivationSignalBehaviorTests` (başarısız sonuçta yazmaz, kiracı yokken yazmaz, kaydedici hatası yutulur ve loglanır); `TenantActivation` domain testleri (ikinci işaret no-op); metrik hesaplayıcı testleri (oran/null/medyan/10 dk sınırı/breakdown sırası).
- Entegrasyon: platform yöneticisi 200, klinik yöneticisi 403; kayıt → örnek klinik → ilk komut akışında `TenantActivations` satırı ve özet sayılar; kohort dönem filtresi; `fromUtc ≥ toUtc` 400.

## 6) Frontend etkisi

- `api-endpoints.ts`: `admin.trialMetrics(fromUtc, toUtc)`.
- Yeni sayfa `/panel/trial-metrics` (`platformAdminGuard`), menüde Satış ortaklarının yanında (platform yöneticisine özel grup); dönem seçici (son 7/30/90 gün), kartlar: deneme başlangıcı, örnek klinik oranı, aktivasyon, 10 dk aktivasyon, medyan süre, dönüşüm; ilk aksiyon dağılımı tablosu; örnek klinik / boş klinik karşılaştırma tablosu.
- Sözleşme kopyası `docs/contracts/` altında.

## 7) Açık sorular / sonraya bırakılanlar

- Günlük zaman serisi (trend grafiği): kohort başına gün kırılımı; ilk sürümde yok.
- "Haftalık aktif klinik" ve "muayene kaydı oluşturma süresi" (release plan) bu sözleşmenin dışında; ayrı iş.
- Ziyaretçi → deneme oranı: tanıtım sitesi analitiği (ADR-034) gelince dışarıda birleştirilir.
- Deneme bitişi ile dönüşüm arasındaki süre ve iptal nedeni: TRIAL-007 ile birlikte.
