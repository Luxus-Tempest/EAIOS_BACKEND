# EAIOS API — Guide d'Utilisation & Prise en Main Complète

Bienvenue dans le guide d'utilisation d'**EAIOS API** (ASP.NET Core .NET 10, EF Core, PostgreSQL).  
Ce document vous guide pas à pas pour démarrer l'application, vous authentifier et exploiter l'ensemble des 12 modules d'API.

---

## 🚀 1. Démarrage Rapide & Accès

### 1.1 Lancer l'application
Dans votre terminal PowerShell dans le dossier `backend` :
```powershell
dotnet run
```

### 1.2 URLs Utiles

| Ressource | URL | Description |
| :--- | :--- | :--- |
| **Documentation Interactive Scalar** | `http://localhost:5257/scalar/v1` | Interface visuelle pour tester les endpoints |
| **Spécification OpenAPI JSON** | `http://localhost:5257/openapi/v1.json` | Contrat Swagger / OpenAPI v3 |
| **Health Check** | `http://localhost:5257/health` | Vérification de la disponibilité de l'API |

### 1.3 Compte Administrateur de Démo (Pré-ensemencé au démarrage)

Au démarrage en mode développement, l'application crée automatiquement une organisation et un utilisateur administrateur :

- **Email :** `admin@eaios.io`
- **Mot de passe :** `Admin@123456!`
- **Organisation ID :** `00000000-0000-0000-0000-000000000001`

---

## 🔑 2. Authentification & Sécurité (`/api/v1/auth`)

Toutes les requêtes sécurisées doivent inclure l'en-tête HTTP :
`Authorization: Bearer <votre_access_token>`

### 2.1 Connexion (Login)
**`POST /api/v1/auth/login`**
```json
{
  "email": "admin@eaios.io",
  "password": "Admin@123456!"
}
```
**Réponse :**
```json
{
  "data": {
    "accessToken": "eyJhbGciOi...",
    "refreshToken": "eak_rf_...",
    "expiresIn": 900,
    "user": {
      "id": "00000000-0000-0000-0000-000000000002",
      "email": "admin@eaios.io",
      "firstName": "System",
      "lastName": "Admin"
    }
  }
}
```

### 2.2 Obtenir le profil courant
**`GET /api/v1/auth/me`** (Avec le header `Authorization: Bearer <accessToken>`)

### 2.3 Rafraîchir le token (RefreshToken)
**`POST /api/v1/auth/refresh`**
```json
{
  "refreshToken": "eak_rf_..."
}
```

---

## 🏢 3. Organisation & Espaces de Travail (`/api/v1/workspaces`)

### 3.1 Lister les Espaces de Travail (Workspaces)
**`GET /api/v1/workspaces`**

### 3.2 Créer un Workspace
**`POST /api/v1/workspaces`**
```json
{
  "name": "Espace R&D IA",
  "description": "Espace dédié aux projets et agents d'Intelligence Artificielle",
  "type": "Custom",
  "color": "#3B82F6",
  "iconCode": "cpu"
}
```

### 3.3 Ajouter un membre à un Workspace
**`POST /api/v1/workspaces/{workspaceId}/members`**
```json
{
  "userId": "00000000-0000-0000-0000-000000000002",
  "role": "Member"
}
```

---

## 📄 4. Gestion Documentaire & Fichiers (`/api/v1/resources`)

### 4.1 Importer un document (Upload)
**`POST /api/v1/resources/upload`** (Content-Type: `multipart/form-data`)
- `file`: (Fichier binaire PDF, DOCX, TXT, etc.)
- `workspaceId`: `00000000-0000-0000-0000-000000000000`

### 4.2 Lister les documents
**`GET /api/v1/resources/documents`**

### 4.3 Appliquer une Rétention Légale (Legal Hold)
**`POST /api/v1/resources/legal-holds`**
```json
{
  "resourceId": "document-guid-here",
  "caseNumber": "CASE-2026-001",
  "reason": "Enquête de conformité légale"
}
```

---

## 🧠 5. Base de Connaissances & RAG Vectoriel (`/api/v1/knowledge`)

### 5.1 Créer un Pack de Connaissance (Knowledge Pack)
**`POST /api/v1/knowledge/packs`**
```json
{
  "name": "Documentation Technique EAIOS",
  "description": "Guides d'architecture et spécifications d'API",
  "isPublic": false
}
```

