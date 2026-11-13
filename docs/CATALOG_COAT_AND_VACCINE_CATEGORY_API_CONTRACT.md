# Katalog: kürk deseni ve aşı sınıfı (API sözleşmesi)

> **Durum:** Uygulandı (kod ve test), dal `feature/katalog-kaynak`. Migration **`AddPetCoatPatternAndVaccineCategory`** yalnız yerel DB'ye uygulandı.
> **Kaynak:** vetinity-product `research/sistem-kataloglari-kaynak-arastirmasi-2026-11.md`. Renk ile desen FIFe EMS'deki gibi ayrıdır. Aşı sınıfı WSAVA 2024'e, antiparaziter ayrımı ESCCAP'a dayanır.

## 1) Renk ve desen

`GET /api/v1/pet-colors` her satırda `kind` döner: `"Color"` ya da `"Pattern"`. Yanıt aynı uçtan gelir ve istemci listeyi `kind` alanına göre ikiye ayırır.

```json
{ "id": "…", "code": "TABBY", "name": "Tekir (Tabby)", "isActive": true, "displayOrder": 101, "kind": "Pattern" }
```

**Desenler:**
- Tekir (Tabby)
- Kaplan Çizgili (Brindle)
- Benekli
- Çizgili
- Alacalı
- Üç Renkli (Calico)
- Kaplumbağa Kabuğu (Tortoiseshell)
- Sivri Renk (Colorpoint)
- Van Deseni
- Merle
- Harlequin
- Kırçıl (Roan)

**Renk satırlarında yapılan değişiklikler:**
- Diğer satırların hepsi renktir.
- Dumanlı, Gümüş, Lila, Tarçın, Fawn, Siyah-Bakır, Samur ve Siyah-Beyaz-Kahve eklendi.
- At donları eklendi: Doru, Al, Kır, Yağız, Kula, İzabel.
- `Kırçıllı-Beyaz` ve `Kahverengi-Kırçıllı` renk ile desenin birleşimiydi; artık pasif. "Kırçıllı" TDK'da kırlaşmış anlamına gelir; brindle karşılığı "Kaplan Çizgili"dir.

**Hasta (`POST /pets`, `PUT /pets/{id}`):**
- İsteğe isteğe bağlı `patternId` alanı eklendi.
- PUT tam değiştirmedir. İstemci `patternId` alanını göndermezse desen temizlenir; bu, `colorId` ile aynı kuraldır.
- `colorId` yalnız `kind = Color` satırı, `patternId` yalnız `kind = Pattern` satırı olabilir.
- Güncellemede, hastada zaten kayıtlı olan pasif renk, desen ya da ırk değişmeden kalabilir. Yeni seçim aktif olmalıdır.

`GET /pets/{id}` yanıtına `patternId` ve `patternName` eklendi. Liste ve okuma modelinde desen yok; liste rengi gösterir.

| Kod | HTTP | Durum |
|---|---|---|
| `Pets.ColorNotFound` | 400 | Renk yok ya da pasif |
| `Pets.ColorIsPattern` | 400 | `colorId` bir desen satırı |
| `Pets.PatternNotFound` | 400 | Desen yok ya da pasif |
| `Pets.PatternIsColor` | 400 | `patternId` bir renk satırı |

**Veri taşıma (migration):**
- Rengi Benekli, Çizgili, Alacalı, Kırçıllı ya da Üç Renkli olan hastalarda bu değer desen alanına taşınır ve renk alanı boşalır.
- Kırçıllı-Beyaz olanlar Beyaz renk ve Kaplan Çizgili desen olur.
- Kahverengi-Kırçıllı olanlar Kahverengi renk ve Kaplan Çizgili desen olur.

**Seed:**
- Kedi ırkı Tekir, Sarman ya da Smokin olan hastaların ırkı `Ev Kedisi (Kısa Tüylü)` olur. Renk ve desen alanları boşsa şöyle doldurulur:
  - Tekir: tekir deseni
  - Sarman: kızıl renk ve tekir deseni
  - Smokin: siyah-beyaz renk
- Hekimin girdiği renk ve desen korunur.

