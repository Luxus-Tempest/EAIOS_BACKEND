using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EAIOS.Api.Migrations.EaiosDb
{
    /// <inheritdoc />
    public partial class AgentSubAgents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubAgentsJson",
                schema: "agent",
                table: "agents",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubAgentsJson",
                schema: "agent",
                table: "agents");
        }
    }
}
