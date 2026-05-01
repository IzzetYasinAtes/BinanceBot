$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Manual close zombi positions (Status=2) ==="

# Get open Status=2 positions
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT Id, Symbol, AverageEntryPrice, MarkPrice, Quantity FROM Positions WHERE Status = 2 AND OpenedAt > '2026-05-01 01:54'"
$r = $cmd.ExecuteReader()
$positions = @()
while ($r.Read()) {
    $positions += [PSCustomObject]@{
        Id = $r['Id']
        Symbol = $r['Symbol']
        Entry = [decimal]$r['AverageEntryPrice']
        Mark = [decimal]$r['MarkPrice']
        Qty = [decimal]$r['Quantity']
    }
}
$r.Close()

$totalClose = 0
foreach ($p in $positions) {
    $exitPrice = $p.Mark  # use mark as exit
    $notional = $exitPrice * $p.Qty
    $exitCommission = $notional * 0.00075  # 0.075% taker BNB discount
    $entryCommission = $p.Entry * $p.Qty * 0.00075
    $realizedPnl = ($exitPrice - $p.Entry) * $p.Qty - $exitCommission - $entryCommission

    $cmdU = $conn.CreateCommand()
    $cmdU.CommandText = "UPDATE Positions SET Status = 3, ClosedAt = SYSUTCDATETIME(), ExitPrice = @e, RealizedPnl = @pnl, ExitCommission = @ec, UnrealizedPnl = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id"
    $cmdU.Parameters.AddWithValue('@e', $exitPrice) | Out-Null
    $cmdU.Parameters.AddWithValue('@pnl', $realizedPnl) | Out-Null
    $cmdU.Parameters.AddWithValue('@ec', $exitCommission) | Out-Null
    $cmdU.Parameters.AddWithValue('@id', $p.Id) | Out-Null
    $rows = $cmdU.ExecuteNonQuery()

    Write-Host ("  $($p.Symbol) Id=$($p.Id) Exit=$exitPrice PnL=`$$([math]::Round($realizedPnl, 4)) ($rows row)")
    $totalClose += $realizedPnl
}

Write-Host ""
Write-Host "Total closed PnL: `$$([math]::Round($totalClose, 4))"

Write-Host ""
Write-Host "=== Verify Realized ==="
$cmdV = $conn.CreateCommand()
$cmdV.CommandText = "SELECT ISNULL(SUM(RealizedPnl), 0) AS Total, COUNT(*) AS Cnt FROM Positions WHERE OpenedAt > '2026-05-01 01:54'"
$rV = $cmdV.ExecuteReader()
$rV.Read() | Out-Null
Write-Host ("  Total Realized: `$$($rV['Total']) / Closed Count: $($rV['Cnt'])")
$rV.Close()

$conn.Close()
