/* =====================================================================
   Trvyam Business Management - SQL Server schema (APPROVAL DRAFT v0.1)
   Source of truth: FRD v2.1. EF Core Fluent API + migrations will be
   generated to match this once approved. Money = DECIMAL(18,2),
   rates = DECIMAL(5,2), quantities = DECIMAL(18,3). No floats.
   Items marked [PROPOSED] are not named in FRD s.14 and need approval.
   ===================================================================== */

/* ---------- Identity & access ---------- */
CREATE TABLE Users (
    Id                  BIGINT IDENTITY PRIMARY KEY,
    Role                TINYINT        NOT NULL,              -- 1=Admin 2=Member 3=Employee(future, view-only)
    Name                NVARCHAR(120)  NOT NULL,
    Username            NVARCHAR(60)   NOT NULL,
    PasswordHash        NVARCHAR(256)  NOT NULL,              -- salted hash (Argon2id/PBKDF2), never plain text
    Mobile              VARCHAR(15)    NOT NULL,
    Email               NVARCHAR(254)  NULL,
    DateOfBirth         DATE           NULL,                  -- members
    AvatarUrl           NVARCHAR(500)  NULL,
    IsActive            BIT            NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2(0)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy           BIGINT         NULL,
    UpdatedAt           DATETIME2(0)   NULL,
    UpdatedBy           BIGINT         NULL,
    IsDeleted           BIT            NOT NULL DEFAULT 0,
    RowVer              ROWVERSION,
    CONSTRAINT CK_Users_Role CHECK (Role IN (1,2,3)),
    CONSTRAINT CK_Users_AdminEmail CHECK (Role <> 1 OR Email IS NOT NULL)  -- Admin reset needs email
);
CREATE UNIQUE INDEX UX_Users_Username ON Users(Username) WHERE IsDeleted = 0;
-- Enforces "exactly one Admin" (FRD s.13). Single Users table instead of separate Admin table [DEVIATION - approve].
CREATE UNIQUE INDEX UX_Users_SingleAdmin ON Users(Role) WHERE Role = 1;

CREATE TABLE RefreshTokens (
    Id                  BIGINT IDENTITY PRIMARY KEY,
    UserId              BIGINT        NOT NULL REFERENCES Users(Id),
    TokenHash           CHAR(64)      NOT NULL,               -- SHA-256 of token; raw token never stored
    ExpiresAt           DATETIME2(0)  NOT NULL,
    CreatedAt           DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    RevokedAt           DATETIME2(0)  NULL,
    ReplacedByTokenId   BIGINT        NULL REFERENCES RefreshTokens(Id),  -- rotation chain
    DeviceInfo          NVARCHAR(200) NULL
);
CREATE UNIQUE INDEX UX_RefreshTokens_Hash ON RefreshTokens(TokenHash);
CREATE INDEX IX_RefreshTokens_User ON RefreshTokens(UserId) INCLUDE (ExpiresAt, RevokedAt);

CREATE TABLE PasswordResetTokens (                            -- Admin temp password: 6h, single-use
    Id                  BIGINT IDENTITY PRIMARY KEY,
    UserId              BIGINT        NOT NULL REFERENCES Users(Id),
    TokenHash           NVARCHAR(256) NOT NULL,
    ExpiresAt           DATETIME2(0)  NOT NULL,
    UsedAt              DATETIME2(0)  NULL,
    CreatedAt           DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_PRT_Expiry CHECK (ExpiresAt > CreatedAt)
);
CREATE INDEX IX_PRT_User ON PasswordResetTokens(UserId, ExpiresAt);

/* ---------- Businesses ---------- */
CREATE TABLE Businesses (
    Id                  BIGINT IDENTITY PRIMARY KEY,
    Name                NVARCHAR(150)  NOT NULL,
    Description         NVARCHAR(1000) NULL,
    GstNumber           CHAR(15)       NULL,
    Category            NVARCHAR(80)   NULL,
    LogoUrl             NVARCHAR(500)  NULL,
    AddressLine         NVARCHAR(400)  NULL,                  -- required on invoices
    StateCode           CHAR(2)        NULL,                  -- GST state code (place of supply logic)
    IsActive            BIT            NOT NULL DEFAULT 1,
    CreatedAt           DATETIME2(0)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy           BIGINT         NOT NULL REFERENCES Users(Id),
    UpdatedAt           DATETIME2(0)   NULL,
    UpdatedBy           BIGINT         NULL REFERENCES Users(Id),
    IsDeleted           BIT            NOT NULL DEFAULT 0,
    RowVer              ROWVERSION,
    CONSTRAINT CK_Biz_Gstin CHECK (GstNumber IS NULL OR GstNumber LIKE '[0-9][0-9][A-Z][A-Z][A-Z][A-Z][A-Z][0-9][0-9][0-9][0-9][A-Z][0-9A-Z]Z[0-9A-Z]')
);
CREATE UNIQUE INDEX UX_Biz_Name ON Businesses(Name) WHERE IsDeleted = 0;
CREATE UNIQUE INDEX UX_Biz_Gstin ON Businesses(GstNumber) WHERE GstNumber IS NOT NULL AND IsDeleted = 0;

