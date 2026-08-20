# EAIOS API — Guide Simple des Migrations & DbContexts

Ce document explique simplement la structure de la base de données PostgreSQL **`eaios`**, la répartition des **13 schémas PostgreSQL** entre les 2 `DbContext`, et fournit un pense-bête des commandes de migration.

---

## 🏛️ Structure Réelle de la Base de Données PostgreSQL (`eaios`)

Toutes les données sont regroupées dans la même base de données **`eaios`**, découpées de manière propre en **13 Schémas PostgreSQL** :

```
🛢️ eaios (Base PostgreSQL unique)
 └── 🔀 Schemas (13)
      │
      ├── 🏛️ PlatformDbContext (Données d'administration & plateforme)
      │    ├── 🔷 platform     → Organizations (Tenants), FeatureFlags & Overrides
      │    └── 🔷 audit        → AuditEvents (Journaux d'audit de sécurité)
      │
      ├── 🚀 EaiosDbContext (Données métier & applicatives multi-tenant)
      │    ├── 🔶 identity     → Users, Sessions, MfaCredentials, ApiKeys, Invitations
      │    ├── 🔶 organization → Workspaces, Departments, Memberships
      │    ├── 🔶 acl          → Roles, Permissions, UserRoles, Policies, ResourceAcls
      │    ├── 🔶 resource     → Documents, Folders, DocumentVersions, Metadata, LegalHolds
      │    ├── 🔶 knowledge    → KnowledgeItems, KnowledgeChunks, KnowledgePacks, Vector Index
      │    ├── 🔶 agent        → Agents, AgentExecutions, AgentMemories, PromptTemplates
      │    ├── 🔶 workflow     → WorkflowDefinitions, WorkflowInstances, WorkflowTasks
      │    ├── 🔶 connector    → ConnectorDefinitions, ConnectorInstances, SyncJobs
      │    ├── 🔶 analytics    → AnalyticsEvents & Metrics
      │    └── 🔶 notification → Notifications, NotificationTemplates
      │
      └── ⚙️ public            → Schéma par défaut PostgreSQL (tables d'historique EF Core)
```

---

## 🛠️ Pense-Bête des Commandes de Migration

### 1️⃣ Pour modifier le schéma **Plateforme** (`platform`, `audit`)
```powershell
# Créer la migration
dotnet ef migrations add NomDeLaMigration --context PlatformDbContext

# Appliquer la migration sur PostgreSQL
dotnet ef database update --context PlatformDbContext
```

### 2️⃣ Pour modifier le schéma **Applicatif / Métier** (`identity`, `resource`, `agent`, `workflow`, etc.)
```powershell
# Créer la migration
dotnet ef migrations add NomDeLaMigration --context EaiosDbContext

# Appliquer la migration sur PostgreSQL
dotnet ef database update --context EaiosDbContext
```

---

## ⚙️ Basculer entre le Mode In-Memory et PostgreSQL

Dans le fichier `appsettings.Development.json` :

- **Mode In-Memory (Dev rapide sans PostgreSQL)** :
  ```json
  "UseInMemoryDatabase": true
  ```
  *(Tout tourne en mémoire vive au lancement de `dotnet run`, aucune migration requise)*

- **Mode PostgreSQL (Production / Test Réel)** :
  ```json
  "UseInMemoryDatabase": false
  ```
  *(Utilise les 13 schémas PostgreSQL décrits ci-dessus)*
