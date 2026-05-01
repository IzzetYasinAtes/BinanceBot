# Loop 73 — patch-zombies.ps1
# ZOMBI POSITION REPAIR + DETECTIVE
#
# BACKGROUND:
#   PositionStatus enum: Open=1, Closed=2  (DAHA FAZLA DEĞER YOK!)
#   Eski restore-zombies.ps1 yanlışlıkla Status=3 yazıyordu — bu enum-dışı bir
#   değer ve ne Open ne Closed sayıldığı için gerçek bir zombi üretiyordu.
#
# BU SCRIPT:
#   1. Status=3 (yanlış-set) row var mı? → varsa Status=2'ye geri döndür.
#   2. Status=2 olup ClosedAt IS NULL veya ExitPrice IS NULL row var mı?
#      → atomic close işleminin yarıda kaldığı tek senaryo. ClosedAt = SYSUTCDATETIME(),
#        ExitPrice = MarkPrice (NULL ise AverageEntryPrice) ile repair.
#   3. RealizedPnl ve EntryCommission/ExitCommission DOKUNULMAZ (zaten Position.Close()
#      içinde tek transaction'da set ediliyor; zarar görme ihtimali yok).
#   4. Verify SELECT — Status histogramı + ClosedAt-NULL counter.
#
# Idempotent: çalıştırılır, hiç repair yoksa "all healthy" döner.