CREATE TABLE BusinessMembers (                                -- grants ACCESS (FRD s.3)
    BusinessId          BIGINT NOT NULL REFERENCES Businesses(Id),
    UserId              BIGINT NOT NULL REFERENCES Users(Id),
    AssignedAt          DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    AssignedBy          BIGINT NOT NULL REFERENCES Users(Id),
    IsActive            BIT NOT NULL DEFAULT 1,
    PRIMARY KEY (BusinessId, UserId)
);
CREATE INDEX IX_BM_User ON BusinessMembers(UserId) WHERE IsActive = 1;
-- "At least one member per business" is enforced in the application service inside a transaction (not expressible as a pure constraint).

CREATE TABLE BusinessPermissions (                            -- controls CAPABILITIES (FRD s.5), per member per business
    BusinessId                  BIGINT NOT NULL,
    UserId                      BIGINT NOT NULL,
    CanViewBusiness             BIT NOT NULL DEFAULT 1,
    CanViewCustomers            BIT NOT NULL DEFAULT 0,
    CanAddCustomers             BIT NOT NULL DEFAULT 0,
    CanEditCustomers            BIT NOT NULL DEFAULT 0,
    CanViewProducts             BIT NOT NULL DEFAULT 0,
    CanViewSales                BIT NOT NULL DEFAULT 0,
    CanAddSales                 BIT NOT NULL DEFAULT 0,
    CanEditOwnSales             BIT NOT NULL DEFAULT 0,
    CanDeleteOwnSales           BIT NOT NULL DEFAULT 0,
    CanViewExpenseTotal         BIT NOT NULL DEFAULT 0,
    CanAddExpenses              BIT NOT NULL DEFAULT 0,
    CanEditOwnExpenses          BIT NOT NULL DEFAULT 0,
    CanDeleteOwnExpenses        BIT NOT NULL DEFAULT 0,
    CanViewMemberWiseExpenses   BIT NOT NULL DEFAULT 0,       -- separate permission (FRD s.4/5/8)
    CanRecordPayments           BIT NOT NULL DEFAULT 0,
    CanViewPayments             BIT NOT NULL DEFAULT 0,
    CanEditOwnPayments          BIT NOT NULL DEFAULT 0,
    CanDeleteOwnPayments        BIT NOT NULL DEFAULT 0,
    CanGenerateInvoices         BIT NOT NULL DEFAULT 0,
    CanViewInvoices             BIT NOT NULL DEFAULT 0,
    CanViewReports              BIT NOT NULL DEFAULT 0,
    UpdatedAt                   DATETIME2(0) NULL,
    UpdatedBy                   BIGINT NULL REFERENCES Users(Id),
    PRIMARY KEY (BusinessId, UserId),
    FOREIGN KEY (BusinessId, UserId) REFERENCES BusinessMembers(BusinessId, UserId)
);

/* ---------- Global customers ---------- */
CREATE TABLE Customers (
    Id              BIGINT IDENTITY PRIMARY KEY,
    Mobile          VARCHAR(15)    NOT NULL,                  -- normalised (digits, +91 stripped) - lookup key
    Name            NVARCHAR(150)  NOT NULL,
    Address         NVARCHAR(400)  NULL,
    StateCode       CHAR(2)        NULL,                      -- [PROPOSED] for GST when no GSTIN
    Gstin           CHAR(15)       NULL,                      -- [PROPOSED] B2B invoices (FRD s.11)
    CreatedAt       DATETIME2(0)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       BIGINT         NOT NULL REFERENCES Users(Id),
    UpdatedAt       DATETIME2(0)   NULL,
    UpdatedBy       BIGINT         NULL REFERENCES Users(Id),
    IsDeleted       BIT            NOT NULL DEFAULT 0,
    RowVer          ROWVERSION,
    CONSTRAINT CK_Cust_Mobile CHECK (Mobile NOT LIKE '%[^0-9]%' AND LEN(Mobile) BETWEEN 10 AND 15)
);
CREATE UNIQUE INDEX UX_Cust_Mobile ON Customers(Mobile) WHERE IsDeleted = 0;   -- no duplicates across businesses
CREATE INDEX IX_Cust_Name ON Customers(Name);

