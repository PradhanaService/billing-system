using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSuppliers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ShortName = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ContactPerson = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 15, nullable: false),
                    AltPhone = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    OpeningBalancePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    OpeningBalanceSide = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                    table.CheckConstraint("CK_Suppliers_OpeningBalance", "OpeningBalancePaise >= 0");
                    table.CheckConstraint("CK_Suppliers_OpeningBalanceSide", "OpeningBalanceSide IN ('Dr', 'Cr')");
                    table.CheckConstraint("CK_Suppliers_Status", "Status IN ('Active', 'Inactive')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_NormalizedName",
                table: "Suppliers",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_Status",
                table: "Suppliers",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Suppliers");
        }
    }
}
