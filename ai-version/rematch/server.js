// -----------------------------------------------------------------------------
// STAGE 7 ARTIFACT - the REGENERATED draft after the improved prompt.
// Same language/framework as the first draft; only the security fixes changed.
// Still not the submission.
// -----------------------------------------------------------------------------
const express = require("express");
const jwt = require("jsonwebtoken");
const crypto = require("crypto");

const app = express();
app.use(express.json());

const SECRET = process.env.JWT_SECRET || "dev-secret";
const users = [];
let nextId = 1;

function hashPassword(password) {
  const salt = crypto.randomBytes(16).toString("hex");
  const derived = crypto.scryptSync(password, salt, 64).toString("hex");
  return `${salt}:${derived}`;
}

function verifyPassword(password, stored) {
  const [salt, key] = stored.split(":");
  const derived = crypto.scryptSync(password, salt, 64).toString("hex");
  return crypto.timingSafeEqual(Buffer.from(key, "hex"), Buffer.from(derived, "hex"));
}

app.post("/auth/signup", (req, res) => {
  const { email, password } = req.body;
  if (!email || !password) {
    return res.status(400).json({ error: "email and password are required" });
  }
  if (users.some((u) => u.email === email)) {
    return res.status(400).json({ error: "email already registered" });
  }
  const user = { id: nextId++, email, password: hashPassword(password) };
  users.push(user);
  return res.status(201).json({ id: user.id, email: user.email });
});

app.post("/auth/login", (req, res) => {
  const { email, password } = req.body;
  const user = users.find((u) => u.email === email);
  if (!user || !verifyPassword(password || "", user.password)) {
    return res.status(401).json({ error: "Invalid login credentials" });
  }
  const access_token = jwt.sign(
    { sub: user.id, email: user.email, role: "user" },
    SECRET,
    { expiresIn: "1h" }
  );
  return res.json({ access_token });
});

function auth(req, res, next) {
  const header = req.headers.authorization || "";
  if (!header.startsWith("Bearer ")) {
    return res.status(401).json({ error: "Access token required" });
  }
  const token = header.slice("Bearer ".length).trim();
  try {
    req.user = jwt.verify(token, SECRET);
    return next();
  } catch {
    return res.status(401).json({ error: "Invalid or expired token" });
  }
}

function adminOnly(req, res, next) {
  if (req.user?.role !== "admin") {
    return res.status(403).json({ error: "Forbidden: admin only" });
  }
  return next();
}

app.get("/public/info", (req, res) =>
  res.json({ message: "Welcome stranger! This info is public." })
);

app.get("/protected/profile", auth, (req, res) =>
  res.json({ id: req.user.sub, email: req.user.email })
);

app.get("/protected/dashboard", auth, (req, res) =>
  res.json({ message: "This is a protected dashboard.", id: req.user.sub })
);

app.get("/protected/admin", auth, adminOnly, (req, res) =>
  res.json({ message: "Welcome, admin.", id: req.user.sub })
);

app.post("/auth/logout", auth, (req, res) => res.status(204).end());

const port = process.env.PORT || 3101;
app.listen(port, () => console.log(`ai-version rematch listening on ${port}`));
