-- Momentte / PostgreSQL 13+. Execute no banco dedicado à Momentte.
-- Crie o banco previamente. Este script não cria banco, não apaga dados
-- e não altera tabelas existentes. Mudanças futuras serão scripts SQL manuais.
BEGIN;

CREATE TABLE IF NOT EXISTS users (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name VARCHAR(150) NOT NULL CHECK (length(btrim(name)) > 0),
    email VARCHAR(254) NOT NULL CHECK (email = lower(btrim(email)) AND length(email) > 0),
    password_hash VARCHAR(255) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Unicidade no banco também protege contra cadastros simultâneos.
CREATE UNIQUE INDEX IF NOT EXISTS uq_users_email ON users (lower(email));

CREATE TABLE IF NOT EXISTS refresh_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    token_hash VARCHAR(64) NOT NULL UNIQUE,
    expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    revoked_at TIMESTAMPTZ
);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_user_id ON refresh_tokens(user_id);
CREATE INDEX IF NOT EXISTS idx_refresh_tokens_expires_at ON refresh_tokens(expires_at);
COMMIT;
-- PostgreSQL 13+. Execute manualmente após init.sql; não há migrations.
BEGIN;
CREATE TABLE IF NOT EXISTS billing_accounts(owner_id UUID PRIMARY KEY REFERENCES users(id),last_synced_at TIMESTAMPTZ);
CREATE TABLE IF NOT EXISTS billing_contracts(
 id UUID PRIMARY KEY, owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id), plan_code TEXT NOT NULL,
 pending_plan TEXT, provider_id TEXT UNIQUE, checkout_url TEXT, cancelled BOOLEAN NOT NULL DEFAULT FALSE,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS billing_cycles(
 id UUID PRIMARY KEY, owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id), contract_id UUID NOT NULL REFERENCES billing_contracts(id),
 plan_code TEXT NOT NULL, starts_at TIMESTAMPTZ NOT NULL, ends_at TIMESTAMPTZ NOT NULL,
 activations_used INTEGER NOT NULL DEFAULT 0 CHECK(activations_used BETWEEN 0 AND 8),
 CHECK(ends_at>starts_at), UNIQUE(contract_id,starts_at)
);
CREATE INDEX IF NOT EXISTS idx_billing_cycle_owner ON billing_cycles(owner_id,starts_at,ends_at);
CREATE TABLE IF NOT EXISTS billing_orders(
 id UUID PRIMARY KEY, owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id), request_key UUID NOT NULL,
 kind TEXT NOT NULL CHECK(kind IN ('event','upgrade')), plan_code TEXT NOT NULL, previous_plan TEXT,
 event_id UUID, cycle_id UUID REFERENCES billing_cycles(id), amount_cents INTEGER NOT NULL CHECK(amount_cents>0),
 status TEXT NOT NULL DEFAULT 'pending', provider_id TEXT, checkout_url TEXT, valid_until TIMESTAMPTZ,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(owner_id,request_key)
);
CREATE TABLE IF NOT EXISTS billing_payments(
 provider_payment_id TEXT PRIMARY KEY, owner_id UUID NOT NULL REFERENCES billing_accounts(owner_id),
 order_id UUID REFERENCES billing_orders(id), contract_id UUID REFERENCES billing_contracts(id),
 amount_cents INTEGER NOT NULL, approved_at TIMESTAMPTZ NOT NULL, status TEXT NOT NULL,
 created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE TABLE IF NOT EXISTS events(
 id UUID PRIMARY KEY, owner_id UUID NOT NULL REFERENCES users(id), name VARCHAR(150) NOT NULL,
 ends_at TIMESTAMPTZ NOT NULL, activated_at TIMESTAMPTZ, ended_at TIMESTAMPTZ,
 paid BOOLEAN NOT NULL DEFAULT FALSE, activation_source TEXT, cycle_id UUID REFERENCES billing_cycles(id),
 storage_limit_bytes BIGINT NOT NULL DEFAULT 1000000000, storage_used_bytes BIGINT NOT NULL DEFAULT 0 CHECK(storage_used_bytes>=0),
 download_hours INTEGER NOT NULL DEFAULT 24, custom_logo_path TEXT, custom_logo_type TEXT, custom_logo_bytes BIGINT NOT NULL DEFAULT 0, theme_color VARCHAR(7),
 deleted_at TIMESTAMPTZ, created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP
);
CREATE INDEX IF NOT EXISTS idx_events_owner ON events(owner_id);
CREATE TABLE IF NOT EXISTS event_media(
 id UUID PRIMARY KEY, event_id UUID NOT NULL REFERENCES events(id), relative_path TEXT NOT NULL,
 content_type TEXT NOT NULL, bytes BIGINT NOT NULL CHECK(bytes>0), deleted_at TIMESTAMPTZ
);
CREATE TABLE IF NOT EXISTS media_delete_queue(relative_path TEXT PRIMARY KEY);
COMMIT;
