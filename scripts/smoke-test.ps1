# Smoke test for the FlyRank BE-03 Auth API.
# Usage: start the API, then run:  pwsh -File scripts/smoke-test.ps1
# It exercises every required route and prints the observed HTTP status codes.

$base = "http://localhost:3000"

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [string]$Token,
        [string]$Json,
        [string]$Label
    )

    $uri = "$base$Path"
    $headers = @{}
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }

    try {
        if ($Json) {
            $resp = Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers `
                -ContentType "application/json" -Body $Json -UseBasicParsing
        }
        else {
            $resp = Invoke-WebRequest -Method $Method -Uri $uri -Headers $headers -UseBasicParsing
        }
        $code = [int]$resp.StatusCode
        $content = $resp.Content
    }
    catch {
        $code = [int]$_.Exception.Response.StatusCode.value__
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $content = $reader.ReadToEnd()
        }
        catch { $content = "" }
    }

    [pscustomobject]@{ Label = $Label; Status = $code; Body = $content }
}

$rows = @()

$rows += Invoke-Api -Method GET  -Path "/public/info" -Label "GET /public/info (no auth) -> expect 200"

$email = "intern+$([guid]::NewGuid().ToString('N').Substring(0, 8))@example.com"
$password = "Passw0rd!"

$rows += Invoke-Api -Method POST -Path "/auth/signup" `
    -Json (@{ email = $email; password = $password; fullName = "Test Intern" } | ConvertTo-Json -Compress) `
    -Label "POST /auth/signup (valid) -> expect 201"

$rows += Invoke-Api -Method POST -Path "/auth/signup" `
    -Json (@{ email = $email } | ConvertTo-Json -Compress) `
    -Label "POST /auth/signup (missing password) -> expect 400"

$login = Invoke-Api -Method POST -Path "/auth/login" `
    -Json (@{ email = $email; password = $password } | ConvertTo-Json -Compress) `
    -Label "POST /auth/login (valid) -> expect 200"
$rows += $login

$token = $null
$refresh = $null
try {
    $parsed = $login.Body | ConvertFrom-Json
    $token = $parsed.accessToken
    $refresh = $parsed.refreshToken
}
catch { }

$rows += Invoke-Api -Method POST -Path "/auth/login" `
    -Json (@{ email = $email; password = "WrongPass1!" } | ConvertTo-Json -Compress) `
    -Label "POST /auth/login (wrong password) -> expect 401"

$rows += Invoke-Api -Method GET -Path "/protected/profile" -Label "GET /protected/profile (NO token) -> expect 401"
$rows += Invoke-Api -Method GET -Path "/protected/profile" -Token $token -Label "GET /protected/profile (valid token) -> expect 200"
$rows += Invoke-Api -Method GET -Path "/protected/dashboard" -Token $token -Label "GET /protected/dashboard (valid token) -> expect 200"
$rows += Invoke-Api -Method GET -Path "/protected/profile" -Token "not.a.real.token" -Label "GET /protected/profile (invalid token) -> expect 401"

$rows += Invoke-Api -Method POST -Path "/auth/logout" -Token $token `
    -Json (@{ refreshToken = $refresh } | ConvertTo-Json -Compress) `
    -Label "POST /auth/logout (valid token) -> expect 204"

$rows += Invoke-Api -Method GET -Path "/protected/profile" -Token $token -Label "GET /protected/profile (AFTER logout / revoked) -> expect 401"

$rows += Invoke-Api -Method GET -Path "/docs/index.html" -Label "GET /docs/index.html (Swagger UI) -> expect 200"

$rows | Select-Object Label, Status | Format-Table -AutoSize | Out-String -Width 200 | Write-Output
