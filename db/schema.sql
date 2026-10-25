-- Future DriveShield 2047 - PostgreSQL schema (PostgreSQL 15+ with PostGIS)
-- Mirrors the EF Core model in src/DriveShield.Api/Infrastructure/AppDbContext.cs
-- (snake_case naming; enums stored as text). Use EITHER this script OR EF migrations, not both.
--
-- Azure Database for PostgreSQL Flexible Server: first allow-list the extension
-- (server parameter "azure.extensions" -> POSTGIS) before running CREATE EXTENSION.

CREATE EXTENSION IF NOT EXISTS postgis;

-- ---------------------------------------------------------------- users
CREATE TABLE users (
    id            uuid PRIMARY KEY,
    external_id   varchar(200) NOT NULL UNIQUE,          -- the sub claim in our access tokens ("otp-..." for code sign-in)
    display_name  text         NOT NULL DEFAULT '',
    phone         text,                                  -- the number a provider shows customers; not used to sign in
    email         varchar(254),                          -- verified sign-in email, lower case
    login_phone   varchar(20),                           -- verified sign-in mobile number, E.164 (+919876543210)
    role          varchar(20)  NOT NULL DEFAULT 'Customer'
                  CHECK (role IN ('Customer', 'Provider', 'Admin')),
    created_at    timestamptz  NOT NULL DEFAULT now()
);
-- each email or number belongs to at most one account
CREATE UNIQUE INDEX ux_users_email       ON users (email)       WHERE email IS NOT NULL;
CREATE UNIQUE INDEX ux_users_login_phone ON users (login_phone) WHERE login_phone IS NOT NULL;

