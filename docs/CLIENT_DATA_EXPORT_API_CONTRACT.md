# Müşteri veri kopyası — API sözleşmesi (ADR-032 madde 2)

Backend tek doğruluk kaynağıdır. Kaynak: ürün deposu `decisions/ADR-032-kvkk-riza-surum-kaydi-ve-veri-kopyasi.md` (madde 2), `operations/kvkk-talep-sureci.md` ("Bilgi/kopya" adımı). İlgili: `KVKK_CONSENT_API_CONTRACT.md`, `CLIENT_BALANCE_API_CONTRACT.md`. Hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Uygulandı (kod + test), dal `feat/musteri-veri-kopyasi`. **Migration yok** (salt okunur). **Yeni izin yok**, seed gerekmez.

Amaç: KVKK bilgi talebinde klinik çalışanı müşteri kartından tek belge üretir (yazdırma + JSON). Uç veri değiştirmez.

---

## `GET /api/v1/clients/{id}/data-export`

- İzin: mevcut **`Clients.Read`** (müşteri kartını okuyabilen). Ücret/ödeme bölümleri için ayrıca `Charges.Read`/`Payments.Read` **istenmez** (ADR-032: "okuma yetkisi olan kullanıcı").
- Kapsam: JWT kiracısı. Başka kiracının veya olmayan müşteri → **404 `Clients.NotFound`** (varlık sızdırılmaz).
- Klinik kapsamı: diğer müşteri özetleriyle aynı (`recent-summary`, `payment-summary`, `balance`): aktif klinik bağlamı varsa yalnız o klinik; yoksa kullanıcının erişebildiği klinikler (kiracı geneli yönetici: tümü; atanmamış kullanıcı: klinik kayıtları boş). Müşteri ve hasta kartları kiracı geneli kayıttır, kapsamdan bağımsız gelir.
- Audit: her çağrı `Client.DataExport` (hedef `ClientId=…`; aktör, rota, IP, zaman standart audit alanlarında). Audit gövdesi yalnız istek (`{"clientId":"…"}`); **dışa verilen veri audit'e yazılmaz**. Başarısız denemeler (404) de `success=false` ile yazılır.
- Üst sınır: bölüm başına **1000** kayıt (`maxRowsPerSection`); fazlası en yeni 1000 ile kesilir ve o bölümde `isTruncated = true` olur. Hastalar ada göre, diğer bölümler en yeni önce sıralıdır.
- Enum alanları **metin** olarak döner (bu yanıtta; diğer uçlardaki sayısal enum davranışı değişmedi).

### Yanıt 200

```json
{
  "generatedAtUtc": "2026-10-16T09:30:00Z",
  "clinicId": null,
  "maxRowsPerSection": 1000,
  "client": {
    "id": "guid", "fullName": "Ayşe Yılmaz", "phone": "905551112233", "email": "ayse@example.com",
    "address": "İzmir", "createdAtUtc": "2025-03-01T10:00:00Z",
    "preferredContactChannel": "Phone", "remindersOptedOut": false
  },
  "kvkkConsent": { "status": "Accepted", "recordedOn": "2026-01-02", "recordedByName": "Mehmet Kaya" },
  "pets": { "isTruncated": false, "items": [
    { "id": "guid", "name": "Pamuk", "speciesName": "Kedi", "breed": "Tekir", "gender": "Female",
      "birthDate": "2021-05-01", "isNeutered": true, "microchipNumber": "…", "passportOrTagNumber": null,
      "status": "Active", "deceasedOn": null } ] },
  "appointments": { "isTruncated": false, "items": [
    { "scheduledAtUtc": "…", "durationMinutes": 30, "petId": "guid", "petName": "Pamuk",
      "appointmentType": "Vaccination", "status": "Cancelled" } ] },
  "examinations": { "isTruncated": false, "items": [
    { "examinedAtUtc": "…", "petId": "guid", "petName": "Pamuk", "complaint": "Kusma",
      "assessment": "Gastrit", "status": "Finalized" } ] },
  "vaccinations": { "isTruncated": false, "items": [
    { "vaccineName": "Kuduz", "appliedAtUtc": "…", "dueAtUtc": null, "petId": "guid", "petName": "Pamuk", "status": "Applied" } ] },
  "treatments": { "isTruncated": false, "items": [
    { "treatmentDateUtc": "…", "title": "Serum", "petId": "guid", "petName": "Pamuk" } ] },
  "prescriptions": { "isTruncated": false, "items": [
    { "prescribedAtUtc": "…", "title": "Reçete", "itemNames": ["İlaç A", "İlaç B"], "petId": "guid", "petName": "Pamuk" } ] },
  "charges": { "isTruncated": false, "items": [
    { "createdAtUtc": "…", "description": "Muayene", "petId": "guid", "petName": "Pamuk", "amount": 500.00,
      "allocatedAmount": 200.00, "outstandingAmount": 300.00, "currency": "TRY", "isVoided": false, "voidedAtUtc": null } ] },
  "payments": { "isTruncated": false, "items": [
    { "paidAtUtc": "…", "amount": 200.00, "allocatedAmount": 200.00, "unallocatedAmount": 0.00, "currency": "TRY",
      "method": "Cash", "petId": null, "petName": null, "isVoided": false, "voidedAtUtc": null } ] },
  "balance": { "currency": "TRY", "chargedAmount": 500.00, "allocatedAmount": 200.00,
               "outstandingAmount": 300.00, "unallocatedPaymentAmount": 0.00 }
}
```

