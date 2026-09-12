using System.Text.Json;
using System.Text.Json.Serialization;
using EAIOS.Api.Application.Agent;
using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Resource;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// Émet la portée d'exécution signée que le runtime d'agents vérifie.
///
/// Le backend est la seule autorité d'autorisation d'EAIOS : il décrit ici ce
/// qu'une exécution a le droit de voir et de dépenser, et le runtime applique
/// cette portée sans jamais l'élargir.
/// </summary>
public interface IAgentContextService
{
    ExecutionContextDto Issue(
        Guid organizationId,
        Guid actorId,
        Domain.Agent.Agent agent,
        string agentVersion,
        Guid executionId,
        Guid? sessionId = null);

    /// <summary>
    /// Portée de l'assistant documentaire intégré, qui n'a pas d'<c>Agent</c>
    /// derrière lui : personne ne l'a configuré, il n'y a donc pas de version à
    /// figer. Il est en <b>lecture seule</b> — la portée ne lui accorde aucun
    /// outil d'écriture, et le runtime n'en monte aucun.
    /// </summary>
    ExecutionContextDto IssueForAssistant(
        Guid organizationId,
        Guid actorId,
        Guid[] knowledgePackIds,
        Guid executionId);
}

public sealed class AgentContextService : IAgentContextService
{
    /// <summary>
    /// Audience du jeton. Elle empêche qu'un jeton émis pour un autre usage —
    /// un jeton d'accès utilisateur, par exemple — soit rejoué comme portée
    /// d'exécution. Le runtime l'exige côté Python.
    /// </summary>
    public const string Audience = "eaios-agent-runtime";
    public const string Issuer   = "eaios-api";

    /// <summary>
    /// Les noms de champs viennent des attributs `[JsonPropertyName]` du
    /// contrat, pas d'une convention globale : c'est ce snake_case que
    /// `ExecutionScope` valide cote Python.
    /// </summary>
    private static readonly JsonSerializerOptions ScopeJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly SymmetricSecurityKey _key;
    private readonly ICurrentUser         _currentUser;
    private readonly int                  _defaultLifetimeSeconds;
    private readonly int                  _defaultBudgetTokens;
    private readonly decimal              _defaultBudgetUsd;

