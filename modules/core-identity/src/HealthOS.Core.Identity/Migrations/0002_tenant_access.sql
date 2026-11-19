-- Platform operators (Ontogenesis staff). Not tenant data.
CREATE TABLE core_identity.platform_operator (
    user_id     uuid PRIMARY KEY REFERENCES core_identity.user_account (id),
    granted_at  timestamptz NOT NULL DEFAULT now(),
    granted_by  uuid NULL REFERENCES core_identity.user_account (id)
);

-- Tenant-scoped access control. Composite foreign keys include tenant_id so a row can never point
-- at another tenant's member or role.
CREATE TABLE core_identity.tenant_membership (
    tenant_id   uuid NOT NULL,
    user_id     uuid NOT NULL REFERENCES core_identity.user_account (id),
    status      varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended')),
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, user_id)
);
CREATE INDEX ix_tenant_membership_user ON core_identity.tenant_membership (user_id);

CREATE TABLE core_identity.role (
    id          uuid NOT NULL,
    tenant_id   uuid NOT NULL,
    code        varchar(64) NOT NULL,
    name        varchar(128) NOT NULL,
    is_system   boolean NOT NULL DEFAULT false,
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (id),
    CONSTRAINT ux_role_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT ux_role_tenant_id UNIQUE (tenant_id, id)
);

CREATE TABLE core_identity.role_permission (
    tenant_id   uuid NOT NULL,
    role_id     uuid NOT NULL,
    permission  varchar(128) NOT NULL,
    PRIMARY KEY (role_id, permission),
    FOREIGN KEY (tenant_id, role_id) REFERENCES core_identity.role (tenant_id, id) ON DELETE CASCADE
);

CREATE TABLE core_identity.role_assignment (
    id          uuid PRIMARY KEY,
    tenant_id   uuid NOT NULL,
    user_id     uuid NOT NULL,
    role_id     uuid NOT NULL,
    site_id     uuid NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (tenant_id, user_id) REFERENCES core_identity.tenant_membership (tenant_id, user_id) ON DELETE CASCADE,
    FOREIGN KEY (tenant_id, role_id) REFERENCES core_identity.role (tenant_id, id) ON DELETE CASCADE
);
CREATE UNIQUE INDEX ux_role_assignment
    ON core_identity.role_assignment (tenant_id, user_id, role_id, COALESCE(site_id, '00000000-0000-0000-0000-000000000000'::uuid));

-- Row-level security: tenant context (app.tenant_id) or platform scope (app.platform = 'on').
-- Fail closed: an unset context matches nothing.
ALTER TABLE core_identity.tenant_membership ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_identity.tenant_membership FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_identity.tenant_membership
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.platform', true) = 'on');
-- A user may always see their own memberships (to choose a tenant), but not change them.
CREATE POLICY own_memberships_read ON core_identity.tenant_membership FOR SELECT
    USING (user_id = NULLIF(current_setting('app.user_id', true), '')::uuid);

ALTER TABLE core_identity.role ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_identity.role FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_identity.role
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.platform', true) = 'on');

ALTER TABLE core_identity.role_permission ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_identity.role_permission FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_identity.role_permission
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.platform', true) = 'on');

ALTER TABLE core_identity.role_assignment ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_identity.role_assignment FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_identity.role_assignment
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.platform', true) = 'on');
