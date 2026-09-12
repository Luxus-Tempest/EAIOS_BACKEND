using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EAIOS.Api.Migrations.EaiosDb
{
    /// <inheritdoc />
    public partial class AgentEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "test_cases",
                schema: "agent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Input = table.Column<string>(type: "text", nullable: false),
                    ExpectedPhrases = table.Column<string[]>(type: "text[]", nullable: false),
                    ForbiddenPhrases = table.Column<string[]>(type: "text[]", nullable: false),
                    RequiresCitation = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_cases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "test_runs",
                schema: "agent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentVersion = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TriggeredBy = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalCases = table.Column<int>(type: "integer", nullable: false),
                    PassedCases = table.Column<int>(type: "integer", nullable: false),
                    FailedCases = table.Column<int>(type: "integer", nullable: false),
                    P95LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    MedianLatencyMs = table.Column<int>(type: "integer", nullable: true),
                    TotalCostUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "test_results",
                schema: "agent",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    CostUsd = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalTokens = table.Column<int>(type: "integer", nullable: false),
                    Output = table.Column<string>(type: "text", nullable: true),
                    CitationCount = table.Column<int>(type: "integer", nullable: false),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_test_results_test_runs_RunId",
                        column: x => x.RunId,
                        principalSchema: "agent",
                        principalTable: "test_runs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_test_cases_AgentId_IsEnabled",
                schema: "agent",
                table: "test_cases",
                columns: new[] { "AgentId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_test_results_RunId_Passed",
                schema: "agent",
                table: "test_results",
                columns: new[] { "RunId", "Passed" });

            migrationBuilder.CreateIndex(
                name: "IX_test_runs_AgentId_StartedAt",
                schema: "agent",
                table: "test_runs",
                columns: new[] { "AgentId", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "test_cases",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "test_results",
                schema: "agent");

            migrationBuilder.DropTable(
                name: "test_runs",
                schema: "agent");
        }
    }
}
