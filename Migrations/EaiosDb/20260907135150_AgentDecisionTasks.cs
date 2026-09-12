using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EAIOS.Api.Migrations.EaiosDb
{
    /// <inheritdoc />
    public partial class AgentDecisionTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_instances_InstanceId",
                schema: "workflow",
                table: "tasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "InstanceId",
                schema: "workflow",
                table: "tasks",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "AgentExecutionId",
                schema: "workflow",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_tasks_AgentExecutionId",
                schema: "workflow",
                table: "tasks",
                column: "AgentExecutionId",
                filter: "\"AgentExecutionId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_instances_InstanceId",
                schema: "workflow",
                table: "tasks",
                column: "InstanceId",
                principalSchema: "workflow",
                principalTable: "instances",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_instances_InstanceId",
                schema: "workflow",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_AgentExecutionId",
                schema: "workflow",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "AgentExecutionId",
                schema: "workflow",
                table: "tasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "InstanceId",
                schema: "workflow",
                table: "tasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_instances_InstanceId",
                schema: "workflow",
                table: "tasks",
                column: "InstanceId",
                principalSchema: "workflow",
                principalTable: "instances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
