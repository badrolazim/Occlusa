-- =============================================================================
-- Occlusa (Malaysian Dental Practice Management Software)
-- Migration: 006_decoupled_billing_credits_and_settings.sql
-- Purpose:
--   1. Decouple invoicing from clinical visit reports (support Walk-In OTC retail
--      invoices without patient_id, OTC commission exclusion on invoice_lines).
--   2. Introduce clinic-group shared Patient Credit Wallet & append-only credit
--      ledger + PatientCredit settlement method.
--   3. Consolidate all clinic items (Bookable Appointments, Odontogram Presets,
--      Radiographs, Medications, and OTC Retail) into ONE Unified Master Catalogue
--      (public.billing_catalog_items) + Per-Doctor Invoice Preferences.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- 1. WALK-IN OTC INVOICES, OTC COMMISSION EXCLUSION & OVERPAYMENT CREDIT TRACKING
-- -----------------------------------------------------------------------------

ALTER TABLE public.invoices
    ALTER COLUMN patient_id DROP NOT NULL;

ALTER TABLE public.invoices
    ADD COLUMN IF NOT EXISTS is_walk_in_retail BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS credited_overpayment_amount NUMERIC(12, 2) NOT NULL DEFAULT 0.00
        CHECK (credited_overpayment_amount >= 0);

ALTER TABLE public.invoice_lines
    ADD COLUMN IF NOT EXISTS catalog_item_id UUID NULL,
    ADD COLUMN IF NOT EXISTS category TEXT NOT NULL DEFAULT 'Procedure'
        CHECK (category IN ('Procedure', 'Radiograph', 'Medication', 'OtcProduct')),
    ADD COLUMN IF NOT EXISTS is_commissionable BOOLEAN NOT NULL DEFAULT TRUE;

ALTER TABLE public.payments
    DROP CONSTRAINT IF EXISTS payments_method_check;

ALTER TABLE public.payments
    ADD CONSTRAINT payments_method_check
        CHECK (method IN ('Cash', 'Card', 'QrTransfer', 'CorporatePanel', 'PatientCredit'));

ALTER TABLE public.daily_sales_reconciliations
    ADD COLUMN IF NOT EXISTS total_credit_redeemed NUMERIC(12, 2) NOT NULL DEFAULT 0.00,
    ADD COLUMN IF NOT EXISTS total_credit_deposited NUMERIC(12, 2) NOT NULL DEFAULT 0.00;

-- -----------------------------------------------------------------------------
-- 2. CLINIC-GROUP SHARED PATIENT CREDIT WALLET & APPEND-ONLY CREDIT LEDGER
-- -----------------------------------------------------------------------------

ALTER TABLE public.patients
    ADD COLUMN IF NOT EXISTS credit_balance NUMERIC(12, 2) NOT NULL DEFAULT 0.00
        CHECK (credit_balance >= 0);

