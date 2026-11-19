-- Global user directory: one row per identity-provider subject. Users are not tenant-owned;
-- tenant membership is a separate, tenant-scoped table (0002).
CREATE TABLE core_identity.user_account (
    id               uuid PRIMARY KEY,
    issuer           varchar(512) NOT NULL,
    subject          varchar(255) NOT NULL,
    display_name     varchar(256),
    email            varchar(320),
    created_at       timestamptz NOT NULL,
    last_sign_in_at  timestamptz NOT NULL,
    CONSTRAINT ux_user_account_issuer_subject UNIQUE (issuer, subject)
);

CREATE INDEX ix_user_account_email ON core_identity.user_account (lower(email));
