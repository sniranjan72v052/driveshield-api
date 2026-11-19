-- Adds the SuperAdmin role and the admin_granted flag (admins added inside the app by a super admin).
-- Safe to run more than once. Keep this file free of curly braces: the API runs it through a format-sensitive call.

ALTER TABLE users ADD COLUMN IF NOT EXISTS admin_granted boolean NOT NULL DEFAULT false;

ALTER TABLE users DROP CONSTRAINT IF EXISTS users_role_check;
ALTER TABLE users ADD CONSTRAINT users_role_check CHECK (role IN ('Customer', 'Provider', 'Admin', 'SuperAdmin'));
