using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteAndRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Suppliers_NormalizedName",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_SalesPersons_NormalizedName",
                table: "SalesPersons");

            migrationBuilder.DropIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_NormalizedName",
                table: "Accounts");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Vehicles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedByUserId",
                table: "Vehicles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Suppliers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedByUserId",
                table: "Suppliers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "SalesPersons",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedByUserId",
                table: "SalesPersons",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Customers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedByUserId",
                table: "Customers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Accounts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedByUserId",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RetiredVehicleCodes",
                columns: table => new
                {
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    RetiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetiredVehicleCodes", x => x.Code);
                });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 2L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 3L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 4L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 5L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 6L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 7L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 8L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 10L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 11L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 12L,
                columns: new[] { "DeletedAtUtc", "DeletedByUserId" },
                values: new object[] { null, null });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_NormalizedName",
                table: "Suppliers",
                column: "NormalizedName",
                unique: true,
                filter: "DeletedAtUtc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SalesPersons_NormalizedName",
                table: "SalesPersons",
                column: "NormalizedName",
                unique: true,
                filter: "DeletedAtUtc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers",
                column: "NormalizedName",
                unique: true,
                filter: "DeletedAtUtc IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedName",
                table: "Accounts",
                column: "NormalizedName",
                unique: true,
                filter: "DeletedAtUtc IS NULL");

            // Records removed before Recovery existed: vehicles hidden earlier and sales persons taken off the
            // list earlier are treated as deleted on the day they were last changed.
            migrationBuilder.Sql("UPDATE Vehicles SET DeletedAtUtc = UpdatedAtUtc WHERE IsDeleted = 1;");
            migrationBuilder.Sql("UPDATE SalesPersons SET DeletedAtUtc = UpdatedAtUtc WHERE Status = 'Inactive';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetiredVehicleCodes");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_NormalizedName",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_SalesPersons_NormalizedName",
                table: "SalesPersons");

            migrationBuilder.DropIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_NormalizedName",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "SalesPersons");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "SalesPersons");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_NormalizedName",
                table: "Suppliers",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesPersons_NormalizedName",
                table: "SalesPersons",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_NormalizedName",
                table: "Customers",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_NormalizedName",
                table: "Accounts",
                column: "NormalizedName",
                unique: true);
        }
    }
}
