namespace BinanceBot.Application.Abstractions.Exchange;

/// <summary>
/// Loop 92 — Futures pozisyonu margin tipi (ADR-0025). ISOLATED tavsiye edilir
/// (pozisyon başına bağımsız margin, kayıp pozisyon ile sınırlı). CROSSED
/// hesabın tüm marjini paylaşır — bot için yüksek risk.
/// </summary>
public enum MarginType
{
    Isolated = 1,
    Crossed = 2,
}