    public AgentContextService(IConfiguration config, ICurrentUser currentUser)
    {
        _currentUser = currentUser;

        // Clé DISTINCTE de `Security:TokenSigningKey`. Signer les portées
        // d'exécution avec la clé des jetons utilisateurs ferait dépendre deux
        // frontières de confiance d'un même secret : la fuite de l'une
        // compromettrait l'autre.
        var secret = config["Security:AgentContextSigningKey"]
            ?? throw new InvalidOperationException(
                "Security:AgentContextSigningKey absente : le runtime d'agents ne peut pas être appelé.");

        _key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret)) { KeyId = "eaios-agent-context" };

        _defaultLifetimeSeconds = config.GetValue("AgentRuntime:ContextLifetimeSeconds", 900);
        _defaultBudgetTokens    = config.GetValue("AgentRuntime:DefaultBudgetTokens", 100_000);
        _defaultBudgetUsd       = config.GetValue("AgentRuntime:DefaultBudgetUsd", 2.0m);
    }

    public ExecutionContextDto Issue(
        Guid organizationId,
        Guid actorId,
        Domain.Agent.Agent agent,
        string agentVersion,
        Guid executionId,
        Guid? sessionId = null) =>
        Sign(new ExecutionScopeDto
        {
            OrganizationId           = organizationId,
            ActorId                  = actorId,
            AgentId                  = agent.Id,
            AgentVersion             = agentVersion,
            ExecutionId              = executionId,
            SessionId                = sessionId,
            KnowledgePackIds         = agent.KnowledgePackIds,
            WorkspaceIds             = agent.WorkspaceIds,
            MaxClassification        = (int)CeilingFor(_currentUser),
            BudgetTokens             = _defaultBudgetTokens,
            BudgetUsd                = _defaultBudgetUsd,
            // L'échéance est la plus courte des deux : celle que l'agent
            // s'impose, et la durée de vie du jeton. Un jeton qui survivrait à
            // l'exécution qu'il autorise serait rejouable.
            Deadline                 = DeadlineFor(agent.MaxExecutionSeconds),
            AllowedTools             = agent.EnabledTools,
            RequireHumanConfirmation = agent.RequireHumanConfirmation,
        }, executionId);

    public ExecutionContextDto IssueForAssistant(
        Guid organizationId,
        Guid actorId,
        Guid[] knowledgePackIds,
        Guid executionId) =>
        Sign(new ExecutionScopeDto
        {
            OrganizationId = organizationId,
            ActorId        = actorId,
            // Pas d'agent : l'identifiant vide est la marque de l'assistant
            // intégré, et le runtime ne va chercher aucune version figée.
            AgentId        = Guid.Empty,
            AgentVersion   = "builtin-1",
            ExecutionId    = executionId,
            KnowledgePackIds  = knowledgePackIds,
            WorkspaceIds      = [],
            MaxClassification = (int)CeilingFor(_currentUser),
            BudgetTokens      = _defaultBudgetTokens,
            BudgetUsd         = _defaultBudgetUsd,
            Deadline          = DeadlineFor(null),
            // Lecture seule, et c'est la portée elle-même qui le dit : même si
            // le runtime montait un outil d'écriture par erreur,
            // `ToolScopeMiddleware` le refuserait.
            AllowedTools             = ["search_knowledge"],
            RequireHumanConfirmation = true,
        }, executionId);

    private DateTimeOffset DeadlineFor(int? maxExecutionSeconds)
    {
        var now         = DateTimeOffset.UtcNow;
        var tokenExpiry = now.AddSeconds(_defaultLifetimeSeconds);
        var own         = maxExecutionSeconds is { } s and > 0 ? now.AddSeconds(s) : (DateTimeOffset?)null;

        return own is { } d && d < tokenExpiry ? d : tokenExpiry;
    }

    private ExecutionContextDto Sign(ExecutionScopeDto scope, Guid executionId)
    {
        var now         = DateTimeOffset.UtcNow;
        var tokenExpiry = now.AddSeconds(_defaultLifetimeSeconds);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer             = Issuer,
            Audience           = Audience,
            IssuedAt           = now.UtcDateTime,
            NotBefore          = now.UtcDateTime,
            Expires            = tokenExpiry.UtcDateTime,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256),
            // `JsonWebTokenHandler` refuse un POCO arbitraire dans une
            // revendication (IDX11025) : il n'accepte que des primitives et des
            // types JSON. On sérialise donc nous-mêmes en `JsonElement`, ce qui
            // a l'avantage de faire respecter les attributs `[JsonPropertyName]`
            // du contrat — c'est-à-dire le snake_case qu'attend Pydantic.
            Claims = new Dictionary<string, object>
            {
                ["scope"] = JsonSerializer.SerializeToElement(scope, ScopeJsonOptions),
            },
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);

        return new ExecutionContextDto(token, executionId, tokenExpiry, scope);
    }

    /// <summary>
    /// Classification maximale que l'exécution pourra consulter.
    ///
    /// <para>
    /// <b>Volontairement conservateur.</b> Le vrai plafond devrait sortir d'une
    /// évaluation ABAC sur les politiques et les ACL de l'utilisateur, que le
    /// backend ne sait pas encore faire. En attendant, on plafonne bas : un
    /// plafond trop bas prive l'agent de documents, un plafond trop haut les lui
    /// expose. Seule la première erreur est rattrapable.
    /// </para>
    /// <para>
    /// <c>StrictlyConfidential</c> n'est jamais accordé automatiquement : il
    /// exigera une autorisation explicite, ressource par ressource.
    /// </para>
    /// </summary>
    private static ResourceClassification CeilingFor(ICurrentUser user) => ClassificationCeiling.For(user);
}
