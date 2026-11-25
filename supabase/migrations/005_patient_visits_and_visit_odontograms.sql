-- =============================================================================
-- Occlusa (Malaysian Dental Practice Management Software)
-- Migration: 005_patient_visits_and_visit_odontograms.sql
-- Purpose: Introduce first-class Patient Visit Records (0..N per patient) and
--          per-visit FDI Odontograms (Permanent 11-48 + Primary 51-85 with
--          5-surface MODBL support) required by MDA/MDC and KKM guidelines,
--          with clinic-group clinical sharing RLS and soft-delete guards.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. PATIENT VISITS TABLE (0..N PER PATIENT)
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.patient_visits (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id      TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    branch_id            TEXT NOT NULL REFERENCES public.branches(id),
    patient_id           UUID NOT NULL REFERENCES public.patients(id),
    appointment_id       UUID NULL REFERENCES public.appointments(id),
    dentist_id           UUID NULL REFERENCES public.dentists(id),
    doctor_name          TEXT NOT NULL DEFAULT '',
    visit_number         INTEGER NOT NULL DEFAULT 1 CHECK (visit_number >= 1),
    visit_date           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    subjective           TEXT NOT NULL DEFAULT '',
    objective            TEXT NOT NULL DEFAULT '',
    assessment           TEXT NOT NULL DEFAULT '',
    plan                 TEXT NOT NULL DEFAULT '',
    anesthesia_used      TEXT NOT NULL DEFAULT '',
    tooth_shade          TEXT NOT NULL DEFAULT '',
    post_op_instructions TEXT NOT NULL DEFAULT '',
    created_by           TEXT NOT NULL DEFAULT '',
    last_updated_by      TEXT NOT NULL DEFAULT '',
    is_deleted           BOOLEAN NOT NULL DEFAULT FALSE,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_patient_visits_appointment
    ON public.patient_visits (appointment_id)
    WHERE appointment_id IS NOT NULL AND is_deleted = FALSE;

CREATE INDEX IF NOT EXISTS idx_patient_visits_patient_date
    ON public.patient_visits (patient_id, visit_date DESC)
    WHERE is_deleted = FALSE;

CREATE INDEX IF NOT EXISTS idx_patient_visits_group
    ON public.patient_visits (clinic_group_id, patient_id)
    WHERE is_deleted = FALSE;

-- -----------------------------------------------------------------------------
-- 2. PER-VISIT ODONTOGRAM ENTRIES (PERMANENT 11-48 & PRIMARY 51-85 + SURFACES)
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.visit_odontogram_entries (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    visit_id        UUID NOT NULL REFERENCES public.patient_visits(id),
    patient_id      UUID NOT NULL REFERENCES public.patients(id),
    tooth_number    INTEGER NOT NULL CHECK (
        (tooth_number BETWEEN 11 AND 18) OR
        (tooth_number BETWEEN 21 AND 28) OR
        (tooth_number BETWEEN 31 AND 38) OR
        (tooth_number BETWEEN 41 AND 48) OR
        (tooth_number BETWEEN 51 AND 55) OR
        (tooth_number BETWEEN 61 AND 65) OR
        (tooth_number BETWEEN 71 AND 75) OR
        (tooth_number BETWEEN 81 AND 85)
    ),
    condition       TEXT NOT NULL DEFAULT 'Healthy'
        CHECK (condition IN (
            'Healthy', 'Caries', 'Filling', 'Crown',
            'RootCanal', 'ExtractionNeeded', 'Missing', 'Implant'
        )),
    surfaces        TEXT NOT NULL DEFAULT '',
    procedure_name  TEXT NOT NULL DEFAULT '',
    fee             NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (fee >= 0),
    stage           TEXT NOT NULL DEFAULT 'Proposed'
        CHECK (stage IN ('Proposed', 'Accepted', 'Completed')),
    doctor_name     TEXT NOT NULL DEFAULT '',
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT uq_visit_odontogram_tooth UNIQUE (visit_id, tooth_number)
);

CREATE INDEX IF NOT EXISTS idx_visit_odontogram_visit
    ON public.visit_odontogram_entries (visit_id, tooth_number)
    WHERE is_deleted = FALSE;

CREATE INDEX IF NOT EXISTS idx_visit_odontogram_patient
    ON public.visit_odontogram_entries (patient_id, visit_id)
    WHERE is_deleted = FALSE;

-- -----------------------------------------------------------------------------
-- 3. LINK APPOINTMENTS & INVOICES TO PATIENT VISITS
-- -----------------------------------------------------------------------------

ALTER TABLE public.appointments
    ADD COLUMN IF NOT EXISTS visit_id UUID NULL REFERENCES public.patient_visits(id);

ALTER TABLE public.invoices
    ADD COLUMN IF NOT EXISTS visit_id UUID NULL REFERENCES public.patient_visits(id);

CREATE INDEX IF NOT EXISTS idx_appointments_visit
    ON public.appointments (visit_id)
    WHERE visit_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_invoices_visit
    ON public.invoices (visit_id)
    WHERE visit_id IS NOT NULL;

-- -----------------------------------------------------------------------------
-- 4. IMMUTABILITY / SOFT-DELETE TRIGGERS & CLINIC-GROUP SHARED CLINICAL RLS
-- -----------------------------------------------------------------------------

DROP TRIGGER IF EXISTS trg_no_delete_patient_visits ON public.patient_visits;
CREATE TRIGGER trg_no_delete_patient_visits
    BEFORE DELETE ON public.patient_visits
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_visit_odontogram ON public.visit_odontogram_entries;
CREATE TRIGGER trg_no_delete_visit_odontogram
    BEFORE DELETE ON public.visit_odontogram_entries
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

ALTER TABLE public.patient_visits ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.visit_odontogram_entries ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS patient_visits_group_select ON public.patient_visits;
CREATE POLICY patient_visits_group_select ON public.patient_visits
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id() AND is_deleted = FALSE);