| Alan | Anlam |
|---|---|
| `clinicId` | Aktif klinik bağlamı (tek klinik); `null` = erişilebilir tüm klinikler |
| `client.createdAtUtc` | Kayıt tarihi |
| `kvkkConsent.recordedByName` | Rızayı işaretleyen kullanıcının görünen adı (kimliği değil); bulunamazsa `null` |
| `examinations[].complaint` | Başvuru nedeni / şikâyet |
| `examinations[].assessment` | Değerlendirme / ön tanı özeti |
| `examinations[].status` | `Draft` (taslak) \| `Finalized` (kesinleşmiş); taslaklar da dahildir |
| `charges[].allocatedAmount` | Ödemeyle karşılanan (ödenen); `outstandingAmount` kalan |
| `payments[].unallocatedAmount` | Ücrete bağlanmamış kısım |
| `isVoided` / `voidedAtUtc` | İptal edilmiş ücret/ödeme; satır **işaretli dahil**, bakiyeye girmez |
| `balance` | `GET /clients/{id}/balance` toplamlarıyla aynı kural (yalnız TRY, iptaller hariç, aynı klinik kapsamı); `unallocatedPaymentAmount` = bağlanmamış TRY ödemeler |

İptal edilmiş randevu (`status = "Cancelled"`), gelmedi (`NoShow`) ve iptal edilmiş aşı (`status = "Cancelled"`) da kendi durumlarıyla dahildir.

| Kod | HTTP | Durum |
|---|---|---|
| `Clients.NotFound` | 404 | Müşteri yok / başka kiracı |
| (doğrulama) | 400 | Boş müşteri kimliği |
| `Tenants.ContextMissing` | 400 | Kiracı bağlamı yok |
| `Clinics.AccessDenied` | 403 | Aktif klinik bağlamı kullanıcıya atanmamış |
| — | 403 | `Clients.Read` yok |

---

## Dahil / hariç alanlar

| Bölüm | Dahil | Hariç (bilinçli) |
|---|---|---|
| Müşteri | id, ad soyad, telefon, e-posta, adres, kayıt tarihi, iletişim kanalı tercihi, hatırlatma istemiyor | `tenantId`, `rowVersion`, `updatedAtUtc`, normalize telefon, peşin müşteri bayrağı |
| KVKK rıza | durum, form tarihi, işaretleyenin adı | işaretleyen kullanıcı kimliği; metin sürümü alanları (ADR-032 madde 1 ayrı dalda; birleşince eklenecek) |
| Hastalar | id, ad, tür, ırk, cinsiyet, doğum tarihi, kısır, mikroçip, karne/küpe no, durum, vefat tarihi | klinik notları, güvenlik uyarıları ve notu, özel protokol no, kilo, renk, `rowVersion` |
| Randevular | tarih, süre, hasta, tür, durum | notlar (iptal/gelmedi gerekçeleri dahil), klinik kimliği, `mutationSequence` |
| Muayene özeti | tarih, hasta, şikâyet, değerlendirme, durum | anamnez, bulgular, plan, notlar, vitaller, ek notlar (addendum), kesinleştiren, denetim alanları |
| Aşılar | aşı adı, uygulama tarihi, planlanan tarih, hasta, durum | notlar, tanım/muayene/içe aktarma kimlikleri |
| Tedavi | tarih, başlık, hasta | açıklama, notlar, kontrol tarihi |
| Reçete | tarih, başlık, kalem adları, hasta | reçete metni, kullanım talimatı, miktar, notlar |
| Ücretler | tarih, açıklama, hasta, tutar, ödenen, kalan, para birimi, iptal bayrağı ve tarihi | iptal gerekçesi, iptal eden/oluşturan kullanıcı, `requestId`, ziyaret/muayene kimlikleri |
| Ödemeler | tarih, tutar, bağlanan, bağlanmamış, para birimi, yöntem, hasta, iptal bayrağı ve tarihi | notlar, iptal gerekçesi ve eden, `requestId`/`requestHash`, randevu/muayene kimlikleri |
| Bakiye | para birimi, ücret toplamı, ödenen, kalan, bağlanmamış ödeme toplamı | hasta/ziyaret kırılımı |

Kapsam dışı (bu sürümde yok): yatış, laboratuvar sonuçları, doğrudan satış, ödeme–ücret tahsis satırları, dosya/ek. Gerekirse ayrı karar.

---

## Kararlar

| # | Konu | Karar | Gerekçe |
|---|---|---|---|
| D1 | İzin | Mevcut `Clients.Read`; yeni izin yok. | ADR-032 madde 2 ve 3 ("okuma yetkisi olan kullanıcı", "mevcut izin sistemi"). |
| D2 | İptaller | İptal edilmiş ücret/ödeme/randevu/aşı **işaretli dahil**; bakiyeye girmez. | Bilgi talebinde işlenen tüm kayıtlar görünür; iptal durumu açıkça okunur. |
| D3 | Üst sınır | Bölüm başına 1000, kesilince `isTruncated`. | Tek yanıt boyutu sınırlı; gerçek müşteride sınır aşımı beklenmez, aşılırsa görünür olur. |
| D4 | Audit | `IAuditableRequest` (`Client.DataExport`); gövde yalnız müşteri kimliği. | ADR-032 "kim, hangi müşteri, ne zaman"; kişisel veri audit tablosuna kopyalanmaz. |
| D5 | Bakiye | `GetClientBalanceQuery` yeniden kullanılır. | Bakiye kuralı tek yerde kalır. |
| D6 | Klinik kapsamı | Diğer müşteri özetleriyle aynı kapsam çözümleyici. | Kullanıcı ekranda göremediği kliniğin kaydını kopyada da görmez. |
