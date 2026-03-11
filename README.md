# MyPasswordVault — Backend API

ASP.NET Core 10 REST API for the MyPasswordVault zero-knowledge password manager.

## Tech Stack

| Package | Version | Purpose |
|---|---|---|
| ASP.NET Core | 10.0 | Web framework |
| Entity Framework Core | 10.0.3 | ORM |
| Npgsql EF Provider | 10.0.0 | PostgreSQL driver |
| BCrypt.Net-Next | 4.1 | — (available, hashing done client-side) |
| System.IdentityModel.Tokens.Jwt | 8.16 | JWT creation & validation |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.3 | JWT middleware |
| Otp.NET | 1.4.1 | TOTP two-factor authentication |
| xUnit + Moq + EF InMemory | — | Unit testing |

## Project Structure

```
server/
├── Controllers/
│   ├── AuthController.cs    # Register, login, 2FA, email verification, password reset
│   ├── VaultController.cs   # CRUD for vault entries (all endpoints require JWT)
│   └── UserController.cs    # Change password, change email, delete account, profile
├── Services/
│   ├── AuthService.cs
│   ├── VaultService.cs
│   ├── UserService.cs
│   ├── EmailService.cs      # MailerSend transactional email
│   └── Interfaces/          # IAuthService, IVaultService, IUserService, IEmailService
├── Models/
│   ├── User.cs
│   ├── VaultEntry.cs
│   ├── RefreshToken.cs
│   ├── TwoFactorBackupCode.cs
│   ├── EmailNotVerifiedException.cs
│   └── TwoFactorRequiredException.cs
├── DTOs/
│   ├── Auth/                # RegisterRequestDto, LoginRequestDto, AuthResponseDto, …
│   └── Vault/               # VaultEntryRequestDto, VaultEntryResponseDto
├── Data/
│   └── AppDbContext.cs
├── Migrations/              # EF Core migration history
├── Middleware/
│   └── ExceptionMiddleware.cs   # Global error handler → JSON error responses
├── MyPasswordVault.Tests/   # xUnit test project (46 tests)
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── Dockerfile
├── docker-compose.yml
└── .github/workflows/
    └── pipeline.yaml        # CI/CD: build → Docker Hub → Coolify
```

## Security Design

### Zero-knowledge vault
- The server **never receives the master password**. The client derives an AES-256 key using PBKDF2 and encrypts all vault data before sending it.
- The server only stores ciphertext (`EncryptedData`) and the AES-GCM initialization vector (`DataIv`).
- Changing or resetting a password wipes all vault entries server-side because the old ciphertext is permanently unreadable.

### Authentication
- **RS256 JWT** access tokens (1-hour lifetime). A 2048-bit RSA key pair is generated once and stored in environment variables.
- **Refresh tokens** stored as SHA-256 hashes in the database, delivered via `HttpOnly` cookies, rotated on every use.
- **Email verification** required before login.
- **TOTP 2FA** (OTP.NET) with 8 single-use backup codes.
- **Password reset** via time-limited (15 min), single-use token emailed through MailerSend.

### Rate limiting (ASP.NET Core built-in)
| Policy | Limit | Applies to |
|---|---|---|
| `auth` | 20 req / min / IP | Register, login, 2FA endpoints |
| `user` | 60 req / min / IP | Profile / user endpoints |
| `vault` | 30 req / min / IP | Vault CRUD endpoints |
Returns `429 Too Many Requests` when exceeded.

## API Endpoints

### Auth — `/api/auth`

| Method | Path | Auth | Description |
|---|---|---|---|
| POST | `/register` | No | Create account, send verification email |
| POST | `/login` | No | Login; returns JWT or `requiresTwoFactor` |
| POST | `/verify-2fa` | No | Complete TOTP login with 6-digit code |
| POST | `/use-backup-code` | No | Complete TOTP login with a backup code |
| POST | `/refresh` | No | Rotate refresh token (reads HttpOnly cookie) |
| POST | `/logout` | No | Revoke refresh token cookie |
| GET | `/salt` | No | Return per-account KDF salt (anti-enumeration safe) |
| POST | `/verify-email` | No | Verify email address using token from email link |
| POST | `/resend-verification` | No | Resend verification email |
| POST | `/forgot-password` | No | Send password reset email |
| POST | `/reset-password` | No | Confirm new password hash + wipe vault |
| GET | `/2fa/setup` | **Yes** | Generate TOTP secret + QR URI |
| POST | `/2fa/enable` | **Yes** | Confirm TOTP code and activate 2FA |
| POST | `/2fa/disable` | **Yes** | Disable 2FA with current TOTP code |
| POST | `/2fa/regenerate-backup-codes` | **Yes** | Replace all backup codes |

### Vault — `/api/vault`

All endpoints require a valid JWT (`Authorization: Bearer <token>`).

| Method | Path | Description |
|---|---|---|
| GET | `/items` | List all vault entries for the authenticated user |
| POST | `/items` | Add a new vault entry |
| PUT | `/items/{id}` | Edit an existing vault entry |
| DELETE | `/items/{id}` | Delete a vault entry |

### User — `/api/user`

