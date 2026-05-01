$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# Loop 74.6: RsiCeiling 50 -> 60 geri al (Loop 73 emit'li seviye)
Write-Host "=== UPDATE RsiNeutralCeiling 50 -> 60 (Loop 73 emit-friendly) ==="
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(ParametersJson, '`"RsiNeutralCeiling`":50,', '`"RsiNeutralCeiling`":60,'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "Updated $rows row(s)"

Write-Host ""
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    Write-Host ("  " + $r['Name'] + " MinScore=" + $j.MinScoreThreshold + " RsiOver=" + $j.RsiOversoldZone + " RsiCeil=" + $j.RsiNeutralCeiling + " TpMul=" + $j.TpAtrMultiplier + " SlMul=" + $j.SlAtrMultiplier + " MaxHold=" + $j.MaxHoldMinutes)
}
$r.Close()

$conn.Close()
