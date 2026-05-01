$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 73 KMS params: TP daralt + SL sıkı + MaxHold kısalt + reactivate
$baseJson = @{
    RsiPeriod = 14
    EmaPeriod = 9
    AtrPeriod = 14
    TradeCountWindow = 20
    RsiOversoldZone = 40
    RsiNeutralCeiling = 60
    MinScoreThreshold = 3
    MinAtrPctLarge = 0.0002
    MinAtrPctMid = 0.0003
    MinAtrPctAlt = 0.0004
    TradeCountSurgeMultiplier = 0.8
    TpAtrMultiplier = 1.2
    SlAtrMultiplier = 0.55
    TpAtrMultiplierLow = 1.0
    TpAtrMultiplierHigh = 1.5
    SlAtrMultiplierLow = 0.65
    SlAtrMultiplierHigh = 0.5
    MinTpPct = 0.003
    MaxTpPct = 0.015
    MinSlPct = 0.002
    MaxSlPct = 0.006
    MaxHoldMinutes = 30
    MaxHoldMinutesLowScore = 20
    MaxHoldMinutesHighScore = 45
    SpreadThresholdPct = 0.005
    CooldownBarsAfterSignal = 3
}

$coinClassMap = @{
    'BTC-KMS' = 'large'
    'ETH-KMS' = 'large'
    'SOL-KMS' = 'mid'
    'XRP-KMS' = 'alt'
    'ADA-KMS' = 'alt'
}

foreach ($name in $coinClassMap.Keys) {
    $params = $baseJson.Clone()
    $params['CoinClass'] = $coinClassMap[$name]
    $json = $params | ConvertTo-Json -Compress

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE Strategies SET ParametersJson = @p, Status = 3, ActivatedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME() WHERE Name = @n"
    $cmd.Parameters.AddWithValue('@p', $json) | Out-Null
    $cmd.Parameters.AddWithValue('@n', $name) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    Write-Host "$name (CoinClass=$($coinClassMap[$name])) reactivated + tune: $rows row"
}

Write-Host ""
Write-Host "=== Verify Strategies ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, Status FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    Write-Host ('  ' + $r['Name'] + ' Status=' + $r['Status'])
}
$r.Close()

$conn.Close()
