# EAIOS Backend — Rapport d'implémentation

Scan exhaustif du backend puis implémentation de tout ce qui restait en stub, en TODO
ou simplement absent. Build vert, 0 erreur. 56 endpoints vérifiés en exécution réelle
(56 réponses 2xx, 13 rejets 4xx attendus, 0 erreur 500, 0 exception non gérée).

---

## 1. Bugs réels corrigés

Ces défauts cassaient des fonctionnalités en production. Ils ont été trouvés en
exécutant réellement l'API, pas seulement en lisant le code.

### 1.1 Fuite de données entre organisations (critique)

`EaiosDbContext.OnModelCreating` construisait les Global Query Filters en capturant
l'instance `ITenantContext` dans l'expression. EF Core met le modèle en cache pour
**toute la durée de vie de l'application** : le tenant de la toute première requête
était donc figé et réappliqué à toutes les suivantes — chaque utilisateur voyait les
données de l'organisation du premier appelant.

Corrigé : le filtre passe désormais par la propriété d'instance `CurrentTenantId` du
DbContext, réévaluée à chaque requête (`ApplyTenantFilter<TEntity>`).

### 1.2 Collections de navigation en tableau — `POST /knowledge/items` en erreur 500

Les 11 collections de navigation du domaine étaient initialisées `= []`, ce qui produit
un **tableau de taille fixe** en C# 12. Au fixup, EF tente d'y ajouter les entités
liées → `NotSupportedException: Collection was of a fixed size`.

Corrigé sur les 11 (`Agent.Executions/Versions`, `ConnectorInstance.SyncJobs`,
`KnowledgeItem.Chunks/Relations`, `Document.Versions/Shares/MetadataValues`,
`WorkflowDefinition.DefinitionVersions/Instances`, `WorkflowInstance.Tasks`).

### 1.3 Authentification impossible hors tenant résolu

