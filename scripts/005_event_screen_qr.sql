-- Banco existente: execute após os scripts anteriores.
-- Instalações novas usam somente init.sql, que já contém este campo.
BEGIN;
ALTER TABLE events ADD COLUMN IF NOT EXISTS show_qr_code BOOLEAN NOT NULL DEFAULT TRUE;
COMMIT;
