$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "UPDATE Strategies SET Status = 3, ActivatedAt = SYSUTCDATETIME(), UpdatedAt = SYSUTCDATETIME() WHERE Name LIKE '%-KMS' AND Status = 2"
$rows = $cmd.ExecuteNonQuery()
Write-Host "Reactivated $rows KMS strategies"
$cmd2 = $conn.CreateCommand()
$cmd2.CommandText = "SELECT Name, Status FROM Strategies WHERE Name LIKE '%-KMS' ORDER BY Name"
$r = $cmd2.ExecuteReader()
while ($r.Read()) { Write-Host ('  ' + $r['Name'] + ' Status=' + $r['Status']) }
$r.Close()
$conn.Close()