`/auth/refresh`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/register` sont
anonymes : aucun tenant n'est résolu, donc les filtres globaux masquaient **toutes** les
lignes. Le rafraîchissement de session renvoyait systématiquement 401.

Corrigé : les trois résolutions par identifiant globalement unique (email, hash de
refresh token, jeton d'invitation) ignorent le filtre tenant, puis le contrôleur adopte
l'organisation trouvée (`AdoptTenant`) pour la suite de la requête.

### 1.4 Perte des rôles au rafraîchissement

`Refresh` réémettait le token avec `[]` en rôles et permissions : l'utilisateur perdait
tous ses droits dès le premier rafraîchissement. Les rôles sont désormais résolus et
réémis (`ResolveGrantsAsync`), partagés avec `Login`.

### 1.5 Inscription laissant le compte inutilisable

`Register` appelait `user.VerifyEmail(user.EmailVerificationToken ?? "")` sur un jeton
toujours `null` : la vérification échouait en silence, le compte restait
`PendingVerification` et ne pouvait jamais se connecter. L'adresse étant déjà prouvée
par l'invitation, le compte est maintenant activé directement. L'invitation est de plus
vérifiée comme émise **pour cette adresse** (sinon détournement possible).

### 1.6 Documents sans version courante

`POST /uploads/direct` ne rappelait jamais `SetCurrentVersion` : le document restait
sans type MIME, sans taille et sans version courante. Le téléchargement et l'affichage
étaient cassés. Les limites `Storage:MaxFileSizeBytes` et `Storage:AllowedExtensions`,
déjà présentes en configuration, n'étaient appliquées nulle part.

### 1.7 Renvoi d'invitation sans effet

`ResendInvitation` n'appelait pas `invitation.Resend()` : le compteur n'augmentait pas
et l'échéance n'était pas repoussée — l'invitation renvoyée expirait à sa date initiale.

### 1.8 Livraison de webhooks structurellement impossible

`PublishEventAsync` faisait un `Task.Run` détaché qui capturait des services scopés déjà
disposés à l'exécution : `LastTriggeredAt` et `LastError` n'étaient jamais persistés,
aucun réessai, aucune trace.

---

## 2. Stubs remplacés par de vraies implémentations

| Zone | Avant | Après |
|---|---|---|
| **Analytics** | Toutes les méthodes renvoyaient des zéros en dur | Agrégations EF réelles sur documents, exécutions d'agents, instances de workflow, utilisateurs et journal d'évènements ; séries temporelles par jour/semaine/mois, ventilation par département, périodes `24h`/`7d`/`30d`/`mtd`/`ytd` |
| **Rapports** | ID de job bidon, « Fichier rapport simulé » | Entité `ReportJob` persistée, worker de fond, génération CSV (RFC 4180, BOM UTF-8, anti-injection de formules) et JSON sur 7 types de rapports, téléchargement, purge à expiration |
| **Admin `/health`** | `{ Status = "Healthy" }` en dur | Sonde réelle des deux bases et du stockage objet, avec latences |
| **Admin `/metrics`** | Chiffres inventés | Métriques processus réelles (mémoire, CPU, threads, GC) et volumétrie plateforme |
| **Création de tenant** | Organisation seule, inutilisable | Provisioning complet : permissions, rôles système, utilisateur admin, rôle `org.admin`, workspace par défaut, email, audit, rollback si échec |
| **Graphe de connaissance** | Relations toujours vides, requêtes « non implémentées » | Traversée BFS réelle, plus court chemin, sous-graphe borné, CRUD des relations, DSL de requête (`neighbors`/`subgraph`/`path`/`relations`) |
| **Moteur de workflow** | Version figée `1.0.0`, premier nœud codé en dur `"start"` | Versionnage réel incrémental, graphe analysé et validé, moteur qui traverse les étapes automatiques, crée les tâches humaines, évalue les branchements et clôt l'instance |
| **Test de connecteur** | `ConnectionSuccessful: true` en dur | Vérification de complétude du schéma puis sonde HTTP authentifiée réelle avec latence mesurée ; mise à jour de l'état de santé |
| **Recherche sémantique** | Commentaire « simulée » | Chaîne complète embeddings → stockage vectoriel → similarité cosinus → RAG, avec repli lexical tant que les vecteurs ne sont pas générés |
| **Fournisseur LLM** | `StubLlmService` unique, `Ai:Provider` sans effet | Client compatible OpenAI / Azure OpenAI (génération, streaming SSE, embeddings, estimation de coût) ; le stub reste le défaut sans clé API |

---

## 3. Fonctionnalités absentes, désormais implémentées

### Authentification
`POST /auth/verify-email`, `/auth/resend-verification`, `/auth/forgot-password`,
`/auth/reset-password` — les DTO existaient déjà sans aucun endpoint. Réponses neutres
pour empêcher l'énumération de comptes, verrouillage après échecs répétés, révocation
de toutes les sessions au changement de mot de passe.

### Dossiers
`PUT /folders/{id}` (renommage), `POST /folders/{id}/move` (avec réécriture du chemin
matérialisé de tout le sous-arbre et refus des cycles), `GET /folders/{id}/breadcrumb`,
`GET /folders/tree`, suppression sécurisée (refus si non vide sans `recursive=true`,
les documents remontent à la racine au lieu d'être détruits).

### Documents
`GET /documents/{id}/download`, téléchargement d'une version précise, restauration de
version (nouvelle version pointant sur le même objet — historique append-only),
`GET/PUT /documents/{id}/metadata`, `GET /documents/trash`, purge définitive,
`POST /documents/{id}/move`, liste des partages, liens publics et
`GET /api/v1/public/documents/{token}` anonyme.

### Métadonnées
`MetadataTemplate` et `MetadataValue` n'étaient référencées nulle part. Nouveau
contrôleur `/api/v1/metadata-templates` avec validation des définitions de champs.

### Upload
Upload multipart complet (`initiate` / `parts/{n}` / `complete` / `abort`) — les
primitives existaient dans `IStorageService` sans aucun endpoint, et les implémentations
locale et S3 étaient toutes deux factices (`UploadPartAsync` ne faisait rien).

### Divers
`GET/PUT /notifications/preferences` (colonne `User.NotificationPreferences` inutilisée),
`POST/DELETE /users/me/avatar`, `IEmailService` (absent — deux implémentations : journal
en dev, SMTP en prod, avec gabarits HTML/texte), suivi analytique (`IAnalyticsTracker`)
qui alimente enfin les tableaux de bord, chiffrement des identifiants de connecteurs via
Data Protection, évaluateur cron pour la planification des synchronisations.

---

## 4. Migrations

Deux migrations EF Core générées, propres et limitées aux nouveautés :

- `20260820222621_AddReportJobs` — table `analytics.report_jobs`
- `20260820223847_AddEmbeddingVector` — colonne `Vector` (`real[]`) sur `Embeddings`

```powershell
dotnet ef database update --context EaiosDbContext
```

---

## 5. Configuration ajoutée

Sections nouvelles dans `appsettings.json` : `Email` (provider, expéditeur, SMTP),
`Provisioning` (quotas par défaut des nouveaux tenants), `Reports` (rétention),
`Ai` étendu (`BaseUrl`, `ApiKey`, `Organization`),
`Storage:MaxAvatarSizeBytes`, `Security:BootstrapAdminEmail`.

Le comportement par défaut reste **sans dépendance externe** : email journalisé, LLM
stub, stockage local.

---

## 6. Points à connaître

- **Le LLM par défaut est le stub.** La chaîne IA fonctionne de bout en bout, mais les
  vecteurs du stub sont dérivés d'un hachage : la pertinence sémantique n'a de sens
  qu'avec un vrai fournisseur (`Ai:Provider` + `Ai:ApiKey`).
- **Recherche vectorielle en base.** Le domaine prévoyait Qdrant ; sans base vectorielle
  déployée, les vecteurs vivent dans `search.embeddings` et la similarité est calculée
  applicativement (plafond de 20 000 vecteurs par requête). Seule `VectorSearchService`
  serait à remplacer le jour où Qdrant arrive.
- **File de webhooks en mémoire.** Bornée à 10 000 livraisons, avec réessais à backoff
  exponentiel et désactivation d'un abonnement après 20 échecs consécutifs. Un
  redémarrage perd les livraisons en attente — un broker durable serait le prochain pas.
- **Vulnérabilité connue.** `Microsoft.OpenApi` 2.0.0 remonte l'avis GHSA-v5pm-xwqc-g5wc
  (dépendance transitive de `Microsoft.AspNetCore.OpenApi` 10.0.10). Préexistante.
