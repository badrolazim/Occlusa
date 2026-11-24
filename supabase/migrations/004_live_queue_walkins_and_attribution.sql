-- =============================================================================
-- Occlusa (Malaysian Dental Practice Management Software)
-- Migration: 004_live_queue_walkins_and_attribution.sql
-- Purpose: Support shared Walk-In / Booked / AI-Booked visit types, stage
--          timestamps (arrived_at, seated_at, checkout_at, completed_at),
--          per-staff attribution, JSONB activity log, and merge legacy
--          'Arrived' status into 'Waiting'.
-- =============================================================================

ALTER TABLE public.appointments
    ADD COLUMN IF NOT EXISTS visit_type TEXT NOT NULL DEFAULT 'Booked'
        CHECK (visit_type IN ('Booked', 'WalkIn', 'AiBooked')),
    ADD COLUMN IF NOT EXISTS arrived_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS seated_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS checkout_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS completed_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS created_by TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS checked_in_by TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS seated_by TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS checkout_by TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS completed_by TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS last_updated_by TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS activity_log JSONB NOT NULL DEFAULT '[]'::jsonb;

UPDATE public.appointments
SET status = 'Waiting',
    arrived_at = COALESCE(arrived_at, updated_at)
WHERE status = 'Arrived';

UPDATE public.appointments
SET visit_type = 'AiBooked'
WHERE status = 'AiBookedPending' AND visit_type = 'Booked';

CREATE INDEX IF NOT EXISTS idx_appointments_branch_visit_type
    ON public.appointments (branch_id, visit_type)
    WHERE is_deleted = FALSE;

