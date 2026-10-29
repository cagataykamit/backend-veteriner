# Kayıt değişiklik geçmişi API sözleşmesi — RECORD-002

Backend tek doğruluk kaynağıdır. Uçlar `api/v1` altında; hata zarfı standart `ProblemDetails` + `extensions.code`.

> **Durum:** Sözleşme. Kapsam: klinik kayıtların "değişiklikleri gör" yüzeyi — kim, ne zaman, hangi alanı neyden neye çevirdi.

---

## 1) Kavramlar

- **Geçmiş satırı (`RecordChange`)**: bir kaydın tek kaydetme anındaki değişikliği. Append-only; düzenlenmez, silinmez.
- **Yazım tek yerde**: geçmiş, kayıt kaydedilirken altyapıda (EF kaydetme yakalayıcısı) **aynı transaction'da** yazılır. Uygulama kodu geçmiş üretmez; yeni bir yazma yolu eklense de geçmiş kendiliğinden tutulur.
- **İzlenen türler**: muayene, tedavi, reçete (satırlarıyla), aşı, laboratuvar sonucu, uygulama kaydı, yatış.
- **Kullanıcısız değişiklik kaydedilmez**: arka plan işleri (içe aktarma, sistem görevleri) geçmiş satırı üretmez; geçmiş yalnız oturum açmış kullanıcının değişikliğini gösterir.
- **Geriye dönük kayıt yoktur**: bu özellikten önceki değişikliklerin geçmişi yoktur; eski kayıtlarda liste boş olabilir.
- `AuditLogs` (güvenlik denetimi) ayrı kalır ve bu ekrana kaynak değildir.

## 2) Uçlar

| Uç | Policy |
|---|---|
| `GET /examinations/{id}/history` | `Examinations.Read` |
| `GET /treatments/{id}/history` | `Treatments.Read` |
| `GET /prescriptions/{id}/history` | `Prescriptions.Read` |
| `GET /vaccinations/{id}/history` | `Vaccinations.Read` |
| `GET /lab-results/{id}/history` | `LabResults.Read` |
| `GET /administrations/{id}/history` | `Administrations.Read` |
| `GET /hospitalizations/{id}/history` | `Hospitalizations.Read` |

Yeni izin yoktur. Yanıt **200**, yeniden eskiye en çok **200** satır:

```json
[
  {
    "id": "guid",
    "kind": "Updated",
    "occurredAtUtc": "2026-10-30T09:15:00Z",
    "actorUserId": "guid",
    "actorName": "Dr. Ayşe Yılmaz",
    "changes": [
      { "field": "Title", "oldValue": "Pansuman", "newValue": "Pansuman ve temizlik" }
    ]
  },
  { "id": "guid", "kind": "Created", "occurredAtUtc": "...", "actorUserId": "guid", "actorName": "...", "changes": [] }
]
```

- `kind`: `Created` | `Updated` | `Deleted`. `changes` yalnız `Updated` için dolu; değer değişmeyen kaydetme satır üretmez.
- **Boş liste** döner: kayıt yoksa, başka kiracıdaysa, kullanıcının erişebildiği kliniklerin dışındaysa (kapsam en son satırın kliniğiyle denetlenir) veya geçmişi yoksa. Ayrım bilerek yapılmaz (sızıntı yok).
- `actorName` kullanıcı silinmişse `null` olabilir.

## 3) Alan değerleri

- `field`: varlığın özellik adı (ör. `Findings`, `WeightKg`, `Status`, `VoidReason`). İstemci kendi etiketine çevirir; bilinmeyen adı olduğu gibi gösterebilir.
- Değerler metindir: tarih/saat ISO 8601 UTC (`2026-10-30T09:15:00.000Z`), tarih `yyyy-MM-dd`, sayı invariant (`4.25`), enum adı (`Finalized`), bool `true`/`false`, kimlik guid metni. `null` = boş.
- Dışlanan teknik alanlar: `Id`, `TenantId`, `RowVersion`, `CreatedAtUtc`, `UpdatedAtUtc`, `CreatedByUserId`, `RequestId`, `ImportBatchId`.
- **Reçete satırları** tek alan gibi izlenir: `field = "items"`, değer her satır için bir metin satırı (`ad · × miktar birim · doz · talimat · parti`, `\n` ile ayrılır). Satırlar yeniden yazılsa da metin aynıysa değişiklik sayılmaz.

## 4) Veri modeli ve migration

Yeni tablo `RecordChanges` (migration `AddRecordChanges`): `Id`, `TenantId`, `ClinicId`, `RecordType` (50), `RecordId`, `Kind` (int), `ActorUserId`, `OccurredAtUtc`, `ChangesJson` (nvarchar(max), JSON dizi). İndeks `(TenantId, RecordType, RecordId, OccurredAtUtc)`. Kiracı filtresi otomatik (ADR-040). Backfill yok.