## 2) Aşı sınıfı

`VaccineDefinitionDto` yanıtına `category` eklendi:
- `"Core"`: çekirdek
- `"NonCore"`: çekirdek dışı
- `"NotRecommended"`: önerilmeyen
- `"ParasiteControl"`: antiparaziter uygulama
- `null`: sınıf yok

`isCore` alanının anlamı değişmedi: **sistem satırı kilididir**, klinik sınıf değildir. Create ve update komutları isteğe bağlı `category` alır; değer string verilir, ör. `"NonCore"`.

**Sistem kataloğu (seed):**
- **Sınıf:** Kedi, köpek ve tavşan aşılarında sınıf dolu. Çiftlik ve kanatlı aşılarında boş, çünkü bu aşıların takvimini devlet programı belirler.
- **Türe göre ayrılan satırlar:** Karma ve Karma + Kuduz türe göre ayrıldı. Yeni kodlar `MIXED_DOG`, `MIXED_CAT`, `MIXED_RABIES_DOG` ve `MIXED_RABIES_CAT`; eski `MIXED` ve `MIXED_RABIES` pasif.
- **Bruselloz:** `BRUCELLOSIS_S19` (sığır) ve `BRUCELLOSIS_REV1` (koyun-keçi) olarak ikiye bölündü; eski `BRUCELLOSIS` pasif.
- **Pasifleşenler:**
  - `FIV`: Türkiye'de ruhsatlı ürün yok; AB'de ruhsat almadı.
  - `FIP`: Türkiye'de ruhsatlı ürün yok; WSAVA önermez.
- **Doldurulan alanlar:** Tür bağı (`speciesId`) ve varsayılan aralık (`defaultNextDueDays`) dolduruldu:
  - Kuduz: 365 gün (Kuduz Yönetmeliği)
  - Yavru serisi: 21 gün
  - İç parazit: 90 gün (ESCCAP)
- **Yeni tanımlar:**
  - Kedi Klamidyozu
  - Güvercin PPMV-1
  - At Herpesvirüsü
  - Sığır: IBR, BVD, Buzağı İshali, Theileriozis
  - Pastörelloz, Klostridyal hastalıklar, Mavi Dil, Bulaşıcı Agalaksi, Ektima, Enzootik Abort
  - Kanatlı: Marek, Gumboro, Enfeksiyöz Bronşit, Kanatlı Çiçeği, ILT
- **Doldurma kuralı:** Seed sınıf, tür ve aralığı yalnız boşsa doldurur. Adı yalnız eski sistem adı duruyorsa değiştirir.

**Başlangıç takvimi:** Aşı takvimi başlangıç şablonunda karma, türe özgü koda geçti; seri 8, 12 ve 16. haftalarda yapılır. Köpek koronavirüs çıkarıldı (WSAVA: önerilmez). Bkz. `VACCINATION_SCHEDULE_API_CONTRACT.md` §6.

## 3) Klinik sözlük ve şablon (seed)

**Klinik terimler:**
- Sistem terimleri seed'le yeniden adlandırılır ya da pasifleşir; silinmez.
- "Genel kontrol" şikâyet değil ziyaret nedeni olduğu için pasif.
- 5 şikâyet ve 2 tanı adı düzeltildi.

**Şablonlar:**
- "Aşı muayenesi"nin adı "Sağlık kontrolü ve aşı" oldu.
- Üç sistem şablonuna VKS, kas kondisyonu, hidrasyon ve ağrı başlıkları eklendi. Değişiklik yalnız metin hâlâ eski sürümle aynıysa uygulanır.
- 9 yeni şablon eklendi.

## 4) Yayın sırası

1. `DbMigrator migrate`: kolonları ekler, renk ve desen verisini taşır.
2. `DbMigrator seed`: tür, ırk, aşı, terim ve şablon katalogunu günceller, ırk yerine kürk adı taşıyan kedileri dönüştürür.
3. `DbMigrator backfill-pet-projections`: okuma modelindeki ırk ve renk adlarını yeniler. Bu adım `feature/katalog-literatur`'daki ad düzeltmeleri için de gereklidir.
