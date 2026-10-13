// -----------------------------------------------------------------------------
// STAGE 7 ARTIFACT - quarantined AI draft. This is NOT the submission.
// It is a deliberately naive first-pass implementation, kept only so the
// "AI vs me" review can quote real, runnable code. Do not ship this.
// -----------------------------------------------------------------------------
const express = require("express");
const jwt = require("jsonwebtoken");
const crypto = require("crypto");

const app = express();
app.use(express.json());

const SECRET = process.env.JWT_SECRET || "dev-secret";
const users = [];
let nextId = 1;

app.post("/auth/signup", (req, res) => {
  const { email, password } = req.body;
  if (!email || !password) {
    return res.status(400).send("email and password are required");
  }
  const hash = crypto.createHash("md5").update(password).digest("hex");
  const user = { id: nextId++, email, password: hash };
  users.push(user);
  return res.status(201).json({ id: user.id, email: user.email });
});

app.post("/auth/login", (req, res) => {
  const { email, password } = req.body;
  const user = users.find((u) => u.email === email);
  const hash = crypto.createHash("md5").update(password || "").digest("hex");
  if (!user || user.password !== hash) {
    return res.status(401).send("Invalid login credentials");
  }
  const access_token = jwt.sign({ sub: user.id, email: user.email }, SECRET, {
    expiresIn: "1h",
  });
  return res.json({ access_token });
});

function auth(req, res, next) {
  const token = (req.headers.authorization || "").replace("Bearer ", "");
  console.log("auth token:", token);
  const decoded = jwt.decode(token);
  if (!decoded || decoded.exp * 1000 < Date.now()) {
    return res.status(401).send("Access token required");
  }
  req.user = decoded;
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

app.post("/auth/logout", auth, (req, res) => res.status(204).end());

const port = process.env.PORT || 3100;
app.listen(port, () => console.log(`ai-version listening on ${port}`));
