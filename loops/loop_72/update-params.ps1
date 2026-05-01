$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

$baseJson = @{
    RsiPeriod = 14
    EmaPeriod = 9
    AtrPeriod = 14
    TradeCountWindow = 20
    RsiOversoldZone = 40
    RsiNeutralCeiling = 60      # 52 -> 60 (genis pencere)
    MinScoreThreshold = 3        # 4 -> 3 (frekans icin permisif)
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
    MaxTpPct = 0.025             # 0.018 -> 0.025 (genis TP)
    MinSlPct = 0.003
    MaxSlPct = 0.008
    MaxHoldMinutes = 45
    MaxHoldMinutesLowScore = 30
    MaxHoldMinutesHighScore = 60
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
    $cmd.CommandText = "UPDATE Strategies SET ParametersJson = @p, UpdatedAt = SYSUTCDATETIME() WHERE Name = @n"
    $cmd.Parameters.AddWithValue('@p', $json) | Out-Null
    $cmd.Parameters.AddWithValue('@n', $name) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    Write-Host "$name (CoinClass=$($coinClassMap[$name])): $rows row updated"
}

$conn.Close()
