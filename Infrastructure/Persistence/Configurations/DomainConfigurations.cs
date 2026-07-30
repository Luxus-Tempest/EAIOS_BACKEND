using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Organization;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Domain.Knowledge;
using EAIOS.Api.Domain.Agent;
using EAIOS.Api.Domain.Workflow;
using EAIOS.Api.Domain.Search;
using EAIOS.Api.Domain.Analytics;
using EAIOS.Api.Domain.Notification;
using EAIOS.Api.Domain.Connector;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EAIOS.Api.Infrastructure.Persistence.Configurations;

// ── Organization ──────────────────────────────────────────────────────────────

public sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> b)
    {
        b.ToTable("workspaces", "organization");
        b.HasKey(w => w.Id); b.Property(w => w.Id).ValueGeneratedNever();
        b.Property(w => w.Name).HasMaxLength(200).IsRequired();
        b.Property(w => w.Slug).HasMaxLength(100).IsRequired();
        b.Property(w => w.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(w => w.Visibility).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(w => new { w.OrganizationId, w.Slug }).IsUnique();
    }
}

public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("departments", "organization");
        b.HasKey(d => d.Id); b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.Name).HasMaxLength(200).IsRequired();
        b.Property(d => d.Code).HasMaxLength(50);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(d => new { d.OrganizationId, d.Code });
    }
}

public sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> b)
    {
        b.ToTable("memberships", "organization");
        b.HasKey(m => m.Id); b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.Role).HasConversion<string>().HasMaxLength(30);
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(m => new { m.UserId, m.WorkspaceId, m.DepartmentId });
    }
}

// ── Access Control ────────────────────────────────────────────────────────────

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("roles", "acl");
        b.HasKey(r => r.Id); b.Property(r => r.Id).ValueGeneratedNever();
        b.HasIndex(r => new { r.OrganizationId, r.Name }).IsUnique();
        b.Property(r => r.Name).HasMaxLength(100).IsRequired();
        b.Property(r => r.Scope).HasConversion<string>().HasMaxLength(30);
        b.Property(r => r.Color).HasMaxLength(20);
    }
}

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.ToTable("permissions", "acl");
        b.HasKey(p => p.Id); b.Property(p => p.Id).ValueGeneratedNever();
        b.HasIndex(p => new { p.OrganizationId, p.Code }).IsUnique();
        b.Property(p => p.Code).HasMaxLength(200).IsRequired();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Module).HasMaxLength(100).IsRequired();
    }
}

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.ToTable("user_roles", "acl");
        b.HasKey(ur => ur.Id); b.Property(ur => ur.Id).ValueGeneratedNever();
        b.HasIndex(ur => new { ur.UserId, ur.RoleId, ur.WorkspaceId });
    }
}

public sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> b)
    {
        b.ToTable("policies", "acl");
        b.HasKey(p => p.Id); b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(p => p.Effect).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.PrincipalType).HasConversion<string>().HasMaxLength(30);
    }
}

// ── Resource ──────────────────────────────────────────────────────────────────

public sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> b)
    {
        b.ToTable("documents", "resource");
        b.HasKey(d => d.Id); b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.Title).HasMaxLength(500).IsRequired();
        b.Property(d => d.ResourceType).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.Classification).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.IndexingStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(d => d.MimeType).HasMaxLength(200);
        b.Property(d => d.Extension).HasMaxLength(20);
        b.Property(d => d.Language).HasMaxLength(10);
        b.HasIndex(d => new { d.OrganizationId, d.FolderId });
        b.HasIndex(d => new { d.OrganizationId, d.Status });
        b.HasMany(d => d.Versions).WithOne().HasForeignKey(v => v.DocumentId);
        b.HasMany(d => d.Shares).WithOne().HasForeignKey(s => s.DocumentId);
        b.HasMany(d => d.LegalHolds).WithOne().HasForeignKey(h => h.DocumentId);
    }
}

public sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> b)
    {
        b.ToTable("folders", "resource");
        b.HasKey(f => f.Id); b.Property(f => f.Id).ValueGeneratedNever();
        b.Property(f => f.Name).HasMaxLength(300).IsRequired();
        b.Property(f => f.Path).HasMaxLength(4000).IsRequired();
        b.Property(f => f.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(f => new { f.OrganizationId, f.ParentId });
    }
}

// ── Knowledge ─────────────────────────────────────────────────────────────────

public sealed class KnowledgeItemConfiguration : IEntityTypeConfiguration<KnowledgeItem>
{
    public void Configure(EntityTypeBuilder<KnowledgeItem> b)
    {
        b.ToTable("items", "knowledge");
        b.HasKey(i => i.Id); b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.Title).HasMaxLength(500).IsRequired();
        b.Property(i => i.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(i => i.Source).HasConversion<string>().HasMaxLength(30);
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(i => i.Language).HasMaxLength(10).HasDefaultValue("fr");
        b.HasMany(i => i.Chunks).WithOne().HasForeignKey(c => c.ItemId);
    }
}

public sealed class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> b)
    {
        b.ToTable("chunks", "knowledge");
        b.HasKey(c => c.Id); b.Property(c => c.Id).ValueGeneratedNever();
        b.HasIndex(c => new { c.ItemId, c.ChunkIndex }).IsUnique();
        b.HasIndex(c => c.IsEmbedded);
    }
}

