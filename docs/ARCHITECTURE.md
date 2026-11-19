# DriveShield architecture

**Style:** modular monolith. One ASP.NET Core API, one PostgreSQL + PostGIS database, one React web app.
The code is split into clear modules so that any of them can later become its own service.

## System view

```mermaid
flowchart LR
    subgraph Clients
        W[React web app<br/>Azure Static Web Apps]
        M[Mobile app<br/>.NET MAUI, later]
    end

    subgraph API["ASP.NET Core API (one deployable, Azure App Service)"]
        direction TB
        AUTH[Auth<br/>OTP sign-in, JWT, refresh tokens]
        CASES[Cases and matching<br/>state machine, offers]
        PROV[Providers<br/>registration, availability]
        VEH[Vehicles]
        POSTS[Comments and photos<br/>edit, delete, moderation]
        ADMIN[Admin<br/>lists, assign, approvals]
        HUB[SignalR hub<br/>live updates]
        WORK[Background workers<br/>offer expiry, token cleanup]
    end

    DB[(PostgreSQL + PostGIS<br/>Azure Database for PostgreSQL)]
    BLOB[(Photo storage<br/>local disk now, Azure Blob later)]
    MAIL[Email: SMTP service]
    SMS[SMS: Twilio]

    W -->|REST + JWT| API
    M -.->|REST + JWT| API
    W <-->|WebSocket| HUB
    API --> DB
    POSTS --> BLOB
    AUTH --> MAIL
    AUTH --> SMS
```

## How a request flows

```mermaid
sequenceDiagram
    participant C as Customer
    participant API as API
    participant DB as PostgreSQL/PostGIS
    participant P as Provider
    participant A as Admin

    C->>API: Report incident (location, vehicle, 2 photos)
    API->>DB: Save case, find approved providers nearby
    API-->>P: NewOffer (SignalR)
    P->>API: Accept (first to accept wins)
    API->>DB: Status ProviderAccepted
    API-->>C: CaseStatusChanged (SignalR)
    P->>API: On the way, in progress, resolved (+ photos, notes)
    API-->>C: Live progress updates
    C->>API: Rate and review
    Note over A,API: Admin can assign, unassign or reassign a provider at any time before resolution
```

## Modules

| Module | Code | Responsibility |
|---|---|---|
| Auth | `Auth/`, `Endpoints/AuthEndpoints.cs` | Email/mobile one-time codes, JWT access tokens (15 min), rotating refresh tokens (30 days), admin rights from `Auth:AdminEmails` |
| Cases and matching | `Application/CaseWorkflowService.cs`, `CaseStateMachine.cs`, `Endpoints/CaseEndpoints.cs` | Case lifecycle, nearest-provider matching (PostGIS), offers and expiry |
| Providers | `Endpoints/ProviderEndpoints.cs` | Registration, approval status, availability, base location, jobs |
| Vehicles | `Endpoints/VehicleEndpoints.cs` | Customer vehicles (customers only) |
| Comments and photos | `Endpoints/CommentEndpoints.cs`, `Infrastructure/LocalPhotoStore.cs` | Per-step posts and photos, edit and delete, admin removal |
| Admin | `Endpoints/AdminEndpoints.cs`, `AdminDirectoryEndpoints.cs`, `Application/AdminAssignmentService.cs` | Customers, providers, requests, assign / unassign / reassign, photo review |
| Live updates | `Hubs/CaseHub.cs` | SignalR groups per case and per provider |
| Workers | `Application/OfferExpiryWorker.cs`, `AuthCleanupWorker.cs` | Expire unanswered offers, escalate stuck cases, clear old codes and tokens |

## Why a modular monolith

- One deployment and one database keep cost, debugging and transactions simple (a case, its offers and its timeline change together).
- Modules talk through plain method calls today, so splitting later does not mean rewriting them.

## If it needs to split later

1. **Photos and notifications:** photo storage to Azure Blob; email and SMS sending into an Azure Function.
2. **Matching and offer expiry:** a separate worker fed by an Azure Service Bus queue.
3. **Identity:** move sign-in to Microsoft Entra External ID (replaces the JWT code).
4. Only then consider separate databases per service.

## Production notes

- Set `Auth__Jwt__SigningKey`, `Auth__Otp__Delivery=Live`, SMTP / Twilio settings, `Cors__Origins`, `ConnectionStrings__Default`.
- Apply `db/patches/*.sql` once; swap local photo storage for Azure Blob before taking real photos.
- Swagger and `/openapi/v1.json` are Development only.