$ErrorActionPreference = 'Stop'
$conn = New-Object System.Data.SqlClient.SqlConnection(
    'Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

try {
    # ---- STEP 1: Yanlış Status=3 set edilmiş row tespit ----
    Write-Host "=== STEP 1: Yanlış Status=3 row arama (eski restore-zombies.ps1 etkisi) ==="
    $cmdDetect3 = $conn.CreateCommand()
    $cmdDetect3.CommandText = @"
SELECT Id, Symbol, Status, ClosedAt, ExitPrice, RealizedPnl
FROM Positions
WHERE Status NOT IN (1, 2)
ORDER BY Id
"@
    $r3 = $cmdDetect3.ExecuteReader()
    $invalidStatusRows = @()
    while ($r3.Read()) {
        $invalidStatusRows += [PSCustomObject]@{
            Id = [long]$r3['Id']
            Symbol = $r3['Symbol']
            Status = [int]$r3['Status']
            ClosedAt = $r3['ClosedAt']
            ExitPrice = $r3['ExitPrice']
            RealizedPnl = $r3['RealizedPnl']
        }
    }
    $r3.Close()

    if ($invalidStatusRows.Count -gt 0) {
        Write-Host "  Bulunan invalid-Status row: $($invalidStatusRows.Count)"
        foreach ($z in $invalidStatusRows) {
            Write-Host "    Id=$($z.Id) Symbol=$($z.Symbol) Status=$($z.Status) ClosedAt=$($z.ClosedAt) Pnl=$($z.RealizedPnl)"
        }
        Write-Host "  → Status=2 (Closed) olarak normalize ediliyor (RealizedPnl korunur)..."
        $cmdFix3 = $conn.CreateCommand()
        $cmdFix3.CommandText = @"
UPDATE Positions
SET Status = 2,
    ClosedAt = ISNULL(ClosedAt, SYSUTCDATETIME()),
    ExitPrice = ISNULL(ExitPrice, AverageEntryPrice),
    UnrealizedPnl = 0,
    UpdatedAt = SYSUTCDATETIME()
WHERE Status NOT IN (1, 2)
"@
        $fixed = $cmdFix3.ExecuteNonQuery()
        Write-Host "  → $fixed row repaired."
    } else {
        Write-Host "  Bulunamadı — Status değeri her row için (1,2) içinde."
    }

    # ---- STEP 2: Status=2 olup ClosedAt IS NULL row var mı? ----
    Write-Host ""
    Write-Host "=== STEP 2: Status=Closed (=2) ama ClosedAt IS NULL veya ExitPrice IS NULL row arama ==="
    $cmdDetect2 = $conn.CreateCommand()
    $cmdDetect2.CommandText = @"
SELECT Id, Symbol, ClosedAt, ExitPrice, MarkPrice, AverageEntryPrice
FROM Positions
WHERE Status = 2
  AND (ClosedAt IS NULL OR ExitPrice IS NULL)
ORDER BY Id
"@
    $r2 = $cmdDetect2.ExecuteReader()
    $partialCloseRows = @()
    while ($r2.Read()) {
        $partialCloseRows += [PSCustomObject]@{
            Id = [long]$r2['Id']
            Symbol = $r2['Symbol']
            ClosedAt = $r2['ClosedAt']
            ExitPrice = if ($r2.IsDBNull(3)) { $null } else { [decimal]$r2['ExitPrice'] }
            MarkPrice = if ($r2.IsDBNull(4)) { $null } else { [decimal]$r2['MarkPrice'] }
            AverageEntryPrice = [decimal]$r2['AverageEntryPrice']
        }
    }
    $r2.Close()

    if ($partialCloseRows.Count -gt 0) {
        Write-Host "  Bulunan partial-close row: $($partialCloseRows.Count)"
        foreach ($p in $partialCloseRows) {
            $exitFallback = if ($p.MarkPrice -ne $null -and $p.MarkPrice -gt 0) {
                $p.MarkPrice
            } else {
                $p.AverageEntryPrice
            }
            Write-Host "    Id=$($p.Id) Symbol=$($p.Symbol) ClosedAt=$($p.ClosedAt) ExitPrice=$($p.ExitPrice) → ExitFallback=$exitFallback"

            $cmdFix2 = $conn.CreateCommand()
            $cmdFix2.CommandText = @"
UPDATE Positions
SET ClosedAt = ISNULL(ClosedAt, SYSUTCDATETIME()),
    ExitPrice = ISNULL(ExitPrice, @e),
    UnrealizedPnl = 0,
    UpdatedAt = SYSUTCDATETIME()
WHERE Id = @id AND Status = 2
"@
            $cmdFix2.Parameters.AddWithValue('@e', $exitFallback) | Out-Null
            $cmdFix2.Parameters.AddWithValue('@id', $p.Id) | Out-Null
            $r = $cmdFix2.ExecuteNonQuery()
            Write-Host "      → $r row repaired"
        }
    } else {
        Write-Host "  Bulunamadı — her Closed row için ClosedAt + ExitPrice dolu."
    }

    # ---- STEP 3: VERIFY SELECT ----
    Write-Host ""
    Write-Host "=== STEP 3: Verify ==="
    $cmdVerify = $conn.CreateCommand()
    $cmdVerify.CommandText = @"
SELECT
    Status,
    COUNT(*) AS Cnt,
    SUM(CASE WHEN ClosedAt IS NULL THEN 1 ELSE 0 END) AS NullClosedAtCnt,
    SUM(CASE WHEN ExitPrice IS NULL THEN 1 ELSE 0 END) AS NullExitPriceCnt,
    ISNULL(SUM(RealizedPnl), 0) AS RealizedSum
FROM Positions
GROUP BY Status
ORDER BY Status
"@
    $rV = $cmdVerify.ExecuteReader()
    while ($rV.Read()) {
        $statusName = switch ([int]$rV['Status']) {
            1 { 'Open' }
            2 { 'Closed' }
            default { "INVALID($([int]$rV['Status']))" }
        }
        Write-Host ("  Status={0} ({1}) Count={2} ClosedAt-NULL={3} ExitPrice-NULL={4} RealizedSum=`${5}" -f `
            $rV['Status'], $statusName, $rV['Cnt'], $rV['NullClosedAtCnt'], $rV['NullExitPriceCnt'], $rV['RealizedSum'])
    }
    $rV.Close()

    Write-Host ""
    Write-Host "=== STEP 4: Open positions detail (capacity audit) ==="
    $cmdOpen = $conn.CreateCommand()
    $cmdOpen.CommandText = @"
SELECT Id, Symbol, Mode, OpenedAt, MarkPrice, Quantity
FROM Positions
WHERE Status = 1
ORDER BY Mode, OpenedAt
"@
    $rO = $cmdOpen.ExecuteReader()
    $openByMode = @{}
    while ($rO.Read()) {
        $mode = [int]$rO['Mode']
        if (-not $openByMode.ContainsKey($mode)) { $openByMode[$mode] = 0 }
        $openByMode[$mode]++
        Write-Host "    Open: Id=$($rO['Id']) $($rO['Symbol']) Mode=$mode OpenedAt=$($rO['OpenedAt'])"
    }
    $rO.Close()
    Write-Host ""
    foreach ($k in $openByMode.Keys | Sort-Object) {
        $modeName = switch ($k) { 1 {'Paper'} 2 {'LiveTestnet'} 3 {'LiveMainnet'} default { "Unknown($k)" } }
        Write-Host "  Mode=$k ($modeName) OpenCount=$($openByMode[$k])"
    }

    Write-Host ""
    Write-Host "=== Done. ==="
}
finally {
    $conn.Close()
}
