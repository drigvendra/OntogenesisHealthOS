-- Tenant entitlements to licensable features ("<productCode>:<feature>" from module manifests).
-- Which plan or contract granted a feature is commercial data held outside this table; code only ever
-- asks "is feature X active for this tenant?". Expiry never deletes data: after valid_to the feature
-- becomes read-only (grace period, then read-only for retention/export).
CREATE TABLE core_licensing.entitlement (
    tenant_id   uuid NOT NULL,
    feature     varchar(128) NOT NULL CHECK (feature ~ '^[A-Z][A-Z0-9_]{1,31}:[a-z][a-z0-9.-]{0,95}$'),
    valid_from  timestamptz NOT NULL,
    valid_to    timestamptz NULL,
    grace_days  integer NOT NULL DEFAULT 30 CHECK (grace_days BETWEEN 0 AND 365),
    source      varchar(16) NOT NULL DEFAULT 'manual' CHECK (source IN ('manual', 'trial', 'subscription')),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    updated_by  uuid NULL,
    PRIMARY KEY (tenant_id, feature),
    CONSTRAINT ck_entitlement_window CHECK (valid_to IS NULL OR valid_to > valid_from)
);

ALTER TABLE core_licensing.entitlement ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_licensing.entitlement FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_licensing.entitlement
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.platform', true) = 'on');
