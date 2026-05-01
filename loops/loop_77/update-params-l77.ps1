$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 77 KMS params: EMA200 hard-gate + BBW score + Loop 76 trailing devam
$baseJson = @{
    RsiPeriod = 14
    EmaPeriod = 9
    AtrPeriod = 14
    TradeCountWindow = 20
    RsiOversoldZone = 40
    RsiNeutralCeiling = 70   # Loop 76.6 widening korundu
    MinScoreThreshold = 4    # Loop 75-76 emit-friendly
    MinAtrPctLarge = 0.0002
    MinAtrPctMid = 0.0003
    MinAtrPctAlt = 0.0004
    TradeCountSurgeMultiplier = 0.8
    TpAtrMultiplier = 1.5
    SlAtrMultiplier = 0.60
    TpAtrMultiplierLow = 1.3
    TpAtrMultiplierHigh = 1.8
    SlAtrMultiplierLow = 0.65
    SlAtrMultiplierHigh = 0.5
    MinTpPct = 0.002
    MaxTpPct = 0.012
    MinSlPct = 0.002
    MaxSlPct = 0.006
    MaxHoldMinutes = 35
    MaxHoldMinutesLowScore = 20
    MaxHoldMinutesHighScore = 45
    SpreadThresholdPct = 0.005
    CooldownBarsAfterSignal = 3
    BeMoveTriggerPct = 0.0010
    BeMoveOffsetPct = 0.0002
    # YENI Loop 77
    Ema200GateEnabled = $true
    BbwScoreEnabled = $true
    BbwThreshold = 0.008
    BbwScorePoints = 1
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
    $cmd.CommandText = "UPDATE Strategies SET ParametersJson = @p, UpdatedAt = SYSUTCDATETIME() WHERE Name = @n"
    $cmd.Parameters.AddWithValue('@p', $json) | Out-Null
    $cmd.Parameters.AddWithValue('@n', $name) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    Write-Host "$name (CoinClass=$($coinClassMap[$name])): $rows row + EMA200 gate + BBW score"
}

Write-Host ""
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    Write-Host ('  ' + $r['Name'] + ' Ema200Gate=' + $j.Ema200GateEnabled + ' BbwScore=' + $j.BbwScoreEnabled + ' BbwThr=' + $j.BbwThreshold + ' MinScore=' + $j.MinScoreThreshold)
}
$r.Close()

$conn.Close()
