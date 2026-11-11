-- Adds sign-in by email or mobile number: the two sign-in columns on users, the one-time codes, and refresh tokens.
-- Safe to run more than once. In Development the API runs every file in db/patches at startup
-- (when Database:AutoCreate is true). Otherwise run this script once in pgAdmin on the driveshield database.
-- Keep this file free of curly braces: the API runs it through a format-sensitive call.

ALTER TABLE users ADD COLUMN IF NOT EXISTS email       varchar(254);
ALTER TABLE users ADD COLUMN IF NOT EXISTS login_phone varchar(20);

-- each email or number belongs to at most one account
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email       ON users (email)       WHERE email IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_users_login_phone ON users (login_phone) WHERE login_phone IS NOT NULL;

-- one row per code sent; only a keyed hash of the code is stored
CREATE TABLE IF NOT EXISTS otp_challenges (
    id           uuid PRIMARY KEY,
    channel      varchar(10)  NOT NULL,
    destination  varchar(254) NOT NULL,
    purpose      varchar(10)  NOT NULL,
    user_id      uuid,
    code_hash    varchar(64)  NOT NULL,
    created_at   timestamptz  NOT NULL DEFAULT now(),
    expires_at   timestamptz  NOT NULL,
    attempts     integer      NOT NULL DEFAULT 0,
    consumed_at  timestamptz
);
CREATE INDEX IF NOT EXISTS ix_otp_challenges_destination_created_at ON otp_challenges (destination, created_at);

-- one row per signed-in device; only a hash of the token is stored
CREATE TABLE IF NOT EXISTS refresh_tokens (
    id              uuid PRIMARY KEY,
    user_id         uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash      varchar(64) NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    expires_at      timestamptz NOT NULL,
    revoked_at      timestamptz,
    replaced_by_id  uuid
);
CREATE UNIQUE INDEX IF NOT EXISTS ux_refresh_tokens_token_hash ON refresh_tokens (token_hash);
CREATE INDEX IF NOT EXISTS ix_refresh_tokens_user_id ON refresh_tokens (user_id);
