# Stage 7 - AI vs me

## Prompt I wrote (sent to the AI)

> Build a secured authentication API in **Node.js with Express**. It must have exactly
> these five routes:
>
> - `POST /auth/signup` - body `{ email, password }`; returns `201` on success, `400` if
>   either field is missing.
> - `POST /auth/login` - verifies the credentials; returns `200` with a JWT access token,
>   or `401` if they are wrong.
> - `POST /auth/logout` - protected; returns `204`.
> - `GET /protected/profile` - protected; returns the current user's id and email; `401`
>   if the token is missing or invalid.
> - `GET /public/info` - open; returns `{ "message": "Welcome stranger! This info is public." }`.
>
> Use JWT bearer tokens: read the token from the `Authorization` header, verify it, and
> put the token check in a **reusable middleware** so more than one protected route can use
> it. Use the same status codes as above. Keep it short.

## Result

The AI produced `server.js` in this folder. It starts and returns the right status codes
for the happy path, but the review below found real security defects.

## Improved prompt (the rematch)

> ...same as above, **plus**: verify the token with `jwt.verify` (not `decode`) and return
> `401` when verification throws; require the literal `Bearer ` scheme prefix and reject a
> bare token; store passwords with `bcrypt`, never MD5 or plaintext; never log the token;
> return every error as JSON `{ "error": "..." }`; add a `GET /protected/admin` route that
> returns `403` for non-admins.

**One-sentence change:** forcing signature verification (`jwt.verify`), strict `Bearer `
parsing, and `{ "error": ... }` bodies turned an API that accepted forged tokens into one
that rejects them.