DROP POLICY IF EXISTS patient_visits_group_insert ON public.patient_visits;
CREATE POLICY patient_visits_group_insert ON public.patient_visits
    FOR INSERT
    WITH CHECK (
        clinic_group_id = public.auth_clinic_group_id()
        AND branch_id = ANY(public.auth_allowed_branches())
    );

DROP POLICY IF EXISTS patient_visits_group_update ON public.patient_visits;
CREATE POLICY patient_visits_group_update ON public.patient_visits
    FOR UPDATE
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS patient_visits_deny_delete ON public.patient_visits;
CREATE POLICY patient_visits_deny_delete ON public.patient_visits
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS visit_odontogram_group_all ON public.visit_odontogram_entries;
CREATE POLICY visit_odontogram_group_all ON public.visit_odontogram_entries
    FOR ALL
    USING (clinic_group_id = public.auth_clinic_group_id() AND is_deleted = FALSE)
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS visit_odontogram_deny_delete ON public.visit_odontogram_entries;
CREATE POLICY visit_odontogram_deny_delete ON public.visit_odontogram_entries
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

-- -----------------------------------------------------------------------------
-- 5. IDEMPOTENT BACKFILL OF LEGACY SOAP NOTES & PATIENT-GLOBAL ODONTOGRAMS
-- -----------------------------------------------------------------------------

INSERT INTO public.patient_visits (
    id, clinic_group_id, branch_id, patient_id, doctor_name, visit_number, visit_date,
    subjective, objective, assessment, plan, anesthesia_used, tooth_shade, post_op_instructions,
    created_by, last_updated_by, created_at, updated_at
)
SELECT
    s.id,
    s.clinic_group_id,
    s.branch_id,
    s.patient_id,
    s.doctor_name,
    ROW_NUMBER() OVER (PARTITION BY s.patient_id ORDER BY s.visit_date ASC, s.created_at ASC),
    s.visit_date,
    s.subjective,
    s.objective,
    s.assessment,
    s.plan,
    s.anesthesia_used,
    s.tooth_shade,
    s.post_op_instructions,
    s.doctor_name,
    s.doctor_name,
    s.created_at,
    s.visit_date
FROM public.soap_notes s
WHERE s.is_voided = FALSE
ON CONFLICT (id) DO NOTHING;

-- Ensure patients with legacy odontogram_entries but zero soap_notes receive an initial baseline visit
INSERT INTO public.patient_visits (
    clinic_group_id, branch_id, patient_id, doctor_name, visit_number, visit_date,
    subjective, created_by, last_updated_by
)
SELECT DISTINCT
    p.clinic_group_id,
    p.primary_branch_id,
    p.id,
    COALESCE(NULLIF(MAX(o.doctor_name), ''), 'Dr. Aina Rahman'),
    1,
    MAX(o.updated_at),
    'Migrated baseline dental chart',
    'System Migration',
    'System Migration'
FROM public.odontogram_entries o
JOIN public.patients p ON p.id = o.patient_id
WHERE o.is_deleted = FALSE
  AND NOT EXISTS (
      SELECT 1 FROM public.patient_visits v
      WHERE v.patient_id = o.patient_id AND v.is_deleted = FALSE
  )
GROUP BY p.clinic_group_id, p.primary_branch_id, p.id;

-- Attach legacy odontogram_entries onto each patient's latest visit if not yet populated
INSERT INTO public.visit_odontogram_entries (
    clinic_group_id, visit_id, patient_id, tooth_number, condition, surfaces,
    procedure_name, fee, stage, doctor_name, updated_at, is_deleted
)
SELECT
    o.clinic_group_id,
    latest_visit.id,
    o.patient_id,
    o.tooth_number,
    o.condition,
    '',
    o.procedure_name,
    o.fee,
    o.stage,
    o.doctor_name,
    o.updated_at,
    FALSE
FROM public.odontogram_entries o
JOIN LATERAL (
    SELECT v.id
    FROM public.patient_visits v
    WHERE v.patient_id = o.patient_id AND v.is_deleted = FALSE
    ORDER BY v.visit_date DESC, v.visit_number DESC
    LIMIT 1
) latest_visit ON TRUE
WHERE o.is_deleted = FALSE
ON CONFLICT (visit_id, tooth_number) DO NOTHING;

