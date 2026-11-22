# Open Decisions & Required Inputs

Defaults below are used unless you say otherwise.

| # | Item | Default |
|---|---|---|
| 1 | Official logo | ✅ Received (raster PNG; SVG master recommended) |
| 2 | App display name | ✅ "Trvyam Management". Bundle ID still needed (proposed `com.trvyam.management`) |
| 3 | Sales returns tables | Add |
| 4 | Invoice series | Per business + FY (`INV/2026-27/0001`) |
| 5 | State-code based CGST/SGST vs IGST | Yes, plus customer `StateCode` |
| 6 | Email provider | SMTP via config |
| 7 | React Native flavour | RN CLI |
| 8 | Advance/overpayment workflow | Deferred; overpayment blocked |
| 9 | Admin separate table vs `Users.Role` | `Users.Role` + unique-admin index |
| 10 | PDF library | QuestPDF (check licence for your revenue) or alternative |
| 11 | Hosting target (Azure/AWS/on-prem) | Docker-based, undecided |
| 12 | Repo visibility | Public (your choice) - see security note in README |

## Decisions taken in Phase 7 (please confirm or change)
| # | Decision | Why |
|---|---|---|
| 13 | Product **purchase (cost) price is Admin-only**; members see selling price, GST, unit only | Cost price reveals margins; FRD only gives members "view available products" |
| 14 | Customer lookup shows a member only business tags for **businesses they are assigned to** | Prevents leaking other businesses' names/customers; Admin sees all tags |
| 15 | Members cannot change their own password or profile; Admin manages both | FRD s.2.2 (Admin-managed members) |
| 16 | Business participants are removed by **deactivation**, not deletion | Preserves history; re-adding restores access |
| 17 | "Members previously marked for a business are auto-selected on business creation" is **not implemented as stored data**: the approved schema has no pre-mark storage before a business exists. The Admin selects members at creation (app can pre-select by Admin's last choice). Tell me if you want a "default for new businesses" flag on members (needs a small migration). | Ambiguity in FRD s.3 |
| 18 | GST rate validated only as 0-100 with 2 decimals (slabs change by law) | Avoid hard-coding tax slabs |
