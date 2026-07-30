using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Repositories.AccessControl;
using EAIOS.Api.Infrastructure.Persistence.Seeds;

namespace EAIOS.Api.Infrastructure.Persistence.Seeds;

/// <summary>
/// Seeder de toutes les permissions système et rôles par défaut.
/// Idempotent : ne recrée pas ce qui existe déjà.
/// </summary>
public static class SystemPermissionsSeed
{
    public static async Task SeedAsync(EaiosDbContext db, Guid organizationId, CancellationToken ct = default)
    {
        await SeedPermissionsAsync(db, organizationId, ct);
        await SeedRolesAsync(db, organizationId, ct);
    }

    private static async Task SeedPermissionsAsync(EaiosDbContext db, Guid organizationId, CancellationToken ct)
    {
        var existing = db.Permissions.Select(p => p.Code).ToHashSet();

        var toAdd = AllPermissions
            .Where(p => !existing.Contains(p.Code))
            .Select(p => Permission.Create(organizationId, p.Code, p.Name, p.Module, isSystem: true, description: p.Description))
            .ToList();

        if (toAdd.Count > 0)
        {
            await db.Permissions.AddRangeAsync(toAdd, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedRolesAsync(EaiosDbContext db, Guid organizationId, CancellationToken ct)
    {
        var existing = db.Roles.Select(r => r.Name).ToHashSet();

        var rolesToSeed = new[]
        {
            (SystemRoles.OrgAdmin, "Administrateur organisation — accès complet",
                AllPermissions.Select(p => p.Code).ToArray()),

            (SystemRoles.OrgMember, "Membre standard de l'organisation",
                new[]
                {
                    Permissions.UserRead, Permissions.OrgRead,
                    Permissions.WorkspaceCreate, Permissions.WorkspaceRead, Permissions.WorkspaceUpdate,
                    Permissions.DeptRead,
                    Permissions.ResourceCreate, Permissions.ResourceRead, Permissions.ResourceUpdate,
                    Permissions.ResourceShare, Permissions.ResourceDownload,
                    Permissions.KnowledgeItemCreate, Permissions.KnowledgeItemRead, Permissions.KnowledgeItemUpdate,
                    Permissions.KnowledgeGraphRead,
                    Permissions.AgentRead, Permissions.AgentExecute,
                    Permissions.WorkflowRead, Permissions.WorkflowExecute,
                    Permissions.SearchBasicExecute, Permissions.SearchAdvancedExecute, Permissions.SearchSave
                }),

            (SystemRoles.OrgGuest, "Invité en lecture seule",
                new[]
                {
                    Permissions.OrgRead, Permissions.WorkspaceRead,
                    Permissions.ResourceRead,
                    Permissions.KnowledgeItemRead,
                    Permissions.SearchBasicExecute
                })
        };

        foreach (var (name, description, permissions) in rolesToSeed)
        {
            if (existing.Contains(name)) continue;
            var role = Role.Create(organizationId, name, RoleScope.Organization, isSystem: true, description: description);
            role.SetPermissions(permissions);
            db.Roles.Add(role);
        }

        await db.SaveChangesAsync(ct);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Catalogue exhaustif des permissions
    // ═══════════════════════════════════════════════════════════════════════════

    public static readonly (string Code, string Name, string Module, string Description)[] AllPermissions =
    [
        // Identity
        (Permissions.UserRead,    "Voir les utilisateurs",    "Identity", "Consulter les profils utilisateurs"),
        (Permissions.UserCreate,  "Inviter des utilisateurs", "Identity", "Créer et inviter de nouveaux utilisateurs"),
        (Permissions.UserUpdate,  "Modifier les utilisateurs","Identity", "Modifier le profil et les paramètres"),
        (Permissions.UserDelete,  "Supprimer des utilisateurs","Identity","Désactiver ou supprimer des comptes"),
        (Permissions.UserSuspend, "Suspendre des utilisateurs","Identity","Suspendre ou débloquer des comptes"),

        // Organization
        (Permissions.OrgRead,   "Voir l'organisation",           "Organization", "Consulter les paramètres de l'organisation"),
        (Permissions.OrgUpdate, "Modifier l'organisation",        "Organization", "Modifier la marque et les paramètres"),
        (Permissions.OrgManage, "Gérer l'organisation",           "Organization", "Contrôle administratif complet"),

        // Workspaces
        (Permissions.WorkspaceCreate, "Créer un espace de travail",   "Organization", "Créer de nouveaux espaces"),
        (Permissions.WorkspaceRead,   "Voir les espaces de travail",  "Organization", "Consulter le contenu des espaces"),
        (Permissions.WorkspaceUpdate, "Modifier un espace de travail","Organization", "Modifier les propriétés"),
        (Permissions.WorkspaceDelete, "Supprimer un espace",          "Organization", "Archiver ou supprimer"),
        (Permissions.WorkspaceManage, "Gérer les espaces",            "Organization", "Gérer les membres et les permissions"),

        // Departments
        (Permissions.DeptCreate, "Créer un département",   "Organization", "Créer de nouveaux départements"),
        (Permissions.DeptRead,   "Voir les départements",  "Organization", "Consulter la hiérarchie"),
        (Permissions.DeptUpdate, "Modifier un département","Organization", "Modifier les paramètres"),
        (Permissions.DeptDelete, "Supprimer un département","Organization","Supprimer des départements"),

        // Access Control
        (Permissions.RoleCreate,  "Créer des rôles",           "AccessControl", "Créer des rôles personnalisés"),
        (Permissions.RoleRead,    "Voir les rôles",             "AccessControl", "Consulter les rôles et matrices"),
        (Permissions.RoleUpdate,  "Modifier des rôles",         "AccessControl", "Mettre à jour les permissions"),
        (Permissions.RoleDelete,  "Supprimer des rôles",        "AccessControl", "Supprimer les rôles personnalisés"),
        (Permissions.RoleAssign,  "Assigner des rôles",         "AccessControl", "Assigner des rôles aux utilisateurs"),
        (Permissions.PolicyCreate,"Créer des politiques ABAC",  "AccessControl", "Créer des politiques basées sur attributs"),
        (Permissions.PolicyRead,  "Voir les politiques",        "AccessControl", "Consulter les politiques d'accès"),
        (Permissions.PolicyUpdate,"Modifier des politiques",    "AccessControl", "Modifier les politiques d'accès"),

        // Resource
        (Permissions.ResourceCreate,   "Créer des ressources",      "Resource", "Uploader des documents"),
        (Permissions.ResourceRead,     "Voir des ressources",       "Resource", "Consulter et prévisualiser"),
        (Permissions.ResourceUpdate,   "Modifier des ressources",   "Resource", "Modifier les métadonnées"),
        (Permissions.ResourceDelete,   "Supprimer des ressources",  "Resource", "Envoyer à la corbeille"),
        (Permissions.ResourceShare,    "Partager des ressources",   "Resource", "Créer des liens de partage"),
        (Permissions.ResourceDownload, "Télécharger des ressources","Resource", "Télécharger les fichiers"),
        (Permissions.ResourceManage,   "Gérer les ressources",      "Resource", "Contrôle complet"),
        (Permissions.LegalHoldManage,  "Gérer les holds légaux",    "Resource", "Appliquer et lever les holds"),

        // Knowledge
        (Permissions.KnowledgeItemCreate,   "Créer des éléments de connaissance","Knowledge","Créer articles et FAQ"),
        (Permissions.KnowledgeItemRead,     "Lire la base de connaissance",      "Knowledge","Lire les éléments"),
        (Permissions.KnowledgeItemUpdate,   "Modifier la connaissance",          "Knowledge","Modifier le contenu"),
        (Permissions.KnowledgeItemDelete,   "Supprimer la connaissance",         "Knowledge","Supprimer des éléments"),
        (Permissions.KnowledgeItemPublish,  "Publier la connaissance",           "Knowledge","Publier les éléments"),
        (Permissions.KnowledgeItemValidate, "Valider la connaissance",           "Knowledge","Valider la précision"),
        (Permissions.KnowledgePackCreate,   "Créer des packs",                   "Knowledge","Créer des bundles"),
        (Permissions.KnowledgePackExport,   "Exporter des packs",                "Knowledge","Exporter des packs"),
        (Permissions.KnowledgeGraphRead,    "Lire le graphe de connaissance",    "Knowledge","Requêter le graphe"),
        (Permissions.KnowledgeGraphManage,  "Gérer le graphe",                   "Knowledge","Gérer entités et relations"),

        // Agent
        (Permissions.AgentCreate,  "Créer des agents",     "Agent", "Configurer de nouveaux agents IA"),
        (Permissions.AgentRead,    "Voir les agents",      "Agent", "Consulter les configurations"),
        (Permissions.AgentUpdate,  "Modifier des agents",  "Agent", "Mettre à jour les prompts"),
        (Permissions.AgentDelete,  "Supprimer des agents", "Agent", "Supprimer les configurations"),
        (Permissions.AgentPublish, "Publier des agents",   "Agent", "Publier pour usage interne"),
        (Permissions.AgentExecute, "Exécuter des agents",  "Agent", "Lancer des agents IA"),
        (Permissions.AgentMonitor, "Surveiller les agents","Agent", "Voir les logs et métriques"),

        // Workflow
        (Permissions.WorkflowCreate,  "Créer des workflows",  "Workflow","Concevoir des définitions"),
        (Permissions.WorkflowRead,    "Voir les workflows",   "Workflow","Consulter les définitions"),
        (Permissions.WorkflowUpdate,  "Modifier des workflows","Workflow","Modifier les définitions"),
        (Permissions.WorkflowDelete,  "Supprimer des workflows","Workflow","Supprimer les définitions"),
        (Permissions.WorkflowExecute, "Exécuter des workflows","Workflow","Lancer des instances"),
        (Permissions.WorkflowManage,  "Gérer les workflows",  "Workflow","Gérer les tâches et SLA"),

        // Search
        (Permissions.SearchBasicExecute,    "Recherche simple",          "Search","Recherche par mots-clés"),
        (Permissions.SearchAdvancedExecute, "Recherche avancée hybride", "Search","Recherche sémantique"),
        (Permissions.SearchSave,            "Sauvegarder les recherches","Search","Sauvegarder et créer des alertes"),
        (Permissions.SearchAnalyticsRead,   "Voir les analytics de recherche","Search","Consulter les stats"),

        // Analytics
        (Permissions.AnalyticsRead, "Voir les analytics", "Analytics", "Consulter les dashboards"),

        // Admin
        (Permissions.AdminUsers,    "Administrer les utilisateurs", "Admin", "Panneau admin utilisateurs"),
        (Permissions.AdminOrg,      "Administrer l'organisation",   "Admin", "Administration organisation"),
        (Permissions.AdminBilling,  "Administrer la facturation",   "Admin", "Gestion abonnements"),
        (Permissions.AdminPlatform, "Super-admin plateforme",       "Admin", "Fonctions superadmin"),
        (Permissions.AdminAudit,    "Voir les logs d'audit",        "Admin", "Consulter les traces de sécurité"),
    ];
}
