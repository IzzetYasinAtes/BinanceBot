# Loop 20 t90 — "Neden hiç işlem yok" teşhisi

## Özet

**Sistem BOZUK DEĞİL. Strateji tasarımı gereği bekliyor.**

90 dakikada:
- 78 KlineClosed event işlendi
- 78 VwapEma evaluator çağrısı yapıldı
- **0 sinyal emit** edildi
- 0 Exception, 0 error, WS Streaming stabil

Tek sebep: **directionGate = false** her seferinde.

## Kanıt — EMA21(1h) için gerekli 21 saatlik pencere

| Sembol | T-21h | T-0 | Net Değişim | EMA21 yönü |
|---|---|---|---|---|
| BTCUSDT | 76167.34 | 75639.94 | **−%0.69** | ↓ aşağı |
| BNBUSDT | 633.21 | 622.68 | **−%1.66** | ↓ aşağı |
| XRPUSDT | 1.4363 | 1.4301 | **−%0.43** | ↓ aşağı |

Bu koşulda EMA21(1h) slope negatif → `directionGate=false` → long-only strateji skip.

## Son 60 dk — ekran görüntüsüyle tutarsızlık değil

UI'da "+%0.85" (BTC son 60 dk) görünmesi → kısa vadeli bounce. Bunun EMA21(1h) hesabına etkisi düşük çünkü EMA21 **21 barlık** (21 saat). Küçük pik 1-2 bar değiştirir, 21-bar ortalama yönünü döndürmek için sürekli yeşil 3-4 saat gerekir.

## Strateji hatası mı, piyasa hatası mı?

**Ne o, ne o — tasarım sonucu.** ADR-0015:
> "EMA21(1h) yukarı (direction gate) → whipsaw azaltır, false breakout önler"

binance-expert araştırması:
> "VWAP+EMA trend-following %65-70 WR, fakat trend yoksa EMA filtre elimine eder — bu kabul edilen trade-off."

Yani: **trade az ama yüksek kalite** vs. **trade çok ama fee drag'lı** arasında 1. tercih edildi.

## Sezar'ın hakkı ölçütü

- Eğer `t240` (4 saat sonu) sinyal = 0 kalırsa → strateji `fazla kısıtlayıcı`, Loop 21'de ayar gerekir.
- Eğer `t240`'a kadar en az 1-2 sinyal çıkar ve pozitif sonuç alırsa → strateji makul, piyasa koşulu konusunda 4h yetersiz data, Loop 21'de "hold" ile devam.

## Loop 21 için olası ayar önerileri (eğer 0 trade 4h kalırsa)

1. **Trend filter gevşet:** EMA21(1h) → EMA12(1h) veya EMA21(15m) → daha kısa pencere
2. **Slope eşiği:** directionGate kriteri "EMA21 > prev" değil "EMA21 slope > -%0.1" gibi gevşek
3. **Dual-timeframe:** hem 1h hem 4h EMA21 şart — veya tek biri yeterli (OR)
4. **VWAP absolute:** reclaim yerine VWAP ± %0.1 band içinde "yakın" da kabul
5. **Her sembolde ayrı direction:** BTC-only aktif, BNB/XRP pasif (performans ayrımı)

Bu öneriler Loop 20 bitene kadar **uygulanmaz** — kullanıcı onayı + ADR-0016 gerek.

## Kullanıcı bilgilendirme

Kullanıcı t90'da "neden hiç işlem yok" diye sordu → bu dosya cevap. Özet ona verildi:
- Strateji bozuk değil
- Piyasa 21h aşağı trend
- Long-only bilinçli bekliyor
- t240'a kadar gözle, ondan sonra karar
