# Müşteri ve hasta silme (yumuşak silme) — API sözleşmesi (ADR-054)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-054-musteri-hasta-yumusak-silme.md`. İlgili: `PET_STATUS_CLIENT_CONTACT_API_CONTRACT.md` (vefat), `PORTAL_API_CONTRACT.md` (bağ kapatma), `EXTERNAL_API_CONTRACT.md` (`deletedAtUtc`), `SEARCH_API_CONTRACT.md`. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feature/musteri-hasta-yumusak-silme`. Migration'lar: komut DB `20261123053019_AddClientPetSoftDelete`, Query DB `20261123053029_AddClientPetReadModelIsDeleted`; yalnız yerel geliştirme DB'sine uygulandı. **Yeni izinler:** `Clients.Delete`, `Pets.Delete` (açılıştaki idempotent tohumlama Admin, Owner, ClinicAdmin rollerine bağlar).

---

## 1) Kavram

- **Kalıcı silme yok.** Silme kaydı veritabanında bırakır (`deletedAtUtc`, `deletedByUserId`; hastada `deletedWithClient`).
- Silinen kayıt **görünmez:** `GET /clients`, `GET /pets` (arama dahil; panel seçicileri bu uçları kullanır), AI hasta arama, özet sayıları ve son kayıtlar, hatırlatma adayları, gecikmiş/yaklaşan aşı listeleri ve operasyonel uyarılar, portal hasta listesi ve ayrıntısı, online randevu müşteri eşleştirmesi, mükerrer kayıt denetimi.
- Silinen kayıt **görünmeye devam eder:** geçmiş kayıtlar ve raporlar (randevu, muayene, aşı, tedavi, reçete, lab, görüntüleme, yatış, ücret, ödeme, satış, belge, e-belge, kasa, defterler, HBS, rapor merkezi, borç raporu) ve bunların ad araması; adlar aynen görünür. Tam veri dışa aktarma silinenleri de içerir.
- `GET /clients/{id}` ve `GET /pets/{id}` silinen kaydı **200** ile döner; `deletedAtUtc` doludur (hastada ayrıca `deletedWithClient`). Panel kartı "Silindi" bandıyla açar.
- Vefat (`status: Deceased`) ayrı alandır; silme vefatı değiştirmez, vefat eden hasta silinebilir.
- **KVKK:** yumuşak silme KVKK anlamında silme ya da anonimleştirme değildir. Kişisel veri veritabanında, yedekte ve dışa aktarmada kalır. KVKK talebi ayrı akıştır (PORTAL-007).

## 2) Müşteri

### `DELETE /api/v1/clients/{id}`

İzin: `Clients.Delete`. Gövde yok. Audit: `Client.Delete` (hedef `ClientId=…`).

- Müşteri ve **silinmemiş tüm hastaları** aynı işlemde silinir (hastalarda `deletedWithClient: true`).
- Aktif portal bağları `ClientDeleted` gerekçesiyle kapanır (`closedByUserId` = silen kullanıcı); bekleyen portal davetleri iptal edilir.
- Zaten silinmişse işlem yapılmaz, **204**.

Yanıt **204**.

| Kod | HTTP | Durum |
|---|---|---|
| `Clients.NotFound` | 404 | Müşteri yok / başka kiracı |
| `Clients.WalkInReadOnly` | 400 | Peşin müşteri (doğrudan satış sistem kaydı) silinemez |
| `Clients.DeleteBlocked` | 409 | Açık iş var; ayrıntı aşağıda |
| `Clients.ConcurrencyConflict` | 409 | Kayıt arada değişti; yenileyip tekrar deneyin |
| `Tenants.TenantInactive` | 403 | Pasif kiracı |

`Clients.DeleteBlocked` ekleri (nedenlerin **tamamı** tek yanıtta):

```json
{
  "code": "Clients.DeleteBlocked",
  "detail": "Müşteri silinemez: açık hesabı var (borç, tahsis edilmemiş ödeme ya da kredi); önce hesabı kapatın; 2 gelecek randevusu var; önce randevuları iptal edin.",
  "reasons": ["OpenAccount", "FutureAppointments"],
  "activeHospitalizationCount": 0,
  "futureAppointmentCount": 2
}
```