CREATE TABLE CustomerBusinesses (
    CustomerId      BIGINT NOT NULL REFERENCES Customers(Id),
    BusinessId      BIGINT NOT NULL REFERENCES Businesses(Id),
    LinkedAt        DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    LinkedBy        BIGINT NOT NULL REFERENCES Users(Id),
    PRIMARY KEY (CustomerId, BusinessId)
);
CREATE INDEX IX_CB_Business ON CustomerBusinesses(BusinessId);

/* ---------- Product catalogue ---------- */
CREATE TABLE Products (
    Id              BIGINT IDENTITY PRIMARY KEY,
    Name            NVARCHAR(150)  NOT NULL,
    Description     NVARCHAR(1000) NULL,
    Sku             NVARCHAR(50)   NOT NULL,
    Category        NVARCHAR(80)   NULL,
    SellingPrice    DECIMAL(18,2)  NOT NULL,
    PurchasePrice   DECIMAL(18,2)  NOT NULL DEFAULT 0,
    GstRate         DECIMAL(5,2)   NOT NULL,
    HsnCode         VARCHAR(8)     NULL,                      -- [PROPOSED] needed on GST invoices
    Unit            NVARCHAR(20)   NOT NULL,
    IsActive        BIT            NOT NULL DEFAULT 1,
    CreatedAt       DATETIME2(0)   NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy       BIGINT         NOT NULL REFERENCES Users(Id),
    UpdatedAt       DATETIME2(0)   NULL,
    UpdatedBy       BIGINT         NULL REFERENCES Users(Id),
    IsDeleted       BIT            NOT NULL DEFAULT 0,
    RowVer          ROWVERSION,
    CONSTRAINT CK_Prod_Prices CHECK (SellingPrice >= 0 AND PurchasePrice >= 0),
    CONSTRAINT CK_Prod_Gst CHECK (GstRate BETWEEN 0 AND 100)
);
CREATE UNIQUE INDEX UX_Prod_Sku ON Products(Sku) WHERE IsDeleted = 0;

CREATE TABLE BusinessProducts (
    BusinessId      BIGINT NOT NULL REFERENCES Businesses(Id),
    ProductId       BIGINT NOT NULL REFERENCES Products(Id),
    PRIMARY KEY (BusinessId, ProductId)
);
CREATE INDEX IX_BP_Product ON BusinessProducts(ProductId);

/* ---------- Expenses ---------- */
CREATE TABLE Expenses (
    Id                   BIGINT IDENTITY PRIMARY KEY,
    BusinessId           BIGINT        NOT NULL REFERENCES Businesses(Id),
    ExpenseDate          DATE          NOT NULL,
    Name                 NVARCHAR(200) NOT NULL,
    Quantity             DECIMAL(18,3) NOT NULL DEFAULT 1,
    UnitPrice            DECIMAL(18,2) NOT NULL,
    TotalAmount          AS CAST(ROUND(Quantity * UnitPrice, 2) AS DECIMAL(18,2)) PERSISTED,
    InputGstAmount       DECIMAL(18,2) NOT NULL DEFAULT 0,    -- [PROPOSED] FRD s.12 "eligible input GST"
    IsInputGstEligible   BIT           NOT NULL DEFAULT 0,
    PurchasedByUserId    BIGINT        NOT NULL REFERENCES Users(Id),   -- who bought
    CreatedBy            BIGINT        NOT NULL REFERENCES Users(Id),   -- who entered
    CreatedAt            DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt            DATETIME2(0)  NULL,
    UpdatedBy            BIGINT        NULL REFERENCES Users(Id),
    IsDeleted            BIT           NOT NULL DEFAULT 0,
    DeletedAt            DATETIME2(0)  NULL,
    DeletedBy            BIGINT        NULL REFERENCES Users(Id),
    RowVer               ROWVERSION,
    CONSTRAINT CK_Exp_Amounts CHECK (Quantity > 0 AND UnitPrice >= 0 AND InputGstAmount >= 0)
);
CREATE INDEX IX_Exp_BizDate ON Expenses(BusinessId, ExpenseDate) INCLUDE (PurchasedByUserId, CreatedBy, TotalAmount) WHERE IsDeleted = 0;
CREATE INDEX IX_Exp_Purchaser ON Expenses(BusinessId, PurchasedByUserId, ExpenseDate) WHERE IsDeleted = 0;

