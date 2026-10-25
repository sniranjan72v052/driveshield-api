-- Adds per-step comments and photos to an existing DriveShield database.
-- Safe to run more than once. In Development the API runs every file in db/patches automatically at startup
-- (when Database:AutoCreate is true). Otherwise run this script once in pgAdmin on the driveshield database.
-- Keep this file free of curly braces: the API runs it through a format-sensitive call.

CREATE TABLE IF NOT EXISTS case_comments (
    id              uuid PRIMARY KEY,
    case_id         uuid        NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    step            varchar(30) NOT NULL,
    author_user_id  uuid        NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    actor           varchar(20) NOT NULL,
    body            varchar(1000),
    created_at      timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_case_comments_case_id_created_at ON case_comments (case_id, created_at);

CREATE TABLE IF NOT EXISTS case_photos (
    id                   uuid PRIMARY KEY,
    case_id              uuid         NOT NULL REFERENCES cases(id) ON DELETE CASCADE,
    comment_id           uuid         NOT NULL REFERENCES case_comments(id) ON DELETE CASCADE,
    uploaded_by_user_id  uuid         NOT NULL,
    content_type         varchar(50)  NOT NULL,
    size_bytes           integer      NOT NULL,
    storage_key          varchar(100) NOT NULL,
    created_at           timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_case_photos_case_id ON case_photos (case_id);
CREATE INDEX IF NOT EXISTS ix_case_photos_comment_id ON case_photos (comment_id);
