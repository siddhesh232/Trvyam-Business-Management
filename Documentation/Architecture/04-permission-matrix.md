# 5. Role & Permission Matrix

Legend: ✔ always · ⚙ requires the per-business permission flag · own = only records the user created · ✖ never.

| Capability | Admin | Member | Employee (future) |
|---|---|---|---|
| Manage users, businesses, products, permissions | ✔ | ✖ | ✖ |
| Set a member's password | ✔ | ✖ | ✖ |
| View assigned business details | ✔ (all) | ⚙ CanViewBusiness | view-only |
| View / add / edit customers | ✔ | ⚙ CanViewCustomers / CanAddCustomers / CanEditCustomers | view-only |
| View products | ✔ | ⚙ CanViewProducts | view-only |
| View sales | ✔ | ⚙ CanViewSales | view-only |
| Add sales | ✔ | ⚙ CanAddSales | ✖ |
| Edit / delete sales | ✔ all | ⚙ CanEditOwnSales / CanDeleteOwnSales + own | ✖ |
| View business expense total | ✔ | ⚙ CanViewExpenseTotal | view-only |
| Add expenses | ✔ | ⚙ CanAddExpenses | ✖ |
| Edit / delete expenses | ✔ all | ⚙ CanEditOwnExpenses / CanDeleteOwnExpenses + own | ✖ |
| **Member-wise expense breakdown** | ✔ | ⚙ CanViewMemberWiseExpenses (independent of total; grants no edit rights) | ✖ |
| Record payments / view history | ✔ | ⚙ CanRecordPayments / CanViewPayments | ✖ / view |
| Edit / reverse payments | ✔ all | ⚙ CanEditOwnPayments / CanDeleteOwnPayments + own | ✖ |
| Generate / view GST invoices | ✔ | ⚙ CanGenerateInvoices / CanViewInvoices | ✖ / view |
| Cancel invoice, credit note, returns | ✔ | ✖ | ✖ |
| Business reports | ✔ | ⚙ CanViewReports | view |
| Consolidated dashboard, audit logs, adjustments | ✔ | ✖ | ✖ |

**Rules:** access to a business requires an active `BusinessMembers` row; a permission flag without membership is ignored. Unassigned businesses return 404. All checks occur in the API.
