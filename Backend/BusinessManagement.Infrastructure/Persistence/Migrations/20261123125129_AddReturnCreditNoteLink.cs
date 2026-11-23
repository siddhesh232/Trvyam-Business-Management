using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReturnCreditNoteLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreditNoteId",
                table: "SalesReturns",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_CreditNoteId",
                table: "SalesReturns",
                column: "CreditNoteId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturns_GstInvoices_CreditNoteId",
                table: "SalesReturns",
                column: "CreditNoteId",
                principalTable: "GstInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturns_GstInvoices_CreditNoteId",
                table: "SalesReturns");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturns_CreditNoteId",
                table: "SalesReturns");

            migrationBuilder.DropColumn(
                name: "CreditNoteId",
                table: "SalesReturns");
        }
    }
}
