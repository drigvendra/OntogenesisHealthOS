-- Append-only, hash-chained audit trail. One chain per tenant; tenant_id NULL is the platform chain.
-- Rows can only be added through core_audit.append(), which takes tenant and actor from the session
-- context (app.tenant_id / app.platform / app.user_id), so callers cannot spoof them. Details must
-- never contain PHI: identifiers and codes only.
CREATE TABLE core_audit.audit_event (
    id             uuid PRIMARY KEY,
    chain_key      uuid NOT NULL,
    tenant_id      uuid NULL,
    seq            bigint NOT NULL,
    occurred_at    timestamptz NOT NULL,
    actor_user_id  uuid NULL,
    action         varchar(128) NOT NULL,
    resource_type  varchar(64) NOT NULL,
    resource_id    varchar(128) NULL,
    outcome        varchar(16) NOT NULL CHECK (outcome IN ('success', 'denied', 'failure')),
    details        jsonb NOT NULL DEFAULT '{}'::jsonb,
    prev_hash      bytea NOT NULL,
    hash           bytea NOT NULL,
    CONSTRAINT ux_audit_chain_seq UNIQUE (chain_key, seq),
    CONSTRAINT ck_audit_chain CHECK (chain_key = COALESCE(tenant_id, '00000000-0000-0000-0000-000000000000'::uuid))
);
CREATE INDEX ix_audit_tenant_time ON core_audit.audit_event (tenant_id, occurred_at DESC);

ALTER TABLE core_audit.audit_event ENABLE ROW LEVEL SECURITY;
ALTER TABLE core_audit.audit_event FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_or_platform_chain ON core_audit.audit_event
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
           OR (tenant_id IS NULL AND current_setting('app.platform', true) = 'on'))
    WITH CHECK (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR (tenant_id IS NULL AND current_setting('app.platform', true) = 'on'));

-- The audit module itself may only read. Nobody gets UPDATE/DELETE/TRUNCATE.
REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON core_audit.audit_event FROM {{module_role}};

-- Canonical text that is hashed. Timestamp formatted explicitly in UTC so the hash does not depend
-- on session settings (DateStyle / TimeZone).
CREATE FUNCTION core_audit.event_digest(
    p_prev bytea, p_chain uuid, p_seq bigint, p_id uuid, p_at timestamptz, p_actor uuid,
    p_action text, p_resource_type text, p_resource_id text, p_outcome text, p_details jsonb)
RETURNS bytea
LANGUAGE sql STABLE
AS $$
    SELECT sha256(p_prev || convert_to(concat_ws('|',
        p_chain::text, p_seq::text, p_id::text,
        to_char(p_at AT TIME ZONE 'UTC', 'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
        COALESCE(p_actor::text, ''), p_action, p_resource_type, COALESCE(p_resource_id, ''), p_outcome,
        p_details::text), 'UTF8'))
$$;

CREATE FUNCTION core_audit.append(
    p_action text, p_resource_type text, p_resource_id text, p_outcome text, p_details jsonb)
RETURNS uuid
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = pg_catalog, core_audit
AS $$
DECLARE
    v_tenant   uuid := NULLIF(current_setting('app.tenant_id', true), '')::uuid;
    v_platform boolean := current_setting('app.platform', true) = 'on';
    v_actor    uuid := NULLIF(current_setting('app.user_id', true), '')::uuid;
    v_chain    uuid;
    v_seq      bigint;
    v_prev     bytea;
    v_id       uuid := gen_random_uuid();
    v_at       timestamptz := date_trunc('microseconds', clock_timestamp());
    v_details  jsonb := COALESCE(p_details, '{}'::jsonb);
BEGIN
    IF v_tenant IS NULL AND NOT v_platform THEN
        RAISE EXCEPTION 'audit: no tenant or platform context' USING ERRCODE = '42501';
    END IF;
    IF p_outcome NOT IN ('success', 'denied', 'failure') THEN
        RAISE EXCEPTION 'audit: invalid outcome %', p_outcome USING ERRCODE = '22023';
    END IF;

    v_chain := COALESCE(v_tenant, '00000000-0000-0000-0000-000000000000'::uuid);
    -- Serialise appends per chain so sequence numbers and hashes form a strict chain.
    PERFORM pg_advisory_xact_lock(hashtextextended('core_audit:' || v_chain::text, 0));

    SELECT e.seq, e.hash INTO v_seq, v_prev
      FROM core_audit.audit_event e
     WHERE e.chain_key = v_chain
     ORDER BY e.seq DESC
     LIMIT 1;

    v_seq  := COALESCE(v_seq, 0) + 1;
    v_prev := COALESCE(v_prev, '\x00'::bytea);

    INSERT INTO core_audit.audit_event
        (id, chain_key, tenant_id, seq, occurred_at, actor_user_id, action, resource_type, resource_id, outcome, details, prev_hash, hash)
    VALUES
        (v_id, v_chain, v_tenant, v_seq, v_at, v_actor, p_action, p_resource_type, p_resource_id, p_outcome, v_details, v_prev,
         core_audit.event_digest(v_prev, v_chain, v_seq, v_id, v_at, v_actor, p_action, p_resource_type, p_resource_id, p_outcome, v_details));
    RETURN v_id;
END
$$;

-- Returns the first sequence number in the visible chain whose link or hash does not verify, or NULL.
CREATE FUNCTION core_audit.verify()
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    WITH chain AS (
        SELECT e.*, lag(e.hash) OVER (PARTITION BY e.chain_key ORDER BY e.seq) AS expected_prev,
               lag(e.seq)  OVER (PARTITION BY e.chain_key ORDER BY e.seq) AS previous_seq
          FROM core_audit.audit_event e)
    SELECT min(seq) FROM chain
     WHERE prev_hash <> COALESCE(expected_prev, '\x00'::bytea)
        OR seq <> COALESCE(previous_seq, 0) + 1
        OR hash <> core_audit.event_digest(prev_hash, chain_key, seq, id, occurred_at, actor_user_id,
                                           action, resource_type, resource_id, outcome, details)
$$;

REVOKE ALL ON FUNCTION core_audit.event_digest(bytea, uuid, bigint, uuid, timestamptz, uuid, text, text, text, text, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION core_audit.append(text, text, text, text, jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION core_audit.verify() FROM PUBLIC;

-- Every module (through the platform-services group) may append; only the audit module reads/verifies.
GRANT USAGE ON SCHEMA core_audit TO {{platform_services_role}};
GRANT EXECUTE ON FUNCTION core_audit.append(text, text, text, text, jsonb) TO {{platform_services_role}};
GRANT EXECUTE ON FUNCTION core_audit.event_digest(bytea, uuid, bigint, uuid, timestamptz, uuid, text, text, text, text, jsonb) TO {{module_role}};
GRANT EXECUTE ON FUNCTION core_audit.verify() TO {{module_role}};
