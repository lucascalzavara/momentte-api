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
