-- =============================================================================
-- Occlusa (Malaysian Dental Practice Management Software)
-- Migration: 001_occlusa_tier1_tier2_schema_rls.sql
-- Target: PostgreSQL 15+ / Supabase Pro (Micro compute friendly, fully indexed)
--
-- ARCHITECTURAL INVARIANTS ENFORCED:
-- 1. Row-Level Security (RLS) enabled on EVERY public table.
-- 2. Clinical records (patients, medical_alerts, odontogram_entries, soap_notes)
--    are shared across sister branches within the same clinic_group_id.
-- 3. Financial & audit tables (invoices, invoice_lines, payments,
--    daily_sales_reconciliations, audit_logs) are strictly branch-isolated via
--    branch_id = ANY(public.auth_allowed_branches()).
-- 4. Immutable clinical/financial history: hard DELETE is blocked via triggers
--    and RLS policies on invoices, invoice_lines, payments, soap_notes,
--    audit_logs, and daily_sales_reconciliations. Audit logs also forbid UPDATE.
-- =============================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- -----------------------------------------------------------------------------
-- 1. JWT CLAIM HELPER FUNCTIONS
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION public.auth_allowed_branches()
RETURNS TEXT[]
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(
        ARRAY(
            SELECT jsonb_array_elements_text(
                COALESCE(
                    NULLIF(current_setting('request.jwt.claims', true), '')::jsonb -> 'allowed_branches',
                    '[]'::jsonb
                )
            )
        ),
        ARRAY[]::TEXT[]
    );
$$;

CREATE OR REPLACE FUNCTION public.auth_clinic_group_id()
RETURNS TEXT
LANGUAGE sql
STABLE
AS $$
    SELECT COALESCE(
        NULLIF(
            NULLIF(current_setting('request.jwt.claims', true), '')::jsonb ->> 'clinic_group_id',
            ''
        ),
        (
            SELECT b.clinic_group_id
            FROM public.branches b
            WHERE b.id = ANY(public.auth_allowed_branches())
            ORDER BY b.id
            LIMIT 1
        )
    );
$$;

-- -----------------------------------------------------------------------------
-- 2. IMMUTABILITY & SOFT-DELETE GUARD TRIGGERS
-- -----------------------------------------------------------------------------

CREATE OR REPLACE FUNCTION public.prevent_hard_delete()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Hard DELETE is prohibited on table %. Use soft-delete or invoice voiding instead.', TG_TABLE_NAME;
END;
$$;

CREATE OR REPLACE FUNCTION public.prevent_audit_log_mutation()
RETURNS TRIGGER
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION 'Table audit_logs is append-only; UPDATE and DELETE are strictly prohibited.';
END;
$$;

