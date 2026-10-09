-- Migration v2.0 — Product Versions
-- Target: licensing_app on 172.16.101.12
-- Run ONCE before deploying v2.0 binaries. Wrapped in a transaction; safe to re-run
-- (idempotent guards on every step).
--
-- What it does:
--   1. Creates the product_versions table (1:N with software_products).
--   2. Seeds one "Default" version per existing product.
--   3. Adds licenses.version_id, backfills it to each product's default version,
--      then makes it NOT NULL with an FK + index.
-- Every existing license ends up pointing at its product's default version, so pre-v2.0
-- clients (which never send VersionId) keep activating unchanged.

BEGIN;

-- 1. product_versions table
CREATE TABLE IF NOT EXISTS product_versions (
    id              uuid         NOT NULL DEFAULT gen_random_uuid(),
    product_id      uuid         NOT NULL,
    name            varchar(200) NOT NULL,
    created_at_utc  timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT product_versions_pkey PRIMARY KEY (id),
    CONSTRAINT product_versions_product_id_fkey
        FOREIGN KEY (product_id) REFERENCES software_products(id) ON DELETE RESTRICT
);
CREATE INDEX IF NOT EXISTS ix_product_versions_product_id ON product_versions(product_id);

-- 2. One default version per existing product (only for products that have none yet)
CREATE TEMP TABLE _version_map AS
SELECT p.id AS product_id, gen_random_uuid() AS version_id
FROM software_products p
WHERE NOT EXISTS (
    SELECT 1 FROM product_versions pv WHERE pv.product_id = p.id
);

INSERT INTO product_versions (id, product_id, name, created_at_utc)
SELECT version_id, product_id, 'Default', now()
FROM _version_map;

-- 3. licenses.version_id — add nullable, backfill, then constrain
ALTER TABLE licenses ADD COLUMN IF NOT EXISTS version_id uuid;

UPDATE licenses l
SET version_id = vm.version_id
FROM _version_map vm
WHERE l.product_id = vm.product_id
  AND l.version_id IS NULL;

ALTER TABLE licenses ALTER COLUMN version_id SET NOT NULL;
ALTER TABLE licenses DROP CONSTRAINT IF EXISTS licenses_version_id_fkey;
ALTER TABLE licenses
    ADD CONSTRAINT licenses_version_id_fkey
        FOREIGN KEY (version_id) REFERENCES product_versions(id) ON DELETE RESTRICT;
CREATE INDEX IF NOT EXISTS ix_licenses_version_id ON licenses(version_id);

DROP TABLE IF EXISTS _version_map;

COMMIT;

-- Verification (run after commit)
SELECT 'product_versions rows'      AS metric, count(*)::text AS value FROM product_versions
UNION ALL
SELECT 'licenses without version_id', count(*)::text FROM licenses WHERE version_id IS NULL;
