# Family Trust Fund

Nigerian guarantor-led, closed-group lending platform.

- **Backend**: .NET 10, ASP.NET Core, EF Core, PostgreSQL, .NET Aspire modular monolith.
- **Frontend**: React 19, TypeScript, Vite, TanStack Query, Zustand.
- **Payments**: Paystack (first provider, behind an `IPaymentProvider` abstraction).
- **Auth**: ASP.NET Core Identity + Google OIDC.
- **Email**: `Afe.PRD.Email.Sender`.

## Repository layout

```
FamilyTrustFund.slnx           Solution (backend projects)
src/
  FamilyTrustFund.Domain         Pure domain models & domain rules
  FamilyTrustFund.Application    Use cases, services, ports (DTOs/interfaces)
  FamilyTrustFund.Infrastructure EF Core, Identity, payments, storage, email
  FamilyTrustFund.Api            ASP.NET Core API host (modular monolith)
  FamilyTrustFund.AppHost        .NET Aspire orchestration (Postgres, Api)
tests/
  FamilyTrustFund.Tests          Unit tests (financial rules)
frontend/                        React + Vite frontend
.github/workflows/ci.yml        CI build/test
```

## Prerequisites

- .NET SDK 10
- Node 22+
- Docker Desktop (for local PostgreSQL via Aspire)
- Google OIDC client (optional — see Development auth)

## Local development

### Backend + Aspire (starts PostgreSQL and the API)

```bash
dotnet run --project src/FamilyTrustFund.AppHost
```

The Aspire dashboard URL is printed to the console. PostgreSQL and the API are
started as resources. The API receives its connection string from Aspire.

### Frontend

```bash
cd frontend
npm install
npm run dev        # http://localhost:5173
```

The Vite dev server proxies `/api` to the API. By default it targets
`http://localhost:64446` (the API's HTTP port in `launchSettings.json`);
override with `VITE_API_PROXY_TARGET` if your local API port differs. Serving
the dev loop over HTTP keeps auth cookies non-Secure so the Vite app can use
them.

### Tests

```bash
dotnet test tests/FamilyTrustFund.Tests/FamilyTrustFund.Tests.csproj
```

### Build everything

```bash
dotnet build FamilyTrustFund.slnx
```

## Development authentication

Two options:

1. **Development sign-in (default, no setup).** When Google OIDC is not
   configured, the login page shows a development-only sign-in form
   (`/api/auth/dev/login`) so the app is usable locally.

2. **Google OIDC.** Create credentials at the Google Cloud Console
   (OAuth consent screen + OAuth client ID of type *Web application*) and
   configure them via the environment or `appsettings.Development.json`
   (which is gitignored):

   ```json
   {
     "Authentication": {
       "Google": {
         "ClientId": "xxx.apps.googleusercontent.com",
         "ClientSecret": "GOCSPX-...",
         "PostLoginRedirect": "http://localhost:5173"
       }
     }
   }
   ```

   Authorized redirect URI to register with Google:
   `http://localhost:64446/api/auth/google/callback`.

## Database

PostgreSQL is the authoritative database. Schema changes are applied through
EF Core migrations. Never modify production schema without a migration.

## Security notes

- Never commit secrets. Use environment variables / secrets management.
- Financial calculations are always performed server-side.
- Money is represented with `decimal` in the domain; provider boundaries use
  integer minor units where required.
- Payment evidence is stored on the filesystem (or a storage abstraction),
  never served directly by path.

## Conventions

- Follow `AGENTS.md`, `PLAN.md`, and `docs/DECISIONS.md` (these files are not
  version-controlled and live alongside the repo).
- Add an ADR to `docs/DECISIONS.md` for any new significant decision.
- Update `PLAN.md` when a planned phase/item is complete.
