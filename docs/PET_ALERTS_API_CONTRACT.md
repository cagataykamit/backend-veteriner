# Hayvan uyarıları (Pet alerts) API sözleşmesi

Backend tek doğruluk kaynağıdır. Kaynak: CHECKIN-013 (Bugün satırında hasta uyarı rozetleri); ADR-011 (ürün kararı) koordinatörde. Hata zarfı standart `ProblemDetails` + `extensions.code`.

## Karar ve gerekçe

**Veri keşfi (kod okunarak):** Sistemde yapılandırılmış alerji / agresiflik / kronik hastalık / davranış verisi **yoktu**. Tek ilgili alan serbest metin `Pet.Notes` (2000 karakter); muayene ve diğer kayıtlardaki `Notes` alanları ziyarete özeldir, kalıcı uyarı taşımaz. Serbest metinden rozet üretilemez (ayrıştırma güvenilir değil, klinik güvenlik için tahmin yasak). Bu yüzden rozet verisi uydurulmaz; yapılandırılmış alan eklendi, eski kayıtlar "uyarı yok" başlar.

**Model (en küçük temiz model):** `Pet` üzerinde bayrak kümesi (`alertFlags`) + tek kısa serbest not (`alertNote`, en çok 200 karakter).

| Bayrak | Anlamı | Neden bu listede |
|---|---|---|
| `Allergy` | Alerji (ilaç, gıda, aşı vb.) | Yanlış ilaç/aşı en doğrudan klinik zarar; not "neye" sorusunu taşır |
| `Aggressive` | Agresif / ısırma riski | Personel güvenliği ve muayene hazırlığı (ağızlık, sedasyon) |
| `ChronicCondition` | Kronik hastalık | Tedavi/ilaç kararlarını etkiler (diyabet, böbrek vb.) |
| `AnesthesiaRisk` | Anestezi / sedasyon riski | Operasyon ve sedasyon öncesi tek bakışta bilinmesi gereken güvenlik bilgisi |

- **Neden yalnızca dört bayrak:** Her biri hekimin muayeneye girmeden bilmesi gereken *kalıcı güvenlik* bilgisidir; ziyaret içeriği (anamnez, plan) değildir. Gebelik, gıda tercihi, vb. kalıcı güvenlik değil ya da serbest nottur; aşırı genişletme bakım maliyeti ve rozet gürültüsü üretir. Yeni bayrak eklemek ileride tek enum değeri ve (gerekirse) tek sözlük satırıdır.
- **Neden bayrak başına not değil, tek `alertNote`:** "Alerjinin ne olduğu" tek kısa cümleyle çözülür; bayrak başına not formu ve alan çoğalması ek karmaşıklık getirir. Not yalnızca en az bir bayrak varken anlamlıdır.
- **Neden ayrı tablo/sözlük değil:** Bayrak kümesi küçük ve sabittir; hasta başına tek satır okuma (Bugün'de ek sorgu yok). Serbest etiket listesi bilinçli olarak yoktur.
- **Triage skoru, hasta geçmişinden otomatik türetme ve müşteri düzeyi uyarı kapsam dışıdır.**

## Alan şekli

`petAlerts` (hem yanıtlarda hem istek alanları aynı kavram):

```json
{ "flags": ["Allergy", "Aggressive"], "note": "Penisiline alerji" }
```

- `flags`: string enum adları; sıra sabit (`Allergy`, `Aggressive`, `ChronicCondition`, `AnesthesiaRisk`). Uyarı yoksa **boş liste**, `note` `null`. `petAlerts` yanıtlarda **hiçbir zaman null değildir**.
- Eski kayıtlar: `flags: []`, `note: null`.

## Yazma: `POST /api/v1/pets` ve `PUT /api/v1/pets/{id}`

İstek gövdesine iki **opsiyonel** alan eklenir (mevcut izinler: `Pets.Create`, `Pets.Update`; yeni izin yok):

| Alan | Tip | Anlamı |
|---|---|---|
| `alertFlags` | string[]? | `null`/yok: **dokunma** (PUT'ta mevcut değer korunur; `POST`'ta uyarı yok). `[]`: tüm bayrakları temizle. Dolu: bayrak kümesini bu liste yapar |
| `alertNote` | string? | `null`/yok: dokunma. `""` (boş/boşluk): notu temizle. Dolu: notu bu metin yapar (trim, en çok 200) |

**Neden "dokunma" semantiği:** `PUT /pets/{id}` tam değiştirme (replace) uygulamasıdır; alanı bilmeyen eski bir istemci gövdesinde `alertFlags` göndermez. Varsayılanı "temizle" yapmak, hastanın kritik uyarısını sessizce silerdi (klinik güvenlik). Bu yüzden alanlar yalnızca açıkça verildiğinde değişir (muayene kısmi PUT desenine paralel: `null` = dokunma, boş = temizle).

Kurallar (domain `Pet.SetAlerts` içinde, tek yerde):

- Bilinmeyen bayrak adı → `400 Pets.Validation` ("Geçersiz uyarı bayrağı").
- Not 200 karakteri aşarsa → `400 Pets.Validation`.
- Etkin bayrak kümesi boş ise (`alertFlags: []` veya hiç bayrak yoksa) not **temizlenir**; boş kümeyle dolu not gönderilirse `400 Pets.Validation` ("Not için en az bir uyarı bayrağı gerekir").
- Aynı bayrağın tekrarı yok sayılır (küme).
- Tenant/klinik kuralları mevcut Pet uçlarıyla aynıdır; `tenantId` bağlamdan çözülür.

## Okuma

- `GET /api/v1/pets/{id}` → `PetDetailDto.petAlerts` (yeni alan). **Muayene hasta bağlamı** (ADR-005) hasta özetini bu uçtan alır; muayene çalışma alanı ayrı bir endpoint gerektirmeden uyarıları gösterebilir.
- `GET /api/v1/visits/today` → her `TodayItemDto` satırında `petAlerts` (hem Visit'li hem planlı randevu satırı; hayvanın uyarıları). Ek sorgu yoktur, mevcut hayvan birleştirmesiyle gelir. Yanlış işaretlenenler görünümünde (`voided=true`) da döner.
- Liste uçları (`GET /pets`) ve Query DB read modeli bu sürümde değişmez (Query DB okuma bayrakları kapalıdır; projeksiyona alan eklenmedi).

## Hızlı kayıt

`POST /clients/quick-register` bu sürümde `alertFlags`/`alertNote` **almaz**: hızlı kayıt resepsiyon hızlı akışıdır, yeni hastanın uyarıları henüz bilinmez; ilk muayenede `PUT /pets/{id}` ile eklenir. Gerekirse ek alanı sonradan opsiyonel eklemek geriye uyumludur.

## Hata kodları

| Kod | HTTP | Durum |
|-----|------|-------|
| `Pets.Validation` | 400 | Geçersiz bayrak adı, not çok uzun veya bayraksız not |

## Veri ve dağıtım

- Migration `AddPetAlerts`: `Pets.AlertFlags` (`int NOT NULL`, varsayılan 0), `Pets.AlertNote` (`nvarchar(200)` NULL). Mevcut kayıtlar "uyarı yok". Query DB şeması değişmez.
- Yeni izin yok; seed gerekmez.
