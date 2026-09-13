-- Initial schema for the Licensing System, matching AppDbContext's fluent configuration.
-- This is a hand-written reference matching the EF Core model 1:1 — useful to inspect
-- or run directly, but the actual migration your project uses should come from
-- `dotnet ef migrations add InitialCreate` (see README.md) so it stays in sync with
-- the model as it evolves.

CREATE EXTENSION IF NOT EXISTS "pgcrypto"; -- for gen_random_uuid()

CREATE TABLE software_products (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name                     varchar(200) NOT NULL,
    vendor                   varchar(200) NOT NULL,
    current_version          varchar(50),
    default_license_model    integer NOT NULL DEFAULT 1,   -- LicenseModel flags, 1 = Machine
    default_max_activations  integer NOT NULL DEFAULT 5,
    created_at_utc           timestamptz NOT NULL DEFAULT now(),
    updated_at_utc           timestamptz
);
CREATE INDEX ix_software_products_name ON software_products (name);

CREATE TABLE licenses (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    product_id               uuid NOT NULL REFERENCES software_products (id) ON DELETE RESTRICT,
    license_key              varchar(50) NOT NULL,
    model_snapshot           integer NOT NULL,
    max_activations          integer NOT NULL DEFAULT 5,
    subscription_expiry_utc  timestamptz,
    status                   varchar(20) NOT NULL DEFAULT 'Active',   -- Active | Revoked | Expired
    signature                bytea NOT NULL,
    customer_email           varchar(320),
    customer_name            varchar(200),
    created_at_utc           timestamptz NOT NULL DEFAULT now(),
    revoked_at_utc           timestamptz,
    revoked_reason           text,
    is_archived              boolean NOT NULL DEFAULT false
);
CREATE UNIQUE INDEX ux_licenses_license_key ON licenses (license_key);
CREATE INDEX ix_licenses_product_id ON licenses (product_id);
CREATE INDEX ix_licenses_status ON licenses (status);
CREATE INDEX ix_licenses_subscription_expiry_utc ON licenses (subscription_expiry_utc);
CREATE INDEX ix_licenses_is_archived ON licenses (is_archived);

CREATE TABLE activations (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    license_id               uuid NOT NULL REFERENCES licenses (id) ON DELETE CASCADE,
    install_guid             uuid NOT NULL,
    cpu_id                   varchar(200) NOT NULL,
    motherboard_serial       varchar(200) NOT NULL,
    tpm_id                   varchar(200) NOT NULL,
    mac_address_primary      varchar(32) NOT NULL,
    os_type                  varchar(100),
    os_version               varchar(50),
    cpu_model                varchar(200),
    ram_gb                   integer,
    is_vm                    boolean NOT NULL DEFAULT false,
    vm_signals               varchar(500),
    status                   varchar(20) NOT NULL DEFAULT 'PendingReview', -- PendingReview | Approved | Rejected | Revoked
    first_activated_at_utc   timestamptz NOT NULL DEFAULT now(),
    last_checkin_at_utc      timestamptz,
    review_deadline_utc      timestamptz,
    approved_at_utc          timestamptz,
    rejected_at_utc          timestamptz,
    reviewed_by              varchar(320),
    review_notes             text
);
CREATE UNIQUE INDEX ux_activations_license_install ON activations (license_id, install_guid);
CREATE INDEX ix_activations_status ON activations (status);
CREATE INDEX ix_activations_review_deadline_utc ON activations (review_deadline_utc);
CREATE INDEX ix_activations_hw_fingerprint
    ON activations (license_id, cpu_id, motherboard_serial, tpm_id, mac_address_primary);

CREATE TABLE admin_users (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    email                    varchar(320) NOT NULL,
    password_hash            text NOT NULL,
    role                     varchar(20) NOT NULL DEFAULT 'ReadOnlyViewer', -- SuperAdmin | SupportStaff | ReadOnlyViewer
    is_active                boolean NOT NULL DEFAULT true,
    created_at_utc           timestamptz NOT NULL DEFAULT now(),
    last_login_at_utc        timestamptz
);
CREATE UNIQUE INDEX ux_admin_users_email ON admin_users (email);

CREATE TABLE audit_log_entries (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    actor                    varchar(320) NOT NULL,
    entity_type              varchar(100) NOT NULL,
    entity_id                varchar(100) NOT NULL,
    action                   varchar(100) NOT NULL,
    details_json             jsonb,
    created_at_utc           timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_audit_log_entity ON audit_log_entries (entity_type, entity_id);
CREATE INDEX ix_audit_log_created_at_utc ON audit_log_entries (created_at_utc);

CREATE TABLE notification_configs (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    smtp_host                varchar(255) NOT NULL,
    smtp_port                integer NOT NULL DEFAULT 587,
    encryption               varchar(20) NOT NULL DEFAULT 'StartTls',  -- None | StartTls | ImplicitTls
    auth_type                varchar(20) NOT NULL DEFAULT 'Basic',     -- Basic | ApiKey
    username                 varchar(320) NOT NULL,
    password_encrypted       bytea NOT NULL,
    from_address             varchar(320) NOT NULL,
    last_test_status         varchar(20) NOT NULL DEFAULT 'NeverTested', -- NeverTested | Success | Failed
    last_test_at_utc         timestamptz,
    event_toggles_json       jsonb NOT NULL DEFAULT '{}',
    updated_at_utc           timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- Optional sample data, so the admin front end isn't empty on first run.
-- Safe to delete this block; nothing else depends on it.
-- ---------------------------------------------------------------------------
INSERT INTO software_products (id, name, vendor, current_version, default_license_model, default_max_activations)
VALUES ('11111111-1111-1111-1111-111111111111', 'InvoicePro', 'Acme Software', '1.4.2', 1, 5);

INSERT INTO licenses (id, product_id, license_key, model_snapshot, max_activations, status, signature, customer_email)
VALUES (
    '22222222-2222-2222-2222-222222222222',
    '11111111-1111-1111-1111-111111111111',
    'K3F9-2A7B3-9F2A-11B0-77CD-90AA-XX',
    1, 5, 'Active',
    '\x00',  -- placeholder signature bytes; real keys are signed by LicenseFileService/an admin key tool
    'customer@example.com'
);

INSERT INTO activations (
    id, license_id, install_guid, cpu_id, motherboard_serial, tpm_id, mac_address_primary,
    os_type, os_version, cpu_model, status, first_activated_at_utc, review_deadline_utc
) VALUES (
    '33333333-3333-3333-3333-333333333333',
    '22222222-2222-2222-2222-222222222222',
    '8b1e6f2a-0000-0000-0000-000000000001',
    'BFEBFBFF000A0655', 'MB-9F21-0001', 'TPM-0001', '00:1A:2B:3C:4D:5E',
    'Windows 11 Pro', '10.0.26100', 'Intel Core i7-13700',
    'PendingReview', now(), now() + interval '15 days'
);