/* ---------- Sales ---------- */
CREATE TABLE SalesOrders (
    Id                   BIGINT IDENTITY PRIMARY KEY,
    BusinessId           BIGINT        NOT NULL REFERENCES Businesses(Id),
    CustomerId           BIGINT        NOT NULL REFERENCES Customers(Id),
    OrderDate            DATE          NOT NULL,
    Subtotal             DECIMAL(18,2) NOT NULL,
    DiscountAmount       DECIMAL(18,2) NOT NULL DEFAULT 0,
    TaxableAmount        DECIMAL(18,2) NOT NULL,
    GstAmount            DECIMAL(18,2) NOT NULL,
    FinalAmount          DECIMAL(18,2) NOT NULL,
    PlaceOfSupplyState   CHAR(2)       NULL,                  -- drives CGST/SGST vs IGST
    Status               TINYINT       NOT NULL DEFAULT 1,    -- 1=Active 2=Cancelled
    CreatedBy            BIGINT        NOT NULL REFERENCES Users(Id),
    CreatedAt            DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt            DATETIME2(0)  NULL,
    UpdatedBy            BIGINT        NULL REFERENCES Users(Id),
    IsDeleted            BIT           NOT NULL DEFAULT 0,
    DeletedAt            DATETIME2(0)  NULL,
    DeletedBy            BIGINT        NULL REFERENCES Users(Id),
    RowVer               ROWVERSION,
    CONSTRAINT CK_SO_Amounts CHECK (Subtotal >= 0 AND DiscountAmount >= 0 AND DiscountAmount <= Subtotal
                                    AND GstAmount >= 0 AND FinalAmount = TaxableAmount + GstAmount),
    CONSTRAINT CK_SO_Status CHECK (Status IN (1,2))
);
CREATE INDEX IX_SO_BizDate ON SalesOrders(BusinessId, OrderDate) WHERE IsDeleted = 0;
CREATE INDEX IX_SO_Customer ON SalesOrders(CustomerId);
CREATE INDEX IX_SO_Creator ON SalesOrders(BusinessId, CreatedBy);

CREATE TABLE SalesOrderItems (
    Id                   BIGINT IDENTITY PRIMARY KEY,
    SalesOrderId         BIGINT        NOT NULL REFERENCES SalesOrders(Id),
    ProductId            BIGINT        NOT NULL REFERENCES Products(Id),
    ProductNameSnapshot  NVARCHAR(150) NOT NULL,              -- snapshots protect history (FRD s.7)
    HsnCodeSnapshot      VARCHAR(8)    NULL,
    Quantity             DECIMAL(18,3) NOT NULL,
    Unit                 NVARCHAR(20)  NOT NULL,
    UnitPrice            DECIMAL(18,2) NOT NULL,
    DiscountAmount       DECIMAL(18,2) NOT NULL DEFAULT 0,
    GstRate              DECIMAL(5,2)  NOT NULL,
    TaxableAmount        DECIMAL(18,2) NOT NULL,
    GstAmount            DECIMAL(18,2) NOT NULL,
    TotalAmount          DECIMAL(18,2) NOT NULL,
    CONSTRAINT CK_SOI_Amounts CHECK (Quantity > 0 AND UnitPrice >= 0 AND DiscountAmount >= 0
                                     AND TotalAmount = TaxableAmount + GstAmount)
);
CREATE INDEX IX_SOI_Order ON SalesOrderItems(SalesOrderId);
CREATE INDEX IX_SOI_Product ON SalesOrderItems(ProductId);

