# Güvenlik

## İndirdiğin dosyayı doğrula

Resmî sürümler yalnızca bu deponun [Releases](https://github.com/aytcode/YH-PCReviewer/releases) sayfasından dağıtılır
ve GitHub Actions tarafından bu depodaki koddan derlenir. Başka bir siteden indirdiysen kullanma.

```bash
gh attestation verify YHPCReviewer-win-x64.exe --repo aytcode/YH-PCReviewer
```

Bu komut, dosyanın bu deponun sürüm iş akışı tarafından hangi commit'ten üretildiğini GitHub'ın imzasıyla doğrular.
Alternatif olarak `Get-FileHash` çıktısını sürümdeki `SHA256SUMS.txt` ile karşılaştırabilirsin.

## Uygulama neleri değiştirebilir?

Analiz yalnızca okur. Bir şeyi değiştiren tek yer, kullanıcının bastığı **hızlı düzeltme** düğmeleridir:

| Düzeltme | Yaptığı | Onay |
|---|---|---|
| DNS önbelleğini temizle | `ipconfig /flushdns` | İster |
| Geçici dosyaları temizle | `%TEMP%` içindeki bir günden eski dosyaları siler; kullanımdakileri atlar | İster |
| Diğerleri | Yalnızca bir Windows ayar sayfasını, aracı ya da üreticinin sürücü sayfasını açar | Gerekmez |

Hiçbiri yönetici izni istemez.

## Açık bildirme

Bir güvenlik açığı bulduysan lütfen herkese açık issue açma.
GitHub'daki **Security → Report a vulnerability** (özel bildirim) özelliğini kullan.
Bildirimlere birkaç gün içinde yanıt verilmesi hedeflenir.
