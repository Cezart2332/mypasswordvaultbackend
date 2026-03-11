# Threat Model

**Application:** MyPasswordVault  
**Date:** March 2026  
**Methodology:** STRIDE (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege)

---

## 1. Assets

These are the assets worth protecting in order of criticality:

| # | Asset | Impact if compromised |
|---|---|---|
| 1 | **Master password** | Full vault compromise; irreversible |
| 2 | **Vault key (AES-256, in-memory)** | All vault entries decryptable |
| 3 | **Plaintext vault entries** | Exposed credentials for all stored services |
| 4 | **RSA private key (JWT signing)** | Attacker can forge tokens for any user |
| 5 | **Refresh tokens** | Persistent account access without password |
| 6 | **TOTP secrets** | 2FA bypass |
| 7 | **Database** | All vault entries (cipher text), hashed passwords, user data |
| 8 | **MailerSend API key** | Send phishing emails from trusted domain |
| 9 | **User identity (email + username)** | Phishing, social engineering |

---

## 2. Trust Boundaries

```
[User brain]
     │ master password (never persisted)
     ▼
[Browser / cryptoStore]
     │ HTTPS (encrypted blobs, hashed auth key, JWTs)
     ▼
[Nginx] ──────────────────────────────────────
     │ internal proxy
     ▼
[ASP.NET Core API] ── [MailerSend API] (TLS)
     │ EF Core / TCP
     ▼
[PostgreSQL]
```

Sensitive data **never crosses** the Browser → Server boundary in plaintext.

---

## 3. STRIDE Analysis

### 3.1 Authentication Layer (`/api/auth/*`)

#### S — Spoofing

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| Credential stuffing | Attacker uses breached credentials to log in | Medium | High | Rate limiting (20 req/min/IP); PBKDF2 makes offline brute-force expensive |
| Session hijacking | Stolen JWT used to act as user | Low | High | Short JWT lifetime (1h); refresh token is httpOnly cookie, inaccessible to JS |
| Refresh token theft | Attacker steals httpOnly refresh cookie via network | Very Low | High | `Secure; SameSite=Strict` cookie flags; HTTPS enforced |
| 2FA bypass via backup code brute-force | Iterating all possible 6-char codes | Low | High | Backup codes are single-use; rate limiting applies; no enumeration endpoint |
| Forged JWT | Attacker creates a JWT for another user | Very Low | Critical | RSA-256 signing; private key never exposed |

**Residual risk:** An insider threat with RSA private key access could forge tokens.  
**Recommendation:** Rotate the RSA key pair periodically; use HSM or secret manager in production.

---

#### T — Tampering

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| Vault entry manipulation | MITM modifies encrypted blobs in transit | Very Low | High | HTTPS/TLS in transit; AES-GCM provides authentication tag (tampering detectable on decrypt) |
| Password reset abuse | Attacker resets victim's password via guessed/brute-forced token | Very Low | Critical | Reset token is 32 random bytes (256-bit entropy); 15-minute expiry; SHA-256 hashed in DB |
| KDF salt substitution | Attacker replaces salt on `/get-salt` to influence key derivation | Low | High | No direct mitigation; response is unauthenticated — attacker supplying a controlled salt would cause decryption failure (user would notice) |

**Recommendation:** Consider signing or MAC-protecting the salt response, or requiring the user to confirm their email after salt changes.

---

#### R — Repudiation

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| No audit trail for vault operations | User denies creating/deleting a vault entry | Low | Low | `CreatedAt` timestamp stored on each entry |
| No login history visible to user | User cannot verify all their logins | Low | Medium | Known device tracking with alerts on new devices |

**Recommendation:** Expose a login history page to users showing known devices and last seen timestamps.

---

#### I — Information Disclosure

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| Email enumeration | Confirm whether an email is registered | Low | Low | `/forgot-password` always returns 200; `/get-salt` returns fake salt for unknown emails |
| Username/ID enumeration | Sequential IDs on vault endpoints reveal item count | Medium | Low | Integer PKs in URLs are sequentially guessable, but `[Authorize]` ensures only owner can access |
| Stack trace leakage | Unhandled exception exposes internal details | Low | Medium | `ExceptionMiddleware` returns generic 500 message; no stack trace in responses |
| TOTP secret exposure | `TwoFactorSecret` stored in plaintext in DB | Medium (DB breach) | High | Encrypted DB transport; secret never exposed via API after setup |
| Vault key logged | Vault key inadvertently written to server logs | Very Low | Critical | Key is never sent to server; no mitigation needed server-side |
| PBKDF2 output (authKey) leaked from DB | Allows offline dictionary attack on master password | Medium (DB breach) | High | 600,000 PBKDF2 iterations makes offline attack extremely slow; each user has a unique salt |

---

#### D — Denial of Service

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| Auth endpoint flooding | Attacker sends thousands of login requests | Medium | Medium | Rate limiting: 20 req/min/IP on auth endpoints |
| Account lockout via reset email flood | Spamming password reset exhausts MailerSend quota | Low | Medium | No explicit protection against repeated password reset requests |
| DB connection exhaustion | Attacker overwhelms DB via API | Low | High | Connection pool managed by EF Core; rate limiting reduces upstream pressure |
| Large vault entry upload | Submitting extremely large encrypted payloads | Low | Medium | No explicit payload size validation visible in vault endpoint |

