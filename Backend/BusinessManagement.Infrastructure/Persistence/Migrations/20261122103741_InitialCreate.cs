using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Role = table.Column<byte>(type: "tinyint", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Username = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Mobile = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    AvatarUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_AdminEmail", "[Role] <> 1 OR [Email] IS NOT NULL");
                    table.CheckConstraint("CK_Users_Role", "[Role] IN (1,2,3)");
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OccurredAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    BusinessId = table.Column<long>(type: "bigint", nullable: true),
                    EntityName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    EntityId = table.Column<long>(type: "bigint", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    OldValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NewValues = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(45)", unicode: false, maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Businesses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    GstNumber = table.Column<string>(type: "char(15)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    StateCode = table.Column<string>(type: "char(2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Businesses", x => x.Id);
                    table.CheckConstraint("CK_Biz_Gstin", "[GstNumber] IS NULL OR [GstNumber] LIKE '[0-9][0-9][A-Z][A-Z][A-Z][A-Z][A-Z][0-9][0-9][0-9][0-9][A-Z][0-9A-Z]Z[0-9A-Z]'");
                    table.ForeignKey(
                        name: "FK_Businesses_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Businesses_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Mobile = table.Column<string>(type: "varchar(15)", unicode: false, maxLength: 15, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    StateCode = table.Column<string>(type: "char(2)", nullable: true),
                    Gstin = table.Column<string>(type: "char(15)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.CheckConstraint("CK_Cust_Mobile", "[Mobile] NOT LIKE '%[^0-9]%' AND LEN([Mobile]) BETWEEN 10 AND 15");
                    table.ForeignKey(
                        name: "FK_Customers_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PasswordResetTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordResetTokens", x => x.Id);
                    table.CheckConstraint("CK_PRT_Expiry", "[ExpiresAt] > [CreatedAt]");
                    table.ForeignKey(
                        name: "FK_PasswordResetTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SellingPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    HsnCode = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.CheckConstraint("CK_Prod_Gst", "[GstRate] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_Prod_Prices", "[SellingPrice] >= 0 AND [PurchasePrice] >= 0");
                    table.ForeignKey(
                        name: "FK_Products_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TokenHash = table.Column<string>(type: "char(64)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ReplacedByTokenId = table.Column<long>(type: "bigint", nullable: true),
                    DeviceInfo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_RefreshTokens_ReplacedByTokenId",
                        column: x => x.ReplacedByTokenId,
                        principalTable: "RefreshTokens",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessMembers",
                columns: table => new
                {
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    AssignedBy = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessMembers", x => new { x.BusinessId, x.UserId });
                    table.ForeignKey(
                        name: "FK_BusinessMembers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessMembers_Users_AssignedBy",
                        column: x => x.AssignedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    ExpenseDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "CAST(ROUND([Quantity] * [UnitPrice], 2) AS DECIMAL(18,2))", stored: true),
                    InputGstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsInputGstEligible = table.Column<bool>(type: "bit", nullable: false),
                    PurchasedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Exp_Amounts", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [InputGstAmount] >= 0");
                    table.ForeignKey(
                        name: "FK_Expenses_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Users_PurchasedByUserId",
                        column: x => x.PurchasedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialAdjustments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    AdjustmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    Direction = table.Column<byte>(type: "tinyint", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialAdjustments", x => x.Id);
                    table.CheckConstraint("CK_FA_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_FinancialAdjustments_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialAdjustments_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialAdjustments_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceSequences",
                columns: table => new
                {
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    FinancialYear = table.Column<string>(type: "char(7)", nullable: false),
                    DocType = table.Column<byte>(type: "tinyint", nullable: false),
                    LastNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceSequences", x => new { x.BusinessId, x.FinancialYear, x.DocType });
                    table.ForeignKey(
                        name: "FK_InvoiceSequences_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerBusinesses",
                columns: table => new
                {
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    LinkedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    LinkedBy = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerBusinesses", x => new { x.CustomerId, x.BusinessId });
                    table.ForeignKey(
                        name: "FK_CustomerBusinesses_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerBusinesses_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerBusinesses_Users_LinkedBy",
                        column: x => x.LinkedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessProducts",
                columns: table => new
                {
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProducts", x => new { x.BusinessId, x.ProductId });
                    table.ForeignKey(
                        name: "FK_BusinessProducts_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessProducts_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BusinessPermissions",
                columns: table => new
                {
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CanViewBusiness = table.Column<bool>(type: "bit", nullable: false),
                    CanViewCustomers = table.Column<bool>(type: "bit", nullable: false),
                    CanAddCustomers = table.Column<bool>(type: "bit", nullable: false),
                    CanEditCustomers = table.Column<bool>(type: "bit", nullable: false),
                    CanViewProducts = table.Column<bool>(type: "bit", nullable: false),
                    CanViewSales = table.Column<bool>(type: "bit", nullable: false),
                    CanAddSales = table.Column<bool>(type: "bit", nullable: false),
                    CanEditOwnSales = table.Column<bool>(type: "bit", nullable: false),
                    CanDeleteOwnSales = table.Column<bool>(type: "bit", nullable: false),
                    CanViewExpenseTotal = table.Column<bool>(type: "bit", nullable: false),
                    CanAddExpenses = table.Column<bool>(type: "bit", nullable: false),
                    CanEditOwnExpenses = table.Column<bool>(type: "bit", nullable: false),
                    CanDeleteOwnExpenses = table.Column<bool>(type: "bit", nullable: false),
                    CanViewMemberWiseExpenses = table.Column<bool>(type: "bit", nullable: false),
                    CanRecordPayments = table.Column<bool>(type: "bit", nullable: false),
                    CanViewPayments = table.Column<bool>(type: "bit", nullable: false),
                    CanEditOwnPayments = table.Column<bool>(type: "bit", nullable: false),
                    CanDeleteOwnPayments = table.Column<bool>(type: "bit", nullable: false),
                    CanGenerateInvoices = table.Column<bool>(type: "bit", nullable: false),
                    CanViewInvoices = table.Column<bool>(type: "bit", nullable: false),
                    CanViewReports = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessPermissions", x => new { x.BusinessId, x.UserId });
                    table.ForeignKey(
                        name: "FK_BusinessPermissions_BusinessMembers_BusinessId_UserId",
                        columns: x => new { x.BusinessId, x.UserId },
                        principalTable: "BusinessMembers",
                        principalColumns: new[] { "BusinessId", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessPermissions_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GstInvoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    DocType = table.Column<byte>(type: "tinyint", nullable: false),
                    FinancialYear = table.Column<string>(type: "char(7)", nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IsInterState = table.Column<bool>(type: "bit", nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Cgst = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Sgst = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Igst = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    OriginalInvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IssuedBy = table.Column<long>(type: "bigint", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GstInvoices", x => x.Id);
                    table.CheckConstraint("CK_Inv_Tax", "([IsInterState] = 1 AND [Cgst] = 0 AND [Sgst] = 0) OR ([IsInterState] = 0 AND [Igst] = 0)");
                    table.CheckConstraint("CK_Inv_Total", "[GrandTotal] = [TaxableAmount] + [Cgst] + [Sgst] + [Igst]");
                    table.ForeignKey(
                        name: "FK_GstInvoices_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GstInvoices_GstInvoices_OriginalInvoiceId",
                        column: x => x.OriginalInvoiceId,
                        principalTable: "GstInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GstInvoices_Users_IssuedBy",
                        column: x => x.IssuedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    CustomerId = table.Column<long>(type: "bigint", nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Subtotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FinalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlaceOfSupplyState = table.Column<string>(type: "char(2)", nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    InvoiceId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    DeletedBy = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.CheckConstraint("CK_SO_Amounts", "[Subtotal] >= 0 AND [DiscountAmount] >= 0 AND [DiscountAmount] <= [Subtotal] AND [GstAmount] >= 0 AND [FinalAmount] = [TaxableAmount] + [GstAmount]");
                    table.CheckConstraint("CK_SO_Status", "[Status] IN (1,2)");
                    table.ForeignKey(
                        name: "FK_SalesOrders_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_GstInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "GstInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Users_DeletedBy",
                        column: x => x.DeletedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMode = table.Column<byte>(type: "tinyint", nullable: false),
                    TransactionReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReceivedBy = table.Column<long>(type: "bigint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ReversedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    ReversedBy = table.Column<long>(type: "bigint", nullable: true),
                    ReversalReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    UpdatedBy = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.CheckConstraint("CK_Pay_Amount", "[Amount] > 0");
                    table.CheckConstraint("CK_Pay_Reversal", "([Status] = 1 AND [ReversedAt] IS NULL) OR ([Status] = 2 AND [ReversedAt] IS NOT NULL AND [ReversalReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Payments_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_ReceivedBy",
                        column: x => x.ReceivedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_ReversedBy",
                        column: x => x.ReversedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payments_Users_UpdatedBy",
                        column: x => x.UpdatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    HsnCodeSnapshot = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderItems", x => x.Id);
                    table.CheckConstraint("CK_SOI_Amounts", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [DiscountAmount] >= 0 AND [TotalAmount] = [TaxableAmount] + [GstAmount]");
                    table.ForeignKey(
                        name: "FK_SalesOrderItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderItems_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BusinessId = table.Column<long>(type: "bigint", nullable: false),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedBy = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturns_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturns_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturns_Users_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesReturnId = table.Column<long>(type: "bigint", nullable: false),
                    SalesOrderItemId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturnItems", x => x.Id);
                    table.CheckConstraint("CK_SRI_Qty", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_SalesReturnItems_SalesOrderItems_SalesOrderItemId",
                        column: x => x.SalesOrderItemId,
                        principalTable: "SalesOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnItems_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_BizTime",
                table: "AuditLogs",
                columns: new[] { "BusinessId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Audit_Entity",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_CreatedBy",
                table: "Businesses",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Businesses_UpdatedBy",
                table: "Businesses",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UX_Biz_Gstin",
                table: "Businesses",
                column: "GstNumber",
                unique: true,
                filter: "[GstNumber] IS NOT NULL AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "UX_Biz_Name",
                table: "Businesses",
                column: "Name",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BM_User",
                table: "BusinessMembers",
                column: "UserId",
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessMembers_AssignedBy",
                table: "BusinessMembers",
                column: "AssignedBy");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessPermissions_UpdatedBy",
                table: "BusinessPermissions",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_BP_Product",
                table: "BusinessProducts",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_CB_Business",
                table: "CustomerBusinesses",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerBusinesses_LinkedBy",
                table: "CustomerBusinesses",
                column: "LinkedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Cust_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedBy",
                table: "Customers",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_UpdatedBy",
                table: "Customers",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UX_Cust_Mobile",
                table: "Customers",
                column: "Mobile",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Exp_BizDate",
                table: "Expenses",
                columns: new[] { "BusinessId", "ExpenseDate" },
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "PurchasedByUserId", "CreatedBy", "TotalAmount" });

            migrationBuilder.CreateIndex(
                name: "IX_Exp_Purchaser",
                table: "Expenses",
                columns: new[] { "BusinessId", "PurchasedByUserId", "ExpenseDate" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CreatedBy",
                table: "Expenses",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_DeletedBy",
                table: "Expenses",
                column: "DeletedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_PurchasedByUserId",
                table: "Expenses",
                column: "PurchasedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_UpdatedBy",
                table: "Expenses",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FA_BizDate",
                table: "FinancialAdjustments",
                columns: new[] { "BusinessId", "AdjustmentDate" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAdjustments_CreatedBy",
                table: "FinancialAdjustments",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialAdjustments_DeletedBy",
                table: "FinancialAdjustments",
                column: "DeletedBy");

            migrationBuilder.CreateIndex(
                name: "IX_GstInvoices_IssuedBy",
                table: "GstInvoices",
                column: "IssuedBy");

            migrationBuilder.CreateIndex(
                name: "IX_GstInvoices_OriginalInvoiceId",
                table: "GstInvoices",
                column: "OriginalInvoiceId");

            migrationBuilder.CreateIndex(
                name: "UX_Inv_Number",
                table: "GstInvoices",
                columns: new[] { "BusinessId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Inv_OneActivePerOrder",
                table: "GstInvoices",
                column: "SalesOrderId",
                unique: true,
                filter: "[DocType] = 1 AND [Status] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Inv_Seq",
                table: "GstInvoices",
                columns: new[] { "BusinessId", "FinancialYear", "DocType", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PRT_User",
                table: "PasswordResetTokens",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Pay_Date",
                table: "Payments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_Pay_Order",
                table: "Payments",
                column: "SalesOrderId")
                .Annotation("SqlServer:Include", new[] { "Amount", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReceivedBy",
                table: "Payments",
                column: "ReceivedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_ReversedBy",
                table: "Payments",
                column: "ReversedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_UpdatedBy",
                table: "Payments",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CreatedBy",
                table: "Products",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_Products_UpdatedBy",
                table: "Products",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "UX_Prod_Sku",
                table: "Products",
                column: "Sku",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_ReplacedByTokenId",
                table: "RefreshTokens",
                column: "ReplacedByTokenId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_User",
                table: "RefreshTokens",
                column: "UserId")
                .Annotation("SqlServer:Include", new[] { "ExpiresAt", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_RefreshTokens_Hash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SOI_Order",
                table: "SalesOrderItems",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SOI_Product",
                table: "SalesOrderItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CreatedBy",
                table: "SalesOrders",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_DeletedBy",
                table: "SalesOrders",
                column: "DeletedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_InvoiceId",
                table: "SalesOrders",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_UpdatedBy",
                table: "SalesOrders",
                column: "UpdatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SO_BizDate",
                table: "SalesOrders",
                columns: new[] { "BusinessId", "OrderDate" },
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SO_Creator",
                table: "SalesOrders",
                columns: new[] { "BusinessId", "CreatedBy" });

            migrationBuilder.CreateIndex(
                name: "IX_SO_Customer",
                table: "SalesOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItems_SalesOrderItemId",
                table: "SalesReturnItems",
                column: "SalesOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItems_SalesReturnId",
                table: "SalesReturnItems",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_CreatedBy",
                table: "SalesReturns",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_SalesOrderId",
                table: "SalesReturns",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SR_BizDate",
                table: "SalesReturns",
                columns: new[] { "BusinessId", "ReturnDate" });

            migrationBuilder.CreateIndex(
                name: "UX_Users_SingleAdmin",
                table: "Users",
                column: "Role",
                unique: true,
                filter: "[Role] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_GstInvoices_SalesOrders_SalesOrderId",
                table: "GstInvoices",
                column: "SalesOrderId",
                principalTable: "SalesOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_Users_CreatedBy",
                table: "Businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_Businesses_Users_UpdatedBy",
                table: "Businesses");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Users_CreatedBy",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Users_UpdatedBy",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_GstInvoices_Users_IssuedBy",
                table: "GstInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Users_CreatedBy",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Users_DeletedBy",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Users_UpdatedBy",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_GstInvoices_Businesses_BusinessId",
                table: "GstInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Businesses_BusinessId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesOrders_Customers_CustomerId",
                table: "SalesOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_GstInvoices_SalesOrders_SalesOrderId",
                table: "GstInvoices");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "BusinessPermissions");

            migrationBuilder.DropTable(
                name: "BusinessProducts");

            migrationBuilder.DropTable(
                name: "CustomerBusinesses");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "FinancialAdjustments");

            migrationBuilder.DropTable(
                name: "InvoiceSequences");

            migrationBuilder.DropTable(
                name: "PasswordResetTokens");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SalesReturnItems");

            migrationBuilder.DropTable(
                name: "BusinessMembers");

            migrationBuilder.DropTable(
                name: "SalesOrderItems");

            migrationBuilder.DropTable(
                name: "SalesReturns");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Businesses");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropTable(
                name: "GstInvoices");
        }
    }
}
