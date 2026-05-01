$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 70 KMS params: RSI 35->38, TC 0.8->0.6, MinAtr 0.0005->0.0003
$paramsJson = '{"RsiPeriod":14,"RsiRecoveryThreshold":38.0,"EmaPeriod":9,"TradeCountWindow":20,"TradeCountMultiplier":0.6,"AtrPeriod":14,"TpAtrMultiplier":1.8,"SlAtrMultiplier":0.75,"MinTpPct":0.005,"MaxTpPct":0.018,"MinSlPct":0.003,"MaxSlPct":0.008,"MaxHoldMinutes":45,"MinAtrPct":0.0003,"SpreadThresholdPct":0.005,"CooldownBarsAfterSignal":3}'

$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = @p, UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS'"
$cmd.Parameters.AddWithValue('@p', $paramsJson) | Out-Null
$rows = $cmd.ExecuteNonQuery()
Write-Host "Updated $rows KMS strategies"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, LEFT(ParametersJson, 120) AS Params FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) { Write-Host ('  ' + $r['Name'] + ': ' + $r['Params']) }
$r.Close()

$conn.Close()
