# 1. Functional Requirements Breakdown (FRD v2.1)

| # | Module | FRD § | Key requirements | Phase |
|---|--------|-------|------------------|-------|
| M1 | Auth & users | 2, 15 | One Admin; Members created by Admin; Employee modelled, view-only. JWT + refresh rotation. Admin reset: temp password by email, valid 6h, single-use. Members have **no** self-service reset; Admin sets it. | 1 |
| M2 | Businesses | 3, 4 | CRUD; ≥1 member mandatory; members pre-selected from member's marked businesses; separate financial summary per business. | 2 |
| M3 | Permissions | 5 | Per member **per business**, individual capabilities; separate `member-wise expense` permission; enforced in API. | 3 |
| M4 | Customers | 6 | Global, unique by mobile, tagged to many businesses; lookup-by-mobile during sale; auto-tag current business; shared edits per access rules. | 4 |
| M5 | Products | 7 | Admin-managed catalogue; mapped to businesses; active/inactive; sales snapshot name/price/GST. | 5 |
| M6 | Expenses | 8, 4 | Purchased By vs Created By; date/member/item filters; own-record edit/delete; total + member-wise breakdown (permission-gated, labelled basis, reconciles). | 6 |
| M7 | Sales | 9 | Multi-item orders from catalogue; item/order discounts, GST; creator tracked; filter by date/customer/product/payment status. | 7 |
| M8 | Payments | 10 | Many payments per order; computed status & outstanding; overpayment blocked; reversals auditable. | 8 |
| M9 | GST invoices | 11 | Unique sequential numbers; CGST/SGST vs IGST; PDF; immutable once issued; amend/cancel/credit note; payment never creates invoice. | 9 |
| M10 | Dashboard / profit | 12 | Gross/net sales, returns, expenses, output/input GST, net GST, adjustments, net profit; per-business + Admin consolidated. | 10 |
| M11 | Reports / audit | 12, 15 | Daily/weekly/monthly/custom; audit logs; employee view-only. | 11 |

**Non-functional:** salted password hashing; backend authorization + data isolation; decimal money; soft delete/reversal for financial records; search across entities; backups & tested recovery.

## Ambiguities needing your decision
1. **Sales returns** have no table in FRD §14 - proposed `SalesReturns/Items` + credit note.
2. **Invoice series:** per business + financial year (proposed) vs global.
3. **CGST/SGST vs IGST:** from GSTIN state codes; `StateCode` on customer for unregistered buyers.
4. **Email provider** for the temp password (SMTP / SendGrid / SES).
5. **Advance/overpayment workflow:** build now or defer (proposed: defer; hard-block overpayment).
6. **Member-wise expense basis:** FRD requires a labelled basis (Purchased By vs Created By). Proposed: default *Purchased By*, selectable.
7. **Shared customer edits:** FRD says "Admin-configured access rules" but gives none. Proposed: Admin always; members need `CanEditCustomers` in a business the customer is tagged to; edits audited.
8. **Hidden-breakdown reconciliation:** for members without the permission, the API returns only the total (if `CanViewExpenseTotal`) and no per-member data.
9. **Net profit formula** (proposed, to validate with accountant): `Net Sales (ex-GST, after discounts & returns) - Expenses (ex eligible input GST) ± Other Adjustments`.
10. **Version:** file is named v2_0 but content is v2.1 - treating v2.1 as baseline.
