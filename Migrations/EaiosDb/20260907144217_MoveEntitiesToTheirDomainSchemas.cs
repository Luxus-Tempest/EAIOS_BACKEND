using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EAIOS.Api.Migrations.EaiosDb
{
    /// <inheritdoc />
    public partial class MoveEntitiesToTheirDomainSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AgentVersions_agents_AgentId",
                table: "AgentVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentShares_documents_DocumentId",
                table: "DocumentShares");

            migrationBuilder.DropForeignKey(
                name: "FK_DocumentVersions_documents_DocumentId",
                table: "DocumentVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeRelations_items_KnowledgeItemId",
                table: "KnowledgeRelations");

            migrationBuilder.DropForeignKey(
                name: "FK_MetadataValues_documents_DocumentId",
                table: "MetadataValues");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkflowDefinitionVersions_definitions_DefinitionId",
                table: "WorkflowDefinitionVersions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Embeddings",
                table: "Embeddings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WorkflowDefinitionVersions",
                table: "WorkflowDefinitionVersions");

            migrationBuilder.DropIndex(
                name: "IX_WorkflowDefinitionVersions_DefinitionId",
                table: "WorkflowDefinitionVersions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_WebhookSubscriptions",
                table: "WebhookSubscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SavedSearches",
                table: "SavedSearches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ResourceAcls",
                table: "ResourceAcls");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PromptTemplates",
                table: "PromptTemplates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NotificationTemplates",
                table: "NotificationTemplates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MetadataValues",
                table: "MetadataValues");

            migrationBuilder.DropPrimaryKey(
                name: "PK_MetadataTemplates",
                table: "MetadataTemplates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_LegalHolds",
                table: "LegalHolds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_KnowledgeRelations",
                table: "KnowledgeRelations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_KnowledgePacks",
                table: "KnowledgePacks");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DocumentVersions",
                table: "DocumentVersions");

            migrationBuilder.DropIndex(
                name: "IX_DocumentVersions_DocumentId",
                table: "DocumentVersions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_DocumentShares",
                table: "DocumentShares");

            migrationBuilder.DropIndex(
                name: "IX_DocumentShares_DocumentId",
                table: "DocumentShares");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AgentVersions",
                table: "AgentVersions");

            migrationBuilder.DropIndex(
                name: "IX_AgentVersions_AgentId",
                table: "AgentVersions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_AgentMemories",
                table: "AgentMemories");

            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.EnsureSchema(
                name: "webhook");

            migrationBuilder.RenameTable(
                name: "Embeddings",
                newName: "embeddings",
                newSchema: "search");

            migrationBuilder.RenameTable(
                name: "WorkflowDefinitionVersions",
                newName: "definition_versions",
                newSchema: "workflow");

            migrationBuilder.RenameTable(
                name: "WebhookSubscriptions",
                newName: "subscriptions",
                newSchema: "webhook");

            migrationBuilder.RenameTable(
                name: "SavedSearches",
                newName: "saved_searches",
                newSchema: "search");

            migrationBuilder.RenameTable(
                name: "ResourceAcls",
                newName: "resource_acls",
                newSchema: "acl");

            migrationBuilder.RenameTable(
                name: "PromptTemplates",
                newName: "prompt_templates",
                newSchema: "agent");

            migrationBuilder.RenameTable(
                name: "NotificationTemplates",
                newName: "templates",
                newSchema: "notification");

            migrationBuilder.RenameTable(
                name: "MetadataValues",
                newName: "metadata_values",
                newSchema: "resource");

            migrationBuilder.RenameTable(
                name: "MetadataTemplates",
                newName: "metadata_templates",
                newSchema: "resource");

            migrationBuilder.RenameTable(
                name: "LegalHolds",
                newName: "legal_holds",
                newSchema: "resource");

            migrationBuilder.RenameTable(
                name: "KnowledgeRelations",
                newName: "relations",
                newSchema: "knowledge");

            migrationBuilder.RenameTable(
                name: "KnowledgePacks",
                newName: "packs",
                newSchema: "knowledge");

            migrationBuilder.RenameTable(
                name: "DocumentVersions",
                newName: "document_versions",
                newSchema: "resource");

            migrationBuilder.RenameTable(
                name: "DocumentShares",
                newName: "document_shares",
                newSchema: "resource");

            // `platform.connector_definitions` existe deja : PlatformDbContext la
            // mappait correctement. Faute de configuration cote EaiosDbContext, une
            // seconde copie vivait dans `public.ConnectorDefinitions`, que rien ne
            // synchronisait. Elle est vide : on la supprime au lieu de la renommer,
            // ce qui echouerait sur une relation existante.
            migrationBuilder.Sql("DROP TABLE IF EXISTS public.\"ConnectorDefinitions\";");

            migrationBuilder.RenameTable(
                name: "AgentVersions",
                newName: "versions",
                newSchema: "agent");

            migrationBuilder.RenameTable(
                name: "AgentMemories",
                newName: "memories",
                newSchema: "agent");

            migrationBuilder.RenameIndex(
                name: "IX_MetadataValues_DocumentId",
                schema: "resource",
                table: "metadata_values",
                newName: "IX_metadata_values_DocumentId");

            migrationBuilder.RenameIndex(
                name: "IX_KnowledgeRelations_KnowledgeItemId",
                schema: "knowledge",
                table: "relations",
                newName: "IX_relations_KnowledgeItemId");

            migrationBuilder.AlterColumn<string>(
                name: "SearchType",
                schema: "search",
                table: "saved_searches",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "search",
                table: "saved_searches",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "PrincipalType",
                schema: "acl",
                table: "resource_acls",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Effect",
                schema: "acl",
                table: "resource_acls",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Role",
                schema: "agent",
                table: "prompt_templates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "agent",
                table: "prompt_templates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Channel",
                schema: "notification",
                table: "templates",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "resource",
                table: "metadata_templates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "resource",
                table: "legal_holds",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Source",
                schema: "knowledge",
                table: "relations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "RelationType",
                schema: "knowledge",
                table: "relations",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "knowledge",
                table: "packs",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "knowledge",
                table: "packs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                schema: "resource",
                table: "document_versions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "TargetType",
                schema: "resource",
                table: "document_shares",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Permission",
                schema: "resource",
                table: "document_shares",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "platform",
                table: "connector_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                schema: "platform",
                table: "connector_definitions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "AuthType",
                schema: "platform",
                table: "connector_definitions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                schema: "agent",
                table: "memories",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                schema: "agent",
                table: "memories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "PK_embeddings",
                schema: "search",
                table: "embeddings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_definition_versions",
                schema: "workflow",
                table: "definition_versions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_subscriptions",
                schema: "webhook",
                table: "subscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_saved_searches",
                schema: "search",
                table: "saved_searches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_resource_acls",
                schema: "acl",
                table: "resource_acls",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_prompt_templates",
                schema: "agent",
                table: "prompt_templates",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_templates",
                schema: "notification",
                table: "templates",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_metadata_values",
                schema: "resource",
                table: "metadata_values",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_metadata_templates",
                schema: "resource",
                table: "metadata_templates",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_legal_holds",
                schema: "resource",
                table: "legal_holds",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_relations",
                schema: "knowledge",
                table: "relations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_packs",
                schema: "knowledge",
                table: "packs",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_document_versions",
                schema: "resource",
                table: "document_versions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_document_shares",
                schema: "resource",
                table: "document_shares",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_versions",
                schema: "agent",
                table: "versions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_memories",
                schema: "agent",
                table: "memories",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_embeddings_ChunkId",
                schema: "search",
                table: "embeddings",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "IX_embeddings_SourceType_SourceId",
                schema: "search",
                table: "embeddings",
                columns: new[] { "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_definition_versions_DefinitionId_VersionNumber",
                schema: "workflow",
                table: "definition_versions",
                columns: new[] { "DefinitionId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_saved_searches_UserId",
                schema: "search",
                table: "saved_searches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_resource_acls_ResourceId_PrincipalId",
                schema: "acl",
                table: "resource_acls",
                columns: new[] { "ResourceId", "PrincipalId" });

            migrationBuilder.CreateIndex(
                name: "IX_templates_EventType_Channel_Language",
                schema: "notification",
                table: "templates",
                columns: new[] { "EventType", "Channel", "Language" });

            migrationBuilder.CreateIndex(
                name: "IX_metadata_values_ResourceId_TemplateId",
                schema: "resource",
                table: "metadata_values",
                columns: new[] { "ResourceId", "TemplateId" });

            migrationBuilder.CreateIndex(
                name: "IX_legal_holds_DocumentId_Status",
                schema: "resource",
                table: "legal_holds",
                columns: new[] { "DocumentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_relations_SourceItemId",
                schema: "knowledge",
                table: "relations",
                column: "SourceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_relations_TargetItemId",
                schema: "knowledge",
                table: "relations",
                column: "TargetItemId");

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_DocumentId_VersionNumber",
                schema: "resource",
                table: "document_versions",
                columns: new[] { "DocumentId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_document_shares_DocumentId_TargetId",
                schema: "resource",
                table: "document_shares",
                columns: new[] { "DocumentId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_versions_AgentId_VersionNumber",
                schema: "agent",
                table: "versions",
                columns: new[] { "AgentId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_memories_AgentId_UserId_Type_Key",
                schema: "agent",
                table: "memories",
                columns: new[] { "AgentId", "UserId", "Type", "Key" });

            migrationBuilder.AddForeignKey(
                name: "FK_definition_versions_definitions_DefinitionId",
                schema: "workflow",
                table: "definition_versions",
                column: "DefinitionId",
                principalSchema: "workflow",
                principalTable: "definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_shares_documents_DocumentId",
                schema: "resource",
                table: "document_shares",
                column: "DocumentId",
                principalSchema: "resource",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_document_versions_documents_DocumentId",
                schema: "resource",
                table: "document_versions",
                column: "DocumentId",
                principalSchema: "resource",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_metadata_values_documents_DocumentId",
                schema: "resource",
                table: "metadata_values",
                column: "DocumentId",
                principalSchema: "resource",
                principalTable: "documents",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_relations_items_KnowledgeItemId",
                schema: "knowledge",
                table: "relations",
                column: "KnowledgeItemId",
                principalSchema: "knowledge",
                principalTable: "items",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_versions_agents_AgentId",
                schema: "agent",
                table: "versions",
                column: "AgentId",
                principalSchema: "agent",
                principalTable: "agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_definition_versions_definitions_DefinitionId",
                schema: "workflow",
                table: "definition_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_document_shares_documents_DocumentId",
                schema: "resource",
                table: "document_shares");

            migrationBuilder.DropForeignKey(
                name: "FK_document_versions_documents_DocumentId",
                schema: "resource",
                table: "document_versions");

            migrationBuilder.DropForeignKey(
                name: "FK_metadata_values_documents_DocumentId",
                schema: "resource",
                table: "metadata_values");

            migrationBuilder.DropForeignKey(
                name: "FK_relations_items_KnowledgeItemId",
                schema: "knowledge",
                table: "relations");

            migrationBuilder.DropForeignKey(
                name: "FK_versions_agents_AgentId",
                schema: "agent",
                table: "versions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_embeddings",
                schema: "search",
                table: "embeddings");

            migrationBuilder.DropIndex(
                name: "IX_embeddings_ChunkId",
                schema: "search",
                table: "embeddings");

            migrationBuilder.DropIndex(
                name: "IX_embeddings_SourceType_SourceId",
                schema: "search",
                table: "embeddings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_versions",
                schema: "agent",
                table: "versions");

            migrationBuilder.DropIndex(
                name: "IX_versions_AgentId_VersionNumber",
                schema: "agent",
                table: "versions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_templates",
                schema: "notification",
                table: "templates");

            migrationBuilder.DropIndex(
                name: "IX_templates_EventType_Channel_Language",
                schema: "notification",
                table: "templates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_subscriptions",
                schema: "webhook",
                table: "subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_saved_searches",
                schema: "search",
                table: "saved_searches");

            migrationBuilder.DropIndex(
                name: "IX_saved_searches_UserId",
                schema: "search",
                table: "saved_searches");

            migrationBuilder.DropPrimaryKey(
                name: "PK_resource_acls",
                schema: "acl",
                table: "resource_acls");

            migrationBuilder.DropIndex(
                name: "IX_resource_acls_ResourceId_PrincipalId",
                schema: "acl",
                table: "resource_acls");

            migrationBuilder.DropPrimaryKey(
                name: "PK_relations",
                schema: "knowledge",
                table: "relations");

            migrationBuilder.DropIndex(
                name: "IX_relations_SourceItemId",
                schema: "knowledge",
                table: "relations");

            migrationBuilder.DropIndex(
                name: "IX_relations_TargetItemId",
                schema: "knowledge",
                table: "relations");

            migrationBuilder.DropPrimaryKey(
                name: "PK_prompt_templates",
                schema: "agent",
                table: "prompt_templates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_packs",
                schema: "knowledge",
                table: "packs");

            migrationBuilder.DropPrimaryKey(
                name: "PK_metadata_values",
                schema: "resource",
                table: "metadata_values");

            migrationBuilder.DropIndex(
                name: "IX_metadata_values_ResourceId_TemplateId",
                schema: "resource",
                table: "metadata_values");

            migrationBuilder.DropPrimaryKey(
                name: "PK_metadata_templates",
                schema: "resource",
                table: "metadata_templates");

            migrationBuilder.DropPrimaryKey(
                name: "PK_memories",
                schema: "agent",
                table: "memories");

            migrationBuilder.DropIndex(
                name: "IX_memories_AgentId_UserId_Type_Key",
                schema: "agent",
                table: "memories");

            migrationBuilder.DropPrimaryKey(
                name: "PK_legal_holds",
                schema: "resource",
                table: "legal_holds");

            migrationBuilder.DropIndex(
                name: "IX_legal_holds_DocumentId_Status",
                schema: "resource",
                table: "legal_holds");

            migrationBuilder.DropPrimaryKey(
                name: "PK_document_versions",
                schema: "resource",
                table: "document_versions");

            migrationBuilder.DropIndex(
                name: "IX_document_versions_DocumentId_VersionNumber",
                schema: "resource",
                table: "document_versions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_document_shares",
                schema: "resource",
                table: "document_shares");

            migrationBuilder.DropIndex(
                name: "IX_document_shares_DocumentId_TargetId",
                schema: "resource",
                table: "document_shares");

            migrationBuilder.DropPrimaryKey(
                name: "PK_definition_versions",
                schema: "workflow",
                table: "definition_versions");

            migrationBuilder.DropIndex(
                name: "IX_definition_versions_DefinitionId_VersionNumber",
                schema: "workflow",
                table: "definition_versions");

            migrationBuilder.RenameTable(
                name: "embeddings",
                schema: "search",
                newName: "Embeddings");

            migrationBuilder.RenameTable(
                name: "versions",
                schema: "agent",
                newName: "AgentVersions");

            migrationBuilder.RenameTable(
                name: "templates",
                schema: "notification",
                newName: "NotificationTemplates");

            migrationBuilder.RenameTable(
                name: "subscriptions",
                schema: "webhook",
                newName: "WebhookSubscriptions");

            migrationBuilder.RenameTable(
                name: "saved_searches",
                schema: "search",
                newName: "SavedSearches");

            migrationBuilder.RenameTable(
                name: "resource_acls",
                schema: "acl",
                newName: "ResourceAcls");

            migrationBuilder.RenameTable(
                name: "relations",
                schema: "knowledge",
                newName: "KnowledgeRelations");

            migrationBuilder.RenameTable(
                name: "prompt_templates",
                schema: "agent",
                newName: "PromptTemplates");

            migrationBuilder.RenameTable(
                name: "packs",
                schema: "knowledge",
                newName: "KnowledgePacks");

            migrationBuilder.RenameTable(
                name: "metadata_values",
                schema: "resource",
                newName: "MetadataValues");

            migrationBuilder.RenameTable(
                name: "metadata_templates",
                schema: "resource",
                newName: "MetadataTemplates");

            migrationBuilder.RenameTable(
                name: "memories",
                schema: "agent",
                newName: "AgentMemories");

            migrationBuilder.RenameTable(
                name: "legal_holds",
                schema: "resource",
                newName: "LegalHolds");

            migrationBuilder.RenameTable(
                name: "document_versions",
                schema: "resource",
                newName: "DocumentVersions");

            migrationBuilder.RenameTable(
                name: "document_shares",
                schema: "resource",
                newName: "DocumentShares");

            migrationBuilder.RenameTable(
                name: "definition_versions",
                schema: "workflow",
                newName: "WorkflowDefinitionVersions");

            migrationBuilder.RenameIndex(
                name: "IX_relations_KnowledgeItemId",
                table: "KnowledgeRelations",
                newName: "IX_KnowledgeRelations_KnowledgeItemId");

            migrationBuilder.RenameIndex(
                name: "IX_metadata_values_DocumentId",
                table: "MetadataValues",
                newName: "IX_MetadataValues_DocumentId");

            migrationBuilder.AlterColumn<int>(
                name: "Channel",
                table: "NotificationTemplates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<int>(
                name: "SearchType",
                table: "SavedSearches",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SavedSearches",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<int>(
                name: "PrincipalType",
                table: "ResourceAcls",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<int>(
                name: "Effect",
                table: "ResourceAcls",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<int>(
                name: "Source",
                table: "KnowledgeRelations",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "RelationType",
                table: "KnowledgeRelations",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(60)",
                oldMaxLength: 60);

            migrationBuilder.AlterColumn<int>(
                name: "Role",
                table: "PromptTemplates",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "PromptTemplates",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "KnowledgePacks",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "KnowledgePacks",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "MetadataTemplates",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<int>(
                name: "Type",
                table: "AgentMemories",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Key",
                table: "AgentMemories",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "LegalHolds",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<int>(
                name: "Status",
                table: "DocumentVersions",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<int>(
                name: "TargetType",
                table: "DocumentShares",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<int>(
                name: "Permission",
                table: "DocumentShares",
                type: "integer",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Embeddings",
                table: "Embeddings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AgentVersions",
                table: "AgentVersions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NotificationTemplates",
                table: "NotificationTemplates",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WebhookSubscriptions",
                table: "WebhookSubscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SavedSearches",
                table: "SavedSearches",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ResourceAcls",
                table: "ResourceAcls",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_KnowledgeRelations",
                table: "KnowledgeRelations",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PromptTemplates",
                table: "PromptTemplates",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_KnowledgePacks",
                table: "KnowledgePacks",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MetadataValues",
                table: "MetadataValues",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_MetadataTemplates",
                table: "MetadataTemplates",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_AgentMemories",
                table: "AgentMemories",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_LegalHolds",
                table: "LegalHolds",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DocumentVersions",
                table: "DocumentVersions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_DocumentShares",
                table: "DocumentShares",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_WorkflowDefinitionVersions",
                table: "WorkflowDefinitionVersions",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_AgentVersions_AgentId",
                table: "AgentVersions",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId",
                table: "DocumentVersions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentShares_DocumentId",
                table: "DocumentShares",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDefinitionVersions_DefinitionId",
                table: "WorkflowDefinitionVersions",
                column: "DefinitionId");

            migrationBuilder.AddForeignKey(
                name: "FK_AgentVersions_agents_AgentId",
                table: "AgentVersions",
                column: "AgentId",
                principalSchema: "agent",
                principalTable: "agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentShares_documents_DocumentId",
                table: "DocumentShares",
                column: "DocumentId",
                principalSchema: "resource",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DocumentVersions_documents_DocumentId",
                table: "DocumentVersions",
                column: "DocumentId",
                principalSchema: "resource",
                principalTable: "documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeRelations_items_KnowledgeItemId",
                table: "KnowledgeRelations",
                column: "KnowledgeItemId",
                principalSchema: "knowledge",
                principalTable: "items",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_MetadataValues_documents_DocumentId",
                table: "MetadataValues",
                column: "DocumentId",
                principalSchema: "resource",
                principalTable: "documents",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkflowDefinitionVersions_definitions_DefinitionId",
                table: "WorkflowDefinitionVersions",
                column: "DefinitionId",
                principalSchema: "workflow",
                principalTable: "definitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
