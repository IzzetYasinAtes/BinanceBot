# Loop 33 — Halt Raporu

**Tarih:** 2026-04-22 20:37 UTC
**Uptime:** 35 dakika (ilk trade 20:03, halt 20:37)
**Verdict:** **FAIL** — Halt kriteri `net < -$0.05` aşıldı (realized **-$0.256**)

## 1. Özet

| Metrik | Değer | Hedef | Durum |
|---|---|---|---|
| Uptime | 35 dk | 4 saat | Erken halt |
| Kapalı Trade | 7 | 10+ | Erken |
| Win Rate | %14 (1/7) | %55 | ÇOK KÖTÜ |
| Realized Net | -$0.256 | > +$0.10 | BAŞARISIZ |
| Unrealized | $0.00 | - | - |
| Total Commission | $0.227 | - | birebir doğru |
| TP Hit | 0 / 7 | ≥ %30 | ANA SORUN |
| SL Hit | 2 / 7 | ≤ %20 | TAMAM |
| TimeStop | 5 / 7 | - | Dominant |
| Monitor TimeStop Çalıştı | ✓ | ✓ | PASS |
| Cash-Symmetric Invariant | ✓ | ✓ | PASS |
| 3-Kart UI | ✓ | ✓ | PASS |

## 2. Kapanış Detayı

| Id | Sym | PnL | Dur | Reason |
|---|---|---|---|---|
| 343 | ADA | -$0.0023 | 8dk | TimeStop |
| 342 | XRP | +$0.0065 | 7dk | TimeStop (sadece kazanan) |
| 341 | ETH | -$0.0082 | 8dk | TimeStop |
| 340 | XRP | -$0.0748 | 4dk | **StopLoss** (büyük) |
| 339 | ETH | -$0.0693 | 5dk | **StopLoss** (büyük) |
| 338 | ADA | -$0.0660 | 8dk | TimeStop |
| 337 | ADA | -$0.0421 | 8dk | TimeStop |

**GrossWin $0.0065 — GrossLoss $0.2627** — kâr/zarar asimetrisi korkunç.

## 3. Root Cause — TP Geometrisi

**Hiçbir trade TP'ye ulaşamadı.** ATR-bazlı TP multiplier (1.4-1.5) ve min TP %0.4-0.5 çok uzak:
- SOL-AtrScalper: TpAtrMult 1.5, MinTpPct %0.5
- ADA-AtrScalper: TpAtrMult 1.4, MinTpPct %0.4
- ETH-MicroScalper: TpGrossPct %0.5

MaxHold 5-8dk içinde bu yüzdeler gerçekleşmedi. Sonuç:
- Fiyat lehe giderse → TimeStop ile cüzi kazanç/zarar
- Fiyat aleyhe giderse → SL hit (büyük) ya da TimeStop (orta)
- Simetrik olmayan dağılım → **gross loss >> gross win**

## 4. PASS Olan Fix'ler (Loop 33 boot değeri)

Loop 33 boot **stratejide başarısız ama altyapıda başarılı**:

1. ✅ **Monitor silent-fail bug ÇÖZÜLDÜ** — Loop 32'deki 3 zombie 26h açık kalma sorunu yok. TimeStop tam 8dk 28s'de tetikledi.
2. ✅ **Cash-symmetric simulator ÇALIŞIYOR** — her fill'de `cash_delta = side_sign * price * qty - quoteFee` matematik birebir tutar.
3. ✅ **ADR-0020 Position fee-aware** — RealizedPnl gerçekten net (fee dahil), UI tablo = API summary.
4. ✅ **ADR-0021 sizing %20** — $20.10 trade size aktif; 3 concurrent exposure cap doğru.
5. ✅ **UI 3 kart hero** — Kapalı/Açık/Toplam ayrıştırması kullanıcı şikayetini çözdü.
6. ✅ **AtrScalperVwapEma1m evaluator çalışıyor** — SOL/ADA'dan sinyal üretiyor, ATR snapshot doluyor.

## 5. Loop 34 Reform Önerisi

### Seçenek A — Parametre Fine-Tune (çabuk)
- **MaxHold 5-8dk → 12-15dk** (TP'ye gidecek daha fazla zaman)
- **TpAtrMult 1.4-1.5 → 0.8-1.0** (daha yakın TP)
- **SlAtrMult 0.7-0.8 → 1.2-1.5** (daha geniş SL, false-stop azalt)
- **MinTpPct %0.4-0.5 → %0.25-0.30** (daha ulaşılabilir TP)
- **SlopeTolerance -0.003 → -0.001** (daha sıkı trend filtre)
- **VolumeMultiplier 0.3-0.5 → 0.8** (düşük hacim sinyalleri ele)

### Seçenek B — Strateji Değişikliği (orta)
- Scalping bırak, **5m swing** (5-20 bar holding, %0.5-1 TP). Fee/gross oranı yarıya iner.
- Farklı sembol: DOGE (yüksek vol, düşük fiyat → qty granular, fee etkisi küçük)

### Seçenek C — Kabul (radikal)
- **$100 sermaye + fee %0.1 paper = break-even bile zor** — AR-GE feasibility verdict'te belirtildi: saatte $0.10+ için sermaye $300 gerek.
- Kullanıcı tabu "$100 sabit" koruyorsa: hedef "kar" yerine "geliştirme/test platformu" olarak değerlendir. Stratejiyi mainnet'e çıkarırken gerçek kar gelir — paper sadece feature validation.

## 6. Şu Anki Durum

- API durduruldu (PID 13112 kapandı)
- 1 açık SOL pozisyonu DB'de kaldı (Id 344, age ~6dk)
- DB reset + Loop 34 boot bekliyor
- Tüm altyapı fix'leri sağlam, sadece strateji paramları fine-tune gerekli

## 7. Sonraki Adım

Kullanıcı seçimi bekliyor:
- **(A) Parametre fine-tune + Loop 34 boot** (30dk içinde aktif, hızlı test)
- **(B) Strateji radikal değişim** (AR-GE re-run, backend-dev yeni evaluator)
- **(C) Paper pause, mainnet hedef değerlendir** (stratejik pivot)

Tavsiyem **(A)** — altyapı fix'leri zaten sağlam, paramları kaydırıp 10+ trade daha gözlemleyelim, sonra (B)/(C) kararı.