-- ---------------------------------------------------------------- providers
CREATE TABLE provider_profiles (
    id                    uuid PRIMARY KEY,
    user_id               uuid NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    business_name         text NOT NULL,
    description           text,
    license_number        text,
    location              geography(Point, 4326),         -- last known / base location
    service_radius_meters integer NOT NULL DEFAULT 15000,
    is_available          boolean NOT NULL DEFAULT false,
    approval_status       varchar(20) NOT NULL DEFAULT 'Pending'
                          CHECK (approval_status IN ('Pending', 'Approved', 'Rejected', 'Suspended')),
    average_rating        double precision NOT NULL DEFAULT 0,
    review_count          integer NOT NULL DEFAULT 0,
    created_at            timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_provider_profiles_location ON provider_profiles USING gist (location);

CREATE TABLE provider_services (
    provider_id uuid        NOT NULL REFERENCES provider_profiles(id) ON DELETE CASCADE,
    category    varchar(30) NOT NULL
                CHECK (category IN ('InsuranceClaimHelp', 'TowingOnSite', 'RoadsideAssistance',
                                    'LegalSupport', 'WorkshopGuidance', 'EvVehicleAdvice',
                                    'WarrantyAssistance')),
    PRIMARY KEY (provider_id, category)
);

-- ---------------------------------------------------------------- customers' vehicles
CREATE TABLE vehicles (
    id                   uuid PRIMARY KEY,
    owner_id             uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    make                 text NOT NULL,
    model                text NOT NULL,
    registration_number  text NOT NULL,
    is_electric          boolean NOT NULL DEFAULT false,
    insurer_name         text,
    policy_number        text,
    created_at           timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_vehicles_owner_id ON vehicles (owner_id);

-- ---------------------------------------------------------------- cases (incidents)
CREATE TABLE cases (
    id             uuid PRIMARY KEY,
    customer_id    uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    vehicle_id     uuid REFERENCES vehicles(id) ON DELETE SET NULL,
    incident_type  varchar(30) NOT NULL
                   CHECK (incident_type IN ('Accident', 'Breakdown', 'NaturalCalamity', 'WrongParking')),
    status         varchar(30) NOT NULL DEFAULT 'Reported'
                   CHECK (status IN ('Reported', 'Assessed', 'ProviderMatched', 'ProviderAccepted',
                                     'EnRoute', 'InProgress', 'Resolved', 'Reviewed',
                                     'Cancelled', 'Escalated')),
    description    text,
    address        text,
    location       geography(Point, 4326) NOT NULL,
    created_at     timestamptz NOT NULL DEFAULT now(),
    updated_at     timestamptz NOT NULL DEFAULT now(),
    resolved_at    timestamptz
    -- optimistic concurrency uses PostgreSQL's built-in xmin system column; no extra column needed
);
CREATE INDEX ix_cases_customer_id_created_at ON cases (customer_id, created_at DESC);
CREATE INDEX ix_cases_status ON cases (status);
CREATE INDEX ix_cases_location ON cases USING gist (location);

-- which services the customer needs for this case
CREATE TABLE case_categories (
    case_id  uuid        NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    category varchar(30) NOT NULL,
    PRIMARY KEY (case_id, category)
);

-- offers sent to providers (one row per provider per category)
CREATE TABLE case_providers (
    id              uuid PRIMARY KEY,
    case_id         uuid        NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    provider_id     uuid        NOT NULL REFERENCES provider_profiles(id) ON DELETE CASCADE,
    category        varchar(30) NOT NULL,
    offer_status    varchar(20) NOT NULL DEFAULT 'Offered'
                    CHECK (offer_status IN ('Offered', 'Accepted', 'Declined', 'Expired', 'Withdrawn')),
    offered_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,
    responded_at    timestamptz,
    distance_meters double precision NOT NULL DEFAULT 0
);
CREATE UNIQUE INDEX ux_case_providers_case_provider_category
    ON case_providers (case_id, provider_id, category);
-- at most ONE accepted provider per case + category (guards against two providers accepting at once)
CREATE UNIQUE INDEX ux_case_providers_one_accepted
    ON case_providers (case_id, category) WHERE offer_status = 'Accepted';
CREATE INDEX ix_case_providers_provider_status ON case_providers (provider_id, offer_status);

-- the timeline the customer sees
CREATE TABLE case_status_history (
    id                 uuid PRIMARY KEY,
    case_id            uuid        NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    status             varchar(30) NOT NULL,
    note               text,
    actor              varchar(20) NOT NULL
                       CHECK (actor IN ('Customer', 'Provider', 'Admin', 'System')),
    changed_by_user_id uuid,
    changed_at         timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_case_status_history_case_changed ON case_status_history (case_id, changed_at);

-- ---------------------------------------------------------------- reviews
CREATE TABLE reviews (
    id             uuid PRIMARY KEY,
    case_id        uuid NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    provider_id    uuid NOT NULL REFERENCES provider_profiles(id) ON DELETE CASCADE,
    customer_id    uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    rating         integer NOT NULL CONSTRAINT ck_reviews_rating CHECK (rating BETWEEN 1 AND 5),
    tags           text[]  NOT NULL DEFAULT '{}',
    comment        text,
    provider_reply text,
    is_hidden      boolean NOT NULL DEFAULT false,   -- set by admin moderation
    created_at     timestamptz NOT NULL DEFAULT now(),
    UNIQUE (case_id, provider_id)                    -- one review per case per provider
);
CREATE INDEX ix_reviews_provider_id ON reviews (provider_id);

-- ---------------------------------------------------------------- per-step comments and photos
-- (also available as db/patches/001_case_comments_photos.sql for databases created before this was added)
CREATE TABLE case_comments (
    id              uuid PRIMARY KEY,
    case_id         uuid        NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    step            varchar(30) NOT NULL,
    author_user_id  uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    actor           varchar(20) NOT NULL,
    body            varchar(1000),
    created_at      timestamptz NOT NULL DEFAULT now(),
    edited_at           timestamptz,                  -- set when the author changes the text
    deleted_at          timestamptz,                  -- a deleted post keeps its row as a placeholder; text and files are gone
    deleted_by_user_id  uuid,
    deleted_by_actor    varchar(20),                  -- Admin = removed by support, otherwise deleted by its author
    removal_reason      varchar(300)
);
CREATE INDEX ix_case_comments_case_id_created_at ON case_comments (case_id, created_at);

CREATE TABLE case_photos (
    id                   uuid PRIMARY KEY,
    case_id              uuid         NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    comment_id           uuid         NOT NULL REFERENCES case_comments(id) ON DELETE CASCADE,
    uploaded_by_user_id  uuid         NOT NULL,
    content_type         varchar(50)  NOT NULL,
    size_bytes           integer      NOT NULL,
    storage_key          varchar(100) NOT NULL,   -- where the file lives in the photo store; the file is not in the database
    created_at           timestamptz  NOT NULL DEFAULT now(),
    removed_at           timestamptz,                 -- set when deleted by its author or removed by an admin; the file is deleted
    removed_by_user_id   uuid,
    removed_by_actor     varchar(20),
    removal_reason       varchar(300)
);
CREATE INDEX ix_case_photos_case_id ON case_photos (case_id);
CREATE INDEX ix_case_photos_comment_id ON case_photos (comment_id);
-- the admin photo review queue reads photos that are still live, newest first
CREATE INDEX ix_case_photos_active_created_at ON case_photos (created_at) WHERE removed_at IS NULL;

-- ---------------------------------------------------------------- sign-in by email or mobile number
-- (also available as db/patches/003_sign_in.sql for databases created before this was added)
CREATE TABLE otp_challenges (
    id           uuid PRIMARY KEY,
    channel      varchar(10)  NOT NULL,               -- Email or Phone
    destination  varchar(254) NOT NULL,               -- lower-case email, or E.164 number
    purpose      varchar(10)  NOT NULL,               -- SignIn or Link
    user_id      uuid,                                -- Link only: the signed-in user adding this email or number
    code_hash    varchar(64)  NOT NULL,               -- keyed hash; the code itself is never stored
    created_at   timestamptz  NOT NULL DEFAULT now(),
    expires_at   timestamptz  NOT NULL,
    attempts     integer      NOT NULL DEFAULT 0,
    consumed_at  timestamptz
);
CREATE INDEX ix_otp_challenges_destination_created_at ON otp_challenges (destination, created_at);

CREATE TABLE refresh_tokens (
    id              uuid PRIMARY KEY,
    user_id         uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash      varchar(64) NOT NULL,             -- the token itself is never stored
    created_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,
    revoked_at      timestamptz,
    replaced_by_id  uuid
);
CREATE UNIQUE INDEX ux_refresh_tokens_token_hash ON refresh_tokens (token_hash);
CREATE INDEX ix_refresh_tokens_user_id ON refresh_tokens (user_id);
