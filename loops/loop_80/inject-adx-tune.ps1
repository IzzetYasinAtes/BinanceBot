$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# KMS: AdxTrendingThreshold 20 -> 18 (JSON inject if missing)
$cmd1 = $conn.CreateCommand()
$cmd1.CommandText = "SELECT Id, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' OR Name LIKE '%-BBR'"
$r = $cmd1.ExecuteReader()
$rows = @()
while ($r.Read()) {
    $rows += [PSCustomObject]@{ Id = [int]$r['Id']; Json = $r['ParametersJson'] }
}
$r.Close()

foreach ($row in $rows) {
    $obj = $row.Json | ConvertFrom-Json
    # KMS için
    if ($obj.PSObject.Properties.Name -contains 'TpAtrMultiplier') {
        $obj | Add-Member -NotePropertyName 'AdxTrendingThreshold' -NotePropertyValue 18 -Force
        $obj | Add-Member -NotePropertyName 'AdxGateEnabled' -NotePropertyValue $true -Force
    }
    # BBR için
    if ($obj.PSObject.Properties.Name -contains 'BbwRangeMax') {
        $obj | Add-Member -NotePropertyName 'AdxRangeMax' -NotePropertyValue 30 -Force
        $obj | Add-Member -NotePropertyName 'AdxGateEnabled' -NotePropertyValue $true -Force
    }
    $newJson = $obj | ConvertTo-Json -Compress

    $cmdU = $conn.CreateCommand()
    $cmdU.CommandText = "UPDATE Strategies SET ParametersJson = @p, UpdatedAt = SYSUTCDATETIME() WHERE Id = @id"
    $cmdU.Parameters.AddWithValue('@p', $newJson) | Out-Null
    $cmdU.Parameters.AddWithValue('@id', $row.Id) | Out-Null
    $u = $cmdU.ExecuteNonQuery()
    Write-Host "Id=$($row.Id) updated ($u row)"
}

Write-Host ""
Write-Host "=== Verify ==="
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, ParametersJson FROM Strategies WHERE Name LIKE '%-KMS' OR Name LIKE '%-BBR' ORDER BY Name"
$r2 = $cmd2.ExecuteReader()
while ($r2.Read()) {
    $j = $r2['ParametersJson'] | ConvertFrom-Json
    $thr = if ($j.AdxTrendingThreshold) { $j.AdxTrendingThreshold } else { '-' }
    $max = if ($j.AdxRangeMax) { $j.AdxRangeMax } else { '-' }
    $ena = if ($j.AdxGateEnabled -ne $null) { $j.AdxGateEnabled } else { '-' }
    Write-Host ('  ' + $r2['Name'] + ' AdxThr=' + $thr + ' AdxMax=' + $max + ' Enabled=' + $ena)
}
$r2.Close()

$conn.Close()
