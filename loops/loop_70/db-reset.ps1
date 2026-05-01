$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

$tables = @('OrderFills', 'Orders', 'StrategySignals', 'Positions', 'SystemEvents', 'BookTickers', 'OrderBookSnapshots', 'VirtualBalances', 'BacktestTrades', 'BacktestRuns')

foreach ($t in $tables) {
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "DELETE FROM $t"
        $r = $cmd.ExecuteNonQuery()
        Write-Host "$t : $r rows deleted"
    } catch {
        Write-Host "$t : SKIP ($($_.Exception.Message))"
    }
}

# Re-seed VirtualBalance fresh $500
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "INSERT INTO VirtualBalances (Mode, Asset, Total, Available, Locked, UpdatedAt) VALUES (1, 'USDT', 500.0, 500.0, 0.0, SYSUTCDATETIME())"
try {
    $cmd2.ExecuteNonQuery() | Out-Null
    Write-Host "VirtualBalances seeded: USDT 500"
} catch {
    Write-Host "VirtualBalance seed failed: $($_.Exception.Message)"
}

$conn.Close()
Write-Host "DB reset complete"
