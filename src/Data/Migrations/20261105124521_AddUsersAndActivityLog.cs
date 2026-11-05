using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WaterBilling.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUsersAndActivityLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EventAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UserId = table.Column<long>(type: "INTEGER", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    EntityId = table.Column<long>(type: "INTEGER", nullable: true),
                    BeforeJson = table.Column<string>(type: "TEXT", nullable: true),
                    AfterJson = table.Column<string>(type: "TEXT", nullable: true),
                    Reason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 15, nullable: true),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    MustChangePassword = table.Column<bool>(type: "INTEGER", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastLoginAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_EntityType_EntityId",
                table: "ActivityLogs",
                columns: new[] { "EntityType", "EntityId" });

            // Append-only (FR-ADM-05): the database itself refuses to change or delete log rows.
            migrationBuilder.Sql(@"CREATE TRIGGER TR_ActivityLogs_NoUpdate BEFORE UPDATE ON ActivityLogs
                BEGIN SELECT RAISE(ABORT, 'Activity log rows cannot be changed.'); END;");
            migrationBuilder.Sql(@"CREATE TRIGGER TR_ActivityLogs_NoDelete BEFORE DELETE ON ActivityLogs
                BEGIN SELECT RAISE(ABORT, 'Activity log rows cannot be deleted.'); END;");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_EventAtUtc",
                table: "ActivityLogs",
                column: "EventAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedUserName",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ActivityLogs_NoUpdate;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_ActivityLogs_NoDelete;");
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
