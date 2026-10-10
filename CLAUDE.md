# Vetinity — ajan kuralları

- Türkçe yanıt ver. Raporlar kısa olsun: ne yapıldı, commit hash'leri, test sonuçları, doğrulanmayanlar, açık sorular.
- Doğrulamadığın şeyi doğrulanmış gibi söyleme. Gerçek test/build çıktısını raporla.
- Önce `WORKFLOW.md` ve `roadmap/AJAN-KUYRUGU.md` oku (ürün deposu: C:\Vetinity\vetinity-product). Backend sözleşmesi tek doğruluk kaynağıdır; önce backend, sonra frontend.
- Feature branch'te çalış. Dosyaları tek tek ekle (`git add -A` yok), ayrı ve anlamlı commit'ler at. Başkasına ait commit dışı değişikliklere dokunma.
- Kullanıcı onayı olmadan YAPMA: push, main'e merge, migration'ı paylaşılan/üretim veritabanına uygulama, deploy, ADR gerektiren ürün kararı. Ürün kararı çıkarsa dur ve sor.
- Kod kalitesi: SOLID, Clean Architecture, mevcut desen ve mimariye uyum, gereksiz soyutlama yok. Kural tek yerde yaşasın. Çevredeki kodun isim, yorum yoğunluğu ve stiline uy.
- Gizli bilgileri (user-secrets, parola, token, bağlantı dizesi) çıktıda maskele; sohbete ve commit'e yazma.
- Windows PowerShell 5.1: `&&` yok, komutları ayrı satırlarda ver.
