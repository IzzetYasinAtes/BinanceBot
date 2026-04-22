-- Loop 33 Boot — DB Reset (Paper mode)
-- Çalıştırma koşulları:
--   1. BinanceBot.Api process DURDURULMUŞ olmalı (migration sonrası + seed öncesi window)
--   2. ADR-0020 migration (20260422191320_Loop33AdrZeroZeroTwentyFeeAware) uygulanmış olmalı
--   3. appsettings.json güncel (Loop 33 stratejisi + sizing ADR-0021 + MaxOpenPositions=3)
--
-- Reset scope: Paper mode (Mode=1). Testnet/Mainnet dokunulmaz.
-- MCP servers ETKİLENMEZ. Instruments/Klines/BookTickers korunur (warmup için).

BEGIN TRANSACTION;

-- 1. Order fills + orders (FK chain)
DELETE FROM OrderFills;
DELETE FROM Orders WHERE Mode = 1;

-- 2. Positions (3 zombie dahil tüm Paper pozisyonları)
DELETE FROM Positions WHERE Mode = 1;

-- 3. Strategy signals (tüm modlar — table mode-agnostic)
DELETE FROM StrategySignals;

-- 4. System events (log temizle)
DELETE FROM SystemEvents;

-- 5. Strategies — DELETE yerine UPDATE (seed'ten dolduruluyor, API restart'ta appsettings senkron eder)
-- Status=3 (Active) reset, UpdatedAt güncel
UPDATE Strategies
  SET Status = 3,
      UpdatedAt = SYSUTCDATETIME(),
      ActivatedAt = SYSUTCDATETIME();

-- 6. VirtualBalance reset (Paper only)
UPDATE VirtualBalances
  SET StartingBalance = 100.0000000000,
      CurrentBalance = 100.0000000000,
      Equity = 100.0000000000,
      IterationId = NEWID(),
      StartedAt = SYSUTCDATETIME(),
      LastResetAt = SYSUTCDATETIME(),
      ResetCount = ResetCount + 1,
      UpdatedAt = SYSUTCDATETIME()
  WHERE Mode = 1;

-- 7. RiskProfile reset
-- KRITIK: PeakEquity=100 reset edilmezse auto-trip olur (Loop 17 tarihsel bug)
-- CircuitBreakerStatus=1 = Normal (enum)
UPDATE RiskProfiles
  SET CircuitBreakerStatus = 1,
      CircuitBreakerTrippedAt = NULL,
      CircuitBreakerReason = NULL,
      ConsecutiveLosses = 0,
      RealizedPnl24h = 0,
      RealizedPnlAllTime = 0,
      PeakEquity = 100.0000,
      CurrentDrawdownPct = 0,
      MaxOpenPositions = 3,          -- ADR-0021: 6 → 3
      UpdatedAt = SYSUTCDATETIME();

COMMIT TRANSACTION;

-- Doğrulama sorguları (manuel)
SELECT COUNT(*) AS OrdersLeft FROM Orders WHERE Mode=1;            -- beklenen 0
SELECT COUNT(*) AS FillsLeft FROM OrderFills;                       -- beklenen 0
SELECT COUNT(*) AS PositionsLeft FROM Positions WHERE Mode=1;       -- beklenen 0
SELECT COUNT(*) AS SignalsLeft FROM StrategySignals;                -- beklenen 0
SELECT COUNT(*) AS EventsLeft FROM SystemEvents;                    -- beklenen 0
SELECT * FROM VirtualBalances WHERE Mode=1;                         -- Cash=100 Equity=100
SELECT Id, CircuitBreakerStatus, ConsecutiveLosses, PeakEquity, MaxOpenPositions FROM RiskProfiles;
SELECT Id, Name, Status, SymbolsCsv FROM Strategies;                -- Status=3 (Active)
