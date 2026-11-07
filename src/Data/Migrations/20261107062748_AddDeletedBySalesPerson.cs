using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletedBySalesPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DeletedBySalesPersonId",
                table: "Vehicles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedBySalesPersonId",
                table: "Suppliers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedBySalesPersonId",
                table: "SalesPersons",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedBySalesPersonId",
                table: "Customers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DeletedBySalesPersonId",
                table: "Accounts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 1L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 2L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 3L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 4L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 5L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 6L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 7L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 8L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 9L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 10L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 11L,
                column: "DeletedBySalesPersonId",
                value: null);

            migrationBuilder.UpdateData(
                table: "Accounts",
                keyColumn: "Id",
                keyValue: 12L,
                column: "DeletedBySalesPersonId",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedBySalesPersonId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "DeletedBySalesPersonId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "DeletedBySalesPersonId",
                table: "SalesPersons");

            migrationBuilder.DropColumn(
                name: "DeletedBySalesPersonId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "DeletedBySalesPersonId",
                table: "Accounts");
        }
    }
}