| `reasons` değeri | Koşul |
|---|---|
| `OpenAccount` | Kalanı olan ücret (devir borcu dahil), tahsis edilmemiş ödeme ya da açık kredi (devir alacağı dahil); kiracı genelinde, iptal edilenler hariç |
| `ActiveHospitalization` | Müşterinin herhangi bir hastasında taburcu edilmemiş, iptal edilmemiş yatış |
| `FutureAppointments` | Müşterinin hastalarında zamanı gelmemiş planlı (`Scheduled`) randevu. **Otomatik iptal edilmez**; kullanıcı önce iptal eder |

### `POST /api/v1/clients/{id}/restore`

İzin: `Clients.Delete`. Gövde yok. Audit: `Client.Restore`. Süre sınırı yok.

- Müşteri ve **onunla birlikte silinen** hastalar geri gelir; tek başına silinmiş hastalar silinmiş kalır.
- Portal bağı **açılmaz**; klinik yeniden davet eder.
- Silinmemişse işlem yapılmaz, **204**.

| Kod | HTTP | Durum |
|---|---|---|
| `Clients.NotFound` | 404 | |
| `Clients.RestoreConflict` | 409 | Aynı e-posta + telefon, ad + e-posta ya da ad + telefonla aktif müşteri var. Ek: `conflictingClientId` |

### `GET /api/v1/clients/deleted`

İzin: `Clients.Delete`. Sorgu: `page` (1+), `pageSize` (1–200, varsayılan 20), `search` (müşteri listesiyle aynı kural, `SEARCH_API_CONTRACT.md`). Sıra: en son silinen önce.

```json
{
  "items": [
    {
      "id": "guid",
      "fullName": "Ayşe Yılmaz",
      "phone": "905321234567",
      "email": "ayse@example.com",
      "deletedAtUtc": "2026-11-23T10:00:00Z",
      "deletedByUserId": "guid",
      "deletedByUserName": "Dr. Ali Veli"
    }
  ],
  "page": 1, "pageSize": 20, "totalItems": 1, "totalPages": 1
}
```

### `GET /api/v1/clients/{id}` (değişen alan)

- `deletedAtUtc`: `null` = aktif; dolu = silinmiş.

### Silinen müşteriye yazma

`PUT /clients/{id}`, `PUT /clients/{id}/kvkk-consent`, `POST /pets` (bu müşteriye), `POST /charges`, `POST /payments`, `POST /sales` (bu müşteriye) ve portal daveti **409 `Clients.Deleted`** döner. Var olan ücret/ödeme/e-belge iptal ve düzeltme uçları açık kalır.

## 3) Hasta

### `DELETE /api/v1/pets/{id}`

İzin: `Pets.Delete`. Audit: `Pet.Delete`. Zaten silinmişse **204**.

| Kod | HTTP | Durum |
|---|---|---|
| `Pets.NotFound` | 404 | |
| `Pets.DeleteBlocked` | 409 | `reasons`: `ActiveHospitalization`, `FutureAppointments` (açık hesap hasta silmeyi engellemez; hesap müşteri düzeyindedir). Ekler müşteriyle aynı |
| `Pets.ConcurrencyConflict` | 409 | |

### `POST /api/v1/pets/{id}/restore`

İzin: `Pets.Delete`. Audit: `Pet.Restore`. Silinmemişse **204**.

| Kod | HTTP | Durum |
|---|---|---|
| `Pets.NotFound` | 404 | |
| `Pets.ClientDeleted` | 409 | Müşterisi silinmiş; önce müşteriyi geri yükleyin |
| `Pets.DuplicatePet` | 409 | Aynı müşteride aynı ad ve türde aktif hasta var |

### `GET /api/v1/pets/deleted`

İzin: `Pets.Delete`. Sorgu: `page`, `pageSize`, `search` (hasta kartı alanları: ad, tür, ırk, mikroçip; sahip alanları aranmaz). Sıra: en son silinen önce.