// ── Agent ─────────────────────────────────────────────────────────────────────

public sealed class AgentEntityConfiguration : IEntityTypeConfiguration<Domain.Agent.Agent>
{
    public void Configure(EntityTypeBuilder<Domain.Agent.Agent> b)
    {
        b.ToTable("agents", "agent");
        b.HasKey(a => a.Id); b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Name).HasMaxLength(200).IsRequired();
        b.Property(a => a.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(a => a.Visibility).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(a => new { a.OrganizationId, a.Status });
        b.HasMany(a => a.Versions).WithOne().HasForeignKey(v => v.AgentId);
        b.HasMany(a => a.Executions).WithOne().HasForeignKey(e => e.AgentId);
    }
}

public sealed class AgentExecutionConfiguration : IEntityTypeConfiguration<AgentExecution>
{
    public void Configure(EntityTypeBuilder<AgentExecution> b)
    {
        b.ToTable("executions", "agent");
        b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(e => new { e.AgentId, e.StartedAt });
        b.HasIndex(e => new { e.UserId, e.StartedAt });
    }
}

// ── Workflow ──────────────────────────────────────────────────────────────────

public sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> b)
    {
        b.ToTable("definitions", "workflow");
        b.HasKey(w => w.Id); b.Property(w => w.Id).ValueGeneratedNever();
        b.Property(w => w.Name).HasMaxLength(300).IsRequired();
        b.Property(w => w.Category).HasMaxLength(100).HasDefaultValue("General");
        b.Property(w => w.Status).HasConversion<string>().HasMaxLength(30);
        b.HasMany(w => w.DefinitionVersions).WithOne().HasForeignKey(v => v.DefinitionId);
    }
}

public sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> b)
    {
        b.ToTable("instances", "workflow");
        b.HasKey(i => i.Id); b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(i => i.TriggerType).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(i => new { i.DefinitionId, i.Status });
        b.HasMany(i => i.Tasks).WithOne().HasForeignKey(t => t.InstanceId);
    }
}

public sealed class WorkflowTaskConfiguration : IEntityTypeConfiguration<WorkflowTask>
{
    public void Configure(EntityTypeBuilder<WorkflowTask> b)
    {
        b.ToTable("tasks", "workflow");
        b.HasKey(t => t.Id); b.Property(t => t.Id).ValueGeneratedNever();
        b.Property(t => t.Title).HasMaxLength(300).IsRequired();
        b.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(t => t.AssigneeType).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(t => new { t.AssigneeId, t.Status });
        b.HasIndex(t => t.DueAt);
    }
}

// ── Analytics ─────────────────────────────────────────────────────────────────

public sealed class AnalyticsEventConfiguration : IEntityTypeConfiguration<AnalyticsEvent>
{
    public void Configure(EntityTypeBuilder<AnalyticsEvent> b)
    {
        b.ToTable("events", "analytics");
        b.HasKey(e => e.Id); b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.EventType).HasMaxLength(200).IsRequired();
        b.HasIndex(e => new { e.OrganizationId, e.OccurredAt });
        b.HasIndex(e => new { e.OrganizationId, e.EventType });
    }
}

// ── Notification ──────────────────────────────────────────────────────────────

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Domain.Notification.Notification>
{
    public void Configure(EntityTypeBuilder<Domain.Notification.Notification> b)
    {
        b.ToTable("notifications", "notification");
        b.HasKey(n => n.Id); b.Property(n => n.Id).ValueGeneratedNever();
        b.Property(n => n.Channel).HasConversion<string>().HasMaxLength(30);
        b.Property(n => n.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(n => n.Priority).HasConversion<string>().HasMaxLength(20);
        b.Property(n => n.Title).HasMaxLength(500).IsRequired();
        b.HasIndex(n => new { n.RecipientId, n.ReadAt });
    }
}

// ── Connector ─────────────────────────────────────────────────────────────────

public sealed class ConnectorInstanceConfiguration : IEntityTypeConfiguration<ConnectorInstance>
{
    public void Configure(EntityTypeBuilder<ConnectorInstance> b)
    {
        b.ToTable("instances", "connector");
        b.HasKey(c => c.Id); b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.Name).HasMaxLength(200).IsRequired();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(c => c.Health).HasConversion<string>().HasMaxLength(30);
        b.HasMany(c => c.SyncJobs).WithOne().HasForeignKey(j => j.ConnectorInstanceId);
    }
}

public sealed class SyncJobConfiguration : IEntityTypeConfiguration<SyncJob>
{
    public void Configure(EntityTypeBuilder<SyncJob> b)
    {
        b.ToTable("sync_jobs", "connector");
        b.HasKey(j => j.Id); b.Property(j => j.Id).ValueGeneratedNever();
        b.Property(j => j.Name).HasMaxLength(200).IsRequired();
        b.Property(j => j.Direction).HasConversion<string>().HasMaxLength(20);
        b.Property(j => j.Status).HasConversion<string>().HasMaxLength(30);
        b.HasIndex(j => new { j.ConnectorInstanceId, j.Status });
    }
}
