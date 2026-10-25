# Future DriveShield 2047 - backend (steps 1 and 2)

ASP.NET Core (.NET 9) API for the vehicle-incident marketplace: customers report an incident, nearby
providers get offers, and the customer follows live progress and reviews the service afterwards.

> Status: written without access to a .NET SDK, so it has **not been compiled or run yet**.
> Expect to fix a few small compile or package-version errors on first build.

## What is here

```
db/schema.sql                         PostgreSQL + PostGIS schema (step 1)
src/DriveShield.Api/
  Domain/         Enums.cs, Entities.cs                  entities (step 1)
  Infrastructure/ AppDbContext.cs                        EF Core mapping, geography columns, xmin concurrency
  Application/    CaseStateMachine.cs                    allowed transitions + who may do them (step 2)
                  CaseWorkflowService.cs                 transition + timeline + matching + SignalR publish
                  OfferExpiryWorker.cs                   expires offers, escalates unanswered cases
  Endpoints/      Case / Provider / Admin endpoints      minimal APIs
  Hubs/CaseHub.cs                                        SignalR hub at /hubs/case
  Auth/           UserResolver.cs, DevAuthHandler.cs     JWT in production, header auth in local dev
```

## Web app

The React website lives in `web/` (see `web/README.md`). With the API running, run `npm install` then `npm run dev` inside
`web/` and open http://localhost:5173.

## Run locally

1. Start PostgreSQL with PostGIS:
   ```
   docker run --name driveshield-db -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgis/postgis:16-3.4
   ```
2. Run the API (creates the `driveshield` database and tables automatically in Development).
   Visual Studio: open `DriveShield.sln` and press F5. Command line:
   ```
   dotnet run --project src/DriveShield.Api
   ```
   Both use `Properties/launchSettings.json`, which sets `ASPNETCORE_ENVIRONMENT=Development` and the URL `http://localhost:5000`.
   Prefer `db/schema.sql`? Set `Database:AutoCreate` to `false`, run the script, then start the API.
   Use either the script or EF migrations, never both.
3. Sign-in (email or mobile, one-time code). Details in "Sign-in" below. For curl in Development you can still skip it with
   the header `X-Dev-User: <any name>`; `dev-admin` is seeded as an admin. (Development only.)

## Try the whole flow

```bash
API=http://localhost:5000   # use the port printed by dotnet run

# 1. provider registers (Pune coordinates as an example), admin approves, provider goes online
curl -X POST $API/api/providers/register -H "X-Dev-User: tow-raj" -H "Content-Type: application/json" \
  -d '{"businessName":"Raj Towing","phone":"+91 90000 00000","categories":["TowingOnSite","RoadsideAssistance"],"latitude":18.5204,"longitude":73.8567}'
curl $API/api/admin/providers/pending -H "X-Dev-User: dev-admin"          # copy the provider id
curl -X POST $API/api/admin/providers/<providerId>/approve -H "X-Dev-User: dev-admin"
curl -X PUT $API/api/providers/me/availability -H "X-Dev-User: tow-raj" -H "Content-Type: application/json" -d '{"isAvailable":true}'

# 2. customer reports a breakdown nearby
curl -X POST $API/api/cases -H "X-Dev-User: asha" -H "Content-Type: application/json" \
  -d '{"incidentType":"Breakdown","latitude":18.53,"longitude":73.86,"description":"Car will not start"}'

# 3. provider sees the offer and accepts
curl $API/api/providers/me/offers -H "X-Dev-User: tow-raj"                 # copy offerId and caseId
curl -X POST $API/api/cases/<caseId>/offers/<offerId>/accept -H "X-Dev-User: tow-raj"

# 4. provider moves the case along; customer reads the timeline
curl -X POST $API/api/cases/<caseId>/status -H "X-Dev-User: tow-raj" -H "Content-Type: application/json" -d '{"status":"EnRoute","note":"On my way, 10 minutes"}'
curl -X POST $API/api/cases/<caseId>/status -H "X-Dev-User: tow-raj" -H "Content-Type: application/json" -d '{"status":"InProgress"}'
curl -X POST $API/api/cases/<caseId>/status -H "X-Dev-User: tow-raj" -H "Content-Type: application/json" -d '{"status":"Resolved","note":"Battery replaced"}'
curl $API/api/cases/<caseId> -H "X-Dev-User: asha"

# 5. customer reviews the provider
curl -X POST $API/api/cases/<caseId>/providers/<providerId>/review -H "X-Dev-User: asha" -H "Content-Type: application/json" \
  -d '{"rating":5,"comment":"Fast and fair","tags":["On time","Professional"]}'
```

