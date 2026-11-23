-- =============================================================================
-- Occlusa (Malaysian Dental Practice Management Software)
-- Migration: 002_appointments_dentists_treatments.sql
-- Purpose: Add relational Dentist and Treatment catalogs, extend appointments
--          with dentist_id, treatment_id, duration_minutes, and notes, and
--          enforce RLS + soft-delete guards consistent with Migration 001.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. DENTISTS & TREATMENTS CATALOG TABLES
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.dentists (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    full_name       TEXT NOT NULL,
    specialty       TEXT NOT NULL DEFAULT '',
    default_room    TEXT NOT NULL DEFAULT 'Suite 1',
    branch_ids      TEXT[] NOT NULL DEFAULT ARRAY['HQ-KL']::TEXT[],
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_dentists_group_name UNIQUE (clinic_group_id, full_name)
);

CREATE TABLE IF NOT EXISTS public.treatments (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id          TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    name                     TEXT NOT NULL,
    category                 TEXT NOT NULL DEFAULT 'General',
    default_duration_minutes INTEGER NOT NULL DEFAULT 30
        CHECK (default_duration_minutes >= 15 AND default_duration_minutes <= 480),
    default_fee              NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (default_fee >= 0),
    is_active                BOOLEAN NOT NULL DEFAULT TRUE,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_treatments_group_name UNIQUE (clinic_group_id, name)
);

-- -----------------------------------------------------------------------------
-- 2. EXTEND APPOINTMENTS TABLE (NO OVERLAP CONSTRAINT — UNRESTRICTED BOOKING)
-- -----------------------------------------------------------------------------

ALTER TABLE public.appointments
    ADD COLUMN IF NOT EXISTS dentist_id UUID NULL REFERENCES public.dentists(id),
    ADD COLUMN IF NOT EXISTS treatment_id UUID NULL REFERENCES public.treatments(id),
    ADD COLUMN IF NOT EXISTS duration_minutes INTEGER NOT NULL DEFAULT 30
        CHECK (duration_minutes >= 15 AND duration_minutes <= 480),
    ADD COLUMN IF NOT EXISTS notes TEXT NOT NULL DEFAULT '';

-- -----------------------------------------------------------------------------
-- 3. PERFORMANCE INDEXES
-- -----------------------------------------------------------------------------

CREATE INDEX IF NOT EXISTS idx_dentists_group
    ON public.dentists (clinic_group_id)
    WHERE is_active = TRUE;

CREATE INDEX IF NOT EXISTS idx_treatments_group
    ON public.treatments (clinic_group_id, category)
    WHERE is_active = TRUE;

CREATE INDEX IF NOT EXISTS idx_appointments_branch_dentist_sched
    ON public.appointments (branch_id, dentist_id, scheduled_at)
    WHERE is_deleted = FALSE;

-- -----------------------------------------------------------------------------
-- 4. IMMUTABILITY / SOFT-DELETE TRIGGERS
-- -----------------------------------------------------------------------------

DROP TRIGGER IF EXISTS trg_no_delete_dentists ON public.dentists;
CREATE TRIGGER trg_no_delete_dentists
    BEFORE DELETE ON public.dentists
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_treatments ON public.treatments;
CREATE TRIGGER trg_no_delete_treatments
    BEFORE DELETE ON public.treatments
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

-- -----------------------------------------------------------------------------
-- 5. ROW-LEVEL SECURITY (CLINIC-GROUP SHARED CATALOGS)
-- -----------------------------------------------------------------------------

ALTER TABLE public.dentists ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.treatments ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS dentists_group_select ON public.dentists;
CREATE POLICY dentists_group_select ON public.dentists
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS dentists_group_insert ON public.dentists;
CREATE POLICY dentists_group_insert ON public.dentists
    FOR INSERT
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS dentists_group_update ON public.dentists;
CREATE POLICY dentists_group_update ON public.dentists
    FOR UPDATE
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS dentists_deny_delete ON public.dentists;
CREATE POLICY dentists_deny_delete ON public.dentists
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS treatments_group_select ON public.treatments;
CREATE POLICY treatments_group_select ON public.treatments
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS treatments_group_insert ON public.treatments;
CREATE POLICY treatments_group_insert ON public.treatments
    FOR INSERT
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS treatments_group_update ON public.treatments;
CREATE POLICY treatments_group_update ON public.treatments
    FOR UPDATE
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS treatments_deny_delete ON public.treatments;
CREATE POLICY treatments_deny_delete ON public.treatments
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

-- -----------------------------------------------------------------------------
-- 6. SEED DATA: DENTISTS & STANDARD TREATMENT CATALOG
-- -----------------------------------------------------------------------------

INSERT INTO public.dentists (id, clinic_group_id, full_name, specialty, default_room, branch_ids)
VALUES
    ('50000000-0000-0000-0000-000000000001', 'OCCLUSA-MY', 'Dr. Aina Rahman', 'General & Restorative', 'Suite 1', ARRAY['HQ-KL', 'PJ-SS2']::TEXT[]),
    ('50000000-0000-0000-0000-000000000002', 'OCCLUSA-MY', 'Dr. Marcus Tan', 'Endodontics & Prosthodontics', 'Suite 2', ARRAY['HQ-KL', 'PJ-SS2']::TEXT[]),
    ('50000000-0000-0000-0000-000000000003', 'OCCLUSA-MY', 'Dr. Priya Nair', 'Preventive & Orthodontics', 'Suite 3', ARRAY['HQ-KL']::TEXT[])
ON CONFLICT (clinic_group_id, full_name) DO NOTHING;

INSERT INTO public.treatments (id, clinic_group_id, name, category, default_duration_minutes, default_fee)
VALUES
    ('60000000-0000-0000-0000-000000000001', 'OCCLUSA-MY', 'Dental Consultation & Checkup', 'Diagnostic', 30, 50.00),
    ('60000000-0000-0000-0000-000000000002', 'OCCLUSA-MY', 'Routine Scaling & Polishing', 'Preventive', 30, 150.00),
    ('60000000-0000-0000-0000-000000000003', 'OCCLUSA-MY', 'Posterior Composite Filling', 'Restorative', 45, 200.00),
    ('60000000-0000-0000-0000-000000000004', 'OCCLUSA-MY', 'Simple Tooth Extraction', 'Oral Surgery', 30, 180.00),
    ('60000000-0000-0000-0000-000000000005', 'OCCLUSA-MY', 'Root Canal Therapy (Stage 1)', 'Endodontics', 60, 450.00),
    ('60000000-0000-0000-0000-000000000006', 'OCCLUSA-MY', 'Crown Preparation & Cementation', 'Prosthodontics', 60, 950.00),
    ('60000000-0000-0000-0000-000000000007', 'OCCLUSA-MY', 'Implant Crown Review', 'Prosthodontics', 30, 350.00)
ON CONFLICT (clinic_group_id, name) DO NOTHING;

