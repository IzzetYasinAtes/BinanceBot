$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

Write-Host "=== Zombi Position'lar icin PositionClosed event'lerinden RealizedPnl cek ==="

# PositionClosed event payload'larından positionId + realizedPnl extract
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT PayloadJson FROM SystemEvents WHERE EventType = 'PositionClosed' AND OccurredAt > '2026-05-01 04:07' ORDER BY OccurredAt"
$r = $cmd.ExecuteReader()
$closedMap = @{}
while ($r.Read()) {
    $payload = $r['PayloadJson']
    # Parse JSON
    try {
        $obj = $payload | ConvertFrom-Json
        $posId = [int]$obj.details.positionId
        $pnl = [decimal]$obj.details.realizedPnl
        $closedMap[$posId] = $pnl
        Write-Host "  Event: positionId=$posId pnl=`$$pnl"
    } catch {
        Write-Host "  Parse error: $($_.Exception.Message)"
    }
}
$r.Close()

Write-Host ""
Write-Host "=== Zombi Position'lar (Status=2) ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Id, Symbol, AverageEntryPrice, MarkPrice, Quantity FROM Positions WHERE Status = 2 AND OpenedAt > '2026-05-01 04:07'"
$r2 = $cmd2.ExecuteReader()
$zombies = @()
while ($r2.Read()) {
    $zombies += [PSCustomObject]@{
        Id = [int]$r2['Id']
        Symbol = $r2['Symbol']
        Entry = [decimal]$r2['AverageEntryPrice']
        Mark = [decimal]$r2['MarkPrice']
        Qty = [decimal]$r2['Quantity']
    }
}
$r2.Close()

$totalRestore = 0
foreach ($z in $zombies) {
    if ($closedMap.ContainsKey($z.Id)) {
        $eventPnl = $closedMap[$z.Id]
        $exitPrice = $z.Mark  # mark fiyat exit reference
        $exitCommission = ($exitPrice * $z.Qty) * 0.00075

        $cmdU = $conn.CreateCommand()
        $cmdU.CommandText = "UPDATE Positions SET Status = 3, ClosedAt = SYSUTCDATETIME(), ExitPrice = @e, RealizedPnl = @pnl, ExitCommission = @ec, UnrealizedPnl = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id"
        $cmdU.Parameters.AddWithValue('@e', $exitPrice) | Out-Null
        $cmdU.Parameters.AddWithValue('@pnl', $eventPnl) | Out-Null
        $cmdU.Parameters.AddWithValue('@ec', $exitCommission) | Out-Null
        $cmdU.Parameters.AddWithValue('@id', $z.Id) | Out-Null
        $rows = $cmdU.ExecuteNonQuery()

        Write-Host ("  RESTORED $($z.Symbol) Id=$($z.Id): event PnL=`$$eventPnl ($rows row)")
        $totalRestore += $eventPnl
    } else {
        Write-Host ("  NO EVENT for $($z.Symbol) Id=$($z.Id) — leaving open")
    }
}

Write-Host ""
Write-Host "Total restored: `$$([math]::Round($totalRestore, 4))"

Write-Host ""
Write-Host "=== Verify ==="
$cmdV = $conn.CreateCommand()
$cmdV.CommandText = "SELECT Status, COUNT(*) AS Cnt, ISNULL(SUM(RealizedPnl), 0) AS Total FROM Positions WHERE OpenedAt > '2026-05-01 04:07' GROUP BY Status"
$rV = $cmdV.ExecuteReader()
while ($rV.Read()) {
    Write-Host ("  Status=$($rV['Status']) Count=$($rV['Cnt']) RealizedSum=`$$($rV['Total'])")
}
$rV.Close()

$conn.Close()
