<#
Test mail.miautrix.tech connectivity for all common mail ports.
#>

param(
    [Parameter(Position = 0)]
    [string]$Target = "mail.miautrix.tech"
)

$ports = @(25, 465, 587, 143, 993, 110, 995)

Write-Host "Testing mail connectivity to $Target`n"

$legend = @{
    25  = "SMTP (plain, old)"
    465 = "SMTP (SSL)"
    587 = "SMTP (STARTTLS)"
    143 = "IMAP (plain)"
    993 = "IMAP (SSL)"
    110 = "POP3 (plain)"
    995 = "POP3 (SSL)"
}

$ports | ForEach-Object {
    $port = $_
    $ok = $false

    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $task = $client.ConnectAsync($Target, $port)
        if ($task.Wait(1500)) {
            $ok = $client.Connected
        }
        $client.Close()
    }
    catch {}

    $status = if ($ok) { "OPEN" } else { "CLOSED/TIMEOUT" }
    $symbol = if ($ok) { "[OK]" } else { "[--]" }
    Write-Host "  $symbol Port $port ($($legend[$port])) - $status"
}

Write-Host "`n  SMTP  = sending email"
Write-Host "  IMAP  = reading email (modern)"
Write-Host "  POP3  = reading email (old, downloads locally)"
Write-Host "  SSL   = encrypted connection from the start"
Write-Host "  STARTTLS = upgrades plain connection to encrypted"
