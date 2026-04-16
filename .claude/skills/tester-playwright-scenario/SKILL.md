---
name: tester-playwright-scenario
description: Playwright MCP ile UI senaryo yazımı ve çalıştırılması. Feature için TR açıklama + EN test adı + adım adım navigate/click/type/query/screenshot. Headed/headless mod. Fail durumunda screenshot kaydeder. tester agent'ının birincil UI testing skill'i.
---

# tester-playwright-scenario

Feature'ın beklenen user journey'ini MCP Playwright tool'ları ile tarayıcıda yürüt.

## Senaryo Şablonu

```
# Senaryo: <TR açıklama — örn. "Kullanıcı dashboard'da BTC kline'larını görebilir">
# Test adı: klines_visible_on_dashboard
# Ön koşul: backend çalışıyor (http://localhost:5000), frontend serve ediliyor (http://localhost:5500)
# Test verisi: DB'de BTCUSDT 1m kline seed edilmiş olmalı.

Adımlar:
1. navigate("http://localhost:5500")
2. waitFor(".dashboard")
3. query(".dashboard h1") → metin "Genel Durum" içeriyor mu?
4. query(".dashboard li") → en az 1 li var mı?
5. screenshot() → artifact olarak kaydet.
6. click("button[data-action='refresh']")
7. waitFor(200ms)
8. query(".dashboard li:first-child") → timestamp güncellendi mi?

Beklenen: tüm query/waitFor başarılı, screenshot mantıklı.
Başarısızlık aksiyonu: screenshot al, logu topla, 🚫 fail raporla.
```

## MCP Tool Çağrı Örneği

```
mcp__playwright__navigate(url="http://localhost:5500")
mcp__playwright__wait_for(selector=".dashboard")
mcp__playwright__snapshot()  # accessibility tree — selector keşfi için
mcp__playwright__click(selector="button[data-action='refresh']")
```

Playwright MCP accessibility tree döndürür (pixel değil) — Claude selector'ları buradan öğrenir, navigasyon token-verimli olur.

## Yaygın Senaryolar

1. **Navigasyon:** root sayfa açılıyor mu?
2. **Data render:** API çağrısı sonrası data UI'a geliyor mu?
3. **Form submit:** input doldur → submit → toast/yönlendirme?
4. **Error state:** API hata dönse UI hata mesajı gösteriyor mu?
5. **Loading state:** tıklandıktan sonra loading indikator var mı?

## Kural

- Her senaryonun **TR açıklaması + EN test adı** olsun.
- Başarısızlıkta screenshot **zorunlu** artifact — `.ai-trace/tester-screenshots/<date>_<test>.png` (gitignore).
- Flaky test varsa `tester-error-scan` ile flaky marker kaydet; 3. başarısızlıkta blocker.
- Production URL'e asla bağlanma — lokal/staging only.

## Senaryo Havuzu

`docs/test-scenarios/*.md` altında saklanabilir (her feature için MD). tester bu havuzdaki senaryoları sırayla yürütür.

## Kaynak

- https://github.com/microsoft/playwright-mcp
- https://playwright.dev/docs/writing-tests
