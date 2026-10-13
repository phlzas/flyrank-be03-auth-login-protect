# Stage 5 - score the endpoint against the labelled cases in evals/cases.json.
# Starts from the assumption that the API is already running on $PORT.
# Usage:  powershell -File scripts/eval.ps1

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

$config = @{}
Get-Content (Join-Path $repoRoot ".env") | ForEach-Object {
    if ($_ -match '^\s*#' -or $_ -notmatch '=') { return }
    $name, $value = $_ -split '=', 2
    $config[$name.Trim()] = $value.Trim()
}
$port = if ($config["PORT"]) { $config["PORT"] } else { 3000 }
$baseUrl = "http://localhost:$port/triage"

$cases = Get-Content (Join-Path $repoRoot "evals\cases.json") -Raw | ConvertFrom-Json
$passed = 0
$results = @()

Write-Host "Running $($cases.Count) cases against $baseUrl"
Write-Host ""

foreach ($case in $cases) {
    $body = @{ text = $case.text } | ConvertTo-Json
    $got = "(error)"
    $status = 0
    $repaired = ""
    try {
        $response = Invoke-WebRequest -Method Post -Uri $baseUrl -ContentType "application/json" -Body $body -UseBasicParsing
        $status = $response.StatusCode
        $repaired = $response.Headers["X-Triage-Repaired"]
        $got = ($response.Content | ConvertFrom-Json).category
    }
    catch {
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    }

    $ok = ($got -eq $case.expected_category)
    if ($ok) { $passed++ }

    $mark = if ($ok) { "PASS" } else { "FAIL" }
    Write-Host ("[{0}] {1}  expected={2} got={3} status={4} repaired={5}" -f $mark, $case.id, $case.expected_category, $got, $status, $repaired)

    $results += [ordered]@{
        id = $case.id
        expected = $case.expected_category
        got = $got
        status = $status
        repaired = $repaired
        pass = $ok
    }
}

Write-Host ""
Write-Host ("Score: {0}/{1}" -f $passed, $cases.Count)

$lastRun = [ordered]@{
    ran_at = (Get-Date).ToUniversalTime().ToString("o")
    score = "$passed/$($cases.Count)"
    results = $results
}
$lastRun | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $repoRoot "evals\last-run.json")
Write-Host "Wrote evals\last-run.json"