## Live progress (SignalR) from React

```ts
import { HubConnectionBuilder } from "@microsoft/signalr";

const conn = new HubConnectionBuilder()
  .withUrl("/hubs/case", { accessTokenFactory: () => token })   // dev: "/hubs/case?dev_user=asha"
  .withAutomaticReconnect()
  .build();

conn.on("CaseStatusChanged", (e) => /* { caseId, status, note, actor, at } -> update the stepper */ {});
await conn.start();
await conn.invoke("JoinCase", caseId);      // customer or provider on the case
// providers also: await conn.invoke("JoinProviderInbox");  and listen for "NewOffer"
```
After a reconnect, call `GET /api/cases/{id}/timeline` to catch up on anything missed.

## Endpoints

| Method | Path | Who |
|---|---|---|
| POST | /api/cases | customer reports an incident (auto assess + match) |
| GET | /api/cases/mine | customer's cases |
| GET | /api/cases/{id} | customer, admin, or provider holding an offer (detail + timeline + allowed next statuses) |
| GET | /api/cases/{id}/timeline | same |
| POST | /api/cases/{id}/status | customer (cancel), accepted provider, admin; checked by the state machine |
| POST | /api/cases/{id}/offers/{offerId}/accept or /decline | provider |
| POST | /api/cases/{id}/providers/{providerId}/review | customer, after Resolved |
| POST | /api/providers/register | any signed-in user |
| PUT | /api/providers/me/location, /me/availability | provider |
| GET | /api/me | signed-in user and role |
| GET | /api/providers/me | provider's own profile and approval status |
| GET | /api/providers/me/offers | provider (polling fallback) |
| GET | /api/providers/me/jobs | cases the provider accepted |
| GET | /api/providers/nearby?lat=&lng=&category=&radiusMeters= | any signed-in user |
| GET | /api/admin/providers/pending | admin |
| POST | /api/admin/providers/{id}/approve or /reject | admin |
| POST | /api/admin/cases/{id}/rematch | admin (escalated cases, optional wider radius) |

## Workflow rules

`Reported -> Assessed -> ProviderMatched -> ProviderAccepted -> EnRoute -> InProgress -> Resolved -> Reviewed`,
plus `Cancelled` and `Escalated`. The full table is in `Application/CaseStateMachine.cs`.

- Incident type picks default services (edit `DefaultCategories` in `CaseEndpoints.cs`); the customer can send their own `categories`.
- One offer per provider per service. First to accept wins; other open offers for that service are withdrawn.
  A unique index and the case's `xmin` concurrency token stop two providers accepting at once.
- Offers expire after `Matching:OfferTtlSeconds`; if nobody is left, the case becomes `Escalated` and an admin can re-match.
- Providers see only an approximate location (~100 m) until they accept.

## Known limits of this first cut

- The case has one status shared by all providers. Per-provider progress (tow truck EnRoute while the insurance
  helper is still working) is the next extension: a status column on `case_providers`.
- No automated tests, migrations, notifications (push/SMS), provider review replies or admin moderation yet.
- Provider location updates do not yet stream to the customer's live map.
- Azure: allow-list the PostGIS extension on Azure Database for PostgreSQL (server parameter `azure.extensions`),
  and configure `Auth:Authority` / `Auth:Audience` for Microsoft Entra External ID before deploying.

