# Hızlı müşteri + hasta kaydı API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: backlog CHECKIN-008, `VISITS_API_CONTRACT.md` (randevusuz geliş). Tüm uçlar `api/v1` altında, JSON, standart `ProblemDetails` + `extensions.code` hata zarfı.

> **Durum:** Sözleşme. Uygulama bu sözleşmeye göre yapılır.

## 1) Amaç ve sınırlar

Resepsiyon, randevusuz gelen yeni bir müşterinin sahibini ve hayvanını **tek istekte, tek işlemde (transaction)** kaydeder. Yarım kayıt oluşmaz: hayvan oluşturulamazsa müşteri de oluşmaz.

- Bu uç **Visit açmaz**. Geliş kaydı ayrıdır: frontend yanıttaki `petId` ile mevcut idempotent `POST /api/v1/visits` çağrısını yapar.
- Müşteri ve hayvan oluşturma kuralları **yeniden yazılmaz**: uç, mevcut `CreateClientCommand` ve `CreatePetCommand` yollarını (doğrulama, mükerrer kontrol, domain, abonelik yazma koruması, projeksiyon outbox olayları) aynen çalıştırır. Müşteri/hasta oluşturmadaki her kural bu uçta da geçerlidir.

## 2) Yetki

`Clients.Create` **ve** `Pets.Create` ikisi birden gerekir (ikisinden biri eksikse `403`). Kiracı yalnızca JWT/bağlamdan çözülür; `tenantId` istekte yoktur.

## 3) `POST /api/v1/clients/quick-register`

İstek:

```json
{
  "fullName": "Ali Veli",
  "phone": "0555 111 22 33",
  "petName": "Pamuk",
  "speciesId": "guid",
  "breedId": "guid?",
  "breed": "string?",
  "gender": "Male | Female | Unknown ?",
  "birthDate": "2024-05-01?",
  "microchipNumber": "string?",
  "isNeutered": false
}
```

| Alan | Zorunlu | Kural |
|------|---------|-------|
| `fullName` | Evet | Mevcut müşteri kuralı: 2-300 karakter |
| `phone` | **Evet** | Türkiye cep telefonu (mevcut `TurkishMobilePhone` normalizasyonu: `05XXXXXXXXX`, `5XXXXXXXXX`, `+90 5XX…` → `905XXXXXXXXX`). Boş veya geçersiz → `400` |
| `petName` | Evet | Mevcut hayvan adı kuralı (1-200) |
| `speciesId` | Evet | Aktif tür olmalı (`Pets.SpeciesNotFound`) |
| `breedId`, `breed`, `gender`, `birthDate`, `microchipNumber`, `isNeutered` | Hayır | `CreatePetCommand` ile aynı kurallar (ırk türle uyumlu, doğum tarihi gelecekte olamaz, vb.) |

Müşterinin e-posta ve adres alanları bu hızlı yolda **yoktur**; sonradan `PUT /clients/{id}` ile eklenir.

Yanıt `201 Created` (`Location` → `GET /clients/{clientId}`):

```json
{ "clientId": "guid", "petId": "guid" }
```

## 4) Yinelenen müşteri

Mevcut kural aynen geçerlidir (`CreateClientCommandHandler`): kiracıda **aynı ad (büyük/küçük harf duyarsız, trim) + aynı telefon** (veya aynı ad + aynı e-posta) ile müşteri varsa `409` + `Clients.DuplicateClient`; **hiçbir şey oluşmaz** (hayvan dahil). Yalnızca telefonun aynı olması engel değildir: aile bireyleri aynı telefonu paylaşabildiği için farklı adla aynı telefonla müşteri açılabilir. Yinelenen hayvan (aynı müşteri, aynı ad ve tür) bu uçta yeni müşteri için oluşamaz.

> Mevcut müşteriyi telefonla bulma ve seçme akışı arama işidir (SEARCH-001); bu uç “eşleşen müşterileri döndürme” yapmaz (açık soru: ürün isterse `409` yanıtına eşleşen müşteri özeti eklenebilir, ayrı karar).

## 5) Atomiklik

Müşteri ve hayvan aynı veritabanı transaction'ında oluşur. Hayvan adımı herhangi bir nedenle başarısız olursa (iş kuralı hatası veya beklenmeyen hata) müşteri kaydı **ve outbox olayları geri alınır**. Başarılı olduğunda müşteri ve hayvan için mevcut yollardaki outbox olayları (`client.created.v1`, `pet.created.v1`) üretilir; Query DB projeksiyonu mevcut müşteri/hayvan oluşturmayla birebir aynıdır.

## 6) Hata kodları

| HTTP | Kod | Durum |
|------|-----|-------|
| 400 | `Validation.FluentValidation` | Eksik/geçersiz alan (telefon boş/geçersiz dahil); hata alan adları istekteki adlardır |
| 400 | `Pets.SpeciesNotFound` / `Pets.BreedNotFound` / `Pets.BreedSpeciesMismatch` | Mevcut hayvan kuralları |
| 403 | — | `Clients.Create` veya `Pets.Create` yok |
| 403 | `Tenants.TenantInactive` / abonelik yazma koruması | Mevcut müşteri/hayvan kuralları |
| 409 | `Clients.DuplicateClient` | Madde 4 |

## 7) Bilinen sınırlar

- Mükerrer kontrol uygulama katmanındadır (veritabanı düzeyinde ad+telefon benzersiz indeksi yoktur; yalnızca e-posta+telefon çifti için filtreli benzersiz indeks vardır). Eşzamanlı iki aynı istek nadiren iki müşteri üretebilir; bu mevcut `POST /clients` ile aynı davranıştır.
