# Smoke test for the FlyRank BE-03 Auth API.
# Usage: start the API, then run:  powershell -ExecutionPolicy Bypass -File scripts/smoke-test.ps1

$base = "http://localhost:3000"

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        [string]$Token,
        [string]$Json,
        [int]$Expected,
        [string]$Label,
        [string]$BodyContains
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

    $statusOk = ($code -eq $Expected)
    $bodyOk = $true
    if ($BodyContains) { $bodyOk = ($content -like "*$BodyContains*") }
    $pass = $statusOk -and $bodyOk

    [pscustomobject]@{
        Pass   = $pass
        Status = $code
        Expect = $Expected
        Label  = $Label
        Body   = $content
    }
}

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$rows = @()

$rows += Invoke-Api -Method GET -Path "/public/info" -Expected 200 `
    -BodyContains "Welcome stranger! This info is public." `
    -Label "GET /public/info -> 200 + exact message"

$email = "intern+$([guid]::NewGuid().ToString('N').Substring(0, 8))@example.com"
$password = "Passw0rd!"

$rows += Invoke-Api -Method POST -Path "/auth/signup" -Expected 201 `
    -Json (@{ email = $email; password = $password; fullName = "Test Intern" } | ConvertTo-Json -Compress) `
    -Label "POST /auth/signup (valid) -> 201"

$rows += Invoke-Api -Method POST -Path "/auth/signup" -Expected 400 `
    -Json (@{ email = $email } | ConvertTo-Json -Compress) `
    -BodyContains '"error"' `
    -Label "POST /auth/signup (missing password) -> 400 + {error}"

$login = Invoke-Api -Method POST -Path "/auth/login" -Expected 200 `
    -Json (@{ email = $email; password = $password } | ConvertTo-Json -Compress) `
    -Label "POST /auth/login (valid) -> 200"
$rows += $login

$token = $null
$refresh = $null
try {
    $parsed = $login.Body | ConvertFrom-Json
    $token = $parsed.access_token
    $refresh = $parsed.refresh_token
}
catch { }

$rows += Invoke-Api -Method POST -Path "/auth/login" -Expected 401 `
    -Json (@{ email = $email; password = "WrongPass1!" } | ConvertTo-Json -Compress) `
    -BodyContains "Invalid login credentials" `
    -Label "POST /auth/login (wrong password) -> 401 + message"

$rows += Invoke-Api -Method GET -Path "/protected/profile" -Expected 401 `
    -BodyContains "Access token required" `
    -Label "GET /protected/profile (NO token) -> 401 + 'Access token required'"

$rows += Invoke-Api -Method GET -Path "/protected/profile" -Token $token -Expected 200 `
    -BodyContains "createdAt" `
    -Label "GET /protected/profile (valid token) -> 200 + createdAt"

$rows += Invoke-Api -Method GET -Path "/protected/dashboard" -Token $token -Expected 200 `
    -Label "GET /protected/dashboard (valid token) -> 200"

$rows += Invoke-Api -Method GET -Path "/protected/profile" -Token "not.a.real.token" -Expected 401 `
    -BodyContains "Invalid or expired token" `
    -Label "GET /protected/profile (tampered token) -> 401 + 'Invalid or expired token'"

$rows += Invoke-Api -Method GET -Path "/protected/admin" -Token $token -Expected 403 `
    -BodyContains "Forbidden" `
    -Label "GET /protected/admin (non-admin) -> 403"

$rows += Invoke-Api -Method POST -Path "/auth/logout" -Token $token -Expected 204 `
    -Json (@{ refreshToken = $refresh } | ConvertTo-Json -Compress) `
    -Label "POST /auth/logout (valid token) -> 204"

$rows += Invoke-Api -Method GET -Path "/protected/profile" -Token $token -Expected 401 `
    -Label "GET /protected/profile (AFTER logout / revoked) -> 401"

$rows += Invoke-Api -Method GET -Path "/docs/index.html" -Expected 200 `
    -Label "GET /docs/index.html (Swagger UI) -> 200"

$rlHit = 0
for ($i = 0; $i -lt 8; $i++) {
    $r = Invoke-Api -Method POST -Path "/auth/login" -Expected 429 `
        -Json (@{ email = $email; password = "WrongPass1!" } | ConvertTo-Json -Compress) `
        -Label "rate-limit probe #$($i + 1)"
    if ($r.Status -eq 429) { $rlHit++ }
}
$rows += [pscustomobject]@{
    Pass   = ($rlHit -ge 1)
    Status = $rlHit
    Expect = ">=1"
    Label  = "POST /auth/login rate limit -> 429 (hits observed)"
    Body   = ""
}

$sw.Stop()

$rows | Select-Object Label, Status, Expect, Pass | Format-Table -AutoSize | Out-String -Width 200 | Write-Output

$passed = ($rows | Where-Object { $_.Pass }).Count
$total = $rows.Count
Write-Output ""
Write-Output "RESULT: $passed / $total checks passed  ($($sw.Elapsed.TotalSeconds.ToString('0.0'))s)"

$rows | Where-Object { -not $_.Pass } | ForEach-Object {
    Write-Output "FAILED: $($_.Label) -> got $($_.Status), expected $($_.Expect); body: $($_.Body)"
}
