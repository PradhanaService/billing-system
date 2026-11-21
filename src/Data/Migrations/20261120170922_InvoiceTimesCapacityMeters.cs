using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceTimesCapacityMeters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CapacityLitres",
                table: "Invoices",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "InTimeUtc",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Meter1EndTenths",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Meter1StartTenths",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Meter2EndTenths",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Meter2StartTenths",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Meter3EndTenths",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Meter3StartTenths",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OutTimeUtc",
                table: "Invoices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReplacedByInvoiceId",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReplacesInvoiceId",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            // Invoices made before this change take their vehicle's capacity.
            migrationBuilder.Sql("UPDATE Invoices SET CapacityLitres = (SELECT TankCapacityLitres FROM Vehicles WHERE Vehicles.Id = Invoices.VehicleId);");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_Capacity",
                table: "Invoices",
                sql: "CapacityLitres >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoices_TripTimes",
                table: "Invoices",
                sql: "InTimeUtc IS NULL OR OutTimeUtc IS NULL OR OutTimeUtc >= InTimeUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_Capacity",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoices_TripTimes",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "CapacityLitres",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "InTimeUtc",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Meter1EndTenths",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Meter1StartTenths",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Meter2EndTenths",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Meter2StartTenths",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Meter3EndTenths",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "Meter3StartTenths",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "OutTimeUtc",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReplacedByInvoiceId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReplacesInvoiceId",
                table: "Invoices");
        }
    }
}
