-- Break-glass: time-boxed, dual-approved, read-only operator access to one tenant.
-- Grants are visible to the tenant (tenant_or_platform) and to the operator who holds them.
CREATE TABLE core_identity.break_glass_grant (
    id           uuid PRIMARY KEY,
    tenant_id    uuid NOT NULL,
    user_id      uuid NOT NULL REFERENCES core_identity.user_account (id),
    reason       varchar(500) NOT NULL CHECK (length(trim(reason)) >= 10),
    requested_at timestamptz NOT NULL,
    expires_at   timestamptz NOT NULL,
    approved_by  uuid NULL REFERENCES core_identity.user_account (id),
    approved_at  timestamptz NULL,
    CONSTRAINT ck_break_glass_window CHECK (expires_at > requested_at AND expires_at <= requested_at + interval '4 hours'),
    CONSTRAINT ck_break_glass_second_person CHECK (approved_by IS NULL OR approved_by <> user_id)
);
CREATE INDEX ix_break_glass_user ON core_identity.break_glass_grant (user_id, expires_at);

ALTER TABLE core_identity.break_glass_grant ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_identity.break_glass_grant FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_identity.break_glass_grant
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.platform', true) = 'on');
CREATE POLICY own_grants_read ON core_identity.break_glass_grant FOR SELECT
    USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);
