using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesPersonsAndTwoRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Two roles only (client decision): existing operator / clerk / accountant logins become Sales Person.
            migrationBuilder.Sql("UPDATE Users SET Role = 'SalesPerson' WHERE Role IN ('DispatchOperator', 'BillingClerk', 'Accountant');");

            migrationBuilder.AddColumn<long>(
                name: "SalesPersonId",
                table: "Invoices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesPersons",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesPersons", x => x.Id);
                    table.CheckConstraint("CK_SalesPersons_Status", "Status IN ('Active', 'Inactive')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_SalesPersonId",
                table: "Invoices",
                column: "SalesPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesPersons_NormalizedName",
                table: "SalesPersons",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Invoices_SalesPersons_SalesPersonId",
                table: "Invoices",
                column: "SalesPersonId",
                principalTable: "SalesPersons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Invoices_SalesPersons_SalesPersonId",
                table: "Invoices");

            migrationBuilder.DropTable(
                name: "SalesPersons");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_SalesPersonId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "SalesPersonId",
                table: "Invoices");
        }
    }
}
