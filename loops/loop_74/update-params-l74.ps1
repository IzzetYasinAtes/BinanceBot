$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 74 binance-expert quick-win spec: MinScore 5, RsiCeiling 50, TP biraz genis, SL gevsek
$baseJson = @{
    RsiPeriod = 14
    EmaPeriod = 9
    AtrPeriod = 14
    TradeCountWindow = 20
    RsiOversoldZone = 40
    RsiNeutralCeiling = 50          # 60 -> 50 (siki RSI)
    MinScoreThreshold = 5           # 4 -> 5 (sadece guclu entry)
    MinAtrPctLarge = 0.0002
    MinAtrPctMid = 0.0003
    MinAtrPctAlt = 0.0004
    TradeCountSurgeMultiplier = 0.8
    TpAtrMultiplier = 1.5            # 1.2 -> 1.5 (biraz genis)
    SlAtrMultiplier = 0.60           # 0.55 -> 0.60 (biraz gevsek)
    TpAtrMultiplierLow = 1.3          # 1.0 -> 1.3
    TpAtrMultiplierHigh = 1.8         # 1.5 -> 1.8
    SlAtrMultiplierLow = 0.65
    SlAtrMultiplierHigh = 0.5
    MinTpPct = 0.002                 # 0.003 -> 0.002
    MaxTpPct = 0.012                 # 0.015 -> 0.012
    MinSlPct = 0.002
    MaxSlPct = 0.006
    MaxHoldMinutes = 35              # 30 -> 35
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
Write-Host "=== Verify ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, Status FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) { Write-Host ('  ' + $r['Name'] + ' Status=' + $r['Status']) }
$r.Close()

$conn.Close()
