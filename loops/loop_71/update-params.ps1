$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 71 KMS skor-tabanli params per coin
$baseJson = @{
    RsiPeriod = 14
    EmaPeriod = 9
    AtrPeriod = 14
    TradeCountWindow = 20
    RsiOversoldZone = 40
    RsiNeutralCeiling = 52
    MinScoreThreshold = 4
    MinAtrPctLarge = 0.0002
    MinAtrPctMid = 0.0003
    MinAtrPctAlt = 0.0004
    TradeCountSurgeMultiplier = 0.8
    TpAtrMultiplier = 1.8
    SlAtrMultiplier = 0.75
    TpAtrMultiplierLow = 1.5
    TpAtrMultiplierHigh = 2.2
    SlAtrMultiplierLow = 0.85
    SlAtrMultiplierHigh = 0.65
    MinTpPct = 0.005
    MaxTpPct = 0.018
    MinSlPct = 0.003
    MaxSlPct = 0.008
    MaxHoldMinutes = 45
    MaxHoldMinutesLowScore = 30
    MaxHoldMinutesHighScore = 60
    SpreadThresholdPct = 0.005
    CooldownBarsAfterSignal = 3
}

# CoinClass per coin
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
    $cmd.CommandText = "UPDATE Strategies SET ParametersJson = @p, UpdatedAt = SYSUTCDATETIME() WHERE Name = @n"
    $cmd.Parameters.AddWithValue('@p', $json) | Out-Null
    $cmd.Parameters.AddWithValue('@n', $name) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    Write-Host "$name (CoinClass=$($coinClassMap[$name])): $rows row updated"
}

Write-Host ""
Write-Host "=== Verify ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, LEFT(ParametersJson, 200) AS Params FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) { Write-Host ('  ' + $r['Name'] + ': ' + $r['Params']) }
$r.Close()

$conn.Close()
