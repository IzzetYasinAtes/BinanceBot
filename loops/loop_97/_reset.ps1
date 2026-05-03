$c = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$c.Open()
$cmd = $c.CreateCommand()

$cmd.CommandText = 'UPDATE RiskProfiles SET ConsecutiveLosses = 0, CircuitBreakerStatus = 1, RealizedPnl24h = 0, RealizedPnlAllTime = 0, CurrentDrawdownPct = 0, PeakEquity = 500.0'
$n = $cmd.ExecuteNonQuery()
Write-Host "RiskProfile reset: $n"

$cmd.CommandText = "UPDATE Strategies SET Status = 1, ParametersJson = JSON_MODIFY(ParametersJson, '`$.RequiredScore', 2) WHERE Type = 3"
$n = $cmd.ExecuteNonQuery()
Write-Host "Strategies activate + RS=2: $n"

$cmd.CommandText = 'DELETE FROM OrderFills'
$n1 = $cmd.ExecuteNonQuery()
$cmd.CommandText = 'DELETE FROM Orders'
$n2 = $cmd.ExecuteNonQuery()
$cmd.CommandText = 'DELETE FROM Positions'
$n3 = $cmd.ExecuteNonQuery()
$cmd.CommandText = 'DELETE FROM StrategySignals'
$n4 = $cmd.ExecuteNonQuery()
$cmd.CommandText = 'DELETE FROM SystemEvents'
$n5 = $cmd.ExecuteNonQuery()
Write-Host "Cleaned: Fills=$n1 Orders=$n2 Pos=$n3 Signals=$n4 Events=$n5"

$cmd.CommandText = 'UPDATE VirtualBalances SET WalletBalance = 500.0, AllocatedMargin = 0, UnrealizedPnl = 0, Equity = 500.0 WHERE Mode = 1'
$n = $cmd.ExecuteNonQuery()
Write-Host "VirtualBalance reset: $n"

Write-Host ''
Write-Host '=== AFTER ==='
$cmd.CommandText = 'SELECT TOP 1 ConsecutiveLosses, CircuitBreakerStatus, RealizedPnl24h FROM RiskProfiles'
$r = $cmd.ExecuteReader()
$r.Read()
Write-Host "Counter=$($r['ConsecutiveLosses']) CB=$($r['CircuitBreakerStatus']) Pnl24h=$($r['RealizedPnl24h'])"
$r.Close()

$cmd.CommandText = "SELECT Id, Status, JSON_VALUE(ParametersJson, '`$.RequiredScore') AS RS FROM Strategies"
$r = $cmd.ExecuteReader()
while ($r.Read()) {
    Write-Host "Strategy$($r['Id']) Status=$($r['Status']) RS=$($r['RS'])"
}
$r.Close()
$c.Close()
