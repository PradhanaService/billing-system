using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    SystemCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Accounts_Status", "Status IN ('Active', 'Inactive')");
                });

            migrationBuilder.CreateTable(
                name: "BusinessDays",
                columns: table => new
                {
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    OpeningCashPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedClosingCashPaise = table.Column<long>(type: "INTEGER", nullable: true),
                    ActualCashPaise = table.Column<long>(type: "INTEGER", nullable: true),
                    CashDifferencePaise = table.Column<long>(type: "INTEGER", nullable: true),
                    DifferenceReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    ClosedByUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessDays", x => x.Date);
                    table.CheckConstraint("CK_BusinessDays_Status", "Status IN ('Open', 'Closed')");
                });

            migrationBuilder.CreateTable(
                name: "NumberSeries",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 6, nullable: false),
                    NextNo = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberSeries", x => x.Code);
                    table.CheckConstraint("CK_NumberSeries_NextNo", "NextNo >= 1");
                });

            migrationBuilder.CreateTable(
                name: "Vouchers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Number = table.Column<long>(type: "INTEGER", nullable: false),
                    DisplayNo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    VoucherDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PartyType = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: true),
                    CustomerId = table.Column<long>(type: "INTEGER", nullable: true),
                    VehicleId = table.Column<long>(type: "INTEGER", nullable: true),
                    PurposeAccountId = table.Column<long>(type: "INTEGER", nullable: true),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 6, nullable: true),
                    ReferenceNo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    PayTo = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    AmountPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    Narration = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ReversesVoucherId = table.Column<long>(type: "INTEGER", nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vouchers", x => x.Id);
                    table.CheckConstraint("CK_Vouchers_Amount", "AmountPaise > 0");
                    table.CheckConstraint("CK_Vouchers_OneParty", "SupplierId IS NULL OR CustomerId IS NULL");
                    table.ForeignKey(
                        name: "FK_Vouchers_Accounts_PurposeAccountId",
                        column: x => x.PurposeAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vouchers_Vouchers_ReversesVoucherId",
                        column: x => x.ReversesVoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PostingSeq = table.Column<long>(type: "INTEGER", nullable: false),
                    VoucherId = table.Column<long>(type: "INTEGER", nullable: false),
                    LineNo = table.Column<int>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<long>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: true),
                    CustomerId = table.Column<long>(type: "INTEGER", nullable: true),
                    VehicleId = table.Column<long>(type: "INTEGER", nullable: true),
                    EntryDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ValueDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Particulars = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReferenceNo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    DebitPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    CreditPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    RunningBalancePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    VehicleRunningBalancePaise = table.Column<long>(type: "INTEGER", nullable: true),
                    ReversesEntryId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_Ledger_OneParty", "SupplierId IS NULL OR CustomerId IS NULL");
                    table.CheckConstraint("CK_Ledger_OneSide", "(DebitPaise > 0 AND CreditPaise = 0) OR (CreditPaise > 0 AND DebitPaise = 0)");
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_LedgerEntries_ReversesEntryId",
                        column: x => x.ReversesEntryId,
                        principalTable: "LedgerEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LedgerEntries_Vouchers_VoucherId",
                        column: x => x.VoucherId,
                        principalTable: "Vouchers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Accounts",
                columns: new[] { "Id", "CreatedAtUtc", "IsSystem", "Name", "NormalizedName", "Status", "SystemCode", "Type", "UpdatedAtUtc", "Version" },
                values: new object[,]
                {
                    { 1L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Cash in Hand", "CASH IN HAND", "Active", "CASH", "Asset", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 2L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Bank", "BANK", "Active", "BANK", "Asset", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 3L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "UPI Clearing", "UPI CLEARING", "Active", "UPI", "Asset", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 4L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Trade Receivables - Suppliers", "TRADE RECEIVABLES - SUPPLIERS", "Active", "AR_SUPPLIERS", "Asset", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 5L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Trade Receivables - Customers", "TRADE RECEIVABLES - CUSTOMERS", "Active", "AR_CUSTOMERS", "Asset", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 6L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Water Sales", "WATER SALES", "Active", "SALES", "Income", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 7L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Cash Over/Short", "CASH OVER/SHORT", "Active", "CASH_OVER_SHORT", "Expense", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 8L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), true, "Opening Balance Equity", "OPENING BALANCE EQUITY", "Active", "OPENING_EQUITY", "Equity", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 9L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "Vehicle Maintenance", "VEHICLE MAINTENANCE", "Active", null, "Expense", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 10L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "Fuel Expense", "FUEL EXPENSE", "Active", null, "Expense", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 11L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "Oil Change", "OIL CHANGE", "Active", null, "Expense", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 },
                    { 12L, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), false, "Others", "OTHERS", "Active", null, "Expense", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1 }
                });

            migrationBuilder.InsertData(
                table: "NumberSeries",
                columns: new[] { "Code", "NextNo", "Prefix" },
                values: new object[,]
                {
                    { "INVOICE", 1L, "INV-" },
                    { "JOURNAL", 1L, "JV-" },
                    { "PAYMENT", 1L, "PV-" },
                    { "RECEIPT", 1L, "RV-" },
                    { "REVERSAL", 1L, "REV-" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedName",
                table: "Accounts",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_SystemCode",
                table: "Accounts",
                column: "SystemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_EntryDate_PostingSeq",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "EntryDate", "PostingSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_SupplierId_CustomerId_PostingSeq",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "SupplierId", "CustomerId", "PostingSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CreatedByUserId",
                table: "LedgerEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CustomerId",
                table: "LedgerEntries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_PostingSeq",
                table: "LedgerEntries",
                column: "PostingSeq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ReversesEntryId",
                table: "LedgerEntries",
                column: "ReversesEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_SupplierId",
                table: "LedgerEntries",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_ValueDate_AccountId",
                table: "LedgerEntries",
                columns: new[] { "ValueDate", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_VehicleId_PostingSeq",
                table: "LedgerEntries",
                columns: new[] { "VehicleId", "PostingSeq" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_VoucherId_LineNo",
                table: "LedgerEntries",
                columns: new[] { "VoucherId", "LineNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CreatedByUserId",
                table: "Vouchers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_CustomerId_VoucherDate",
                table: "Vouchers",
                columns: new[] { "CustomerId", "VoucherDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_DisplayNo",
                table: "Vouchers",
                column: "DisplayNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_PurposeAccountId",
                table: "Vouchers",
                column: "PurposeAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_ReversesVoucherId",
                table: "Vouchers",
                column: "ReversesVoucherId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_SupplierId_VoucherDate",
                table: "Vouchers",
                columns: new[] { "SupplierId", "VoucherDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_Type_Number",
                table: "Vouchers",
                columns: new[] { "Type", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_ValueDate_Type",
                table: "Vouchers",
                columns: new[] { "ValueDate", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Vouchers_VehicleId",
                table: "Vouchers",
                column: "VehicleId");

            // Append-only ledger (FR-LED-02, NFR-SEC-06): the database refuses to change or delete lines.
            migrationBuilder.Sql(@"CREATE TRIGGER TR_LedgerEntries_NoUpdate BEFORE UPDATE ON LedgerEntries
                BEGIN SELECT RAISE(ABORT, 'Ledger lines cannot be changed. Post a reversal instead.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER TR_LedgerEntries_NoDelete BEFORE DELETE ON LedgerEntries
                BEGIN SELECT RAISE(ABORT, 'Ledger lines cannot be deleted. Post a reversal instead.'); END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_LedgerEntries_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_LedgerEntries_NoDelete;");
            migrationBuilder.DropTable(
                name: "BusinessDays");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "NumberSeries");

            migrationBuilder.DropTable(
                name: "Vouchers");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
