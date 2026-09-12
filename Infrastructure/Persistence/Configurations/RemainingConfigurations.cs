using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Domain.Connector;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Domain.Search;
using EAIOS.Api.Domain.Webhook;
using EAIOS.Api.Domain.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EAIOS.Api.Infrastructure.Persistence.Configurations;

// ═══════════════════════════════════════════════════════════════════════════════
// LES DIX-SEPT ENTITÉS QUI N'AVAIENT PAS DE CONFIGURATION
//
// Faute de mapping explicite, EF les avait placées dans `public`, en PascalCase
// pluralisé — `public.DocumentVersions`, `public.LegalHolds` — alors que le
// domaine documente `resource.document_versions` et `resource.legal_holds`.
//
// L'écart n'était pas cosmétique : le découpage par schéma est la lisibilité de
// ce modèle, et une table hors de son schéma échappe aux conventions de
// nommage, aux droits accordés par schéma, et à la lecture qu'un DBA en fait.
//
// Deux écarts assumés par rapport aux commentaires du domaine :
//   • `ResourceAcl` va dans `acl` et non `access`, pour rejoindre
//     `acl.permissions`, `acl.policies`, `acl.roles`, `acl.user_roles` ;
//   • `WebhookSubscription` n'a pas de destination documentée : elle prend
//     `webhook`, sur le modèle « un schéma par dossier du domaine ».
// ═══════════════════════════════════════════════════════════════════════════════

// ── Agent ─────────────────────────────────────────────────────────────────────

public sealed class AgentVersionConfiguration : IEntityTypeConfiguration<AgentVersion>
{
    public void Configure(EntityTypeBuilder<AgentVersion> b)
    {
        b.ToTable("versions", "agent");
        b.HasKey(v => v.Id); b.Property(v => v.Id).ValueGeneratedNever();
        // Une version est un instantané immuable : le couple agent/numéro est
        // unique, et republier à l'identique doit échouer en base, pas seulement
        // dans le service.
        b.HasIndex(v => new { v.AgentId, v.VersionNumber }).IsUnique();
    }
}

public sealed class AgentConversationConfiguration : IEntityTypeConfiguration<AgentConversation>
{
    public void Configure(EntityTypeBuilder<AgentConversation> b)
    {
        b.ToTable("conversations", "agent");
        b.HasKey(c => c.Id); b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Title).HasMaxLength(200).IsRequired();
        b.Property(c => c.LastStatus).HasConversion<string>().HasMaxLength(30);
        // La liste d'historique lit « mes conversations, les plus récentes d'abord ».
        b.HasIndex(c => new { c.UserId, c.LastActivityAt });
        b.HasIndex(c => new { c.AgentId, c.LastActivityAt });
    }
}

public sealed class AgentMemoryConfiguration : IEntityTypeConfiguration<AgentMemory>
{
    public void Configure(EntityTypeBuilder<AgentMemory> b)
    {
        b.ToTable("memories", "agent");
        b.HasKey(m => m.Id); b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(m => m.Key).HasMaxLength(200).IsRequired();
        b.HasIndex(m => new { m.AgentId, m.UserId, m.Type, m.Key });
    }
}

public sealed class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> b)
    {
        b.ToTable("prompt_templates", "agent");
        b.HasKey(p => p.Id); b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Role).HasConversion<string>().HasMaxLength(30);
    }
}

// ── Resource ──────────────────────────────────────────────────────────────────

public sealed class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> b)
    {
        b.ToTable("document_versions", "resource");
        b.HasKey(v => v.Id); b.Property(v => v.Id).ValueGeneratedNever();
        b.Property(v => v.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(v => new { v.DocumentId, v.VersionNumber });
    }
}

public sealed class DocumentShareConfiguration : IEntityTypeConfiguration<DocumentShare>
{
    public void Configure(EntityTypeBuilder<DocumentShare> b)
    {
        b.ToTable("document_shares", "resource");
        b.HasKey(s => s.Id); b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.TargetType).HasConversion<string>().HasMaxLength(30);
        b.Property(s => s.Permission).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(s => new { s.DocumentId, s.TargetId });
    }
}

public sealed class LegalHoldConfiguration : IEntityTypeConfiguration<LegalHold>
{
    public void Configure(EntityTypeBuilder<LegalHold> b)
    {
        b.ToTable("legal_holds", "resource");
        b.HasKey(h => h.Id); b.Property(h => h.Id).ValueGeneratedNever();
        b.Property(h => h.Status).HasConversion<string>().HasMaxLength(30);
        // Une conservation légale se cherche par ressource et par état : c'est
        // ce qui décide si une suppression est légalement possible.
        b.HasIndex(h => new { h.DocumentId, h.Status });
    }
}

public sealed class MetadataTemplateConfiguration : IEntityTypeConfiguration<MetadataTemplate>
{
    public void Configure(EntityTypeBuilder<MetadataTemplate> b)
    {
        b.ToTable("metadata_templates", "resource");
        b.HasKey(t => t.Id); b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Name).HasMaxLength(200).IsRequired();
    }
}

public sealed class MetadataValueConfiguration : IEntityTypeConfiguration<MetadataValue>
{
    public void Configure(EntityTypeBuilder<MetadataValue> b)
    {
        b.ToTable("metadata_values", "resource");
        b.HasKey(v => v.Id); b.Property(v => v.Id).ValueGeneratedNever();
        b.HasIndex(v => new { v.ResourceId, v.TemplateId });
    }
}

// ── Knowledge ─────────────────────────────────────────────────────────────────

