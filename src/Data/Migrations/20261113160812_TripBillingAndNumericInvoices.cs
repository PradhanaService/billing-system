using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    // Client changes, Nov 2026: new invoice numbers are numbers only (no "INV-"), and every vehicle
    // is billed per trip. Invoices already saved keep their number and amount (the ledger is permanent).
    /// <inheritdoc />
    public partial class TripBillingAndNumericInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "NumberSeries",
                keyColumn: "Code",
                keyValue: "INVOICE",
                column: "Prefix",
                value: "");

            migrationBuilder.Sql("UPDATE Vehicles SET BillingBasis = 'Trip' WHERE BillingBasis = 'Volume';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "NumberSeries",
                keyColumn: "Code",
                keyValue: "INVOICE",
                column: "Prefix",
                value: "INV-");
        }
    }
}
