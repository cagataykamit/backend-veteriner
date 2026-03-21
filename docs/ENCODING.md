# Karakter seti ve Türkçe metin (UTF-8)

## Kural

- Tüm kaynak dosyalar (.cs, .json, .md vb.) **UTF-8** ile kaydedilir.
- Türkçe açıklama, hata mesajı ve seed metinleri doğrudan UTF-8 Türkçe karakter (ı, ş, ğ, ü, ö, ç, İ) ile yazılır.

## Neden

- Farklı encoding (Windows-1254, Latin-1 vb.) ile kaydedilen dosyalar, başka ortamlarda veya CI’da bozulur (Tanılama → Tanýlama, görüntüleme → grntleme).
- Veritabanında Permission.Description / Group ve diğer metin alanları **nvarchar** (Unicode) olduğu için UTF-8 ile yazılan metinler doğru saklanır.

## Yapılacaklar

1. **IDE / Editor:** Varsayılan dosya encoding’i UTF-8 yapın (VS: “Save with Encoding” → UTF-8).
2. **Yeni dosya:** Türkçe metin eklerken doğrudan Türkçe karakter kullanın; ASCII fallback (goruntuleme, olusturma) kullanmayın.
3. **Seed:** PermissionCatalog ve diğer seed metinleri UTF-8 Türkçe ile güncellenmiştir; seed tekrar çalıştığında veritabanındaki açıklamalar düzelir.

## Kontrol

- `.editorconfig` kök dizinde `charset = utf-8` tanımlı; editörler bu ayarı uygular.
- Build ve test ortamında dil/region ayarı Türkçe olmasa bile UTF-8 kullanıldığı için metinler bozulmaz.
