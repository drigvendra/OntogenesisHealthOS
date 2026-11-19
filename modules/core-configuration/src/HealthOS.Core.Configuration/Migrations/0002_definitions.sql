-- Versioned form and workflow definitions. Each publish adds a version; versions are immutable so
-- records and running workflow instances can stay pinned to the version they started with.
CREATE TABLE core_configuration.definition (
    tenant_id   uuid NOT NULL,
    kind        varchar(16) NOT NULL CHECK (kind IN ('form', 'workflow')),
    code        varchar(64) NOT NULL CHECK (code ~ '^[a-z][a-z0-9-]{1,63}$'),
    version     integer NOT NULL CHECK (version >= 1),
    document    jsonb NOT NULL,
    source      varchar(16) NOT NULL DEFAULT 'tenant' CHECK (source IN ('tenant', 'template')),
    created_at  timestamptz NOT NULL,
    created_by  uuid NULL,
    PRIMARY KEY (tenant_id, kind, code, version)
);
REVOKE UPDATE, DELETE, TRUNCATE ON core_configuration.definition FROM {{module_role}};

ALTER TABLE core_configuration.definition ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_configuration.definition FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_configuration.definition
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid OR current_setting('app.platform', true) = 'on');
