# Loop 2 Özeti

## Süre
- Başlangıç: 2026-04-17 20:59 UTC → Bitiş: 2026-04-18 01:05 UTC (4h06m)

## Sonuç
- **Paper: $100.00 → $98.93 (-%1.07 net)**
- Peak: $178.70 (t+90dk) → Trough: $98.93 (t+240dk)
- **Peak-to-trough drawdown: -%44.7** (büyük volatilite)
- Toplam sinyal: ~408 / Paper fill: bilinmiyor (history endpoint 500) / Win rate: bilinmiyor
- Circuit breaker: Healthy (tetiklemedi — gerçekte tetiklemeliydi, broken)

## Strateji Performansı
| Strateji | Sinyal | Fill | PnL | WR | Karar |
|---|---|---|---|---|---|
| BTC-Trend-Fast | ~60 | ? | ? | ? | infra fix sonrası tekrar değerlendir |
| BNB-MeanRev | ~280 (dominant) | ? | ? | ? | sinyal flood — eşik daraltılmalı |
| XRP-Grid | ~az | ? | ? | ? | düşük aktivite — config gözden geçir |

## Karar: MUTATE (kullanıcı backlog'una odaklan, strateji algoritması değişikliği YOK)

Loop 2'nin -%44.7 drawdown'ı strateji kalitesinden değil, kullanıcının zaten tespit ettiği infra/UX hatalarından kaynaklanıyor:
- **Madde #3** (adet hesaplama yanlış): pozisyon sizing patlamasını açıklıyor
- **Madde #4** (uzun açık pozisyon, az kapanış): kar realize edilmiyor
- **Madde #7** (risk tablosu çalışmıyor): drawdown takibi koptu, CB tetiklenmedi
- **Madde #8** (risk %1 / max %10): %1 risk ama equity büyük dalgalanma → sizing bug

## Bulunan Yeni Hatalar (Loop 3 backlog'a eklendi)
- **#10:** Backfill sonrası 11 retro-sinyal patlaması (geçmiş bar'lar canlı emit)
- **#11:** /api/orders/history ve /api/strategies/signals/latest 500 hatası (ValidationBehavior `.Single()` boş seq)

## Bir Sonraki Loop İçin Plan (Loop 3)
**Kullanıcı talimatı:** DB silmeden ÖNCE 9 madde + 2 yeni bug = 11 madde için tek tek plan yap, sonra uygula, sonra Loop 3 normal akışı.

Sıra:
1. Loop 3 boot: API durdur (DB DOKUNMA)
2. Mevcut DB üzerinden tanı (her madde için endpoint çağrısı + DB sorgusu + frontend okuma)
3. `loop_3/plan.md` — her madde: bulgu + çözüm + agent + etki
4. Plan'a göre kod değişikliği (binance-expert? gerek yok, yerel iş; architect ADR'ler; backend-dev; frontend-dev; reviewer)
5. SONRA DB drop + API restart + Loop 3 normal 4h cycle

## Commit
- Hash: (commit sonrası eklenecek)
- Push: main (artık autonomous)
- Dosyalar: loop_2/{snapshot.json, summary.md, health-t*.jsonl, snap-*.json}, loop_3_backlog.md (#10 + #11 eklendi)
