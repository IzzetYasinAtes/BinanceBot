using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BinanceBot.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR-0020 §20.6 + §20.10 — fee-aware paper accounting schema change.
    /// Adds <c>Positions.EntryCommission</c> + <c>Positions.ExitCommission</c>
    /// (quote USDT, decimal(18,8), NOT NULL DEFAULT 0). Best-effort backfill
    /// quote-equivalents Loop 32 legacy OrderFills into the new columns:
    ///   - BUY leg fees (base asset): quote_eq = Commission * Price.
    ///   - SELL leg fees (quote asset): quote_eq = Commission.
    /// Legacy RealizedPnl values are deliberately left gross (ADR §20.10 —
    /// PositionClosedEvent semantics at that time already propagated the gross
    /// value; rewriting it would invalidate downstream idempotent logs).
    /// </summary>
    public partial class Loop33AdrZeroZeroTwentyFeeAware : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "EntryCommission",
                table: "Positions",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExitCommission",
                table: "Positions",
                type: "decimal(18,8)",
                nullable: false,
                defaultValue: 0m);

            // ADR-0020 §20.10 — best-effort backfill for legacy rows. Positions
            // currently have no FK to Orders, so the join is done on
            // (Symbol, Mode, Side) + timestamp window. Entry fees use the BUY-side
            // orders created around OpenedAt; exit fees use the opposite-side
            // orders created around ClosedAt. BUY commission is in base asset and
            // must be quote-equivalent'd via f.Price. SELL commission is already
            // quote-denominated.
            migrationBuilder.Sql(@"
-- Backfill EntryCommission (BUY leg for Long, SELL leg for Short). Using a 60s
-- window around OpenedAt because Loop 32 OrderFilledPositionHandler writes the
-- Position inside the same SaveChangesAsync batch as the order fill.
WITH EntryFills AS (
    SELECT
        p.Id AS PositionId,
        SUM(
            CASE WHEN o.Side = 1 /* Buy */
                 THEN f.Commission * f.Price
                 ELSE f.Commission
            END
        ) AS QuoteFee
    FROM Positions p
    INNER JOIN Orders o
        ON o.Symbol = p.Symbol
       AND o.Mode = p.Mode
       AND o.Status = 3 /* Filled */
       AND ((p.Side = 1 /* Long */  AND o.Side = 1 /* Buy  */) OR
            (p.Side = 2 /* Short */ AND o.Side = 2 /* Sell */))
       AND o.CreatedAt BETWEEN DATEADD(SECOND, -60, p.OpenedAt)
                           AND DATEADD(SECOND,  60, p.OpenedAt)
    INNER JOIN OrderFills f ON f.OrderId = o.Id
    GROUP BY p.Id
)
UPDATE Positions
   SET EntryCommission = ISNULL(EntryFills.QuoteFee, 0)
  FROM Positions
  INNER JOIN EntryFills ON EntryFills.PositionId = Positions.Id;

-- Backfill ExitCommission (opposite-side fill around ClosedAt). Only for
-- positions that are actually closed; open positions keep ExitCommission = 0.
WITH ExitFills AS (
    SELECT
        p.Id AS PositionId,
        SUM(
            CASE WHEN o.Side = 1 /* Buy */
                 THEN f.Commission * f.Price
                 ELSE f.Commission
            END
        ) AS QuoteFee
    FROM Positions p
    INNER JOIN Orders o
        ON o.Symbol = p.Symbol
       AND o.Mode = p.Mode
       AND o.Status = 3 /* Filled */
       AND ((p.Side = 1 /* Long */  AND o.Side = 2 /* Sell */) OR
            (p.Side = 2 /* Short */ AND o.Side = 1 /* Buy  */))
       AND p.ClosedAt IS NOT NULL
       AND o.CreatedAt BETWEEN DATEADD(SECOND, -60, p.ClosedAt)
                           AND DATEADD(SECOND,  60, p.ClosedAt)
    INNER JOIN OrderFills f ON f.OrderId = o.Id
    WHERE p.Status = 2 /* Closed */
    GROUP BY p.Id
)
UPDATE Positions
   SET ExitCommission = ISNULL(ExitFills.QuoteFee, 0)
  FROM Positions
  INNER JOIN ExitFills ON ExitFills.PositionId = Positions.Id;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EntryCommission",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ExitCommission",
                table: "Positions");
        }
    }
}
