-- Momentte / PostgreSQL 13+.
-- Inicialização completa de um banco vazio, com o schema atual da aplicação.
-- Crie e selecione previamente o banco dedicado à Momentte.
-- Execute somente este arquivo em instalações novas; não execute 002, 003 e 004 depois.
-- Bancos existentes devem usar apenas os scripts incrementais ainda não aplicados.
-- Nenhum dado é apagado. Se uma tabela já existir, a transação falhará.
BEGIN;

CREATE TABLE users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(150) NOT NULL CHECK (length(btrim(name)) > 0),
    email VARCHAR(254) NOT NULL CHECK (email = lower(btrim(email)) AND length(email) > 0),
    password_hash VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE UNIQUE INDEX uq_users_email ON users (lower(email));

CREATE TABLE refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash VARCHAR(64) NOT NULL UNIQUE,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at TIMESTAMPTZ
);
CREATE INDEX idx_refresh_tokens_user_id ON refresh_tokens(user_id);
CREATE INDEX idx_refresh_tokens_expires_at ON refresh_tokens(expires_at);

CREATE TABLE billing_accounts (
    owner_id UUID PRIMARY KEY REFERENCES users(id),
    last_synced_at TIMESTAMPTZ
);

CREATE TABLE billing_contracts (
    id UUID PRIMARY KEY,
    owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id),
    plan_code TEXT NOT NULL,
    pending_plan TEXT,
    provider_id TEXT UNIQUE,
    checkout_url TEXT,
    cancelled BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE billing_cycles (
    id UUID PRIMARY KEY,
    owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id),
    contract_id UUID NOT NULL REFERENCES billing_contracts(id),
    plan_code TEXT NOT NULL,
    starts_at TIMESTAMPTZ NOT NULL,
    ends_at TIMESTAMPTZ NOT NULL,
    activations_used INTEGER NOT NULL DEFAULT 0 CHECK (activations_used BETWEEN 0 AND 8),
    CHECK (ends_at > starts_at),
    UNIQUE (contract_id, starts_at)
);
CREATE INDEX idx_billing_cycle_owner ON billing_cycles(owner_id, starts_at, ends_at);

CREATE TABLE billing_orders (
    id UUID PRIMARY KEY,
    owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id),
    request_key UUID NOT NULL,
    kind TEXT NOT NULL CHECK (kind IN ('event', 'upgrade')),
    plan_code TEXT NOT NULL,
    previous_plan TEXT,
    -- NULL até um crédito avulso ser consumido ao ativar um evento.
    event_id UUID,
    cycle_id UUID REFERENCES billing_cycles(id),
    amount_cents INTEGER NOT NULL CHECK (amount_cents > 0),
    status TEXT NOT NULL DEFAULT 'pending',
    provider_id TEXT,
    checkout_url TEXT,
    valid_until TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (owner_id, request_key)
);

CREATE TABLE billing_payments (
    provider_payment_id TEXT PRIMARY KEY,
    owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id),
    order_id UUID REFERENCES billing_orders(id),
    contract_id UUID REFERENCES billing_contracts(id),
    amount_cents INTEGER NOT NULL,
    approved_at TIMESTAMPTZ NOT NULL,
    status TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE events (
    id UUID PRIMARY KEY,
    owner_id UUID NOT NULL REFERENCES users(id),
    name VARCHAR(100) NOT NULL,
    public_code VARCHAR(32) NOT NULL,
    starts_at TIMESTAMPTZ NOT NULL,
    ends_at TIMESTAMPTZ NOT NULL,
    activated_at TIMESTAMPTZ,
    ended_at TIMESTAMPTZ,
    media_mode TEXT NOT NULL DEFAULT 'images',
    moderation_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    show_qr_code BOOLEAN NOT NULL DEFAULT TRUE,
    paid BOOLEAN NOT NULL DEFAULT FALSE,
    activation_source TEXT,
    cycle_id UUID REFERENCES billing_cycles(id),
    storage_limit_bytes BIGINT NOT NULL DEFAULT 1000000000,
    storage_used_bytes BIGINT NOT NULL DEFAULT 0 CHECK (storage_used_bytes >= 0),
    download_hours INTEGER NOT NULL DEFAULT 24,
    custom_logo_path TEXT,
    custom_logo_type TEXT,
    custom_logo_bytes BIGINT NOT NULL DEFAULT 0,
    theme_color VARCHAR(7),
    text_color VARCHAR(7) NOT NULL DEFAULT '#fffaf1',
    font_family TEXT NOT NULL DEFAULT 'sans',
    text_scale TEXT NOT NULL DEFAULT 'medium',
    deleted_at TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_events_duration CHECK (
        ends_at > starts_at AND ends_at <= starts_at + INTERVAL '48 hours'
    ),
    CONSTRAINT ck_events_media_mode CHECK (media_mode IN ('images', 'texts', 'both'))
);
CREATE INDEX idx_events_owner ON events(owner_id);
CREATE UNIQUE INDEX uq_events_public_code ON events(public_code);

CREATE TABLE event_participants (
    event_id UUID NOT NULL REFERENCES events(id),
    user_id UUID NOT NULL REFERENCES users(id),
    joined_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (event_id, user_id)
);
CREATE INDEX idx_event_participants_user ON event_participants(user_id);

CREATE TABLE event_media (
    id UUID PRIMARY KEY,
    event_id UUID NOT NULL REFERENCES events(id),
    -- Envios somente de texto não têm arquivo nem content_type.
    relative_path TEXT,
    content_type TEXT,
    bytes BIGINT NOT NULL,
    text_content VARCHAR(500),
    author_id UUID REFERENCES users(id),
    moderation_status TEXT NOT NULL DEFAULT 'approved',
    display_author_name BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    deleted_at TIMESTAMPTZ,
    CONSTRAINT ck_event_media_bytes CHECK (bytes >= 0),
    CONSTRAINT ck_event_media_status CHECK (moderation_status IN ('pending', 'approved', 'rejected'))
);

CREATE TABLE media_delete_queue (
    relative_path TEXT PRIMARY KEY
);

COMMIT;
