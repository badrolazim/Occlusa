-- =============================================================================
-- Occlusa (Malaysian Dental Practice Management Software)
-- Migration: 003_dentist_treatment_capabilities.sql
-- Purpose: Map each dentist to the standard treatments they are qualified to
--          perform so front-desk scheduling filters eligible clinicians first.
-- =============================================================================

ALTER TABLE public.dentists
    ADD COLUMN IF NOT EXISTS supported_treatment_ids UUID[] NOT NULL DEFAULT ARRAY[]::UUID[];

UPDATE public.dentists
SET supported_treatment_ids = ARRAY[
    '60000000-0000-0000-0000-000000000001'::UUID,
    '60000000-0000-0000-0000-000000000002'::UUID,
    '60000000-0000-0000-0000-000000000003'::UUID,
    '60000000-0000-0000-0000-000000000004'::UUID
]
WHERE id = '50000000-0000-0000-0000-000000000001'::UUID;

UPDATE public.dentists
SET supported_treatment_ids = ARRAY[
    '60000000-0000-0000-0000-000000000001'::UUID,
    '60000000-0000-0000-0000-000000000003'::UUID,
    '60000000-0000-0000-0000-000000000004'::UUID,
    '60000000-0000-0000-0000-000000000005'::UUID,
    '60000000-0000-0000-0000-000000000006'::UUID,
    '60000000-0000-0000-0000-000000000007'::UUID
]
WHERE id = '50000000-0000-0000-0000-000000000002'::UUID;

UPDATE public.dentists
SET supported_treatment_ids = ARRAY[
    '60000000-0000-0000-0000-000000000001'::UUID,
    '60000000-0000-0000-0000-000000000002'::UUID,
    '60000000-0000-0000-0000-000000000003'::UUID
]
WHERE id = '50000000-0000-0000-0000-000000000003'::UUID;

