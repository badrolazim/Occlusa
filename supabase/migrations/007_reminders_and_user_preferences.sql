-- =============================================================================
-- Occlusa (Malaysian Dental Practice Management Software)
-- Migration: 007_reminders_and_user_preferences.sql
-- Purpose:
--   1. Track appointment reminder outreach and patient confirmation state
--      (WhatsApp / PhoneCall / SMS) directly on public.appointments.
--   2. Persist per-staff Live Queue & workspace view preferences
--      (Simple vs Detailed view, default queue scope, wait timers, inline
--      notes, finished-row filtering, and MyKad quick-bar visibility).
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. APPOINTMENT REMINDER TRACKING COLUMNS
-- -----------------------------------------------------------------------------

ALTER TABLE public.appointments
    ADD COLUMN IF NOT EXISTS reminder_status TEXT NOT NULL DEFAULT 'NotReminded'
        CHECK (reminder_status IN ('NotReminded', 'Reminded', 'Confirmed', 'NoReply')),
    ADD COLUMN IF NOT EXISTS reminder_channel TEXT NOT NULL DEFAULT 'None'
        CHECK (reminder_channel IN ('None', 'WhatsApp', 'PhoneCall', 'Sms')),
    ADD COLUMN IF NOT EXISTS reminded_at TIMESTAMPTZ NULL,
    ADD COLUMN IF NOT EXISTS reminded_by TEXT NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS reminder_confirmed_at TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS idx_appointments_branch_reminder_status
    ON public.appointments (branch_id, reminder_status, scheduled_at)
    WHERE is_deleted = FALSE;

-- -----------------------------------------------------------------------------
-- 2. PER-USER WORKSPACE VIEW PREFERENCES
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.user_workspace_preferences (
    id                          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id             TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    staff_key                   TEXT NOT NULL,
    staff_display_name          TEXT NOT NULL DEFAULT '',
    queue_view_mode             TEXT NOT NULL DEFAULT 'Simple'
        CHECK (queue_view_mode IN ('Simple', 'Detailed')),
    default_queue_scope         TEXT NOT NULL DEFAULT 'AllClinic'
        CHECK (default_queue_scope IN ('MyQueue', 'AllClinic')),
    hide_finished_queue_rows    BOOLEAN NOT NULL DEFAULT TRUE,
    show_wait_timers            BOOLEAN NOT NULL DEFAULT FALSE,
    show_inline_reception_notes BOOLEAN NOT NULL DEFAULT FALSE,
    show_mykad_quick_bar        BOOLEAN NOT NULL DEFAULT FALSE,
    updated_at                  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_user_workspace_prefs_staff_key
    ON public.user_workspace_preferences (clinic_group_id, LOWER(staff_key));

DROP TRIGGER IF EXISTS trg_no_delete_user_workspace_preferences ON public.user_workspace_preferences;
CREATE TRIGGER trg_no_delete_user_workspace_preferences
    BEFORE DELETE ON public.user_workspace_preferences
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

ALTER TABLE public.user_workspace_preferences ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS user_workspace_preferences_group_all ON public.user_workspace_preferences;
CREATE POLICY user_workspace_preferences_group_all ON public.user_workspace_preferences
    FOR ALL
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

