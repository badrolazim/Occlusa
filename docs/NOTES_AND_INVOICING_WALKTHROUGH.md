# Clinical Notes & Invoicing — Step-by-Step User Walkthrough

> **Core Rule:** Clinical **Visit Records** (Odontogram + SOAP Notes) contain **zero RM amounts**. Prices, OTC retail products, discounts, corporate panels, and shared patient credits live **exclusively** inside **Invoices**. Receptionists can adjust draft invoice items freely without ever altering a doctor's medico-legal clinical report.

---

## 1. Configure the Unified `Master Catalogue` & Doctor Suggestions

1. Open **Master Catalogue** (6th tab on the left sidebar). This is the **single source of truth** powering Appointments, Odontogram Quick Presets, Invoices, WhatsApp AI quotes, and Doctor Suggestion Rules.
2. Under **`1. Master Catalogue (Appts, Chart & Prices)`**, double-click any item or click **`+ Add Catalogue Item`**:
   - Choose **`Procedure`**, **`Radiograph`**, **`Medication`**, or **`OtcProduct`** (`OtcProduct` strictly enforces **`0%` doctor commission**).
   - Toggle **`Bookable in Appointments`** (with slot duration) to control whether receptionists and WhatsApp AI can book it on the calendar.
   - Toggle **`Show as Odontogram Quick Preset Chip`** (with default tooth condition) to control which clinical presets appear in the doctor's Visit Chart (zero prices shown on the chart).
   - Add comma-separated **SOAP Note Keywords** (e.g., `extraction, exo, cabut` or `pcm, paracetamol, panadol`) so Occlusa's suggestion engine recognizes shorthand in doctors' clinical notes.
   - Add optional **Medical Alert Safety Tags** (e.g., `Penicillin`, `NSAID`, `Pregnancy`).
3. Switch to **`2. Doctor Invoice Modes & Suggestions`**:
   - Pick **`Clinic Default (All Doctors)`** or a specific **Dentist**.
   - Choose the **Checkout Mode** (`Auto-Populate + Suggestions`, `Blank Invoice + Suggestions`, or `Blank Invoice (Manual Only)`) and click **`Save Mode`**.
   - Double-click any **Primary Treatment** row (e.g., *Simple Tooth Extraction*, *Surgical Extraction (MOS)*) to toggle which companion items (painkillers, antibiotics, X-rays, mouthwash) are suggested for that treatment. Doctors without a custom override inherit **Clinic Default** automatically.

---

## 2. Record Pure Clinical Visit Notes (`Visit Chart`)

1. On **Live Queue**, select an **`In Chair`** patient and click **`Visit Chart`** (or double-click the row).
2. Chart teeth & surfaces (`M`, `O`, `D`, `B`, `L`) and mark finished procedures **`3. Mark Completed`**, then switch to **`2. Clinical Visit Notes (SOAP)`** to enter clinical notes. No prices appear anywhere in the clinical workspace.
3. Click **`Save Visit Record`** (to save clinical notes only) **OR** click **`Save & Create Invoice →`** to save notes and open the Draft Invoice immediately.

---

## 3. Initiate Draft Invoice at Checkout (Dentist)

1. Click **`Checkout / Invoice`** on **Live Queue** (or **`Save & Create Invoice →`** inside the Visit Record).
2. Review primary items (auto-populated or blank according to that doctor's Checkout Mode) and click any **Smart Suggestion Chips** above the grid (e.g., `+ Paracetamol 500mg · RM 10.00`). Items conflicting with the patient's **Medical Alerts** display `[⚠]` and ask for confirmation.
3. Click **`Clear Blank`** anytime for a manual blank invoice, or **`Scan Dr Notes`** to scan SOAP notes on demand.
4. Click **`Send Draft to Counter →`** to move the patient to **`Checkout`** (`AwaitingCheckout`) for reception, or **`No-Charge Visit`** for RM 0 follow-ups.

---

## 4. Edit, Redeem/Store Shared Credit & Settle (Receptionist)

1. Double-click the **`Checkout`** row on **Live Queue** (or open the `Draft` invoice in **Billing & Invoices**).
2. Select any line item to **`Update`** its unit price/quantity, **`Remove`** it, or **`+ Add`** counter items—the doctor's Visit Report remains completely untouched.
3. If the patient has a balance in **Patient Credit Wallet (All Branches)**, click **`Max Credit`** (or enter an amount in **`Use Credit (RM)`**).
4. Enter **Cash**, **Card**, or **QR / DuitNow**:
   - When tendered payment exceeds the balance due, choose **`Cash Change`** (returns cash change) or **`Save to Credit`** (stores the excess in the patient's clinic-group Credit Wallet for future visits at any branch).
5. Click **`Settle Payment`** — once `Balance Due` is `RM 0.00`, the Live Queue row automatically transitions to **`Completed`**.

---

## 5. Walk-In Counter OTC Sale (Toothbrush / Toothpaste)

1. Go to **Billing & Invoices** and click **`+ Walk-In OTC Sale`**.
2. Keep **`Walk-In Retail (OTC)`** selected (no NRIC, medical chart, or doctor required; `0%` doctor commission enforced).
3. Select retail items from **Catalog** (e.g., *Ultra-Soft Bristle Toothbrush*, *Fluoride Desensitizing Toothpaste*), click **`+ Add`**, then click **`Full Cash`** / **`Full QR`** and **`Settle Payment`**.

