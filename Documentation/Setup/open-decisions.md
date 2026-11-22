# Open Decisions & Required Inputs

Defaults below are used unless you say otherwise.

| # | Item | Default |
|---|---|---|
| 1 | Official logo (PNG/SVG, transparent) | **Required** - splash/app icon blocked until supplied |
| 2 | App display name & bundle ID (e.g. `com.trvyam.businessmanagement`) | Needed before native config |
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