CREATE TABLE IF NOT EXISTS public.patient_credit_ledger (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    branch_id       TEXT NOT NULL REFERENCES public.branches(id),
    patient_id      UUID NOT NULL REFERENCES public.patients(id),
    invoice_id      UUID NULL REFERENCES public.invoices(id),
    invoice_number  TEXT NOT NULL DEFAULT '',
    amount_delta    NUMERIC(12, 2) NOT NULL CHECK (amount_delta <> 0),
    balance_after   NUMERIC(12, 2) NOT NULL CHECK (balance_after >= 0),
    entry_type      TEXT NOT NULL CHECK (entry_type IN ('CREDIT_DEPOSIT', 'CREDIT_REDEEMED', 'CREDIT_VOID_REVERSAL')),
    notes           TEXT NOT NULL DEFAULT '',
    recorded_by     TEXT NOT NULL DEFAULT '',
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_patient_credit_ledger_patient
    ON public.patient_credit_ledger (patient_id, created_at DESC);

DROP TRIGGER IF EXISTS trg_no_delete_patient_credit_ledger ON public.patient_credit_ledger;
CREATE TRIGGER trg_no_delete_patient_credit_ledger
    BEFORE DELETE ON public.patient_credit_ledger
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

ALTER TABLE public.patient_credit_ledger ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS patient_credit_ledger_group_select ON public.patient_credit_ledger;
CREATE POLICY patient_credit_ledger_group_select ON public.patient_credit_ledger
    FOR SELECT
    USING (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS patient_credit_ledger_group_insert ON public.patient_credit_ledger;
CREATE POLICY patient_credit_ledger_group_insert ON public.patient_credit_ledger
    FOR INSERT
    WITH CHECK (
        clinic_group_id = public.auth_clinic_group_id()
        AND branch_id = ANY(public.auth_allowed_branches())
    );

DROP POLICY IF EXISTS patient_credit_ledger_deny_delete ON public.patient_credit_ledger;
CREATE POLICY patient_credit_ledger_deny_delete ON public.patient_credit_ledger
    AS RESTRICTIVE FOR DELETE
    USING (FALSE);

-- -----------------------------------------------------------------------------
-- 3. UNIFIED MASTER CATALOGUE & PER-DOCTOR INVOICE SUGGESTION PREFERENCES
-- -----------------------------------------------------------------------------

CREATE TABLE IF NOT EXISTS public.billing_catalog_items (
    id                         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id            TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    name                       TEXT NOT NULL,
    category                   TEXT NOT NULL DEFAULT 'Procedure'
        CHECK (category IN ('Procedure', 'Radiograph', 'Medication', 'OtcProduct')),
    clinical_subcategory       TEXT NOT NULL DEFAULT 'General',
    unit_price                 NUMERIC(12, 2) NOT NULL DEFAULT 0.00 CHECK (unit_price >= 0),
    is_commissionable          BOOLEAN NOT NULL DEFAULT TRUE,
    is_bookable_appointment    BOOLEAN NOT NULL DEFAULT FALSE,
    default_duration_minutes   INTEGER NOT NULL DEFAULT 30 CHECK (default_duration_minutes BETWEEN 15 AND 480),
    show_in_odontogram_presets BOOLEAN NOT NULL DEFAULT FALSE,
    default_tooth_condition    TEXT NOT NULL DEFAULT 'Healthy',
    note_keywords              TEXT NOT NULL DEFAULT '',
    safety_alert_tags          TEXT NOT NULL DEFAULT '',
    is_active                  BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at                 TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT chk_otc_zero_commission CHECK (category <> 'OtcProduct' OR is_commissionable = FALSE)
);

-- Idempotent backfill migrating legacy public.treatments into the single Master Catalogue
INSERT INTO public.billing_catalog_items (
    id, clinic_group_id, name, category, clinical_subcategory, unit_price,
    is_commissionable, is_bookable_appointment, default_duration_minutes,
    show_in_odontogram_presets, default_tooth_condition, is_active
)
SELECT
    t.id,
    t.clinic_group_id,
    t.name,
    'Procedure',
    t.category,
    t.default_fee,
    TRUE,
    TRUE,
    t.default_duration_minutes,
    (t.name NOT ILIKE '%Consultation%'),
    CASE
        WHEN t.name ILIKE '%Filling%' OR t.name ILIKE '%Composite%' THEN 'Filling'
        WHEN t.name ILIKE '%Extraction%' THEN 'ExtractionNeeded'
        WHEN t.name ILIKE '%Root Canal%' THEN 'RootCanal'
        WHEN t.name ILIKE '%Crown%' AND t.name NOT ILIKE '%Implant%' THEN 'Crown'
        WHEN t.name ILIKE '%Implant%' THEN 'Implant'
        ELSE 'Healthy'
    END,
    t.is_active
FROM public.treatments t
ON CONFLICT (id) DO NOTHING;

CREATE TABLE IF NOT EXISTS public.doctor_invoice_preferences (
    id                         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    clinic_group_id            TEXT NOT NULL DEFAULT 'OCCLUSA-MY',
    dentist_id                 UUID NULL REFERENCES public.dentists(id),
    doctor_name                TEXT NOT NULL DEFAULT 'Clinic Default (All Doctors)',
    init_mode                  TEXT NOT NULL DEFAULT 'AutoPopulateWithSuggestions'
        CHECK (init_mode IN ('AutoPopulateWithSuggestions', 'BlankWithSuggestions', 'BlankManualOnly')),
    enable_note_keyword_engine BOOLEAN NOT NULL DEFAULT TRUE,
    treatment_rules_json       JSONB NOT NULL DEFAULT '[]'::JSONB,
    updated_at                 TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE UNIQUE INDEX IF NOT EXISTS uq_doctor_invoice_pref_default
    ON public.doctor_invoice_preferences (clinic_group_id)
    WHERE dentist_id IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS uq_doctor_invoice_pref_dentist
    ON public.doctor_invoice_preferences (clinic_group_id, dentist_id)
    WHERE dentist_id IS NOT NULL;

DROP TRIGGER IF EXISTS trg_no_delete_billing_catalog ON public.billing_catalog_items;
CREATE TRIGGER trg_no_delete_billing_catalog
    BEFORE DELETE ON public.billing_catalog_items
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

DROP TRIGGER IF EXISTS trg_no_delete_doctor_invoice_pref ON public.doctor_invoice_preferences;
CREATE TRIGGER trg_no_delete_doctor_invoice_pref
    BEFORE DELETE ON public.doctor_invoice_preferences
    FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete();

ALTER TABLE public.billing_catalog_items ENABLE ROW LEVEL SECURITY;
ALTER TABLE public.doctor_invoice_preferences ENABLE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS billing_catalog_group_all ON public.billing_catalog_items;
CREATE POLICY billing_catalog_group_all ON public.billing_catalog_items
    FOR ALL
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());

DROP POLICY IF EXISTS doctor_invoice_pref_group_all ON public.doctor_invoice_preferences;
CREATE POLICY doctor_invoice_pref_group_all ON public.doctor_invoice_preferences
    FOR ALL
    USING (clinic_group_id = public.auth_clinic_group_id())
    WITH CHECK (clinic_group_id = public.auth_clinic_group_id());
