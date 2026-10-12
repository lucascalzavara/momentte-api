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

COMMIT;
