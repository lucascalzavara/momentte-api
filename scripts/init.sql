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
