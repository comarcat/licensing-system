-- 002_add_license_archive.sql — E4: manual archive/deactivate for licenses.
-- Additive, backward-compatible: existing rows default to is_archived = false, so
-- nothing already issued changes behavior. Safe to run once against a database
-- already on 001_initial_schema.sql (or the equivalent EF Core migration history).

ALTER TABLE licenses ADD COLUMN IF NOT EXISTS is_archived boolean NOT NULL DEFAULT false;
CREATE INDEX IF NOT EXISTS ix_licenses_is_archived ON licenses (is_archived);
