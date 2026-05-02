$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET ParametersJson = REPLACE(REPLACE(ParametersJson, '`"AdxTrendingThreshold`":20', '`"AdxTrendingThreshold`":18'), '`"AdxRangeMax`":25', '`"AdxRangeMax`":30'), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS' OR Name LIKE '%-BBR'"
$rows = $cmd.ExecuteNonQuery()
Write-Host "ADX gate gevsetme: $rows row(s) updated"
Write-Host "  KMS AdxTrendingThreshold 20 -> 18 (daha permisif)"
Write-Host "  BBR AdxRangeMax 25 -> 30 (range daha geniş)"

$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' OR Name LIKE '%-BBR' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) {
    $j = $r['ParametersJson'] | ConvertFrom-Json
    $thr = if ($j.AdxTrendingThreshold) { $j.AdxTrendingThreshold } else { '-' }
    $max = if ($j.AdxRangeMax) { $j.AdxRangeMax } else { '-' }
    Write-Host ('  ' + $r['Name'] + ' AdxThr=' + $thr + ' AdxMax=' + $max)
}
$r.Close()

$conn.Close()
