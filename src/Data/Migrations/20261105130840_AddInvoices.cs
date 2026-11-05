using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceNo = table.Column<long>(type: "INTEGER", nullable: false),
                    DisplayNo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    VehicleId = table.Column<long>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: false),
                    BillToType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CustomerId = table.Column<long>(type: "INTEGER", nullable: true),
                    PaymentMode = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    Material = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Trips = table.Column<int>(type: "INTEGER", nullable: false),
                    VolumeTenths = table.Column<long>(type: "INTEGER", nullable: false),
                    BillingBasis = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RatePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    RateSource = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    AmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    AllocatedPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    VoucherId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReversalVoucherId = table.Column<long>(type: "INTEGER", nullable: true),
                    CancelledByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.CheckConstraint("CK_Invoices_Allocated", "AllocatedPaise >= 0 AND AllocatedPaise <= AmountPaise");
                    table.CheckConstraint("CK_Invoices_Amount", "AmountPaise > 0 AND RatePaise > 0");
                    table.CheckConstraint("CK_Invoices_BillTo", "(BillToType = 'Supplier' AND CustomerId IS NULL) OR (BillToType = 'Customer' AND CustomerId IS NOT NULL)");
                    table.CheckConstraint("CK_Invoices_Trips", "Trips >= 1 AND VolumeTenths >= 0");
                    table.ForeignKey(
                        name: "FK_Invoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Vouchers_ReversalVoucherId",
                        column: x => x.ReversalVoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Invoices_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CreatedByUserId",
                table: "Invoices",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CustomerId_InvoiceDate",
                table: "Invoices",
                columns: new[] { "CustomerId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceDate_Status",
                table: "Invoices",
                columns: new[] { "InvoiceDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_InvoiceNo",
                table: "Invoices",
                column: "InvoiceNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_ReversalVoucherId",
                table: "Invoices",
                column: "ReversalVoucherId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SupplierId_InvoiceDate",
                table: "Invoices",
                columns: new[] { "SupplierId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_VehicleId_InvoiceDate",
                table: "Invoices",
                columns: new[] { "VehicleId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_VoucherId",
                table: "Invoices",
                column: "VoucherId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}
