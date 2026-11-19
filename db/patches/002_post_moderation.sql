-- Adds edit, delete and admin-removal tracking to comments and photos.
-- Safe to run more than once. In Development the API runs every file in db/patches at startup
-- (when Database:AutoCreate is true). Otherwise run this script once in pgAdmin on the driveshield database.
-- Keep this file free of curly braces: the API runs it through a format-sensitive call.

ALTER TABLE case_comments ADD COLUMN IF NOT EXISTS edited_at          timestamptz;
ALTER TABLE case_comments ADD COLUMN IF NOT EXISTS deleted_at         timestamptz;
ALTER TABLE case_comments ADD COLUMN IF NOT EXISTS deleted_by_user_id uuid;
ALTER TABLE case_comments ADD COLUMN IF NOT EXISTS deleted_by_actor   varchar(20);
ALTER TABLE case_comments ADD COLUMN IF NOT EXISTS removal_reason     varchar(300);

ALTER TABLE case_photos ADD COLUMN IF NOT EXISTS removed_at         timestamptz;
ALTER TABLE case_photos ADD COLUMN IF NOT EXISTS removed_by_user_id uuid;
ALTER TABLE case_photos ADD COLUMN IF NOT EXISTS removed_by_actor   varchar(20);
ALTER TABLE case_photos ADD COLUMN IF NOT EXISTS removal_reason     varchar(300);

-- the admin review queue reads photos that are still live, newest first
CREATE INDEX IF NOT EXISTS ix_case_photos_active_created_at ON case_photos (created_at) WHERE removed_at IS NULL;
