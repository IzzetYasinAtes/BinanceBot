namespace BinanceBot.Domain.RiskProfiles;

public enum CircuitBreakerStatus
{
    Healthy = 1,
    Cooldown = 2,
    Tripped = 3,
}
