# Loop 92 Halt — t30 KRİTİK BUG

Tarih: 2026-05-03 09:21 UTC | Boot: 08:49 UTC | Süre: ~32dk

## Özet

Bot 32dk Futures testnet'te koştu, **3 pozisyon açtı (1 Short, 2 Long)**, AMA **Position.EntryCommission ETH için $117.29 göründü** (OrderFill commission'ı doğru: $0.05). Cash hesabı 2 farklı yerden farklı sonuç verdi (VirtualBalance.Wallet=$399.99, PortfolioSummary.currentCash=$79.16). Halt edildi, Loop 93 fix gerekiyor.

## DB Snapshot (Halt Anı)

### Positions (3 açık, 0 kapalı)
| Symbol | Direction | Qty | Entry | UPnL | EntryCommission | OpenedAt |
|---|---|---|---|---|---|---|
| **BTCUSDT** | **2 (SHORT)** | 0.0013 | $78315.32 | -$0.057 | $0.0509 | 08:59:59 |
| ADAUSDT | 1 (Long) | 403 | $0.2483 | -$0.070 | $0.0124 | 09:05:00 |
| **ETHUSDT** | 1 (Long) | 0.044 | $2309.03 | -$0.037 | **$117.29 ⚠ BUG** | 09:09:59 |

**Pozitif gözlem**: Bot Short pozisyon AÇTI (BTC), Long+Short composer ÇALIŞIYOR. Pivot temel olarak başarılı.

### OrderFills (commission KAYNAĞI — doğru)
| Symbol | Side | FillPx | FillQty | Commission |
|---|---|---|---|---|
| ETHUSDT | 1 (BUY) | 2309.03 | 0.044 | **$0.0508** ✓ |
| ADAUSDT | 1 (BUY) | 0.2483 | 403 | $0.0500 ✓ |
| BTCUSDT | 2 (SELL) | 78315.32 | 0.0013 | $0.0509 ✓ |

**OrderFill commission doğru — Position.EntryCommission yanlış**. AddFill veya MarkToMarket fee accumulation bug.

### StrategySignals (6 emit / 30dk = 12/h)
| Symbol | Direction | EmittedAt |
|---|---|---|
| XRPUSDT | 1 | 09:14:59 |
| BTCUSDT | 1 | 09:14:59 |
| SOLUSDT | 1 | 09:09:59 |
| ETHUSDT | 1 | 09:09:59 |
| ADAUSDT | 1 | 09:05:00 |
| **BTCUSDT** | **2 (SHORT)** | 08:59:59 |

5 coin'den emit geldi (≥5 coin sağlandı). 1 Short emit (BTC). Frekans 12/h (hedef 30/h'a yakın değil, ama yarım saat erken). MaxOpenPositions=3 yüzünden son 3 emit pozisyon olmadı (XRP+SOL+BTC#2 skipped).

### VirtualBalance vs PortfolioSummary Çelişkisi
- **VirtualBalance.WalletBalance**: $399.99 (sadece commission düşmüş, pozisyon notional MARGIN'A alınmamış — AllocatedMargin=$0)
- **PortfolioSummary.currentCash**: $79.16 (eski Spot formülü: cash = start - notional - commission)
- **PortfolioSummary.netPnl**: -$117.53 (-23.5%) — komisyon $117.36 yüzünden

İki view farklı kaynak hesaplıyor. Reviewer'ın işaret ettiği "AllocateMarginForPosition wiring eksik" Loop 93 minor'u — boot edilince akut hale geldi.

### Orders (3 Filled)
- 3 order, hepsi Status=3 (Filled)

### RiskProfile
- Counter=0, CB=Healthy, RealizedPnl24h=$0 (0 closed trade)
- DD=0.00%

## Halt Sebebi

**Halt eşiği**: Realized PnL < -$1.50  
**Gerçek**: realizedPnl=$0 (0 closed trade) AMA **netPnl=-$117.53** (commission DB bug + PortfolioSummary view formülü Spot kalmış).

Aslında VirtualBalance.WalletBalance ekonomik gerçeği gösteriyor: $500→$399.99 = **gerçek zarar $0.06** (3 pozisyon commission). Bu halt eşiği değil. AMA:

1. Position.EntryCommission ETH'de $117 yazılmış — bu DB bug (OrderFill $0.05 ama Position $117)
2. PortfolioSummary cash hesabı eski formül kullanıyor (notional cash'ten düşülüyor, ama Futures margin'da bekler)
3. UI bu yanlış değeri gösteriyor

Bot davranışsal olarak SAĞLIKLI (5 coin emit, Short emit aktif, 3 pozisyon açık), AMA accounting layer'da 2 bug tespit edildi. Loop 93'te fix edilmeden devam = UI yanıltıcı + halt eşik yanlış tetiklenir.

## Loop 92 Net Sonuç

- ✅ Spot → Futures pivot kod-seviyesinde başarılı (14 commit, 0 build hata, 332/332 test)
- ✅ Long+Short emit aktif (1 Short emit BTC)
- ✅ 5 coin emit (BTC, ETH, XRP, SOL, ADA)
- ✅ 3 pozisyon açık (Long+Short carmix)
- ⚠ Position.EntryCommission $117 bug (ETH)
- ⚠ PortfolioSummary cash formülü Spot kalıntı
- ⚠ AllocatedMargin wiring eksik (Loop 93 minor → akut)

**Karar**: Halt + Loop 93 spec (commission bug fix + cash formülü) + restart.

## Sonraki Adım: Loop 93

`loops/loop_93/spec.md` yazılacak — backend-dev'e iki bug fix delegasyonu:
1. Position.EntryCommission $117 ETH bug — Position.AddFill veya MarkToMarketWorker fee accumulation hatası
2. GetPortfolioSummaryQuery cash formülü Futures'a uyarla (cash = WalletBalance, notional düşülmez; openPositionsValue ayrı)
3. (opsiyonel) AllocateMarginForPosition wiring (PlaceOrderCommandHandler içinde) — açık AllocatedMargin doğru görünür

Tahmin: 2-4 commit, 30-60dk backend-dev. Sonra restart + Loop 93 boot.
