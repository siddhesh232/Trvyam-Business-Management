> **Source of truth:** the live Swagger/OpenAPI document (`/swagger` when `Swagger__Enabled=true` or in Development). The list below is the
> original approval draft; the implemented routes are business-scoped (e.g. `/api/v1/businesses/{businessId}/sales/{saleId}/payments`).
> Notable additions: `/my-permissions`, `/customers/resolve`, `/expenses/summary`, `/expenses/by-member`, `/sales/{id}/returns`,
> `/invoices/{id}/pdf`, `/dashboard`, `/dashboard/consolidated`, `/reports`, `/adjustments`, `/audit-logs`.

# 3. API Endpoint List (v1, approval draft)

Base: `/api/v1`. All except `auth/login`, `auth/refresh`, `auth/admin/forgot-password`, `auth/admin/reset-password` require JWT. `{bid}` = business id; access + permission always checked server-side.

## Auth & profile
| Method | Route | Who |
|---|---|---|
| POST | /auth/login | anyone |
| POST | /auth/refresh · /auth/logout | authenticated |
| POST | /auth/admin/forgot-password | Admin (emails temp password, 6h, single-use) |
| POST | /auth/admin/reset-password | Admin (temp + new password) |
| POST | /auth/change-password | authenticated |
| GET/PUT | /me · POST /me/avatar | authenticated |

## Admin: users
| GET/POST | /users | Admin |
|---|---|---|
| GET/PUT/DELETE | /users/{id} | Admin |
| POST | /users/{id}/set-password | Admin (only way a member password is reset) |

## Businesses & permissions
| Method | Route | Permission |
|---|---|---|
| GET | /businesses | Admin all; member assigned only |
| POST | /businesses | Admin (≥1 member) |
| GET | /businesses/{bid} | CanViewBusiness |
| PUT/DELETE | /businesses/{bid} | Admin |
| PUT | /businesses/{bid}/members | Admin (rejects empty set) |
| GET/PUT | /businesses/{bid}/members/{uid}/permissions | Admin |
| GET | /users/{id}/businesses | Admin (pre-select marks) |

## Customers (global)
| GET | /customers/lookup?mobile= | CanViewCustomers/CanAddCustomers (needs `bid` header/query) |
|---|---|---|
| GET | /businesses/{bid}/customers | CanViewCustomers |
| POST | /businesses/{bid}/customers | CanAddCustomers (find-or-create + tag) |
| PUT | /customers/{id} | Admin or CanEditCustomers in a tagged business |
| GET | /customers (all) | Admin |

## Products
| GET/POST/PUT | /products | Admin |
|---|---|---|
| PUT | /products/{id}/businesses | Admin |
| GET | /businesses/{bid}/products | CanViewProducts (active only) |

## Expenses
| GET | /businesses/{bid}/expenses?from&to&purchasedBy&q | CanViewExpenseTotal / own |
|---|---|---|
| POST | /businesses/{bid}/expenses | CanAddExpenses |
| PUT/DELETE | /expenses/{id} | Admin, or creator + CanEdit/DeleteOwnExpenses |
| GET | /businesses/{bid}/expenses/summary?from&to | CanViewExpenseTotal (total only) |
| GET | /businesses/{bid}/expenses/by-member?from&to&basis=purchasedBy\|createdBy | **CanViewMemberWiseExpenses** (403 otherwise; no data leakage) |

## Sales, payments, invoices, returns
| GET/POST | /businesses/{bid}/sales | CanViewSales / CanAddSales |
|---|---|---|
| GET/PUT/DELETE | /sales/{id} | view / own edit / own delete (Admin all) |
| GET/POST | /sales/{id}/payments | CanViewPayments / CanRecordPayments |
| PUT | /payments/{id} | Admin or creator + CanEditOwnPayments |
| POST | /payments/{id}/reverse | Admin, or creator + CanDeleteOwnPayments (reason required) |
| POST | /sales/{id}/invoice | CanGenerateInvoices |
| GET | /invoices/{id} · /invoices/{id}/pdf | CanViewInvoices |
| POST | /invoices/{id}/cancel · /invoices/{id}/credit-note | Admin |
| POST | /sales/{id}/returns | Admin (proposed) |

## Dashboard, reports, adjustments, audit
| GET | /businesses/{bid}/dashboard?from&to | CanViewBusiness (+ per-metric permissions) |
|---|---|---|
| GET | /dashboard/consolidated | Admin |
| GET | /businesses/{bid}/reports/{daily\|weekly\|monthly\|custom} | CanViewReports |
| GET/POST/DELETE | /businesses/{bid}/adjustments | Admin |
| GET | /audit-logs?entity&businessId&from&to | Admin |
