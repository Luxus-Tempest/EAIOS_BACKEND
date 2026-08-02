using EAIOS.Api.Domain.Search;

namespace EAIOS.Api.Application.Search;

public interface ISearchService
{
    Task<object> SearchAsync(Guid tenantId, Guid actorId, SearchRequest req, CancellationToken ct = default);
    Task<object> SuggestAsync(Guid tenantId, string query, CancellationToken ct = default);
    Task<object> AskAsync(Guid tenantId, Guid actorId, AskRequest req, CancellationToken ct = default);
    
    Task<IReadOnlyList<SavedSearch>> GetSavedSearchesAsync(Guid actorId, CancellationToken ct = default);
    Task<SavedSearch> SaveSearchAsync(Guid tenantId, Guid actorId, SaveSearchRequest req, CancellationToken ct = default);
    Task DeleteSavedSearchAsync(Guid id, Guid actorId, CancellationToken ct = default);
}
