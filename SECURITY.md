# Security Policy

## Overview

MyPasswordVault is a **zero-knowledge password manager**. The server never has access to plaintext vault data or the master password. All encryption and decryption happens exclusively in the browser using the [Web Crypto API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Crypto_API).

---

## Cryptographic Design

### Master Password Handling

The master password never leaves the client. Before any network request:

1. The client fetches the user's **KDF salt** from the server (`/api/auth/get-salt`).
2. **PBKDF2-SHA256** (600,000 iterations) derives two separate 256-bit keys from the password + salt:
   - **Auth key** — sent to the server as the "password hash" for login verification.
   - **Vault key** — used locally to encrypt/decrypt vault entries; never transmitted.
3. The vault key is held in memory (`cryptoStore`) and discarded on logout or page refresh.

> If users revisit the app without refreshing, an **UnlockVaultModal** prompts for the master password to re-derive the key locally — the server is not involved.

### Vault Entry Encryption

| Property | Value |
|---|---|
| Algorithm | AES-GCM |
| Key size | 256-bit |
| IV | 12 random bytes per entry |
| Plaintext | JSON blob: `{ title, username, url, notes, category, password }` |
| Storage | Base64-encoded ciphertext + IV — server never sees plaintext |

### Password Hashing (server-side)

The server stores only the **auth key** (the PBKDF2 output), not the master password. The stored hash is compared using `CryptographicOperations.FixedTimeEquals()` to prevent timing attacks.

### Token Security

| Token | Algorithm | Storage | Lifetime | Transport |
|---|---|---|---|---|
| JWT access token | RSA-256 | Client memory | 1 hour | `Authorization: Bearer` header |
| Refresh token | 32 random bytes | SHA-256 hash in DB | 7 days | `httpOnly; Secure; SameSite=Strict` cookie |
| Email verification token | 32 random bytes | SHA-256 hash in DB | 15 minutes | URL query param in email |
| Password reset token | 32 random bytes | SHA-256 hash in DB | 15 minutes | URL query param in email |
| Pending 2FA token | 32 random bytes | SHA-256 hash in DB | 10 minutes | Response body (short-lived) |

Refresh tokens are **rotated on every use** and explicitly revoked on logout.

---

## Authentication Security

### Two-Factor Authentication (TOTP)

- Algorithm: TOTP (RFC 6238) via [OtpNet](https://github.com/kspearrin/Otp.NET)
- Period: 30 seconds, SHA-1, 6 digits
- Window tolerance: ±1 period to account for clock drift
- Backup codes: 8 single-use 6-character codes, stored as SHA-256 hashes

### Known Device Tracking

On every successful login, a device fingerprint is computed as `SHA-256(IP|UserAgent)`:
- **New device** → login alert email sent to the user
- **Known device** → `LastSeenAt` updated silently
- IP addresses are **anonymized** before storage (last IPv4 octet zeroed; IPv6 collapsed to first 3 groups)

### Anti-Enumeration Protections

| Endpoint | Protection |
|---|---|
| `POST /auth/forgot-password` | Always returns 200, email only sent if user exists |
| `POST /auth/get-salt` | Returns an HMAC-derived fake salt for non-existent emails |

---

## Session Management

- Access tokens expire after **1 hour**
- A silent refresh is attempted automatically on 401 responses
- Refresh tokens are **revoked on logout**
- On password change or password reset, **all refresh tokens are invalidated** (forced logout everywhere)

---

## Rate Limiting

| Scope | Limit |
|---|---|
| Auth endpoints (`/api/auth/*`) | 20 requests / minute / IP |
| User endpoints (`/api/user/*`) | 60 requests / minute / IP |
| Vault endpoints (`/api/vault/*`) | 30 requests / minute / IP |

Exceeded limits return `429 Too Many Requests`.

---

## Password Reset Implications

Resetting the master password is a **destructive operation**:

- All existing vault entries are **permanently deleted** (they are encrypted with the old key, which is now irrecoverably gone).
- All active sessions are invalidated.

Users are warned about this in the UI before proceeding.

---

## Infrastructure

- All traffic is served over **HTTPS** via Nginx
- The API is not directly exposed; Nginx acts as a reverse proxy
- Docker Compose is used for container isolation
- Secrets (DB connection string, RSA keys, MailerSend API key) are stored in `appsettings.Development.json`, which is gitignored and never committed

---

## Reporting a Vulnerability

If you discover a security vulnerability in this project, please report it responsibly:

1. **Do not** open a public GitHub issue for security vulnerabilities.
2. Email the maintainer at the address listed on the GitHub profile.
3. Include a clear description of the vulnerability, steps to reproduce, and potential impact.

You can expect an acknowledgement within 48 hours. Critical vulnerabilities will be addressed as a priority.

---

## Out of Scope

The following are **not** considered security issues for this project:

- Vulnerabilities requiring physical access to the user's machine
- Social engineering attacks
- Attacks on the user's master password strength (users are encouraged to use a strong passphrase)
- Rate limit bypasses using distributed IPs
