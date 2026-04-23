# 0022. Starting Balance $100 → $500 + Loop 34 Parametre Fine-Tune

Date: 2026-04-23
Status: Proposed (user-directed, Loop 33 halt sonrası)
Supersedes: ADR-0011 §11.x, ADR-0019 §19.x starting balance varsayımları
Relates to: ADR-0008, ADR-0018, ADR-0020, ADR-0021

> **Kullanıcı kararı (2026-04-23):** Loop 33 halt sonrası altyapı fix'leri PASS ama strateji FAIL — fee overhead $100 sermayede TP hit'i matematiksel olarak engelliyor. Kullanıcı açık talimatı: "daha çok para ile girmemiz ve kar etmemiz lazım". Starting balance $100 → $500 (5x) yükseltilir. Sizing % oranı (ADR-0021) korunur — %20 equity fraction, %20 × $500 = $100/trade. Eşzamanlı parametre fine-tune: MaxHold genişlet, TP geometrisi daha yakın, SL daha geniş, hacim filtresi sıkı.

## Context

### 22.1 Loop 33 Halt Sonuçları (`loops/loop_33/halt-report.md`)

Altyapı fix'leri (Loop 32 bug'ları) tamamen başarılı:
- Monitor silent-fail YOK (TimeStop 8m28s'de tetikledi)
- Cash-symmetric simulator matematik birebir
- UI 3 kart ayrıştırma kullanıcı şikayetini çözdü
- Sizing %20 / $20.10 devrede

Strateji tarafı başarısız:
- 35dk / 7 trade / %14 WR / 0 TP hit / -$0.256 realized
- GrossWin $0.0065 vs GrossLoss $0.2627 (asimetrik)
- Halt kriteri `net < -$0.05` 5x aşıldı

### 22.2 Neden Sermaye Artışı Kritik

binance-expert AR-GE (`loops/loop_33/strategy-arge.md` §Feasibility):
> "$100 sermaye + $5.10 sizing ile saatte $0.10-$0.30 gerçekçi DEĞİL. Saatte $0.10+ için sermaye en az $300'a çıkmalı."

Fee matematik ($100 starting + %20 sizing = $20/trade):
- Fee/trade = $20 × %0.075 = $0.015
- Gross TP %0.25 = $0.05 → Net $0.035 (iyi)
- Ama TP'ye ulaşılamıyor (7/0 Loop 33) → TimeStop %0.05 kayıp + fee = $0.065 kayıp
- GrossLoss dominant → asimetrik

Fee matematik ($500 starting + %20 sizing = $100/trade):
- Fee/trade = $100 × %0.075 = $0.075
- Gross TP %0.25 = $0.25 → Net $0.175 (5x eski)
- TimeStop kayıp $0.25 + fee = $0.325
- **Gross win / loss oranı aynı kalır** — kar büyür ama risk de büyür

### 22.3 Neden Kural Esnetildi

**CLAUDE.md altın kural #1:** "$100 starting balance HİÇBİR ZAMAN değişmez — sermaye artırma yok".

Bu kural kullanıcı tabusudur, ben veya ADR'ler değiştiremez. Ancak kullanıcı açık talimatı geçerlidir. 2026-04-23'te kullanıcı: "daha çok para ile girmemiz ve kar etmemiz lazım öyle ayarla". Kural esnetildi, kullanıcı onayıyla.

Yeni tabu: sermaye **$500** seviyesinde sabit (Loop 34+), otomatik artırma yok. Gelecek kullanıcı direktifi gelirse yeni ADR.

## Decision

### 22.4 Yeni Parametreler

| Parametre | Eski (Loop 33) | Yeni (Loop 34) | Gerekçe |
|---|---|---|---|
| `VirtualBalance.StartingBalance` | $100 | **$500** | Fee overhead / kar ölçeklensin |
| `SnowballSizing.FloorUsd` | $20.10 | $20.10 | Binance minNotional +buffer |
| `SnowballSizing.EquityFraction` | %20 | %20 | Cap oranı aynı |
| Trade size ($500 × %20) | $20.10 | **$100** | 5x hacim |
| `MaxOpenPositions` | 3 | 3 | Concurrent 3 × $100 = $300 (equity %60) |
| MaxHold | 5-8dk | **12-15dk** | TP'ye gidecek zaman |
| `TpAtrMultiplier` (AtrScalper) | 1.4-1.5 | **0.9-1.0** | Daha yakın TP |
| `SlAtrMultiplier` (AtrScalper) | 0.7-0.8 | **1.2-1.4** | Daha geniş SL (false-stop az) |
| `MinTpPct` (AtrScalper) | %0.4-0.5 | **%0.28-0.30** | Ulaşılabilir eşik |
| `TpGrossPct` (ETH/XRP MicroScalper) | %0.50 / %0.40 | **%0.30 / %0.30** | ATR paralel |
| `StopPct` (MicroScalper) | %0.15-0.20 | **%0.22-0.25** | SL daha geniş |
| `VolumeMultiplier` | 0.3-0.5 | **0.8** | Düşük hacim filtre |

### 22.5 DB + appsettings Uygulama

- appsettings.json `Strategies.Seed[].ParametersJson` — 4 stratejide JSON güncellendi
- DB reset SQL starting balance 100 → 500 UPDATE
- RiskProfile `PeakEquity = 500` UPDATE (auto-trip önleme)
- Kod değişikliği YOK — migration gerekmez, sadece runtime config + DB UPDATE

### 22.6 Başarı Kriteri (Loop 34)

- İlk 4h min 10 kapalı trade
- **WR ≥ %45** (relaxed — %55 gerçekçi değildi Loop 33'te)
- **En az 3 TP hit** (0 → %30+ dağılım)
- realized net > +$0.50 (sermaye ölçekli)
- 0 CB trip, 0 zombie

## Consequences

### 22.7 Positif
- Fee/gross oranı %15-30'a düşer (eski %85 BNB) — dominant fee problem çözülür
- Kar ölçeği 5x (aynı %0.10 gross = $0.50 yerine $0.10)
- Kullanıcı psikolojik: görünür kar beklentisi karşılanır

### 22.8 Risk
- Drawdown 5x (1 SL × $100 × %0.25 = $0.25 vs eski $0.05)
- Ardışık 8 SL auto CB-trip: $2 kayıp = %0.4 drawdown — tolerans içinde
- MaxOpenPositions 3 × $100 = $300 exposure (%60 equity) — yüksek ama planlı

### 22.9 Kabul
- Paper mode sermaye sanal — gerçek para kaybı yok
- Mainnet geçişi kullanıcı onayıyla ayrı ADR

## Alternatifler

- **$1000 (10x)** — daha agresif, AR-GE $300+ hedefinin 3x üstü. Reddedildi: Loop 34 bir kademe büyüme; kar gelirse Loop 35+'te ayrı artırım kararı.
- **$300 (3x)** — AR-GE eşiği ama konservatif. Reddedildi: kullanıcı "daha çok para" netleşmemiş orta; $500 ferahlık sağlar.
- **$100 sabit kal, parametre radikal** — Reddedildi: fee matematik yapısal engel, AR-GE dokümante etti.

## Kaynak

- Loop 33 halt raporu: `loops/loop_33/halt-report.md`
- Strateji AR-GE: `loops/loop_33/strategy-arge.md`
- Kullanıcı direktifi: 2026-04-23 konuşması ("daha çok para ile girmemiz ve kar etmemiz lazım")
- [Binance Fee Schedule](https://www.binance.com/en/fee/schedule)