## Photos and comments on each step

The customer, a provider who has accepted the case, and admins can add a comment and up to 5 photos to any step the
case has reached (Reported, Case assessed, Provider accepted, Provider on the way, and so on). Everyone on the case
sees them under that step, and new ones appear live. A provider who has only been *offered* the case sees none of it
until they accept.

| Method | Path | Notes |
|---|---|---|
| POST | /api/cases/{id}/comments | multipart form: `step` (optional, defaults to the latest step), `body` (optional), `photos` (repeat for several files) |
| GET | /api/cases/{id}/photos/{photoId} | the image; needs sign-in, so the apps download it with their auth header |

`GET /api/cases/{id}` now also returns `canComment` and `comments`. Live event: `CommentAdded { caseId, step }`.

- **Checks:** JPEG, PNG or WebP only (checked from the file's own bytes), max 5 MB each, 5 per post, 60 per case.
- **Storage:** files go to `src/DriveShield.Api/App_Data/uploads` (git-ignored) through `IPhotoStore`; the database keeps
  only the key. For production, write an Azure Blob Storage implementation of `IPhotoStore` and register it in `Program.cs`.
- **Privacy:** the website shrinks photos to 1600 px and re-saves them, which also removes hidden metadata such as GPS.

**Upgrading an existing database:** two new tables, `case_comments` and `case_photos`. In Development the API creates them
at startup from `db/patches/001_case_comments_photos.sql` (the project copies that folder next to the app). If you set
`Database:AutoCreate` to false, run that script once in pgAdmin on the `driveshield` database.

## Sign-in (customers and providers)

Everyone signs in the same way: enter an email address or mobile number, receive a 6-digit code, type it in.
There are no passwords. Customers and providers share one account type; a person becomes a provider by registering a
business on the Provider page.

- `POST /api/auth/otp/start` `{identifier}` then `POST /api/auth/otp/verify` `{challengeId, code}` returns a 15-minute
  access token and a 30-day refresh token (rotated on every use; reuse of an old one signs the session out).
- `POST /api/auth/refresh`, `POST /api/auth/logout`.
- `POST /api/auth/link/start` and `/link/verify` (signed in) add the missing email or mobile to the same account.
  The web **Account** page uses these. If someone uses email and mobile without linking, they get two accounts.
- Mobile numbers without a country code are treated as India (+91).

Settings (`appsettings*.json`):

| Setting | Purpose |
|---|---|
| `Auth:Jwt:SigningKey` | Secret, at least 32 bytes. Production refuses to start with the dev key. |
| `Auth:AdminEmails` | Emails that become admins when they sign in. Set yours in `appsettings.Development.json`. |
| `Auth:Otp:Delivery` | `Console` (code is written to the API log; Development only) or `Live` (real email/SMS). |
| `Auth:Otp:ExposeDevCode` | Development only: the web sign-in page shows the code so you need no email or SMS. |
| `Auth:Smtp:*` | Email delivery (host, port, user, password, from). |
| `Auth:Twilio:*` | SMS delivery (account SID, auth token, from number). |

Restart the API after pulling this update: it applies `db/patches/002_post_moderation.sql` and `003_sign_in.sql`.
Test accounts created by the old name-only sign-in are not reachable from the web app any more; sign in fresh.

## Editing, deleting and moderating posts

- Authors can **edit** their own comments (shown as "edited") and **delete** their own comments and photos, while the
  case is still open. Deleted photo files are removed from storage immediately.
- Admins can **remove** any comment or photo (never rewrite it). A reason is required; people see a placeholder that
  says it was removed and why. Removal is recorded (who, when, reason).
- Admin page **Photos** lists recent photos from every case for review.
- Endpoints: `PATCH /api/cases/{id}/comments/{commentId}`, `DELETE .../comments/{commentId}?reason=`,
  `DELETE .../photos/{photoId}?reason=`, `GET /api/admin/photos`.

Known limits: no "report this photo" button for users yet; photos are stored on local disk (swap `IPhotoStore` for
Azure Blob in production); SMS needs a Twilio account; the web refresh token is kept in localStorage.

## Admin: requests, customers, providers, assigning

All under `/api/admin` (admins only).

| Endpoint | Purpose |
|---|---|
| `GET /customers?search=&skip=&take=` | People who have raised a request, with total and open request counts |
| `GET /providers?search=&approval=&skip=&take=` | All providers with approval status, availability, active and finished jobs |
| `GET /cases?status=&customerId=&providerId=&search=` | Every request with status, customer and who is assigned. `status`: `all`, `open` (default in the web app), `attention` (nobody found), `active`, `resolved`, `Cancelled`, or an exact status name |
| `GET /cases/{id}` | One request: customer, status, and each needed service with its assigned provider |
| `GET /cases/{id}/candidates?category=&radiusKm=15&availableOnly=` | Approved providers offering that service within the range, nearest first. The web app offers 5, 10, 15, 20, 30 and 50 km |
| `POST /cases/{id}/assign` `{category, providerId, replace, note}` | Put a provider on a service. `replace: true` swaps out the current one |
| `POST /cases/{id}/unassign` `{category, reason}` | Take the provider off a service |

How assigning moves the request:
- Assigning to an unassigned request (waiting, or "needs our team") puts it at *Provider accepted*. The provider sees it in
  their jobs straight away. Any other providers still being asked for that service are withdrawn.
- Unassigning the last provider moves the request back to *Needs our team* so another can be chosen. Unassigning one of
  several providers leaves the status alone.
- Reassigning while the first provider was travelling or working puts the request back to *Provider accepted*, because the
  new provider has not started.
- Not allowed once a request is resolved, reviewed or cancelled, or for a provider who is not approved, does not offer
  the service, or is the customer.
- Every change adds a line to the timeline the customer sees.

Photos at the request stage: a customer can attach at most 2 photos when reporting (enforced by the API on the
*Reported* step). Other steps still allow 5 per post.

## Vehicles

Customers save their vehicle(s) once; it is attached to each request they raise.

- `GET/POST /api/vehicles`, `PUT/DELETE /api/vehicles/{id}`: make, model, registration number (required, e.g. `MH12 AB 1234`),
  electric yes/no, and optional insurer and policy number. Up to 10 per person; no duplicate registration numbers.
- `POST /api/cases` takes `vehicleId`. `GET /api/cases/{id}` and `GET /api/admin/cases/{id}` return the `vehicle`.
  The vehicle (including insurance details) is shown only to the customer, admins, and providers who have accepted
  or been assigned the request, never to providers who were only offered it.
- `GET /api/me` now includes `vehicleCount`.
- Deleting a vehicle keeps past requests; they just stop showing a vehicle.
- No database change is needed: the `vehicles` table was already in the schema.

## Keeping your own settings (admin email)

Put your own settings in `src/DriveShield.Api/appsettings.Local.json` (copy `appsettings.Local.json.example`). It is read
after the other settings files and is never part of an update, so unzipping a new version cannot reset it. Example:

```json
{ "Auth": { "AdminEmails": [ "you@example.com" ] } }
```

If you put the admin email in `appsettings.Development.json` instead, replacing the project files resets it, and the next
time you sign in your account goes back to a normal one (the Admin menu disappears).

Saved vehicles are for customers only (accounts that are not providers or admins).

Admin rights are checked on every request against `Auth:AdminEmails`, so after editing the list and restarting the API
you only need to reload the web page (no sign-out needed). The API log says "Admin emails configured: N" at startup.
Admin is matched on the verified **email**: a mobile-number-only sign-in is never an admin until an email is added under
Your account. The Your account page shows which email you are signed in with and your account type.