public sealed class KnowledgePackConfiguration : IEntityTypeConfiguration<KnowledgePack>
{
    public void Configure(EntityTypeBuilder<KnowledgePack> b)
    {
        b.ToTable("packs", "knowledge");
        b.HasKey(p => p.Id); b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
    }
}

public sealed class KnowledgeRelationConfiguration : IEntityTypeConfiguration<KnowledgeRelation>
{
    public void Configure(EntityTypeBuilder<KnowledgeRelation> b)
    {
        b.ToTable("relations", "knowledge");
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.RelationType).HasMaxLength(60).IsRequired();
        b.Property(r => r.Source).HasConversion<string>().HasMaxLength(30);
        // Le graphe se parcourt dans les deux sens.
        b.HasIndex(r => r.SourceItemId);
        b.HasIndex(r => r.TargetItemId);
    }
}

// ── Search ────────────────────────────────────────────────────────────────────

public sealed class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    public void Configure(EntityTypeBuilder<Embedding> b)
    {
        b.ToTable("embeddings", "search");
        b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
        b.HasIndex(e => new { e.SourceType, e.SourceId });
        b.HasIndex(e => e.ChunkId);
    }
}

public sealed class SavedSearchConfiguration : IEntityTypeConfiguration<SavedSearch>
{
    public void Configure(EntityTypeBuilder<SavedSearch> b)
    {
        b.ToTable("saved_searches", "search");
        b.HasKey(s => s.Id); b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Name).HasMaxLength(200).IsRequired();
        b.Property(s => s.SearchType).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(s => s.UserId);
    }
}

// ── Notification ──────────────────────────────────────────────────────────────

public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> b)
    {
        b.ToTable("templates", "notification");
        b.HasKey(t => t.Id); b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Channel).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(t => new { t.EventType, t.Channel, t.Language });
    }
}

// ── Access control ────────────────────────────────────────────────────────────

public sealed class ResourceAclConfiguration : IEntityTypeConfiguration<ResourceAcl>
{
    public void Configure(EntityTypeBuilder<ResourceAcl> b)
    {
        b.ToTable("resource_acls", "acl");
        b.HasKey(a => a.Id); b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.PrincipalType).HasConversion<string>().HasMaxLength(30);
        b.Property(a => a.Effect).HasConversion<string>().HasMaxLength(30);
        // Répondre à « cette personne a-t-elle accès à cette ressource » est la
        // requête la plus chaude du contrôle d'accès.
        b.HasIndex(a => new { a.ResourceId, a.PrincipalId });
    }
}

// ── Workflow ──────────────────────────────────────────────────────────────────

public sealed class WorkflowDefinitionVersionConfiguration : IEntityTypeConfiguration<WorkflowDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinitionVersion> b)
    {
        b.ToTable("definition_versions", "workflow");
        b.HasKey(v => v.Id); b.Property(v => v.Id).ValueGeneratedNever();
        b.HasIndex(v => new { v.DefinitionId, v.VersionNumber }).IsUnique();
    }
}

// ── Webhook ───────────────────────────────────────────────────────────────────

public sealed class WebhookSubscriptionConfiguration : IEntityTypeConfiguration<WebhookSubscription>
{
    public void Configure(EntityTypeBuilder<WebhookSubscription> b)
    {
        b.ToTable("subscriptions", "webhook");
        b.HasKey(s => s.Id); b.Property(s => s.Id).ValueGeneratedNever();
    }
}

// ── Connector ─────────────────────────────────────────────────────────────────

public sealed class ConnectorDefinitionConfiguration : IEntityTypeConfiguration<ConnectorDefinition>
{
    public void Configure(EntityTypeBuilder<ConnectorDefinition> b)
    {
        // Le catalogue de connecteurs est **global**, pas propre à un tenant, et
        // `PlatformDbContext` le mappait déjà ici. Faute de configuration de ce
        // côté, `EaiosDbContext` en lisait une seconde copie dans
        // `public.ConnectorDefinitions` : deux tables pour un même catalogue,
        // que rien ne synchronisait. Les deux contextes pointent désormais sur
        // la même.
        b.ToTable("connector_definitions", "platform");
        b.HasKey(d => d.Id); b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.Name).HasMaxLength(200).IsRequired();
        b.Property(d => d.Category).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.AuthType).HasConversion<string>().HasMaxLength(30);
    }
}

// ── Évaluations ───────────────────────────────────────────────────────────────

public sealed class AgentTestCaseConfiguration : IEntityTypeConfiguration<AgentTestCase>
{
    public void Configure(EntityTypeBuilder<AgentTestCase> b)
    {
        b.ToTable("test_cases", "agent");
        b.HasKey(c => c.Id); b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.HasIndex(c => new { c.AgentId, c.IsEnabled });
    }
}

public sealed class AgentTestRunConfiguration : IEntityTypeConfiguration<AgentTestRun>
{
    public void Configure(EntityTypeBuilder<AgentTestRun> b)
    {
        b.ToTable("test_runs", "agent");
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(r => new { r.AgentId, r.StartedAt });
        b.HasMany(r => r.Results).WithOne().HasForeignKey(x => x.RunId);
        // Calculees a partir des compteurs : les persister ouvrirait la porte a
        // une incoherence entre le taux affiche et les cas reellement comptes.
        b.Ignore(r => r.FailureRate);
        b.Ignore(r => r.AverageCostUsd);
    }
}

public sealed class AgentTestResultConfiguration : IEntityTypeConfiguration<AgentTestResult>
{
    public void Configure(EntityTypeBuilder<AgentTestResult> b)
    {
        b.ToTable("test_results", "agent");
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.HasIndex(r => new { r.RunId, r.Passed });
    }
}
