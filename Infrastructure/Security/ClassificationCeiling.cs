using EAIOS.Api.Application.Common.Interfaces;
using EAIOS.Api.Domain.AccessControl;
using EAIOS.Api.Domain.Resource;

namespace EAIOS.Api.Infrastructure.Security;

/// <summary>
/// La classification maximale qu'une personne peut consulter sans autorisation
/// nominative. <b>Une seule règle</b>, appliquée à la lecture humaine (listes,
/// fiches, téléchargements, recherche) comme au laissez-passer de l'agent : ce
/// que l'agent voit pour quelqu'un est exactement ce que cette personne voit.
///
/// <list type="bullet">
///   <item>administrateur plateforme : <c>StrictlyConfidential</c> ;</item>
///   <item>administrateur d'organisation, gestion des ressources ou des conservations légales : <c>Confidential</c> ;</item>
///   <item>tout autre compte : <c>Internal</c>.</item>
/// </list>
/// <c>StrictlyConfidential</c> n'est <b>jamais</b> accordé automatiquement dans
/// une organisation : un document de ce niveau reste lisible seulement si une
/// ACL nominative l'accorde — même à un administrateur, qui doit se l'attribuer
/// explicitement, et ce geste est journalisé.
/// </summary>
public static class ClassificationCeiling
{
    public static ResourceClassification For(IEnumerable<string> roles, IEnumerable<string> permissions, bool isPlatformAdmin = false, bool isOrganizationAdmin = false)
    {
        var roleSet = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var permSet = permissions.ToHashSet(StringComparer.Ordinal);

        if (isPlatformAdmin || roleSet.Contains(SystemRoles.PlatformAdmin) || roleSet.Contains(SystemRoles.PlatformOwner))
            return ResourceClassification.StrictlyConfidential;

        if (isOrganizationAdmin || roleSet.Contains(SystemRoles.OrgAdmin) || permSet.Contains("*")
            || permSet.Contains(Permissions.ResourceManage) || permSet.Contains(Permissions.LegalHoldManage))
            return ResourceClassification.Confidential;

        return ResourceClassification.Internal;
    }

    public static ResourceClassification For(ICurrentUser user) =>
        For(user.Roles, user.Permissions, user.IsPlatformAdmin, user.IsOrganizationAdmin);
}
