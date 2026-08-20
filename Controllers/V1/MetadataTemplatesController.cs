using EAIOS.Api.Application.Resource;
using EAIOS.Api.Domain.Resource;
using EAIOS.Api.Infrastructure.Persistence.Repositories.Resource;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EAIOS.Api.Controllers.V1;

/// <summary>
/// Modèles de métadonnées : définissent les champs applicables aux documents
/// (schéma documentaire de l'organisation).
/// Route : /api/v1/metadata-templates
/// </summary>
[Route("api/v1/metadata-templates")]
[Authorize]
public sealed class MetadataTemplatesController(
    IMetadataTemplateRepository templateRepo) : V1ApiController
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] SupportedFieldTypes =
        ["text", "number", "date", "boolean", "list"];

    // ── Lecture ───────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool activeOnly = true,
        [FromQuery] string? resourceType = null,
        CancellationToken ct = default)
    {
        var templates = string.IsNullOrWhiteSpace(resourceType)
            ? await templateRepo.GetAllAsync(activeOnly, ct)
            : await templateRepo.GetForResourceTypeAsync(resourceType, ct);

        return Ok200(templates.Select(Map).ToList());
    }

    [HttpGet("{id:guid}", Name = "GetMetadataTemplate")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var template = await templateRepo.GetByIdAsync(id, ct);
        return template == null ? NotFound("Modèle de métadonnées introuvable.") : Ok200(Map(template));
    }

    // ── Écriture ──────────────────────────────────────────────────────────────

    [HttpPost]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> Create([FromBody] CreateMetadataTemplateRequest req, CancellationToken ct)
    {
        if (!ActorId.HasValue) return Unauthorized();

        if (string.IsNullOrWhiteSpace(req.Name))
            return UnprocessableEntity("Le nom du modèle est obligatoire.");

        var validationError = ValidateFields(req.Fields);
        if (validationError is not null)
            return UnprocessableEntity(validationError);

        var existing = await templateRepo.GetAllAsync(activeOnly: false, ct);
        if (existing.Any(t => string.Equals(t.Name, req.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict($"Un modèle nommé « {req.Name.Trim()} » existe déjà.");

        var template = MetadataTemplate.Create(
            TenantId,
            req.Name,
            ActorId.Value,
            JsonSerializer.Serialize(req.Fields ?? []),
            req.Description,
            req.ApplicableResourceTypes);

        await templateRepo.AddAsync(template, ct);
        await templateRepo.SaveAsync(ct);

        return Created201("GetMetadataTemplate", new { id = template.Id }, Map(template));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateMetadataTemplateRequest req, CancellationToken ct)
    {
        var template = await templateRepo.GetByIdAsync(id, ct);
        if (template == null) return NotFound("Modèle de métadonnées introuvable.");

        if (template.IsSystem)
            return Conflict("Un modèle système ne peut pas être modifié.");

        var validationError = ValidateFields(req.Fields);
        if (validationError is not null)
            return UnprocessableEntity(validationError);

        template.Update(
            req.Name,
            req.Description,
            req.Fields is null ? null : JsonSerializer.Serialize(req.Fields),
            req.ApplicableResourceTypes);

        if (req.IsActive.HasValue)
        {
            if (req.IsActive.Value) template.Activate();
            else                    template.Deactivate();
        }

        templateRepo.Update(template);
        await templateRepo.SaveAsync(ct);

        return Ok200(Map(template));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "resource.manage")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var template = await templateRepo.GetByIdAsync(id, ct);
        if (template == null) return NotFound("Modèle de métadonnées introuvable.");

        if (template.IsSystem)
            return Conflict("Un modèle système ne peut pas être supprimé.");

        templateRepo.SoftDelete(template);
        await templateRepo.SaveAsync(ct);

        return NoContent204();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Renvoie un message d'erreur si la définition des champs est incohérente, sinon null.</summary>
    private static string? ValidateFields(IReadOnlyList<MetadataFieldDefinition>? fields)
    {
        if (fields is null) return null;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Key))
                return "Chaque champ doit avoir une clé.";

            if (!seen.Add(field.Key.Trim()))
                return $"Clé de champ dupliquée : « {field.Key} ».";

            if (!SupportedFieldTypes.Contains(field.Type, StringComparer.OrdinalIgnoreCase))
                return $"Type de champ non supporté pour « {field.Key} » : « {field.Type} ». " +
                       $"Types acceptés : {string.Join(", ", SupportedFieldTypes)}.";

            // Un champ de type liste sans options n'offrirait aucune valeur sélectionnable.
            if (field.Type.Equals("list", StringComparison.OrdinalIgnoreCase)
                && (field.Options is null || field.Options.Length == 0))
                return $"Le champ « {field.Key} » est de type list mais ne définit aucune option.";
        }

        return null;
    }

    private static MetadataTemplateDto Map(MetadataTemplate t)
    {
        IReadOnlyList<MetadataFieldDefinition> fields;
        try
        {
            fields = JsonSerializer.Deserialize<List<MetadataFieldDefinition>>(t.FieldsJson, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            // Un modèle dont le JSON est corrompu doit rester listable plutôt que
            // de faire échouer toute la collection.
            fields = [];
        }

        return new MetadataTemplateDto(
            t.Id, t.Name, t.Description, t.IsSystem, t.IsActive,
            fields, t.ApplicableResourceTypes, t.CreatedAt);
    }
}
