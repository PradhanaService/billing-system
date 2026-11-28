using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminDoneByAndPasswordDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordChangedAtUtc",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoneBy",
                table: "ActivityLogs",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PasswordChangedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DoneBy",
                table: "ActivityLogs");
        }
    }
}
