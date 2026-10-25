# DriveShield web app (React + TypeScript)

A mobile-friendly website for the three roles. It talks to the ASP.NET Core API and gets live progress
over SignalR. Maps use Leaflet with OpenStreetMap (free, no key needed).

> Status: written without being able to install packages or build in the authoring environment,
> so it has **not been built or run yet**. If `npm run build` reports a type error, send it back.

## Run it

1. Install Node.js 20 or newer (the LTS version from nodejs.org).
2. Start the API in Visual Studio (F5) so it listens on `http://localhost:5000`.
3. In this `web` folder:
   ```
   npm install
   npm run dev
   ```
4. Open `http://localhost:5173`.

If your API uses another address, change `VITE_API_URL` in `.env.development`.

## See the whole flow (about 5 minutes)

Use two browser windows, so you can be two people at once. Use a private/incognito window for the second one,
because the sign-in is remembered per browser.

Signing in: enter an email or mobile number and press **Send me a code**. In Development the code is shown on screen
(and written to the API log), so no email or SMS setup is needed. Use different emails for each person, for example
`raj@example.com`, `asha@example.com`, and the admin email you put in `Auth:AdminEmails` in
`appsettings.Development.json`.

1. **Window A, provider:** sign in as `raj@example.com`. Open **Provider**, fill in the registration, tap
   **Use my location** (or tap the map), choose a service such as *Roadside assistance*, and register.
2. **Window B, admin:** sign in with the admin email, open **Admin**, and approve Raj's business.
3. **Window A:** the page says approved. Turn on **You're available for requests**.
4. **Window C (or sign out of B), customer:** sign in as `asha@example.com`, choose **Breakdown**, place the pin near the
   provider's pin, and press **Get help now**. The case page opens with the progress route.
5. **Window A:** the request appears under **Requests for you** within a second or two. Press **Accept**.
6. **Window C:** the route moves to *Provider accepted* by itself. In Window A press **I'm on my way**, then
   **Start the work**, then **Mark as resolved**, and watch Window C follow each step.
7. **Window C:** a review form appears. Give stars and submit.

Location needs `localhost` or https. On a phone over your Wi-Fi (plain http) the browser will refuse "Use my location",
so tap the map instead, or use the deployed https site later.

## What is where

```
src/api/          API client, types, labels
src/hooks/        useLive.ts  (SignalR: live case updates, provider inbox)
src/components/   MapView, ProgressRoute (the route tracker), Icons
src/pages/        Home, Report, MyCases, CasePage, ProviderHome, Admin, AdminPhotos, SignIn, Account
src/styles.css    colours, type, layout
```

## Before going live

- Set a real `Auth:Jwt:SigningKey`, `Auth:Otp:Delivery` = `Live`, and the SMTP / Twilio settings in the API.
- Deploy the built site (`npm run build`, output in `dist/`) to Azure Static Web Apps, and add its address to
  `Cors:Origins` in the API settings.

## Photos and comments

Under every step of the progress route, the customer, the provider who accepted the case, and admins see
**Add a photo or comment**. Photos can come from the camera or gallery (up to 5 at a time) and are shrunk in the
browser before upload. The customer can also attach photos on the "What happened?" form; they go in as the first
comment on the *Reported* step.

To try it: after the provider accepts (step 5 of the walkthrough above), open the case in both windows, add a photo
and a comment under *Provider accepted* in one window, and watch them appear in the other without a refresh.
Restart the API first so it creates the new tables.

## Edit, delete and remove

On a case page, your own comments show **Edit** and **Delete** (while the case is open). Open a photo to delete it.
Admins see **Remove** on anyone's post or photo and must enter a reason; others then see "Removed by an admin" with
the reason. Admin > **Photos** shows recent photos from all cases. The **Account** page (tap your name) lets you add
the missing email or mobile so both sign in to the same account.

## Admin screens

Sign in with an admin email, then open **Admin**. The menu has:

- **Requests**: every request with its status and the provider on it. Filter by Open, Needs our team, With a provider,
  Resolved or Cancelled, or search by customer. Press **Manage** on a request to assign, unassign or reassign a provider.
  In the provider list, the **Within 5 / 10 / 15 / 20 / 30 / 50 km** buttons narrow it to providers near the request,
  nearest first, and you can limit it to providers who are available now.
- **Customers**: who has asked for help, how many requests, how many are open. **View requests** shows one customer's requests.
- **Providers**: all providers with approval status, availability and jobs. **View jobs** shows one provider's requests.
- **Approvals** and **Photos**: as before.

When reporting a problem, a customer can add up to 2 photos.

## Vehicles

A new customer is asked for their vehicle right after their name (they can skip it). Providers and admins never see the vehicle pages. Vehicles are managed under
**Your account > Your vehicles** (`/vehicles`). The "report a problem" form lets the customer pick one; the provider and
support see it on the case page, and support also sees it on **Admin > Requests > Manage**.

## Look and feel, and the home page

The whole app uses one style: white pages, deep navy text, crimson buttons, rounded cards (fonts: Plus Jakarta Sans and
DM Sans). Colours and sizes are the variables at the top of `src/styles.css`.

- **Home** (`/`): hero, how it works, incident types, services, and a "for providers" banner. Visitors see Sign in,
  Join as a customer and Register as provider; signed-in people see Get help now, My cases and the provider dashboard
  (admins see shortcuts to Requests, Providers and Approvals).
- **Get help** (the report form) is now at `/help`.
- Signed-out visitors who open a page that needs an account (for example Provider) are sent to sign in and then
  carried on to that page.
- Run `npm install` again after updating: the fonts changed.
