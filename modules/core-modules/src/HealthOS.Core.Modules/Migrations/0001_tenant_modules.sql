-- Which non-platform modules each tenant has switched on. Disabling only flips this flag: module data
-- is never touched (retention), and re-enabling restores access to it.
CREATE TABLE core_modules.tenant_module (
    tenant_id   uuid NOT NULL,
    module_id   varchar(128) NOT NULL CHECK (module_id ~ '^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$'),
    enabled     boolean NOT NULL,
    changed_at  timestamptz NOT NULL,
    changed_by  uuid NULL,
    PRIMARY KEY (tenant_id, module_id)
);

ALTER TABLE core_modules.tenant_module ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_modules.tenant_module FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform ON core_modules.tenant_module
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR current_setting('app.platform', true) = 'on')
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.platform', true) = 'on');
