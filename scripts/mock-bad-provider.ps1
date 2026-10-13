# Stage 3 helper: a deliberately broken OpenAI-compatible provider.
# With -Status 200 it answers a well-formed HTTP 200 whose *content* is invalid
# triage JSON, so the app must reject it (422) and quarantine it - never return it.
# With -Status 401 it simulates a provider that rejects our key, to exercise the
# non-retryable client-error path.
# Usage:  powershell -File scripts/mock-bad-provider.ps1 [-Port 3999] [-Status 200]

param([int]$Port = 3999, [int]$Status = 200)

$ErrorActionPreference = "Stop"
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
$listener.Start()
Write-Host "Mock provider listening on http://localhost:$Port/ (status=$Status, Ctrl+C to stop)"

$badContent = '{"category":"banana","urgency":"whenever","suggested_team":"vibes","confidence":"very","reason":12345,"extra":"nope"}'

while ($listener.IsListening) {
    $context = $listener.GetContext()
    $response = $context.Response
    $response.StatusCode = $Status
    if ($Status -eq 200) {
        $payload = @{
            id = "mock-1"
            object = "chat.completion"
            choices = @(@{ index = 0; message = @{ role = "assistant"; content = $badContent }; finish_reason = "stop" })
            usage = @{ prompt_tokens = 10; completion_tokens = 20 }
        } | ConvertTo-Json -Depth 8
    }
    else {
        $payload = '{"error":{"message":"Invalid API key","type":"invalid_request_error"}}'
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    $response.ContentType = "application/json"
    $response.ContentLength64 = $bytes.Length
    $response.OutputStream.Write($bytes, 0, $bytes.Length)
    $response.OutputStream.Close()
    Write-Host "served status=$Status for $($context.Request.Url.AbsolutePath)"
}

