$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

$bootTime = '2026-05-02 02:05'

Write-Host "=== SystemEvents (Loop 80 boot $bootTime UTC sonrasi) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT EventType, COUNT(*) AS Cnt FROM SystemEvents WHERE EventType IN ('SignalEmitted','SignalSkipped','OrderPlaced','OrderFilled','RiskAlert','PositionOpened','PositionClosed') AND OccurredAt > '$bootTime' GROUP BY EventType ORDER BY EventType"
$r = $cmd.ExecuteReader()
$emitCount = 0
$skipCount = 0
$riskCount = 0
$placedCount = 0
$filledCount = 0
$openedCount = 0
$closedCount = 0
while ($r.Read()) {
    $type = $r['EventType']
    $cnt = [int]$r['Cnt']
    Write-Host ('  ' + $type + ': ' + $cnt)
    if ($type -eq 'SignalEmitted') { $emitCount = $cnt }
    if ($type -eq 'SignalSkipped') { $skipCount = $cnt }
    if ($type -eq 'RiskAlert') { $riskCount = $cnt }
    if ($type -eq 'OrderPlaced') { $placedCount = $cnt }
    if ($type -eq 'OrderFilled') { $filledCount = $cnt }
    if ($type -eq 'PositionOpened') { $openedCount = $cnt }
    if ($type -eq 'PositionClosed') { $closedCount = $cnt }
}
$r.Close()

Write-Host ""
Write-Host "=== Realized PnL (Loop 80 sonrasi kapatilan) ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT ISNULL(SUM(RealizedPnl), 0) AS Total, COUNT(*) AS Cnt FROM Positions WHERE ClosedAt > '$bootTime'"
$r2 = $cmd2.ExecuteReader()
$realized = 0
$closedPosCount = 0
while ($r2.Read()) {
    $realized = [decimal]$r2['Total']
    $closedPosCount = [int]$r2['Cnt']
    Write-Host ('  Realized: $' + $realized + ' / Closed Position: ' + $closedPosCount)
}
$r2.Close()

Write-Host ""
Write-Host "=== Open Positions (Status 1=Open, 2=Closing) ==="
$cmd3 = $conn.CreateCommand()
$cmd3.CommandText = "SELECT Id, Symbol, OpenedAt, AverageEntryPrice, Quantity, MarkPrice, UnrealizedPnl, DATEDIFF(MINUTE, OpenedAt, GETUTCDATE()) AS HoldMin, Status FROM Positions WHERE OpenedAt > '$bootTime' AND Status IN (1,2) ORDER BY OpenedAt"
$r3 = $cmd3.ExecuteReader()
$openCount = 0
while ($r3.Read()) {
    $sym = $r3['Symbol']
    $hold = $r3['HoldMin']
    $qty = $r3['Quantity']
    $entry = $r3['AverageEntryPrice']
    $mark = if ($r3['MarkPrice'] -ne [DBNull]::Value) { $r3['MarkPrice'] } else { 'N/A' }
    $upnl = if ($r3['UnrealizedPnl'] -ne [DBNull]::Value) { $r3['UnrealizedPnl'] } else { 'N/A' }
    Write-Host ("  $sym Id=" + $r3['Id'] + " Status=" + $r3['Status'] + " Hold=${hold}min Qty=$qty Entry=$entry Mark=$mark UPnl=$upnl")
    $openCount++
}
$r3.Close()
Write-Host "Open Count: $openCount"

Write-Host ""
Write-Host "=== Son 15 SystemEvents (no Warmup/WsState) ==="
$cmd4 = $conn.CreateCommand()
$cmd4.CommandText = "SELECT TOP 15 EventType, Severity, OccurredAt, LEFT(PayloadJson, 100) AS Payload FROM SystemEvents WHERE OccurredAt > '$bootTime' AND EventType NOT IN ('WarmupCompleted','WsStateChanged','order.placed','PaperFillExecuted','PriceQuote','LiquidityProbe') ORDER BY OccurredAt DESC"
$r4 = $cmd4.ExecuteReader()
while ($r4.Read()) {
    $payload = if ($r4['Payload'] -ne [DBNull]::Value) { $r4['Payload'] } else { '-' }
    Write-Host ('  [' + $r4['OccurredAt'] + '] ' + $r4['EventType'] + ' | ' + $payload)
}
$r4.Close()

Write-Host ""
Write-Host "=== Strategy Active Check (KMS params) ==="
$cmd5 = $conn.CreateCommand()
$cmd5.CommandText = "SELECT Name, Status, LEFT(ParametersJson, 100) AS Params FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r5 = $cmd5.ExecuteReader()
while ($r5.Read()) {
    Write-Host ('  ' + $r5['Name'] + ' Status=' + $r5['Status'])
}
$r5.Close()

Write-Host ""
Write-Host "=== KARAR OZETI ==="
Write-Host "SignalEmitted: $emitCount"
Write-Host "SignalSkipped: $skipCount"
Write-Host "RiskAlert: $riskCount"
Write-Host "OrderPlaced: $placedCount"
Write-Host "OrderFilled: $filledCount"
Write-Host "PositionOpened: $openedCount"
Write-Host "PositionClosed: $closedCount"
Write-Host "Realized: `$$realized"
Write-Host "OpenPos: $openCount"

$conn.Close()