```json
{
  "items": [
    {
      "id": "guid",
      "name": "Pamuk",
      "speciesName": "Kedi",
      "clientId": "guid",
      "clientFullName": "Ayşe Yılmaz",
      "clientIsDeleted": true,
      "deletedWithClient": true,
      "deletedAtUtc": "2026-11-23T10:00:00Z",
      "deletedByUserId": "guid",
      "deletedByUserName": "Dr. Ali Veli"
    }
  ],
  "page": 1, "pageSize": 20, "totalItems": 1, "totalPages": 1
}
```

`clientIsDeleted: true` ise satırda "Geri yükle" yerine "Önce müşteriyi geri yükleyin" gösterilir.

### `GET /api/v1/pets/{id}` (değişen alanlar)

- `deletedAtUtc`, `deletedWithClient`.

### Silinen hastaya yazma

`PUT /pets/{id}`, `PUT /pets/{id}/status`, yeni randevu, geliş, muayene (`EnsureCanStartCare`) ve yeni yatış **409 `Pets.Deleted`** döner. Vefat eden hastadaki `Pets.Deceased` aynen kalır; silinmiş hastada `Pets.Deleted` önceliklidir.

## 4) Panel için Türkçe metinler

Hata mesajları `detail` alanında Türkçe döner; panel doğrudan gösterebilir. Önerilen anahtarlar (panel i18n):

| Anahtar | Metin |
|---|---|
| `deletion.confirm.client` | Müşteri ve hastaları listelerden ve hatırlatmalardan kalkar. Muayene, ödeme ve belgeler korunur. Yönetici "Silinenler"den geri yükleyebilir. KVKK silme talebi için bu işlem kullanılmaz. |
| `deletion.confirm.pet` | Hasta listelerden ve hatırlatmalardan kalkar. Geçmiş kayıtları korunur. Yönetici "Silinenler"den geri yükleyebilir. |
| `deletion.blocked.OpenAccount` | Açık hesabı var (borç, tahsis edilmemiş ödeme ya da kredi); önce hesabı kapatın. |
| `deletion.blocked.ActiveHospitalization` | Aktif yatışı var; önce taburcu edin ya da yatışı iptal edin. |
| `deletion.blocked.FutureAppointments` | {futureAppointmentCount} gelecek randevusu var; önce randevuları iptal edin. |
| `deletion.deletedBanner` | Bu kayıt {deletedAtUtc} tarihinde silindi. |
| `deletion.restore.clientDeleted` | Hastanın müşterisi silinmiş; önce müşteriyi geri yükleyin. |
| `deletion.restore.conflict` | Aynı bilgilerle aktif bir müşteri var; o kaydı kullanın ya da bilgileri düzenleyin. |

## 5) Okuma modeli ve olaylar

- Silme ve geri yükleme `client.updated.v1` / `pet.updated.v1` olayını `isDeleted` alanıyla üretir. `ClientReadModels` / `PetReadModels` satırı silinmez, `IsDeleted` taşır; okuma modeli liste yolu silinenleri dışlar, kimlikle ad eşlemesi dışlamaz.
- `client.deleted.v1` / `pet.deleted.v1` yalnız kalıcı silme içindir (içe aktarma geri alması, örnek klinik temizliği).
- Kayıt geçmişi (`IChangeTracked`) ve denetim kaydı kim/ne zaman bilgisini tutar.

## 6) Testler

- Domain: `ClientPetSoftDeleteTests` (silme, engel nedenleri, geri yükleme, birlikte silinen hastalar, portal bağı gerekçesi).
- Uygulama: `ClientPetDeletePermissionBindingsTests` (izin grupları ve rol bağları).
- Entegrasyon: `ClientPetSoftDeleteIntegrationTests` (liste/arama dışlaması, bağlı ödeme ve aşının adla okunması, Silinenler listeleri, yazma kapıları, geri yükleme, engeller, mükerrer/geri yükleme çakışması, portal bağı, izin); `ReminderProcessorServiceTests.ProcessOnce_Should_Exclude_Deleted_Pets_And_Deleted_Clients`.

## 7) Bilinen sınırlar

- Aşı, tedavi, reçete, lab, görüntüleme ve uygulama girişleri ilk dilimde ayrı `Pets.Deleted` kapısı almaz; seçici silinen hastayı göstermez ve açık yatış/gelecek randevu varken hasta silinemez.
- Silme gerekçesi alanı yok.
- Hasta "Silinenler" araması sahip adına bakmaz.
