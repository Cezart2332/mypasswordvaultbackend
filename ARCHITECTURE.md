# Architecture

## System Overview

MyPasswordVault is a **zero-knowledge password manager** built on a client-server architecture. The critical design principle is that the server **never** processes or stores plaintext vault data. All encryption and decryption is performed exclusively in the browser.

---

## High-Level Architecture

```mermaid
graph TB
    subgraph Browser["Browser — React 18 + TypeScript"]
        UI["React UI (Vite)"]
        WCA["Web Crypto API\nPBKDF2-SHA256 · 600k iterations\nAES-GCM 256-bit"]
        MEM["cryptoStore\nIn-memory vault key only"]
        AX["Axios Client\nBearer JWT + httpOnly Cookie"]
    end

    subgraph DockerServer["Docker — Server"]
        NX["Nginx\nReverse Proxy / TLS termination"]
        API["ASP.NET Core 8\nJWT RSA-256 · Rate Limiting\nException Middleware"]
        subgraph Svc ["Services"]
            AS["AuthService"]
            VS["VaultService"]
            US["UserService"]
            ES["EmailService / TokenService"]
        end
    end

    subgraph DockerDB["Docker — Database"]
        PG[("PostgreSQL\nusers\nvault_entries — encrypted blob\nrefresh_tokens — SHA-256 hash\nknown_devices\n2fa_backup_codes — SHA-256 hashes")]
    end

    subgraph External["External"]
        MS["MailerSend\nTransactional Email (TLS)"]
    end

    UI -- "derive vault key (local)" --> WCA
    WCA -- "AES-GCM key in memory" --> MEM
    UI -- "encrypt entry before send" --> WCA
    UI -- "HTTPS\nencrypted blobs + hashed auth key" --> AX
    AX --> NX
    NX --> API
    API --> Svc
    Svc --> PG
    Svc -- "email via TLS" --> MS

    style MEM fill:#ffe4b5,stroke:#cc8800
    style WCA fill:#d4edda,stroke:#155724
    style PG fill:#cce5ff,stroke:#004085
```

> **Key property:** The vault key (derived from the master password) only ever exists in `cryptoStore` (browser memory). It is never sent to the server or persisted anywhere.

---

## Component Breakdown

### Client (Browser)

| Component | Responsibility |
|---|---|
| React UI | All user-facing pages and interactions |
| Web Crypto API | PBKDF2 key derivation + AES-GCM encryption/decryption |
| `cryptoStore` | Holds the 256-bit vault key in memory after login; cleared on logout/refresh |
| Axios Interceptors | Attaches JWT Bearer token; handles silent refresh on 401 responses |

**Build:** Vite → served by Nginx as a static SPA.

### Server (ASP.NET Core 8)

| Component | Responsibility |
|---|---|
| Nginx | Reverse proxy, TLS termination, static file serving |
| Controllers | HTTP routing (`/api/auth`, `/api/vault`, `/api/user`) |
| JWT Middleware | Validates RSA-256 signed access tokens on protected routes |
| Rate Limiter | Per-endpoint per-IP request limits (20–60 req/min) |
| ExceptionMiddleware | Translates custom exceptions to HTTP responses without leaking internals |
| AuthService | Registration, login, 2FA, email verification, password reset, device tracking |
| VaultService | CRUD for encrypted vault entries (no decryption ever happens here) |
| UserService | Account management, password/email change, account deletion |
| EmailService | Sends transactional emails via MailerSend API |
| TokenService | Generates and hashes all short-lived tokens |

### Database (PostgreSQL)

| Table | Sensitive columns | How protected |
|---|---|---|
| `users` | `PasswordHash`, `kdfSalt` | PBKDF2 output; never plaintext password |
| `users` | `TwoFactorSecret` | Base32 TOTP secret; DB access required to exploit |
| `vault_entries` | `EncryptedData`, `DataIv` | AES-GCM ciphertext; useless without vault key |
| `refresh_tokens` | `TokenHash` | SHA-256 hash only |
| `2fa_backup_codes` | `CodeHash` | SHA-256 hash only |
| `users` | `EmailVerificationToken`, `PasswordResetToken`, etc. | SHA-256 hash only |

---

## Data Flow Diagrams

### Login & Vault Unlock