-- -----------------------------------------------------------------------------
-- 3. CORE TABLES
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.branches (
    id              TEXT PRIMARY KEY,
    clinic_group_id TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    name            TEXT NOT NULL,
    address         TEXT NOT NULL DEFAULT '',
    phone           TEXT NOT NULL DEFAULT '',
    operating_hours TEXT NOT NULL DEFAULT 'Mon-Sat 09:00-18:00',
    is_active       BOOLEAN NOT NULL DEFAULT TRUE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.patients (
    id                UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id   TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    primary_branch_id TEXT NOT NULL REFERENCES public.branches(id),
    full_name         TEXT NOT NULL,
    ic_number         TEXT NOT NULL,
    date_of_birth     DATE NOT NULL,
    gender            TEXT NOT NULL DEFAULT '',
    phone             TEXT NOT NULL DEFAULT '',
    address           TEXT NOT NULL DEFAULT '',
    is_mykad_verified BOOLEAN NOT NULL DEFAULT FALSE,
    consent_signed_at TIMESTAMPTZ NULL,
    is_deleted        BOOLEAN NOT NULL DEFAULT FALSE,
    deleted_at        TIMESTAMPTZ NULL,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_patients_group_ic UNIQUE (clinic_group_id, ic_number)
);

CREATE TABLE IF NOT EXISTS public.medical_alerts (
    patient_id       UUID PRIMARY KEY REFERENCES public.patients(id),
    clinic_group_id  TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    allergies        TEXT NOT NULL DEFAULT '',
    on_anticoagulants BOOLEAN NOT NULL DEFAULT FALSE,
    has_diabetes     BOOLEAN NOT NULL DEFAULT FALSE,
    is_pregnant      BOOLEAN NOT NULL DEFAULT FALSE,
    notes            TEXT NOT NULL DEFAULT '',
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.appointments (
    id                        UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id           TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    branch_id                 TEXT NOT NULL REFERENCES public.branches(id),
    patient_id                UUID NOT NULL REFERENCES public.patients(id),
    patient_name              TEXT NOT NULL,
    patient_ic                TEXT NOT NULL DEFAULT '',
    doctor_name               TEXT NOT NULL DEFAULT '',
    room_name                 TEXT NOT NULL DEFAULT '',
    scheduled_at              TIMESTAMPTZ NOT NULL,
    procedure_summary         TEXT NOT NULL DEFAULT '',
    status                    TEXT NOT NULL DEFAULT 'Scheduled'
        CHECK (status IN (
            'Scheduled', 'AiBookedPending', 'Arrived', 'Waiting',
            'InChair', 'AwaitingCheckout', 'Completed', 'Cancelled'
        )),
    has_critical_medical_alert BOOLEAN NOT NULL DEFAULT FALSE,
    alert_summary             TEXT NOT NULL DEFAULT '',
    is_deleted                BOOLEAN NOT NULL DEFAULT FALSE,
    created_at                TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at                TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.odontogram_entries (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    patient_id      UUID NOT NULL REFERENCES public.patients(id),
    tooth_number    INTEGER NOT NULL CHECK (
        (tooth_number BETWEEN 11 AND 18) OR
        (tooth_number BETWEEN 21 AND 28) OR
        (tooth_number BETWEEN 31 AND 38) OR
        (tooth_number BETWEEN 41 AND 48)
    ),
    condition       TEXT NOT NULL DEFAULT 'Healthy'
        CHECK (condition IN (
            'Healthy', 'Caries', 'Filling', 'Crown',
            'RootCanal', 'ExtractionNeeded', 'Missing', 'Implant'
        )),
    procedure_name  TEXT NOT NULL DEFAULT '',
    fee             NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (fee >= 0),
    stage           TEXT NOT NULL DEFAULT 'Proposed'
        CHECK (stage IN ('Proposed', 'Accepted', 'Completed')),
    doctor_name     TEXT NOT NULL DEFAULT '',
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_deleted      BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT uq_odontogram_patient_tooth UNIQUE (patient_id, tooth_number)
);

CREATE TABLE IF NOT EXISTS public.soap_notes (
    id                   UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id      TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    branch_id            TEXT NOT NULL REFERENCES public.branches(id),
    patient_id           UUID NOT NULL REFERENCES public.patients(id),
    doctor_name          TEXT NOT NULL,
    visit_date           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    subjective           TEXT NOT NULL DEFAULT '',
    objective            TEXT NOT NULL DEFAULT '',
    assessment           TEXT NOT NULL DEFAULT '',
    plan                 TEXT NOT NULL DEFAULT '',
    anesthesia_used      TEXT NOT NULL DEFAULT '',
    tooth_shade          TEXT NOT NULL DEFAULT '',
    post_op_instructions TEXT NOT NULL DEFAULT '',
    is_voided            BOOLEAN NOT NULL DEFAULT FALSE,
    void_reason          TEXT NULL,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.soap_macros (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id     TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    title               TEXT NOT NULL,
    category            TEXT NOT NULL,
    subjective_template TEXT NOT NULL DEFAULT '',
    objective_template  TEXT NOT NULL DEFAULT '',
    assessment_template TEXT NOT NULL DEFAULT '',
    plan_template       TEXT NOT NULL DEFAULT '',
    default_fee         NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (default_fee >= 0),
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_soap_macros_group_title UNIQUE (clinic_group_id, title)
);

CREATE TABLE IF NOT EXISTS public.invoices (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_number           TEXT NOT NULL UNIQUE,
    branch_id                TEXT NOT NULL REFERENCES public.branches(id),
    patient_id               UUID NOT NULL REFERENCES public.patients(id),
    patient_name             TEXT NOT NULL,
    doctor_name              TEXT NOT NULL DEFAULT '',
    appointment_id           UUID NULL REFERENCES public.appointments(id),
    subtotal                 NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (subtotal >= 0),
    discount_amount          NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (discount_amount >= 0),
    panel_covered_amount     NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (panel_covered_amount >= 0),
    panel_name               TEXT NULL,
    patient_payable_amount   NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (patient_payable_amount >= 0),
    paid_amount              NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (paid_amount >= 0),
    doctor_commission_amount NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (doctor_commission_amount >= 0),
    is_voided                BOOLEAN NOT NULL DEFAULT FALSE,
    void_reason              TEXT NULL,
    voided_by                TEXT NULL,
    voided_at                TIMESTAMPTZ NULL,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.invoice_lines (
    id             UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_id     UUID NOT NULL REFERENCES public.invoices(id),
    branch_id      TEXT NOT NULL REFERENCES public.branches(id),
    tooth_number   INTEGER NULL,
    procedure_name TEXT NOT NULL,
    unit_price     NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (unit_price >= 0),
    quantity       INTEGER NOT NULL DEFAULT 1 CHECK (quantity >= 0),
    line_total     NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (line_total >= 0),
    source_stage   TEXT NULL CHECK (source_stage IS NULL OR source_stage IN ('Proposed', 'Accepted', 'Completed')),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.payments (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    invoice_id   UUID NOT NULL REFERENCES public.invoices(id),
    branch_id    TEXT NOT NULL REFERENCES public.branches(id),
    method       TEXT NOT NULL CHECK (method IN ('Cash', 'Card', 'QrTransfer', 'CorporatePanel')),
    amount       NUMERIC(12, 2) NOT NULL CHECK (amount > 0),
    reference_no TEXT NULL,
    panel_name   TEXT NULL,
    collected_by TEXT NOT NULL DEFAULT '',
    paid_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.daily_sales_reconciliations (
    id                       UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id                TEXT NOT NULL REFERENCES public.branches(id),
    sales_date               DATE NOT NULL,
    total_invoices_count     INTEGER NOT NULL DEFAULT 0,
    voided_invoices_count    INTEGER NOT NULL DEFAULT 0,
    gross_revenue            NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    total_discounts          NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    total_panel_claims       NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    total_cash_collected     NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    total_card_collected     NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    total_qr_collected       NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    total_doctor_commissions NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    net_collected            NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    is_balanced              BOOLEAN NOT NULL DEFAULT FALSE,
    reconciled_by            TEXT NOT NULL,
    reconciled_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT uq_daily_reconcile_branch_date UNIQUE (branch_id, sales_date)
);

CREATE TABLE IF NOT EXISTS public.whatsapp_threads (
    thread_id              TEXT PRIMARY KEY,
    clinic_group_id        TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    branch_id              TEXT NOT NULL REFERENCES public.branches(id),
    patient_phone          TEXT NOT NULL,
    patient_name           TEXT NOT NULL DEFAULT '',
    is_emergency_escalated BOOLEAN NOT NULL DEFAULT FALSE,
    emergency_reason       TEXT NULL,
    last_updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS public.whatsapp_messages (
    id                 UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    thread_id          TEXT NOT NULL REFERENCES public.whatsapp_threads(thread_id),
    branch_id          TEXT NOT NULL REFERENCES public.branches(id),
    sender             TEXT NOT NULL,
    text               TEXT NOT NULL,
    timestamp          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    created_booking_id UUID NULL REFERENCES public.appointments(id)
);

CREATE TABLE IF NOT EXISTS public.audit_logs (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    branch_id   TEXT NOT NULL REFERENCES public.branches(id),
    user_id     TEXT NOT NULL,
    timestamp   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    action      TEXT NOT NULL,
    entity_name TEXT NOT NULL,
    entity_id   TEXT NOT NULL,
    old_value   TEXT NULL,
    new_value   TEXT NULL
);

CREATE TABLE IF NOT EXISTS public.backup_verifications (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id     TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    verified_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    file_name           TEXT NOT NULL,
    sha256_checksum     TEXT NOT NULL,
    size_bytes          BIGINT NOT NULL CHECK (size_bytes >= 0),
    is_encrypted        BOOLEAN NOT NULL DEFAULT TRUE,
    restore_test_passed BOOLEAN NOT NULL DEFAULT FALSE,
    verified_by         TEXT NOT NULL DEFAULT 'System',
    notes               TEXT NOT NULL DEFAULT ''
);

-- -----------------------------------------------------------------------------
-- 4. PERFORMANCE INDEXES (MICRO COMPUTE OPTIMIZED)
-- -----------------------------------------------------------------------------

CREATE INDEX IF NOT EXISTS idx_branches_group ON public.branches (clinic_group_id);
CREATE INDEX IF NOT EXISTS idx_patients_group_name ON public.patients (clinic_group_id, full_name) WHERE is_deleted = FALSE;
CREATE INDEX IF NOT EXISTS idx_patients_phone ON public.patients (clinic_group_id, phone) WHERE is_deleted = FALSE;
CREATE INDEX IF NOT EXISTS idx_medical_alerts_group ON public.medical_alerts (clinic_group_id);
CREATE INDEX IF NOT EXISTS idx_appointments_branch_sched ON public.appointments (branch_id, scheduled_at) WHERE is_deleted = FALSE;
CREATE INDEX IF NOT EXISTS idx_appointments_branch_status ON public.appointments (branch_id, status) WHERE is_deleted = FALSE;
CREATE INDEX IF NOT EXISTS idx_appointments_patient ON public.appointments (patient_id);
CREATE INDEX IF NOT EXISTS idx_odontogram_patient ON public.odontogram_entries (patient_id, tooth_number) WHERE is_deleted = FALSE;
CREATE INDEX IF NOT EXISTS idx_odontogram_group ON public.odontogram_entries (clinic_group_id, patient_id);
CREATE INDEX IF NOT EXISTS idx_soap_notes_patient_visit ON public.soap_notes (patient_id, visit_date DESC) WHERE is_voided = FALSE;
CREATE INDEX IF NOT EXISTS idx_soap_macros_group ON public.soap_macros (clinic_group_id, category) WHERE is_active = TRUE;
CREATE INDEX IF NOT EXISTS idx_invoices_branch_created ON public.invoices (branch_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_invoices_branch_void ON public.invoices (branch_id, is_voided);
CREATE INDEX IF NOT EXISTS idx_invoices_patient ON public.invoices (patient_id);
CREATE INDEX IF NOT EXISTS idx_invoice_lines_invoice ON public.invoice_lines (invoice_id);
CREATE INDEX IF NOT EXISTS idx_invoice_lines_branch ON public.invoice_lines (branch_id);
CREATE INDEX IF NOT EXISTS idx_payments_invoice ON public.payments (invoice_id);
CREATE INDEX IF NOT EXISTS idx_payments_branch_paid_at ON public.payments (branch_id, paid_at DESC);
CREATE INDEX IF NOT EXISTS idx_daily_reconcil_branch_date ON public.daily_sales_reconciliations (branch_id, sales_date DESC);
CREATE INDEX IF NOT EXISTS idx_whatsapp_threads_branch ON public.whatsapp_threads (branch_id, last_updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_whatsapp_messages_thread ON public.whatsapp_messages (thread_id, timestamp);
CREATE INDEX IF NOT EXISTS idx_audit_logs_branch_ts ON public.audit_logs (branch_id, timestamp DESC);
CREATE INDEX IF NOT EXISTS idx_audit_logs_entity ON public.audit_logs (entity_name, entity_id);
CREATE INDEX IF NOT EXISTS idx_backup_verifications_group_ts ON public.backup_verifications (clinic_group_id, verified_at DESC);

-- -----------------------------------------------------------------------------
-- 5. PREVENT HARD DELETES & AUDIT LOG MUTATIONS
-- -----------------------------------------------------------------------------

DROP TRIGGER IF EXISTS trg_no_delete_invoices ON public.invoices;
CREATE TRIGGER trg_no_delete_invoices
    BEFORE DELETE ON public.invoices
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_invoice_lines ON public.invoice_lines;
CREATE TRIGGER trg_no_delete_invoice_lines
    BEFORE DELETE ON public.invoice_lines
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_payments ON public.payments;
CREATE TRIGGER trg_no_delete_payments
    BEFORE DELETE ON public.payments
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_soap_notes ON public.soap_notes;
CREATE TRIGGER trg_no_delete_soap_notes
    BEFORE DELETE ON public.soap_notes
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_daily_reconciliations ON public.daily_sales_reconciliations;
CREATE TRIGGER trg_no_delete_daily_reconciliations
    BEFORE DELETE ON public.daily_sales_reconciliations
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_patients ON public.patients;
CREATE TRIGGER trg_no_delete_patients
    BEFORE DELETE ON public.patients
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_immutable_audit_logs ON public.audit_logs;
CREATE TRIGGER trg_immutable_audit_logs
    BEFORE UPDATE OR DELETE ON public.audit_logs
    FOR EACH ROW EXECUTE FUNCTION public.prevent_audit_log_mutation();

-- -----------------------------------------------------------------------------
-- 6. ROW-LEVEL SECURITY (ENABLE ON EVERY PUBLIC TABLE)
-- -----------------------------------------------------------------------------

ALTER TABLE public.branches ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.patients ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.medical_alerts ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.appointments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.odontogram_entries ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.soap_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.soap_macros ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.invoice_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.payments ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.daily_sales_reconciliations ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.whatsapp_threads ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.whatsapp_messages ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.audit_logs ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.backup_verifications ENABLE ROW LEVEL SECURITY;

-- Branches: readable within user's clinic group
DROP POLICY IF EXISTS branches_group_select ON public.branches;
CREATE POLICY branches_group_select ON public.branches
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id());

-- CLINICAL GROUP-SHARED POLICIES (patients, medical_alerts, odontogram_entries, soap_notes, soap_macros)
DROP POLICY IF EXISTS patients_group_select ON public.patients;
CREATE POLICY patients_group_select ON public.patients
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id() AND is_deleted = FALSE);

DROP POLICY IF EXISTS patients_group_insert ON public.patients;
CREATE POLICY patients_group_insert ON public.patients
    FOR INSERT
    WITH CHECK (
        clinic_group_id = public.auth_clinic_group_id()
        AND primary_branch_id = ANY(public.auth_allowed_branches())
    );

DROP POLICY IF EXISTS patients_group_update ON public.patients;
CREATE POLICY patients_group_update ON public.patients
    FOR UPDATE
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS medical_alerts_group_all ON public.medical_alerts;
CREATE POLICY medical_alerts_group_all ON public.medical_alerts
    FOR ALL
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS odontogram_group_all ON public.odontogram_entries;
CREATE POLICY odontogram_group_all ON public.odontogram_entries
    FOR ALL
    USING (clinic_group_id = public.auth_clinic_group_id() AND is_deleted = FALSE)
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS soap_notes_group_select ON public.soap_notes;
CREATE POLICY soap_notes_group_select ON public.soap_notes
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id() AND is_voided = FALSE);

DROP POLICY IF EXISTS soap_notes_group_insert ON public.soap_notes;
CREATE POLICY soap_notes_group_insert ON public.soap_notes
    FOR INSERT
    WITH CHECK (
        clinic_group_id = public.auth_clinic_group_id()
        AND branch_id = ANY(public.auth_allowed_branches())
    );

DROP POLICY IF EXISTS soap_notes_group_update ON public.soap_notes;
CREATE POLICY soap_notes_group_update ON public.soap_notes
    FOR UPDATE
    USING (clinic_group_id = public.auth_clinic_group_id() AND branch_id = ANY(public.auth_allowed_branches()))
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS soap_notes_deny_delete ON public.soap_notes;
CREATE POLICY soap_notes_deny_delete ON public.soap_notes
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS soap_macros_group_select ON public.soap_macros;
CREATE POLICY soap_macros_group_select ON public.soap_macros
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id() AND is_active = TRUE);

-- OPERATIONAL BRANCH-ISOLATED POLICIES (appointments, whatsapp)
DROP POLICY IF EXISTS appointments_branch_all ON public.appointments;
CREATE POLICY appointments_branch_all ON public.appointments
    FOR ALL
    USING (branch_id = ANY(public.auth_allowed_branches()) AND is_deleted = FALSE)
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS whatsapp_threads_branch_all ON public.whatsapp_threads;
CREATE POLICY whatsapp_threads_branch_all ON public.whatsapp_threads
    FOR ALL
    USING (branch_id = ANY(public.auth_allowed_branches()))
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS whatsapp_messages_branch_all ON public.whatsapp_messages;
CREATE POLICY whatsapp_messages_branch_all ON public.whatsapp_messages
    FOR ALL
    USING (branch_id = ANY(public.auth_allowed_branches()))
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

-- STRICT FINANCIAL BRANCH-ISOLATED POLICIES (invoices, invoice_lines, payments, daily_sales_reconciliations, audit_logs)
DROP POLICY IF EXISTS invoices_branch_select ON public.invoices;
CREATE POLICY invoices_branch_select ON public.invoices
    FOR SELECT
    USING (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS invoices_branch_insert ON public.invoices;
CREATE POLICY invoices_branch_insert ON public.invoices
    FOR INSERT
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS invoices_branch_update ON public.invoices;
CREATE POLICY invoices_branch_update ON public.invoices
    FOR UPDATE
    USING (branch_id = ANY(public.auth_allowed_branches()))
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS invoices_deny_delete ON public.invoices;
CREATE POLICY invoices_deny_delete ON public.invoices
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS invoice_lines_branch_select ON public.invoice_lines;
CREATE POLICY invoice_lines_branch_select ON public.invoice_lines
    FOR SELECT
    USING (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS invoice_lines_branch_insert ON public.invoice_lines;
CREATE POLICY invoice_lines_branch_insert ON public.invoice_lines
    FOR INSERT
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS invoice_lines_branch_update ON public.invoice_lines;
CREATE POLICY invoice_lines_branch_update ON public.invoice_lines
    FOR UPDATE
    USING (branch_id = ANY(public.auth_allowed_branches()))
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS invoice_lines_deny_delete ON public.invoice_lines;
CREATE POLICY invoice_lines_deny_delete ON public.invoice_lines
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS payments_branch_select ON public.payments;
CREATE POLICY payments_branch_select ON public.payments
    FOR SELECT
    USING (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS payments_branch_insert ON public.payments;
CREATE POLICY payments_branch_insert ON public.payments
    FOR INSERT
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS payments_deny_delete ON public.payments;
CREATE POLICY payments_deny_delete ON public.payments
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS daily_reconcil_branch_select ON public.daily_sales_reconciliations;
CREATE POLICY daily_reconcil_branch_select ON public.daily_sales_reconciliations
    FOR SELECT
    USING (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS daily_reconcil_branch_insert ON public.daily_sales_reconciliations;
CREATE POLICY daily_reconcil_branch_insert ON public.daily_sales_reconciliations
    FOR INSERT
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS daily_reconcil_branch_update ON public.daily_sales_reconciliations;
CREATE POLICY daily_reconcil_branch_update ON public.daily_sales_reconciliations
    FOR UPDATE
    USING (branch_id = ANY(public.auth_allowed_branches()))
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS daily_reconcil_deny_delete ON public.daily_sales_reconciliations;
CREATE POLICY daily_reconcil_deny_delete ON public.daily_sales_reconciliations
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS audit_logs_branch_select ON public.audit_logs;
CREATE POLICY audit_logs_branch_select ON public.audit_logs
    FOR SELECT
    USING (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS audit_logs_branch_insert ON public.audit_logs;
CREATE POLICY audit_logs_branch_insert ON public.audit_logs
    FOR INSERT
    WITH CHECK (branch_id = ANY(public.auth_allowed_branches()));

DROP POLICY IF EXISTS audit_logs_deny_delete ON public.audit_logs;
CREATE POLICY audit_logs_deny_delete ON public.audit_logs
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

DROP POLICY IF EXISTS backup_verifications_group_select ON public.backup_verifications;
CREATE POLICY backup_verifications_group_select ON public.backup_verifications
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS backup_verifications_group_insert ON public.backup_verifications;
CREATE POLICY backup_verifications_group_insert ON public.backup_verifications
    FOR INSERT
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

-- -----------------------------------------------------------------------------
-- 7. SEED DATA: CLINIC GROUP BRANCHES & SMART SOAP MACROS
-- -----------------------------------------------------------------------------

INSERT INTO public.branches (id, clinic_group_id, name, address, phone, operating_hours)
VALUES
    ('HQ-KL', 'OCCLUSA-MY', 'Occlusa Dental KL Sentral HQ', 'Level 3, Menara Sentral, Jalan Tun Sambanthan, 50470 Kuala Lumpur', '+603-2274-8810', 'Mon-Sat 09:00-18:00'),
    ('PJ-SS2', 'OCCLUSA-MY', 'Occlusa Dental PJ SS2', 'No. 48, Jalan SS 2/67, 47300 Petaling Jaya, Selangor', '+603-7876-4420', 'Mon-Sat 09:00-18:00')
ON CONFLICT (id) DO NOTHING;

INSERT INTO public.soap_macros (id, clinic_group_id, title, category, subjective_template, objective_template, assessment_template, plan_template, default_fee)
VALUES
    (
        '10000000-0000-0000-0000-000000000101',
        'OCCLUSA-MY',
        'Routine Scaling & Polishing',
        'Preventive',
        'Patient presents for routine 6-monthly dental hygiene scaling and polishing. No acute pain.',
        'Mild-to-moderate supragingival calculus lower linguals. Gingiva mildly inflamed, no mobile teeth.',
        'Generalized mild plaque-induced gingivitis.',
        'Full mouth ultrasonic scaling, prophy paste polishing, OHI reinforced. Recall in 6 months.',
        150.00
    ),
    (
        '10000000-0000-0000-0000-000000000102',
        'OCCLUSA-MY',
        'Posterior Composite Filling',
        'Restorative',
        'Patient reports localized cold sensitivity on posterior tooth. No spontaneous nocturnal pain.',
        'Occlusal caries present. Cold vitality test positive with brief response. No percussion tenderness.',
        'Dentinal caries without pulpal involvement.',
        'LA infiltration, caries excavation, selective enamel etch, composite restoration (Shade A2), occlusion checked.',
        200.00
    ),
    (
        '10000000-0000-0000-0000-000000000103',
        'OCCLUSA-MY',
        'Simple Tooth Extraction',
        'Oral Surgery',
        'Patient complains of broken non-restorable tooth with intermittent discomfort.',
        'Grossly carious crown. Adjacent gingiva intact. Pre-op PA confirms favourable root morphology.',
        'Non-restorable tooth indicated for simple forceps extraction.',
        'LA 2% Lidocaine w/ 1:80,000 adrenaline, atraumatic elevation & forceps delivery, hemostasis achieved with gauze pack. Post-op instructions given.',
        180.00
    ),
    (
        '10000000-0000-0000-0000-000000000104',
        'OCCLUSA-MY',
        'New Patient Consultation & OPG',
        'Diagnostic',
        'New patient seeking general dental assessment and treatment plan.',
        'Extra-oral NAD. Intra-oral exam completed; full FDI chart recorded.',
        'Routine dental assessment completed.',
        'Discuss treatment priorities, preventive care, and panel coverage options.',
        50.00
    )
ON CONFLICT (clinic_group_id, title) DO NOTHING;

-- -----------------------------------------------------------------------------
-- SAMPLE CLINIC DATA & RLS VERIFICATION REFERENCE (COMMENTS)
-- -----------------------------------------------------------------------------
-- Example: Insert a sample patient & alert under OCCLUSA-MY:
-- INSERT INTO public.patients (id, clinic_group_id, primary_branch_id, full_name, ic_number, date_of_birth, gender, phone, address, is_mykad_verified)
-- VALUES ('20000000-0000-0000-0000-000000000001', 'OCCLUSA-MY', 'HQ-KL', 'NURUL AISYAH BINTI ZULKIFLI', '920415-14-5678', '1992-04-15', 'Female', '+6012-3456789', '12 Jalan Bangsar, 59000 Kuala Lumpur', TRUE);
--
-- INSERT INTO public.medical_alerts (patient_id, clinic_group_id, allergies, on_anticoagulants, has_diabetes, is_pregnant, notes)
-- VALUES ('20000000-0000-0000-0000-000000000001', 'OCCLUSA-MY', 'Penicillin', FALSE, FALSE, FALSE, 'Use Clindamycin if prophylaxis needed');
--
-- Example: Verify RLS isolation in psql / Supabase SQL Editor:
-- BEGIN;
--   SELECT set_config('request.jwt.claims', '{"sub":"staff-pj","clinic_group_id":"OCCLUSA-MY","allowed_branches":["PJ-SS2"]}', true);
--   SELECT full_name, ic_number FROM public.patients; -- Returns HQ-KL + PJ-SS2 clinical records (shared within OCCLUSA-MY).
--   SELECT invoice_number, branch_id FROM public.invoices; -- Returns ONLY PJ-SS2 invoices (HQ-KL strictly hidden).
-- COMMIT;
