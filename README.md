# FlyRank BE-03 — Auth: Login & Protect

A secure authentication API built with **ASP.NET Core 9**, **ASP.NET Core Identity**
and **JWT bearer tokens**. It implements sign-up, login, logout and JWT-protected
routes, and ships an interactive **Swagger UI** at `/docs`.

> **Note on stack choice.** The assignment lists *JavaScript* and *Python* as example
> lanes and its own Q&A states *"you are welcome to use any resources at your disposal,
> it doesn't strictly have to be Next.js."* This submission implements the **.NET
> lane** instead of Supabase Auth, using ASP.NET Core Identity for credential storage
> (PBKDF2 password hashing) and the framework's JWT bearer handler for token
> issue/verification. The behaviour, routes and status codes match the specification
> exactly. We do **not** hand-roll any cryptography — hashing, signing and token
> validation are all delegated to Microsoft-maintained libraries.

---

## Table of contents

- [Overview](#overview)
- [Requirements](#requirements)
- [Setup](#setup)
- [Running the API](#running-the-api)
- [API reference](#api-reference)
- [Auth flow](#auth-flow)
- [Swagger UI](#swagger-ui)
- [Testing](#testing)
- [Security notes](#security-notes)
- [Project structure](#project-structure)
- [AI vs Me](#ai-vs-me)

---

## Overview

| Requirement | Implementation |
|---|---|
| User accounts | ASP.NET Core Identity (`UserManager<AppUser>`) |
| Password storage | PBKDF2 via Identity (no custom hashing) |
| Tokens | HS256 JWTs signed with a secret from `.env` |
| Token verification | `Microsoft.AspNetCore.Authentication.JwtBearer` |
| Protected routes | `[Authorize]` + reusable revoked-token middleware |
| Logout | `jti` deny-list + refresh-token revocation |
| Docs | Swagger UI (Swashbuckle) at `/docs` with Bearer support |
| Database | SQLite via EF Core Migrations (no external server) |

## Requirements

- [.NET SDK 9.0](https://dotnet.microsoft.com/download) (built and tested on `9.0.317`)
- No database server, no Docker, no external accounts required.

## Setup

1. **Clone** the repository.
2. **Create your `.env`** from the template:

   ```bash
   cp .env.example .env
   ```

3. **Set a strong `JWT_SECRET`** (at least 32 characters):

   ```bash
   # bash
   openssl rand -hex 32
   ```

   ```powershell
   # Windows PowerShell
   -join ((1..32) | ForEach-Object { '{0:x2}' -f (Get-Random -Max 256) })
   ```

   Paste the result into `.env` as `JWT_SECRET=...`.

   > The app **refuses to start** if `JWT_SECRET` is missing or shorter than 32
   > characters, so it can never boot insecurely.

4. **Restore** dependencies:

   ```bash
   dotnet restore
   ```

The `.env` file is listed in `.gitignore` and will never be committed.

## Running the API

```bash
dotnet run --project src/FlyRank.Auth.Api
```

The app applies EF Core migrations on startup (creating `flyrank_auth.db` on first
run) and listens on **http://localhost:3000** (configurable via `PORT` in `.env`).

Then open **http://localhost:3000/docs** for the interactive Swagger UI.

## API reference

| Method | Route | Auth | Success | Errors |
|---|---|---|---|---|
| `POST` | `/auth/signup` | No | `201 Created` | `400` missing/invalid fields or duplicate email |
| `POST` | `/auth/login` | No | `200 OK` (access + refresh token) | `400` missing fields · `401` bad credentials |
| `POST` | `/auth/logout` | **Yes** | `204 No Content` | `401` missing/invalid/expired/revoked token |
| `POST` | `/auth/refresh` | No | `200 OK` (rotated token pair) | `400` missing field · `401` invalid/expired/revoked refresh token |
| `GET` | `/protected/profile` | **Yes** | `200 OK` | `401` missing/invalid/expired/revoked token |
| `GET` | `/protected/dashboard` | **Yes** | `200 OK` | `401` missing/invalid/expired/revoked token |
| `GET` | `/public/info` | No | `200 OK` | — |

### Example requests

**Sign up**

```bash
curl -i -X POST http://localhost:3000/auth/signup \
  -H "Content-Type: application/json" \
  -d '{"email":"intern@example.com","password":"Passw0rd!","fullName":"Test Intern"}'
```

**Log in**

```bash
curl -i -X POST http://localhost:3000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"intern@example.com","password":"Passw0rd!"}'
```

**Call a protected route**

```bash
curl -i http://localhost:3000/protected/profile \
  -H "Authorization: Bearer <ACCESS_TOKEN>"
```

**Log out**

```bash
curl -i -X POST http://localhost:3000/auth/logout \
  -H "Authorization: Bearer <ACCESS_TOKEN>" \
  -H "Content-Type: application/json" \
  -d '{"refreshToken":"<REFRESH_TOKEN>"}'
```

## Auth flow

1. **Sign up** → a user record is created; the password is hashed by Identity.
2. **Log in** → credentials are verified, then a short-lived **access token** (JWT)
   and an opaque **refresh token** are returned.
3. **Protected routes** → the client sends `Authorization: Bearer <access token>`.
   The JWT bearer middleware validates signature, issuer, audience and lifetime, and
   a reusable middleware rejects tokens whose `jti` has been revoked.
4. **Log out** → the access token's `jti` is deny-listed and the refresh token is
   revoked, so neither can be reused. The deny-listed access token now yields `401`.
5. **Refresh** *(optional)* → a valid refresh token is exchanged for a fresh pair
   (rotation: the old refresh token is retired).

## Swagger UI

The interactive docs live at **`/docs`** and include an **Authorize** button that
applies the Bearer token to every request.

![Swagger UI](docs/swagger-ui.png)

## Testing

A PowerShell smoke test exercises every required route and asserts the status codes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/smoke-test.ps1
```

Observed results (all passing):

| Check | Expected | Observed |
|---|---|---|
| `GET /public/info` (no auth) | 200 | ✅ 200 |
| `POST /auth/signup` (valid) | 201 | ✅ 201 |
| `POST /auth/signup` (missing password) | 400 | ✅ 400 |
| `POST /auth/login` (valid) | 200 | ✅ 200 |
| `POST /auth/login` (wrong password) | 401 | ✅ 401 |
| `GET /protected/profile` (no token) | 401 | ✅ 401 |
| `GET /protected/profile` (valid token) | 200 | ✅ 200 |
| `GET /protected/dashboard` (valid token) | 200 | ✅ 200 |
| `GET /protected/profile` (invalid token) | 401 | ✅ 401 |
| `POST /auth/logout` (valid token) | 204 | ✅ 204 |
| `GET /protected/profile` (after logout) | 401 | ✅ 401 |
| `GET /docs/index.html` (Swagger UI) | 200 | ✅ 200 |

## Security notes

- **No custom cryptography.** Password hashing (PBKDF2) and JWT signing/verification
  are handled by framework libraries.
- **Secrets stay out of git.** `.env` (and `*.db`) are git-ignored; only
  `.env.example` is committed.
- **Fail-fast secret check.** The app throws on startup if `JWT_SECRET` is weak.
- **Uniform login errors.** Unknown email and wrong password return the same `401`
  body, preventing user enumeration.
- **Real revocation.** Logout is meaningful: the `jti` is deny-listed and the refresh
  token is retired, so a stolen access token stops working immediately.
- **Refresh-token rotation.** Each refresh retires the old token, limiting replay.
- **Short access-token lifetime** (15 minutes by default) bounds exposure.

## Project structure

```
flyrank-be03-auth-login-protect/
├── .env.example                # template for environment variables
├── .gitignore                  # excludes .env, *.db, bin/obj
├── .config/dotnet-tools.json   # pins the dotnet-ef local tool
├── FlyRank.Auth.sln
├── docs/
│   └── swagger-ui.png
├── scripts/
│   └── smoke-test.ps1
└── src/FlyRank.Auth.Api/
    ├── Controllers/             # Auth, Protected, Public
    ├── Data/                    # ApplicationDbContext
    ├── Dtos/                    # request/response contracts
    ├── Middleware/              # RevokedTokenMiddleware
    ├── Migrations/              # EF Core migrations
    ├── Models/                  # AppUser, RefreshToken, RevokedAccessToken
    ├── Options/                 # JwtOptions
    ├── Services/                # JwtTokenService
    └── Program.cs
```

## AI vs Me

**What I asked the AI to do:** scaffold an ASP.NET Core Web API with Identity + JWT,
wire up Swagger with Bearer support, implement the five required routes with exact
status codes, and add a reusable token-checking middleware.

**What the AI produced:** the project skeleton, the controller/service/DTO code, the
Swagger configuration, and a first draft of this README.

**What I reviewed and corrected:**

1. **Package targeting.** The first dependency install pulled `10.x` packages that
   require `net10.0`; the installed SDK is `9.0`. I pinned every Microsoft package to
   the `9.0.13` line.
2. **Swashbuckle version.** `10.x` depends on `Microsoft.OpenApi` `2.x`, whose API
   surface differs from the widely-documented `1.6` API. I pinned `Swashbuckle 6.9.0`
   for a stable, well-understood configuration.
3. **Missing migration in the running build.** The first server start logged
   *"No migrations were found"* because the DLL was built before the migration was
   generated. I rebuilt, reset the SQLite file, and confirmed the schema was applied.
4. **Swagger route.** The template had no docs UI at all; I added Swashbuckle and
   moved the UI to the required `/docs` prefix.
5. **Logout semantics.** A naive logout just deletes a client-side token. I made
   logout meaningful by deny-listing the `jti` and revoking the refresh token.
```