```mermaid
sequenceDiagram
    actor User
    participant Browser
    participant API
    participant DB

    User->>Browser: Enter email + master password
    Browser->>API: GET /api/auth/get-salt (email)
    API->>DB: Lookup kdfSalt for email
    DB-->>API: kdfSalt (or HMAC-derived fake salt)
    API-->>Browser: kdfSalt

    Browser->>Browser: PBKDF2(password, salt, 600k) → authKey + vaultKey
    Note over Browser: vaultKey stored in cryptoStore (memory only)

    Browser->>API: POST /api/auth/login (email, authKey)
    API->>DB: Compare authKey hash (FixedTimeEquals)
    DB-->>API: Match

    alt 2FA Enabled
        API-->>Browser: { requiresTwoFactor: true, pendingToken }
        User->>Browser: Enter TOTP code
        Browser->>API: POST /api/auth/verify-2fa (pendingToken, code)
    end

    API->>DB: Store refresh token hash + device fingerprint
    API-->>Browser: JWT (1h) + Set-Cookie: refreshToken (httpOnly, 7d)

    Browser->>API: GET /api/vault/items (Bearer JWT)
    API->>DB: Fetch encrypted vault entries
    DB-->>API: [{ encryptedData, dataIv }, ...]
    API-->>Browser: [{ encryptedData, dataIv }, ...]
    Browser->>Browser: AES-GCM.decrypt(encryptedData, dataIv, vaultKey)
    Browser-->>User: Plaintext vault entries
```

### Add Vault Entry

```mermaid
sequenceDiagram
    actor User
    participant Browser
    participant API
    participant DB

    User->>Browser: Fill in title, username, URL, password, notes
    Browser->>Browser: JSON.stringify(fields)
    Browser->>Browser: iv = crypto.getRandomValues(12 bytes)
    Browser->>Browser: AES-GCM.encrypt(json, vaultKey, iv) → ciphertext
    Browser->>API: POST /api/vault/items { encryptedData, dataIv } (Bearer JWT)
    API->>DB: INSERT vault_entry (encryptedData, dataIv, userId)
    DB-->>API: Saved entry (id, timestamps)
    API-->>Browser: VaultEntry DTO
    Browser->>Browser: Decrypt and display new entry
    Browser-->>User: Entry added to vault
```

### Token Refresh

```mermaid
sequenceDiagram
    participant Browser
    participant API
    participant DB

    Browser->>API: Any request (expired JWT)
    API-->>Browser: 401 Unauthorized
    Browser->>Browser: Axios interceptor intercepts 401
    Browser->>API: POST /api/auth/refresh (httpOnly cookie auto-sent)
    API->>DB: Validate refreshToken hash + expiry
    DB-->>API: Valid
    API->>DB: Rotate refresh token (new hash, revoke old)
    API-->>Browser: New JWT + new Set-Cookie (rotated refresh token)
    Browser->>API: Retry original request (new JWT)
    API-->>Browser: 200 OK
```

---

## Deployment Architecture

```mermaid
graph LR
    Internet -- "HTTPS :443" --> NX
    subgraph DockerCompose["Docker Compose"]
        NX["Nginx\n(client container)\n:80 / :443"]
        BE["ASP.NET Core API\n(server container)\n:5010"]
        DB["PostgreSQL\n(db container)\n:5432"]
        NX -- "proxy /api/*" --> BE
        BE -- "EF Core" --> DB
    end
    BE -- "HTTPS" --> MS["MailerSend"]
```

| Container | Image | Exposed port |
|---|---|---|
| client | nginx:alpine + React SPA | 80, 443 |
| server | mcr.microsoft.com/dotnet/aspnet:8.0 | 5010 (internal) |
| db | postgres:latest | 5432 (internal) |

---

## Security Boundaries

```
┌─────────────────────────────────────────────────────────────────┐
│  TRUST BOUNDARY: Browser (user controlled)                      │
│                                                                 │
│  • Master password lives here only                              │
│  • Vault key (AES-256) lives here only                          │
│  • Plaintext vault data lives here only                         │
│                                                                 │
└────────────────────────┬────────────────────────────────────────┘
                         │  HTTPS — only ciphertext crosses this boundary
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│  TRUST BOUNDARY: Server (operator controlled)                   │
│                                                                 │
│  • Sees: encrypted blobs, hashed auth key, hashed tokens        │
│  • Never sees: master password, vault key, plaintext entries    │
│                                                                 │
│  ┌───────────────────────────────────────────┐                 │
│  │  TRUST BOUNDARY: Database                 │                 │
│  │  • Encrypted vault entries                │                 │
│  │  • Hashed tokens (SHA-256)                │                 │
│  │  • Hashed password (PBKDF2)               │                 │
│  └───────────────────────────────────────────┘                 │
└─────────────────────────────────────────────────────────────────┘
```
