using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PCTimeLimitServer.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedAllowedUsageSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AllowedUsageUpdatedAtUtc",
                table: "Computers",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.CreateTable(
                name: "ComputerAllowedUsageRanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComputerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    EndMinute = table.Column<int>(type: "INTEGER", nullable: false),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComputerAllowedUsageRanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComputerAllowedUsageRanges_Computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "Computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComputerAllowedUsageRanges_ComputerId_DayOfWeek_StartMinute",
                table: "ComputerAllowedUsageRanges",
                columns: new[] { "ComputerId", "DayOfWeek", "StartMinute" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComputerAllowedUsageRanges");

            migrationBuilder.DropColumn(
                name: "AllowedUsageUpdatedAtUtc",
                table: "Computers");
        }
    }
}