| Method | Path | Auth | Description |
|---|---|---|---|
| GET | `/me` | **Yes** | Get username, email, 2FA status |
| POST | `/change-password` | **Yes** | Change password (wipes vault + invalidates sessions) |
| POST | `/change-email` | **Yes** | Initiate email change (sends verification email) |
| POST | `/verify-email-change` | No | Confirm email change using token |
| DELETE | `/delete` | **Yes** | Permanently delete account and all data |

## Getting Started (Local Development)

### Prerequisites

- .NET 10 SDK
- PostgreSQL (local or Docker)

### 1 — Clone and configure

Copy the development settings and fill in your values:

```jsonc
// appsettings.Development.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=mypwvault;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "PrivateKey": "<base64-encoded PKCS8 RSA private key>",
    "PublicKey":  "<base64-encoded SubjectPublicKeyInfo RSA public key>",
    "Issuer":     "MyPasswordVault",
    "Audience":   "MyPasswordVault",
    "ExpiresInHours": 1
  },
  "Cors": {
    "AllowedOrigins": [ "http://localhost:5173" ]
  },
  "MailerSend": {
    "ApiKey":          "<your MailerSend API key>",
    "FromEmail":       "noreply@yourdomain.com",
    "FrontendBaseUrl": "http://localhost:5173"
  }
}
```

#### Generate an RSA key pair

```bash
# Private key (PKCS8, base64)
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 \
  | openssl pkcs8 -topk8 -nocrypt -outform DER \
  | base64 -w0

# Public key (SubjectPublicKeyInfo, base64)
openssl rsa -in <(openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048) \
  -pubout -outform DER | base64 -w0
```

### 2 — Run the API

```bash
cd server
dotnet run
```

The API starts at **http://localhost:5000** (or the port in `launchSettings.json`). EF Core migrations are applied automatically on startup.

### 3 — Apply migrations manually (optional)

```bash
cd server
dotnet ef database update
```

## Running Tests

```bash
cd server/MyPasswordVault.Tests
dotnet test
```

The test suite contains **46 unit tests** covering `AuthService`, `UserService`, and `VaultService` using an in-memory database and Moq. No external dependencies (database, email, network) are required.

```bash
# With verbose output
dotnet test --logger "console;verbosity=normal"
```

## Docker

### Build manually

```bash
cd server
docker build -t mypwvault-api .
```

### docker-compose (production)

```yaml
# server/docker-compose.yml — pulled by Coolify
services:
  backend:
    image: cezarique/passbackend:latest
    pull_policy: always
    restart: unless-stopped
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
      - ConnectionStrings__DefaultConnection=${DB_CONNECTION_STRING}
      - Jwt__PrivateKey=${JWT_PRIVATE_KEY}
      - Jwt__PublicKey=${JWT_PUBLIC_KEY}
      - Jwt__Issuer=${JWT_ISSUER:-MyPasswordVault}
      - Jwt__Audience=${JWT_AUDIENCE:-MyPasswordVault}
      - Jwt__ExpiresInHours=${JWT_EXPIRES_IN_HOURS:-1}
      - Cors__AllowedOrigins__0=${FRONTEND_URL}
      - MailerSend__ApiKey=${MAILERSEND_API_KEY}
      - MailerSend__FromEmail=${MAILERSEND_FROM_EMAIL}
      - MailerSend__FrontendBaseUrl=${FRONTEND_URL}
    volumes:
      - dataprotection-keys:/root/.aspnet/DataProtection-Keys
volumes:
  dataprotection-keys:
```

Traefik (provided by Coolify) handles TLS and reverse proxying. No `ports:` mapping is needed on the backend container.

## CI/CD

Pushing to `main` triggers `.github/workflows/pipeline.yaml`:

1. Builds a Docker image from the `server/` directory.
2. Pushes `cezarique/passbackend:latest` and `cezarique/passbackend:<sha>` to Docker Hub.
3. Calls the Coolify webhook to redeploy automatically.

### Required GitHub Secrets

| Secret | Description |
|---|---|
| `DOCKER_USERNAME` | Docker Hub username |
| `DOCKERHUB_TOKEN` | Docker Hub access token |
| `COOLIFY_DOMAIN` | Coolify instance URL |
| `COOLIFY_UUID` | Coolify application UUID |
| `COOLIFY_TOKEN` | Coolify API token |

## Environment Variables Reference

| Variable | Example | Description |
|---|---|---|
| `ConnectionStrings__DefaultConnection` | `Host=…;Database=…` | PostgreSQL connection string |
| `Jwt__PrivateKey` | `MIIEvQ…` | Base64 PKCS8 RSA private key |
| `Jwt__PublicKey` | `MIIBIj…` | Base64 SubjectPublicKeyInfo RSA public key |
| `Jwt__Issuer` | `MyPasswordVault` | JWT issuer claim |
| `Jwt__Audience` | `MyPasswordVault` | JWT audience claim |
| `Jwt__ExpiresInHours` | `1` | Access token lifetime |
| `Cors__AllowedOrigins__0` | `https://mypasswordvault.cloud` | Allowed CORS origin (add more with `__1`, `__2`, …) |
| `MailerSend__ApiKey` | `mlsn.…` | MailerSend API key |
| `MailerSend__FromEmail` | `noreply@yourdomain.com` | Sender address |
| `MailerSend__FrontendBaseUrl` | `https://mypasswordvault.cloud` | Used to build links in emails |
| `ASPNETCORE_URLS` | `http://+:8080` | Listening address inside Docker |
