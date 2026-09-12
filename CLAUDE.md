# backend — Règles de travail

> Complète `../CLAUDE.md`, ne le contredit jamais. La progression se consigne dans `BOUCLAGE.md`.

---

## 1. Ce que ce service est

Le **système d'enregistrement** d'EAIOS et son autorité unique : identité, permissions, cycle de
vie documentaire, audit, workflows, tâches humaines. Il ne raisonne pas — aucune boucle d'agent,
aucun RAG, aucune orchestration ; le runtime propose, le backend dispose.

---

## 2. Ne jamais développer à la main ce qu'une bibliothèque gratuite fournit

Règle mère (`../CLAUDE.md`, § 3), déclinée ici :

| Besoin | On utilise | On n'écrit pas |
|---|---|---|
| Requêtes, transactions, concurrence, filtres globaux, migrations | **EF Core** (`HasQueryFilter`, `IsRowVersion`, `Migrate`) | de SQL à la main, un « ORM » maison, une table de migrations parallèle |
| Validation des requêtes | **FluentValidation** | des `if` de validation recopiés dans chaque contrôleur |
| Authentification, autorisation, politiques | **ASP.NET Core** (`[Authorize(Policy = …)]`, handlers) | des vérifications de rôle écrites à la main dans les actions |
| Sérialisation, enums, casse des noms | **System.Text.Json** (`JsonStringEnumConverter`, `JsonPropertyName`, politiques de nommage) | du JSON assemblé par concaténation, une casse « corrigée » par remplacement de chaîne |
| Tâches de fond, planification | **`BackgroundService`** / hosted services, cron via bibliothèque | des threads ou des boucles `Task.Run` sans arrêt propre |
| Résilience des appels sortants | **`HttpClientFactory`** + politiques de réessai de bibliothèque | des boucles de retry maison |
| Journalisation, traçage | **`ILogger`**, OpenTelemetry | des `Console.WriteLine`, un journal fichier maison |
| Tests | **xUnit** + doubles légers | un harnais de test maison |

Avant tout mécanisme nouveau — un cache, un verrou, un compteur, un protocole de flux, un
générateur de rapports, un parseur — vérifier qu'une bibliothèque NuGet gratuite et maintenue ne
le fournit pas déjà. Si oui, on l'ajoute au `.csproj` et on l'utilise ; si non, on écrit le
strict nécessaire et on le dit dans `BOUCLAGE.md`.

**Application progressive.** L'existant ne respecte pas partout cette règle, et ce n'est pas
grave : pas de réécriture de masse. Chaque modification suit la règle ; ce qu'on touche s'aligne.

---

## 3. Les contrats qui traversent les services sont épinglés par un test

Ce que le runtime lit (instantané d'agent, portée signée, résultat de course, décision humaine)
et ce que le frontend lit (enums en chaînes, camelCase, enveloppe `data`/`meta`) sont des
**contrats**. Une casse de nom, un champ renommé ou une enveloppe changée casse l'autre service
sans qu'aucun test local ne le voie : chaque contrat a donc un test qui fixe sa forme exacte
(`tests/EAIOS.Api.Tests`).

---

## 4. Multi-tenant : jamais de requête sans tenant

Toute entité métier hérite de `TenantEntity` ; le filtre global EF Core s'applique à **toute**
requête. Une requête sans tenant résolu échoue — elle ne retombe jamais sur un schéma par défaut.
`IgnoreQueryFilters()` n'est admis que dans un ouvrier de fond qui pose ensuite le tenant
explicitement, une ligne à la fois.

---

## 5. Langue

Code en anglais (types, méthodes, fichiers) ; commentaires, messages d'erreur retournés à
l'utilisateur, journaux destinés à un humain en **français**.

---

## 6. Rythme

`BOUCLAGE.md` est mis à jour après chaque lot : ce qui est fait, ce qui reste, les décisions,
les dettes ouvertes. Il doit suffire seul à reprendre le travail.
