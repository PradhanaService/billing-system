using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    VehicleNo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    NormalizedVehicleNo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Nickname = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: false),
                    TankCapacityLitres = table.Column<int>(type: "INTEGER", nullable: false),
                    BillingBasis = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    TripCostPaise = table.Column<long>(type: "INTEGER", nullable: false),
                    RatePaise = table.Column<long>(type: "INTEGER", nullable: false),
                    WaterDetails = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    DriverName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    DriverPhone = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    DriverLicenceNo = table.Column<string>(type: "TEXT", maxLength: 25, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 12, nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.CheckConstraint("CK_Vehicles_Basis", "BillingBasis IN ('Trip', 'Volume')");
                    table.CheckConstraint("CK_Vehicles_Capacity", "TankCapacityLitres > 0");
                    table.CheckConstraint("CK_Vehicles_Rate", "RatePaise > 0");
                    table.CheckConstraint("CK_Vehicles_Status", "Status IN ('Active', 'Inactive', 'Maintenance')");
                    table.CheckConstraint("CK_Vehicles_TripCost", "TripCostPaise >= 0");
                    table.ForeignKey(
                        name: "FK_Vehicles_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Code",
                table: "Vehicles",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_NormalizedVehicleNo",
                table: "Vehicles",
                column: "NormalizedVehicleNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_SupplierId_Status",
                table: "Vehicles",
                columns: new[] { "SupplierId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
