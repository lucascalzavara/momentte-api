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
-- Execute manualmente após os scripts anteriores. Sem migrations.
BEGIN;
ALTER TABLE events ADD COLUMN IF NOT EXISTS public_code VARCHAR(32);
UPDATE events SET public_code=replace(gen_random_uuid()::text,'-','') WHERE public_code IS NULL;
ALTER TABLE events ALTER COLUMN public_code SET NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS uq_events_public_code ON events(public_code);
ALTER TABLE events ADD COLUMN IF NOT EXISTS starts_at TIMESTAMPTZ;
UPDATE events SET starts_at=ends_at-INTERVAL '24 hours' WHERE starts_at IS NULL;
ALTER TABLE events ALTER COLUMN starts_at SET NOT NULL;
ALTER TABLE events ADD COLUMN IF NOT EXISTS media_mode TEXT NOT NULL DEFAULT 'images';
ALTER TABLE events ADD COLUMN IF NOT EXISTS moderation_enabled BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE events ADD COLUMN IF NOT EXISTS text_color VARCHAR(7) NOT NULL DEFAULT '#fffaf1';
ALTER TABLE events ADD COLUMN IF NOT EXISTS font_family TEXT NOT NULL DEFAULT 'sans';
ALTER TABLE events ADD COLUMN IF NOT EXISTS text_scale TEXT NOT NULL DEFAULT 'medium';
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='ck_events_duration' AND conrelid='events'::regclass) THEN
  ALTER TABLE events ADD CONSTRAINT ck_events_duration CHECK(ends_at>starts_at AND ends_at<=starts_at+INTERVAL '48 hours');
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='ck_events_media_mode' AND conrelid='events'::regclass) THEN
  ALTER TABLE events ADD CONSTRAINT ck_events_media_mode CHECK(media_mode IN ('images','texts','both'));
 END IF;
END $$;
CREATE TABLE IF NOT EXISTS event_participants(
 event_id UUID NOT NULL REFERENCES events(id), user_id UUID NOT NULL REFERENCES users(id),
 joined_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP, PRIMARY KEY(event_id,user_id)
);
CREATE INDEX IF NOT EXISTS idx_event_participants_user ON event_participants(user_id);
ALTER TABLE event_media ALTER COLUMN relative_path DROP NOT NULL;
ALTER TABLE event_media ALTER COLUMN content_type DROP NOT NULL;
ALTER TABLE event_media ADD COLUMN IF NOT EXISTS text_content VARCHAR(500);
ALTER TABLE event_media ADD COLUMN IF NOT EXISTS author_id UUID REFERENCES users(id);
ALTER TABLE event_media ADD COLUMN IF NOT EXISTS moderation_status TEXT NOT NULL DEFAULT 'approved';
ALTER TABLE event_media ADD COLUMN IF NOT EXISTS created_at TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP;
ALTER TABLE event_media ADD COLUMN IF NOT EXISTS display_author_name BOOLEAN NOT NULL DEFAULT FALSE;
UPDATE event_media m SET author_id=e.owner_id FROM events e WHERE m.event_id=e.id AND m.author_id IS NULL;
ALTER TABLE event_media DROP CONSTRAINT IF EXISTS event_media_bytes_check;
DO $$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='ck_event_media_bytes' AND conrelid='event_media'::regclass) THEN
  ALTER TABLE event_media ADD CONSTRAINT ck_event_media_bytes CHECK(bytes>=0);
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_constraint WHERE conname='ck_event_media_status' AND conrelid='event_media'::regclass) THEN
  ALTER TABLE event_media ADD CONSTRAINT ck_event_media_status CHECK(moderation_status IN ('pending','approved','rejected'));
 END IF;
END $$;
-- Compras ainda não consumidas passam a ser créditos da conta, sem vínculo ao rascunho.
UPDATE billing_orders SET event_id=NULL WHERE kind='event' AND status IN ('paid','pending');
COMMIT;