/* ---------- Payments (separate from orders; many per order) ---------- */
CREATE TABLE Payments (
    Id                   BIGINT IDENTITY PRIMARY KEY,
    SalesOrderId         BIGINT        NOT NULL REFERENCES SalesOrders(Id),
    PaymentDate          DATE          NOT NULL,
    Amount               DECIMAL(18,2) NOT NULL,
    PaymentMode          TINYINT       NOT NULL,              -- 1=Cash 2=UPI 3=BankTransfer 4=Cheque 5=Card 9=Other
    TransactionReference NVARCHAR(100) NULL,
    ReceivedBy           BIGINT        NOT NULL REFERENCES Users(Id),
    Notes                NVARCHAR(500) NULL,
    Status               TINYINT       NOT NULL DEFAULT 1,    -- 1=Valid 2=Reversed (never hard-deleted)
    ReversedAt           DATETIME2(0)  NULL,
    ReversedBy           BIGINT        NULL REFERENCES Users(Id),
    ReversalReason       NVARCHAR(300) NULL,
    CreatedAt            DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UpdatedAt            DATETIME2(0)  NULL,
    UpdatedBy            BIGINT        NULL REFERENCES Users(Id),
    RowVer               ROWVERSION,
    CONSTRAINT CK_Pay_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Pay_Reversal CHECK ((Status = 1 AND ReversedAt IS NULL) OR (Status = 2 AND ReversedAt IS NOT NULL AND ReversalReason IS NOT NULL))
);
CREATE INDEX IX_Pay_Order ON Payments(SalesOrderId) INCLUDE (Amount, Status);
CREATE INDEX IX_Pay_Date ON Payments(PaymentDate);
-- Overpayment prevention is enforced in the service layer under a serialisable/row-locked transaction on the SalesOrder.

/* View: payment status is CALCULATED, never stored (FRD s.10) */
GO
CREATE VIEW vw_SalesOrderPaymentSummary AS
SELECT so.Id AS SalesOrderId, so.BusinessId, so.FinalAmount,
       ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) AS TotalReceived,
       so.FinalAmount - ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) AS Outstanding,
       CASE WHEN ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) = 0 THEN 'Unpaid'
            WHEN ISNULL(SUM(CASE WHEN p.Status = 1 THEN p.Amount END), 0) < so.FinalAmount THEN 'PartiallyPaid'
            ELSE 'Paid' END AS PaymentStatus
FROM SalesOrders so LEFT JOIN Payments p ON p.SalesOrderId = so.Id
WHERE so.IsDeleted = 0 AND so.Status = 1
GROUP BY so.Id, so.BusinessId, so.FinalAmount;
GO

/* ---------- Sales returns [PROPOSED - FRD s.12 uses Sales Returns but s.14 has no table] ---------- */
CREATE TABLE SalesReturns (
    Id               BIGINT IDENTITY PRIMARY KEY,
    BusinessId       BIGINT NOT NULL REFERENCES Businesses(Id),
    SalesOrderId     BIGINT NOT NULL REFERENCES SalesOrders(Id),
    ReturnDate       DATE   NOT NULL,
    TaxableAmount    DECIMAL(18,2) NOT NULL,
    GstAmount        DECIMAL(18,2) NOT NULL,
    TotalAmount      DECIMAL(18,2) NOT NULL,
    Reason           NVARCHAR(300) NULL,
    Status           TINYINT NOT NULL DEFAULT 1,              -- 1=Accepted 2=Voided
    CreatedBy        BIGINT NOT NULL REFERENCES Users(Id),
    CreatedAt        DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME()
);
CREATE INDEX IX_SR_BizDate ON SalesReturns(BusinessId, ReturnDate);
CREATE TABLE SalesReturnItems (
    Id                  BIGINT IDENTITY PRIMARY KEY,
    SalesReturnId       BIGINT NOT NULL REFERENCES SalesReturns(Id),
    SalesOrderItemId    BIGINT NOT NULL REFERENCES SalesOrderItems(Id),
    Quantity            DECIMAL(18,3) NOT NULL CHECK (Quantity > 0),
    TaxableAmount       DECIMAL(18,2) NOT NULL,
    GstAmount           DECIMAL(18,2) NOT NULL
);

/* ---------- GST invoices ---------- */
CREATE TABLE InvoiceSequences (                               -- gapless sequential numbering per business + FY + doc type
    BusinessId      BIGINT  NOT NULL REFERENCES Businesses(Id),
    FinancialYear   CHAR(7) NOT NULL,                         -- e.g. 2026-27
    DocType         TINYINT NOT NULL,                         -- 1=Invoice 2=CreditNote
    LastNumber      INT     NOT NULL DEFAULT 0,
    PRIMARY KEY (BusinessId, FinancialYear, DocType)
);

