# Entity Relationship Diagram (approval draft)

Renders natively on GitHub. Source: `Database/ERD/erd.mmd`. Full DDL: `Database/Schema/001_schema.sql`.

```mermaid
erDiagram
    Users ||--o{ BusinessMembers : "assigned to"
    Businesses ||--o{ BusinessMembers : "has"
    BusinessMembers ||--|| BusinessPermissions : "configured by Admin"
    Users ||--o{ RefreshTokens : owns
    Users ||--o{ PasswordResetTokens : requests
    Customers ||--o{ CustomerBusinesses : "tagged"
    Businesses ||--o{ CustomerBusinesses : "has"
    Products ||--o{ BusinessProducts : "available in"
    Businesses ||--o{ BusinessProducts : "offers"
    Businesses ||--o{ Expenses : records
    Users ||--o{ Expenses : "purchased by / created by"
    Businesses ||--o{ SalesOrders : records
    Customers ||--o{ SalesOrders : places
    SalesOrders ||--|{ SalesOrderItems : contains
    Products ||--o{ SalesOrderItems : "snapshotted from"
    SalesOrders ||--o{ Payments : "receives many"
    SalesOrders ||--o{ GstInvoices : "invoiced by"
    GstInvoices ||--o{ GstInvoices : "credit note / amendment"
    Businesses ||--o{ InvoiceSequences : numbers
    SalesOrders ||--o{ SalesReturns : "returned via"
    SalesReturns ||--|{ SalesReturnItems : contains
    SalesOrderItems ||--o{ SalesReturnItems : "returned item"
    Businesses ||--o{ FinancialAdjustments : has
    Users ||--o{ AuditLogs : "performed"
```

## Design notes
- **Users** holds Admin, Member and Employee (role column). A filtered unique index guarantees exactly one Admin. *Deviation from FRD s.14, which lists a separate Admin table - needs approval.*
- **Access vs capability:** `BusinessMembers` grants access; `BusinessPermissions` (1:1) holds explicit boolean capabilities incl. `CanViewMemberWiseExpenses`.
- **Snapshots:** `SalesOrderItems` stores name/price/GST/HSN; `GstInvoices.SnapshotJson` freezes business + customer + lines.
- **Payments** are separate rows, never hard-deleted (`Status` Valid/Reversed). Payment status and outstanding come from `vw_SalesOrderPaymentSummary`, not stored columns.
- **Invoice numbering:** `InvoiceSequences` (per business + financial year + doc type), incremented inside the issuing transaction with a row lock.
- **Soft delete:** `IsDeleted` + audit columns on financial/master records; `AuditLogs` records old/new values.
- **Proposed additions (not in FRD s.14):** `SalesReturns`, `SalesReturnItems`, `InvoiceSequences`, `RefreshTokens`, `PasswordResetTokens`, plus columns `Customers.StateCode/Gstin`, `Products.HsnCode`, `Expenses.InputGstAmount/IsInputGstEligible`, `Businesses.AddressLine/StateCode`.
