-- Tenants are administered by platform operators and pinned to one deployment cell (data residency).
-- The tenant table is the tenant directory itself, so it has no tenant_id column.
CREATE TABLE core_tenancy.tenant (
    id           uuid PRIMARY KEY,
    code         varchar(32) NOT NULL CHECK (code ~ '^[a-z][a-z0-9-]{1,31}$'),
    name         varchar(200) NOT NULL,
    region_cell  varchar(32) NOT NULL,
    status       varchar(16) NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended')),
    created_at   timestamptz NOT NULL DEFAULT now(),
    created_by   uuid NULL,
    CONSTRAINT ux_tenant_code UNIQUE (code)
);

-- Organization structure belongs to the tenant. Platform scope is deliberately NOT allowed here:
-- operators administer tenants, not their internal structure.
CREATE TABLE core_tenancy.organization (
    id          uuid NOT NULL,
    tenant_id   uuid NOT NULL REFERENCES core_tenancy.tenant (id),
    name        varchar(200) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (id),
    CONSTRAINT ux_organization_tenant_id UNIQUE (tenant_id, id)
);

CREATE TABLE core_tenancy.site (
    id               uuid NOT NULL,
    tenant_id        uuid NOT NULL,
    organization_id  uuid NOT NULL,
    code             varchar(32) NOT NULL CHECK (code ~ '^[A-Z0-9][A-Z0-9-]{0,31}$'),
    name             varchar(200) NOT NULL,
    time_zone        varchar(64) NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (id),
    CONSTRAINT ux_site_tenant_code UNIQUE (tenant_id, code),
    CONSTRAINT ux_site_tenant_id UNIQUE (tenant_id, id),
    FOREIGN KEY (tenant_id, organization_id) REFERENCES core_tenancy.organization (tenant_id, id)
);

CREATE TABLE core_tenancy.department (
    id          uuid PRIMARY KEY,
    tenant_id   uuid NOT NULL,
    site_id     uuid NOT NULL,
    name        varchar(200) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ux_department_site_name UNIQUE (tenant_id, site_id, name),
    FOREIGN KEY (tenant_id, site_id) REFERENCES core_tenancy.site (tenant_id, id)
);

ALTER TABLE core_tenancy.organization ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_tenancy.organization FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_only ON core_tenancy.organization
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE core_tenancy.site ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_tenancy.site FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_only ON core_tenancy.site
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE core_tenancy.department ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_tenancy.department FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_only ON core_tenancy.department
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
