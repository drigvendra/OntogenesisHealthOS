-- Tenant profile: which country / industry / specialty profiles apply (set during onboarding).
CREATE TABLE core_configuration.tenant_profile (
    tenant_id   uuid PRIMARY KEY,
    country     varchar(16) NOT NULL,
    industry    varchar(32) NOT NULL,
    specialty   varchar(32) NULL,
    updated_at  timestamptz NOT NULL,
    updated_by  uuid NULL
);

-- Drafts: proposed full set of tenant-level entries (organization/site/role/user).
CREATE TABLE core_configuration.config_draft (
    id            uuid PRIMARY KEY,
    tenant_id     uuid NOT NULL,
    base_version  integer NOT NULL,
    summary       varchar(500) NOT NULL,
    entries       jsonb NOT NULL,
    status        varchar(16) NOT NULL CHECK (status IN ('draft', 'approved', 'published', 'discarded')),
    created_at    timestamptz NOT NULL,
    created_by    uuid NOT NULL,
    approved_at   timestamptz NULL,
    approved_by   uuid NULL
);
CREATE INDEX ix_config_draft_tenant ON core_configuration.config_draft (tenant_id, created_at DESC);

-- Published snapshots: immutable. Rollback publishes a new snapshot that copies an older one.
CREATE TABLE core_configuration.config_snapshot (
    tenant_id       uuid NOT NULL,
    version         integer NOT NULL CHECK (version >= 1),
    entries         jsonb NOT NULL,
    created_at      timestamptz NOT NULL,
    created_by      uuid NOT NULL,
    draft_id        uuid NULL,
    rolled_back_to  integer NULL,
    PRIMARY KEY (tenant_id, version)
);
REVOKE UPDATE, DELETE, TRUNCATE ON core_configuration.config_snapshot FROM {{module_role}};

ALTER TABLE core_configuration.tenant_profile ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_configuration.tenant_profile FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_configuration.tenant_profile
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid OR current_setting('app.platform', true) = 'on');

ALTER TABLE core_configuration.config_draft ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_configuration.config_draft FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_only ON core_configuration.config_draft
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

ALTER TABLE core_configuration.config_snapshot ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_configuration.config_snapshot FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_only ON core_configuration.config_snapshot
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
