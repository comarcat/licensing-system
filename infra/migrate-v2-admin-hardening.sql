-- v2.0 admin hardening migration. Idempotent: safe to re-run.
-- Applies every schema change the v2.0 code expects onto an existing prod DB.
-- Order matters: product_versions must exist before licenses.version_id can FK to it.
DO $$
DECLARE
    _rows_without_version integer;
BEGIN
    -- 1) software_products.is_archived
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name='software_products' AND column_name='is_archived') THEN
        ALTER TABLE software_products ADD COLUMN is_archived boolean NOT NULL DEFAULT false;
        CREATE INDEX IF NOT EXISTS ix_software_products_is_archived ON software_products (is_archived);
    END IF;

    -- 2) product_versions table
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name='product_versions') THEN
        CREATE TABLE product_versions (
            id             uuid PRIMARY KEY DEFAULT gen_random_uuid(),
            product_id     uuid NOT NULL REFERENCES software_products(id) ON DELETE RESTRICT,
            name           varchar(200) NOT NULL,
            created_at_utc timestamptz NOT NULL DEFAULT now()
        );
        CREATE INDEX ix_product_versions_product_id ON product_versions (product_id);
    END IF;

    -- 3) licenses.is_archived
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name='licenses' AND column_name='is_archived') THEN
        ALTER TABLE licenses ADD COLUMN is_archived boolean NOT NULL DEFAULT false;
        CREATE INDEX IF NOT EXISTS ix_licenses_is_archived ON licenses (is_archived);
    END IF;

    -- 4) licenses.version_id  (non-nullable FK -> backfill existing rows first)
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns
                   WHERE table_name='licenses' AND column_name='version_id') THEN
        -- Add as nullable so existing rows survive the ALTER.
        ALTER TABLE licenses ADD COLUMN version_id uuid;

        -- Ensure every product that owns a license has at least one default version.
        INSERT INTO product_versions (id, product_id, name, created_at_utc)
        SELECT gen_random_uuid(), p.id, 'Default', now()
        FROM software_products p
        WHERE EXISTS (SELECT 1 FROM licenses l WHERE l.product_id = p.id)
          AND NOT EXISTS (SELECT 1 FROM product_versions v WHERE v.product_id = p.id);

        -- Backfill: point each license at its product's earliest version.
        UPDATE licenses l
        SET version_id = (
            SELECT v.id FROM product_versions v
            WHERE v.product_id = l.product_id
            ORDER BY v.created_at_utc ASC
            LIMIT 1
        )
        WHERE l.version_id IS NULL;

        -- Fail loudly rather than lock a broken NOT NULL if any row is still unmapped.
        SELECT count(*) INTO _rows_without_version FROM licenses WHERE version_id IS NULL;
        IF _rows_without_version > 0 THEN
            RAISE EXCEPTION 'Cannot enforce NOT NULL on licenses.version_id: % rows unmapped', _rows_without_version;
        END IF;

        ALTER TABLE licenses ALTER COLUMN version_id SET NOT NULL;
        ALTER TABLE licenses ADD CONSTRAINT fk_licenses_version
            FOREIGN KEY (version_id) REFERENCES product_versions(id) ON DELETE RESTRICT;
        CREATE INDEX IF NOT EXISTS ix_licenses_version_id ON licenses (version_id);
    END IF;

    -- 5) email_log_entries table
    IF NOT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name='email_log_entries') THEN
        CREATE TABLE email_log_entries (
            id               uuid PRIMARY KEY,
            recipient_domain varchar(255) NOT NULL,
            status           varchar(100) NOT NULL,
            email_type       varchar(50) NOT NULL,
            created_at_utc   timestamp NOT NULL
        );
        CREATE INDEX ix_email_log_entries_created_at_utc ON email_log_entries (created_at_utc);
    END IF;
END $$;