**Recommendation:**
- Add a cooldown on `/auth/forgot-password` per email (e.g., 1 request per 5 minutes).
- Enforce a maximum payload size on vault endpoints (e.g., 64 KB per entry).

---

#### E — Elevation of Privilege

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| Accessing another user's vault | User A requests vault entries belonging to User B | Very Low | Critical | `VaultService` always filters by `userId` extracted from JWT claim; ownership validated before every operation |
| JWT claim manipulation | Attacker modifies userId claim in JWT | Very Low | Critical | RSA-256 signature validation; any modification invalidates the signature |
| TOTP secret exposed via setup endpoint | Re-calling `/auth/2fa/setup` after 2FA is enabled | Low | Medium | Setup endpoint generates a new secret each call; does not expose the existing active secret |
| Privilege escalation via insecure deserialization | Malicious payload in vault entry decrypted server-side | N/A | Critical | Server never decrypts vault entries; not applicable |

---

### 3.2 Client-Side Threats

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| XSS — vault key theft | Injected script reads `cryptoStore` vault key | Low | Critical | Vite/React escapes all rendered content by default; no `dangerouslySetInnerHTML` usage |
| XSS — CSRF with JWT | Injected script reads in-memory access token and makes API calls | Low | High | Same as above; JWT stored in memory (not `localStorage`), limiting XSS exposure |
| CSRF | Malicious site triggers state-changing API requests | Low | High | `SameSite=Strict` on refresh token cookie prevents cross-site submission |
| Clipboard sniffing | Copied password read by another browser extension/process | Medium | Medium | OS-level concern; no browser mitigation available |
| Shoulder surfing / screen recording | Vault entries visible on screen | Medium | Medium | Password fields masked by default with reveal-on-demand toggle |

---

### 3.3 Infrastructure Threats

| Threat | Scenario | Likelihood | Impact | Mitigation in place |
|---|---|---|---|---|
| Database breach | Attacker gains read access to PostgreSQL | Low | High | Vault data is ciphertext; tokens are hashes; password is PBKDF2 output with unique salts |
| Secret exfiltration | `appsettings.Development.json` committed to public repo | Very Low | Critical | File is in `.gitignore`; confirmed never committed |
| Docker image supply chain | Compromised base image (nginx:alpine, dotnet:8) | Very Low | High | Use digest-pinned images in production; regularly rebuild and scan images |
| TLS downgrade / MITM | Attacker intercepts HTTPS traffic | Very Low | High | HTTPS enforced via Nginx; recommend adding HSTS header |
| Server-side request forgery (SSRF) | User-supplied URL in vault entry used in a server-side request | N/A | High | Server never fetches user-supplied URLs; SSRF not applicable |

---

## 4. Risk Summary

| Risk | Severity | Likelihood | Mitigated? |
|---|---|---|---|
| RSA private key compromise → forged JWTs | Critical | Very Low | Partial — file protected, not rotated |
| Database breach → offline PBKDF2 attack | High | Low | Yes — 600k iterations + unique salts |
| Database breach → TOTP secrets exposed | High | Low | Partial — no server-side encryption of TOTP secret |
| XSS → vault key theft | Critical | Low | Yes — React escaping + in-memory storage |
| Refresh token theft via network | High | Very Low | Yes — httpOnly + Secure + SameSite=Strict |
| Credential stuffing | High | Medium | Partial — rate limiting, no account lockout |
| Password reset flooding MailerSend | Medium | Low | No — recommend per-email cooldown |
| Large payload DoS on vault endpoint | Medium | Low | No — recommend explicit size limits |

---

## 5. Recommendations

### High Priority

1. ~~**Encrypt the TOTP secret at rest**~~  ✅ **Implemented** — `TwoFactorSecret` is now encrypted with AES-GCM-256 before storage. The key (`Security:TotpEncryptionKey`) must be set as a 32-byte Base64 value in config/environment. Legacy plaintext values are transparently handled during migration.

2. ~~**Add per-email cooldown on `/auth/forgot-password`**~~ ✅ **Implemented** — `ResetPassword()` now silently returns if a reset email was sent within the last 5 minutes, preventing MailerSend quota exhaustion.

3. ~~**Add HSTS header in Nginx**~~ ✅ **Implemented** — `Strict-Transport-Security: max-age=31536000; includeSubDomains` is now set in `client/nginx/default.conf`.

### Medium Priority

4. ~~**Enforce vault entry payload size limit**~~ ✅ **Implemented** — `[RequestSizeLimit(65_536)]` (64 KB) added to `POST /vault/items` and `PUT /vault/items/{id}`.

5. **Expose a login history page**  
   Surface known devices and `LastSeenAt` timestamps to users so they can identify suspicious logins and revoke devices.

6. ~~**Pin Docker image digests**~~ ✅ **Implemented** — Instructions added to both Dockerfiles. Run the `docker inspect` commands shown in each Dockerfile to get the current digest and replace the mutable tags.

### Low Priority

7. **Rotate RSA key pair periodically**  
   Use a secret manager (e.g., Azure Key Vault, AWS Secrets Manager, Docker secrets) to store and rotate the JWT signing key without redeployment.

8. ~~**Consider rate-limiting per-user on vault endpoints**~~ ✅ **Implemented** — The `vault` rate-limit policy now partitions by authenticated user ID (30 req/min/user). Unauthenticated requests fall back to IP-based partitioning.
