$baseUrl = "http://localhost:5000"
$headers = @{ "Content-Type" = "application/json" }
$sessionId = $null

Clear-Host
Write-Host "=== Chat Client (localhost:5000/chat) ===" -ForegroundColor Cyan
Write-Host "Type your message and press Enter. Type 'quit' to exit." -ForegroundColor DarkGray
Write-Host ""

while ($true) {
    Write-Host "You: " -ForegroundColor Green -NoNewline
    $input_msg = Read-Host
    if ($input_msg -eq "quit") { break }
    if ([string]::IsNullOrWhiteSpace($input_msg)) { continue }

    $payload = @{ message = $input_msg }
    if ($sessionId) { $payload.sessionId = $sessionId }
    $body = $payload | ConvertTo-Json -Depth 10

    try {
        # Fire request async so we can animate while waiting
        $task = (Invoke-WebRequest -Uri "$baseUrl/chat" -Method POST -Body $body -Headers $headers -UseBasicParsing -TimeoutSec 120 &)
        $frames = @('.  ', '.. ', '...')
        $i = 0
        while ($task.State -eq 'Running') {
            Write-Host "`r$($frames[$i % $frames.Count])" -NoNewline -ForegroundColor DarkGray
            $i++
            Start-Sleep -Milliseconds 400
        }
        Write-Host "`r   `r" -NoNewline

        $resp = $task | Receive-Job -Wait -AutoRemoveJob
        $data = $resp.Content | ConvertFrom-Json

        # Capture sessionId for subsequent requests
        if ($data.sessionId) { $sessionId = $data.sessionId }

        $reply = if ($data.response)    { $data.response }
                 elseif ($data.message)  { $data.message }
                 elseif ($data.reply)    { $data.reply }
                 elseif ($data.content)  { $data.content }
                 elseif ($data.text)     { $data.text }
                 else                    { $resp.Content }

        Write-Host "Bot: " -ForegroundColor Yellow -NoNewline
        Write-Host $reply
        Write-Host ""
    }
    catch {
        $status = $_.Exception.Response.StatusCode.value__
        $errBody = $_.ErrorDetails.Message
        if ($errBody) {
            Write-Host "Server ($status): $errBody" -ForegroundColor Red
        } else {
            Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
        }
        Write-Host ""
    }
}

Write-Host "Goodbye!" -ForegroundColor Cyan