### 5.2 Ajouter un élément de connaissance avec texte
**`POST /api/v1/knowledge/items`**
```json
{
  "packId": "pack-guid-here",
  "title": "Architecture Clean Architecture .NET 10",
  "content": "EAIOS utilise une Clean Architecture découpée en 13 schémas PostgreSQL avec isolation multi-tenant.",
  "type": "Document",
  "tags": ["architecture", "dotnet"]
}
```

### 5.3 Poser une question RAG à la base de connaissance (Ask / Q&A)
**`POST /api/v1/knowledge/ask`**
```json
{
  "query": "Comment la Clean Architecture d'EAIOS est-elle organisée ?",
  "topK": 3
}
```

---

## 🤖 6. Agents IA & Modèles LLM (`/api/v1/agents`)

### 6.1 Créer un Agent IA
**`POST /api/v1/agents`**
```json
{
  "name": "assistant-code-expert",
  "displayName": "Expert Développeur .NET",
  "type": "Conversational",
  "description": "Agent spécialisé dans la révision et le refactoring de code C#",
  "systemPrompt": "Tu es un expert senior en C# .NET 10 et Clean Architecture.",
  "llmConfig": {
    "provider": "OpenAi",
    "model": "gpt-4o",
    "temperature": 0.3
  }
}
```

### 6.2 Exécuter l'agent en mode synchrone
**`POST /api/v1/agents/{agentId}/execute`**
```json
{
  "input": "Explique-moi la différence entre Scoped et Singleton dans ASP.NET Core DI."
}
```

### 6.3 Exécuter l'agent en mode Streaming (Server-Sent Events)
**`POST /api/v1/agents/{agentId}/execute/stream`**
```json
{
  "input": "Génère une classe C# pour un contrôleur Web API."
}
```

---

## ⚡ 7. Moteur de Workflows Visuels (`/api/v1/workflows`)

### 7.1 Créer une Définition de Workflow (Graphe JSON)
**`POST /api/v1/workflows`**
```json
{
  "name": "Validation de Document Légal",
  "description": "Workflow d'approbation à 2 étapes pour les contrats",
  "category": "Juridique",
  "nodesJson": "[{\"id\":\"node-1\",\"type\":\"trigger\"},{\"id\":\"node-2\",\"type\":\"human-task\"}]",
  "edgesJson": "[{\"id\":\"edge-1\",\"source\":\"node-1\",\"target\":\"node-2\"}]"
}
```

### 7.2 Publier le Workflow
**`POST /api/v1/workflows/{workflowId}/publish`**

### 7.3 Démarrer une Instance de Workflow
**`POST /api/v1/workflows/{workflowId}/instances`**
```json
{
  "variables": {
    "documentId": "guid-du-document",
    "montantContrat": 50000
  }
}
```

### 7.4 Compléter une Tâche Humaine dans le Workflow
**`POST /api/v1/workflows/tasks/{taskId}/complete`**
```json
{
  "decision": "Approuvé",
  "comment": "Contrat vérifié et valide par le service juridique.",
  "formData": { "validePar": "Jean Dupont" }
}
```

---

## 🔎 8. Recherche Hybride (`/api/v1/search`)

**`POST /api/v1/search`**
```json
{
  "query": "spécification API RAG workflows",
  "type": "Hybrid",
  "highlight": true,
  "page": 1,
  "pageSize": 20
}
```

---

## 🛡️ 9. Administration Plateforme (`/api/v1/admin`)

### 9.1 Consulter les Journaux d'Audit (Audit Logs)
**`GET /api/v1/admin/audit-logs`**

### 9.2 Modifier un Feature Flag
**`PUT /api/v1/admin/feature-flags/{flagId}`**
```json
{
  "isActive": true,
  "defaultValue": true,
  "description": "Activation du moteur de graphe RAG"
}
```

---

## 💡 Conseils pour l'Intégration Frontend (React / Next.js / Vue)

1. Stockez le `accessToken` retourné par `POST /api/v1/auth/login` en mémoire ou cookie sécurisé.
2. Ajoutez le header `Authorization: Bearer ${accessToken}` à chaque appel API.
3. Pour la réponse unifiée, l'API retourne toujours un objet enveloppé dans `{ "data": ... }` ou `{ "data": [...], "totalCount": ... }`.
