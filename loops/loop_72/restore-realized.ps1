$conn = New-Object System.Data.SqlClient.SqlConnection('Server=(localdb)\MSSQLLocalDB;Database=BinanceBot;Trusted_Connection=True;TrustServerCertificate=True')
$conn.Open()

# PositionClosed event'lerinden gercek PnL'leri yaz
$updates = @(
    @{ Id = 10479; Pnl = -0.06795928840000000000; Symbol = 'XRPUSDT' },
    @{ Id = 10480; Pnl = -0.08905911930000000000; Symbol = 'ADAUSDT' },
    @{ Id = 10481; Pnl = 0.55706217300000000000; Symbol = 'ETHUSDT' },
    @{ Id = 10482; Pnl = 0.45014359529995800000; Symbol = 'BTCUSDT' }
)

foreach ($u in $updates) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "UPDATE Positions SET RealizedPnl = @p WHERE Id = @id"
    $cmd.Parameters.AddWithValue('@p', $u.Pnl) | Out-Null
    $cmd.Parameters.AddWithValue('@id', $u.Id) | Out-Null
    $rows = $cmd.ExecuteNonQuery()
    Write-Host ("$($u.Symbol) Id=$($u.Id) Pnl=`$$($u.Pnl) ($rows row)")
}

Write-Host ""
$cmdV = $conn.CreateCommand()
$cmdV.CommandText = "SELECT ISNULL(SUM(RealizedPnl), 0) AS Total FROM Positions WHERE Status = 3 AND OpenedAt > '2026-05-01 01:54'"
$rV = $cmdV.ExecuteReader()
$rV.Read() | Out-Null
Write-Host ("Restored Total Realized: `$$($rV['Total'])")
$rV.Close()

$conn.Close()
