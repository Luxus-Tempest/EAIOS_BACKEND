using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EAIOS.Api.Migrations.EaiosDb
{
    /// <inheritdoc />
    public partial class correctionV1_11 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "organization",
                table: "workspaces",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "WorkflowDefinitionVersions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "identity",
                table: "users",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "acl",
                table: "user_roles",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "workflow",
                table: "tasks",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "connector",
                table: "sync_jobs",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "identity",
                table: "sessions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "SavedSearches",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "acl",
                table: "roles",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "ResourceAcls",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "acl",
                table: "policies",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "acl",
                table: "permissions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "NotificationTemplates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "notification",
                table: "notifications",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "identity",
                table: "mfa_credentials",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "MetadataValues",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "MetadataTemplates",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "organization",
                table: "memberships",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "LegalHolds",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "KnowledgeRelations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "KnowledgePacks",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "knowledge",
                table: "items",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "identity",
                table: "invitations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "workflow",
                table: "instances",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "connector",
                table: "instances",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "resource",
                table: "folders",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "agent",
                table: "executions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "analytics",
                table: "events",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Embeddings",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "DocumentVersions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "DocumentShares",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "resource",
                table: "documents",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "organization",
                table: "departments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "knowledge",
                table: "chunks",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "identity",
                table: "api_keys",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "AgentVersions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                schema: "agent",
                table: "agents",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "AgentMemories",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateTable(
                name: "ConnectorDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    AuthType = table.Column<int>(type: "integer", nullable: false),
                    SchemaJson = table.Column<string>(type: "text", nullable: false),
                    SupportedCapabilities = table.Column<string[]>(type: "text[]", nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectorDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: true),
                    SubscribedEvents = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastTriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectorDefinitions");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "organization",
                table: "workspaces");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "WorkflowDefinitionVersions");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "acl",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "workflow",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "connector",
                table: "sync_jobs");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "identity",
                table: "sessions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "acl",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "ResourceAcls");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "acl",
                table: "policies");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "acl",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "NotificationTemplates");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "notification",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "identity",
                table: "mfa_credentials");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "MetadataValues");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "MetadataTemplates");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "organization",
                table: "memberships");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "LegalHolds");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "KnowledgeRelations");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "KnowledgePacks");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "knowledge",
                table: "items");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "identity",
                table: "invitations");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "workflow",
                table: "instances");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "connector",
                table: "instances");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "resource",
                table: "folders");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "agent",
                table: "executions");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "analytics",
                table: "events");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Embeddings");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "DocumentVersions");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "DocumentShares");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "resource",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "organization",
                table: "departments");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "knowledge",
                table: "chunks");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "identity",
                table: "api_keys");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "AgentVersions");

            migrationBuilder.DropColumn(
                name: "xmin",
                schema: "agent",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "AgentMemories");
        }
    }
}