CREATE TABLE GstInvoices (
    Id                  BIGINT IDENTITY PRIMARY KEY,
    BusinessId          BIGINT        NOT NULL REFERENCES Businesses(Id),
    SalesOrderId        BIGINT        NOT NULL REFERENCES SalesOrders(Id),
    DocType             TINYINT       NOT NULL DEFAULT 1,     -- 1=TaxInvoice 2=CreditNote
    FinancialYear       CHAR(7)       NOT NULL,
    SequenceNumber      INT           NOT NULL,
    InvoiceNumber       NVARCHAR(40)  NOT NULL,
    InvoiceDate         DATE          NOT NULL,
    IsInterState        BIT           NOT NULL,
    TaxableAmount       DECIMAL(18,2) NOT NULL,
    Cgst                DECIMAL(18,2) NOT NULL DEFAULT 0,
    Sgst                DECIMAL(18,2) NOT NULL DEFAULT 0,
    Igst                DECIMAL(18,2) NOT NULL DEFAULT 0,
    GrandTotal          DECIMAL(18,2) NOT NULL,
    SnapshotJson        NVARCHAR(MAX) NOT NULL,               -- immutable business/customer/line snapshot
    Status              TINYINT       NOT NULL DEFAULT 1,     -- 1=Issued 2=Cancelled 3=Amended
    OriginalInvoiceId   BIGINT        NULL REFERENCES GstInvoices(Id),  -- credit note / amendment link
    CancelReason        NVARCHAR(300) NULL,
    IssuedBy            BIGINT        NOT NULL REFERENCES Users(Id),
    IssuedAt            DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Inv_Tax CHECK ((IsInterState = 1 AND Cgst = 0 AND Sgst = 0) OR (IsInterState = 0 AND Igst = 0)),
    CONSTRAINT CK_Inv_Total CHECK (GrandTotal = TaxableAmount + Cgst + Sgst + Igst)
);
CREATE UNIQUE INDEX UX_Inv_Number ON GstInvoices(BusinessId, InvoiceNumber);
CREATE UNIQUE INDEX UX_Inv_Seq ON GstInvoices(BusinessId, FinancialYear, DocType, SequenceNumber);
-- One active tax invoice per sales order; corrections go through credit note / amendment rows.
CREATE UNIQUE INDEX UX_Inv_OneActivePerOrder ON GstInvoices(SalesOrderId) WHERE DocType = 1 AND Status = 1;
-- Issued rows are immutable: enforced by an UPDATE-blocking trigger on all columns except Status/CancelReason (added in migration).

ALTER TABLE SalesOrders ADD InvoiceId BIGINT NULL REFERENCES GstInvoices(Id);

/* ---------- Adjustments & audit ---------- */
CREATE TABLE FinancialAdjustments (
    Id              BIGINT IDENTITY PRIMARY KEY,
    BusinessId      BIGINT NOT NULL REFERENCES Businesses(Id),
    AdjustmentDate  DATE   NOT NULL,
    Type            TINYINT NOT NULL,                         -- 1=OtherIncome 2=Loss 3=Adjustment
    Direction       TINYINT NOT NULL,                         -- 1=Increases profit 2=Decreases profit
    Amount          DECIMAL(18,2) NOT NULL CHECK (Amount > 0),
    Description     NVARCHAR(400) NOT NULL,
    CreatedBy       BIGINT NOT NULL REFERENCES Users(Id),
    CreatedAt       DATETIME2(0) NOT NULL DEFAULT SYSUTCDATETIME(),
    IsDeleted       BIT NOT NULL DEFAULT 0,
    DeletedBy       BIGINT NULL REFERENCES Users(Id),
    DeletedAt       DATETIME2(0) NULL
);
CREATE INDEX IX_FA_BizDate ON FinancialAdjustments(BusinessId, AdjustmentDate) WHERE IsDeleted = 0;

CREATE TABLE AuditLogs (
    Id              BIGINT IDENTITY PRIMARY KEY,
    OccurredAt      DATETIME2(3)  NOT NULL DEFAULT SYSUTCDATETIME(),
    UserId          BIGINT        NULL REFERENCES Users(Id),
    BusinessId      BIGINT        NULL,
    EntityName      NVARCHAR(60)  NOT NULL,
    EntityId        BIGINT        NOT NULL,
    Action          TINYINT       NOT NULL,                   -- 1=Create 2=Update 3=Delete 4=Reverse 5=Cancel 6=Login 7=PermissionChange
    OldValues       NVARCHAR(MAX) NULL,
    NewValues       NVARCHAR(MAX) NULL,
    IpAddress       VARCHAR(45)   NULL
);
CREATE INDEX IX_Audit_Entity ON AuditLogs(EntityName, EntityId);
CREATE INDEX IX_Audit_BizTime ON AuditLogs(BusinessId, OccurredAt);
