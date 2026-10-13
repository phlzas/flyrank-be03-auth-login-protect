# Stage 0 - "hello": prove the LLM key actually works before writing any app code.
# Makes exactly one call to the OpenAI-compatible provider and prints the reply + latency.
# Usage:  powershell -File scripts/hello.ps1

param(
    [string]$Prompt = "Reply with the single word: ready"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$envFile = Join-Path $repoRoot ".env"

$config = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*#' -or $_ -notmatch '=') { return }
    $name, $value = $_ -split '=', 2
    $config[$name.Trim()] = $value.Trim()
}

$baseUrl = $config["LLM_BASE_URL"]
$apiKey = $config["LLM_API_KEY"]
$model = $config["LLM_MODEL"]

Write-Host "Provider : $baseUrl"
Write-Host "Model    : $model"
Write-Host "Prompt   : $Prompt"
Write-Host ""

$body = @{
    model = $model
    messages = @(
        @{ role = "user"; content = $Prompt }
    )
    temperature = 0
} | ConvertTo-Json -Depth 6

$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$response = Invoke-RestMethod -Method Post -Uri "$baseUrl/chat/completions" `
    -Headers @{ Authorization = "Bearer $apiKey" } `
    -ContentType "application/json" -Body $body
$stopwatch.Stop()

$reply = $response.choices[0].message.content
Write-Host "Reply    : $reply"
Write-Host "Latency  : $($stopwatch.ElapsedMilliseconds) ms"
if ($response.usage) {
    Write-Host "Tokens   : prompt=$($response.usage.prompt_tokens) completion=$($response.usage.completion_tokens)"
}
