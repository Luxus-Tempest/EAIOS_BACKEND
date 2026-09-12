using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EAIOS.Api.Migrations.EaiosDb
{
    /// <inheritdoc />
    public partial class WorkflowSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "NextRunAt",
                schema: "workflow",
                table: "definitions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleCron",
                schema: "workflow",
                table: "definitions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NextRunAt",
                schema: "workflow",
                table: "definitions");

            migrationBuilder.DropColumn(
                name: "ScheduleCron",
                schema: "workflow",
                table: "definitions");
        }
    }
}
