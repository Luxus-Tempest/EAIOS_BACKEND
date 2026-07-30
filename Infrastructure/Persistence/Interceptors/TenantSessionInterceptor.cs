using EAIOS.Api.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;

namespace EAIOS.Api.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Intercepte chaque ouverture de connexion PostgreSQL pour exécuter :
///   SET LOCAL app.current_tenant_id = '{orgId}';
///   SET LOCAL app.current_user_id   = '{userId}';
/// Ces paramètres de session alimentent les Row-Level Security policies PostgreSQL.
/// </summary>
public sealed class TenantSessionInterceptor(
    ITenantContext tenantContext,
    ICurrentUser currentUser) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken ct = default)
    {
        await SetSessionVariablesAsync(connection, ct);
        await base.ConnectionOpenedAsync(connection, eventData, ct);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetSessionVariablesAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
        base.ConnectionOpened(connection, eventData);
    }

    private async Task SetSessionVariablesAsync(DbConnection connection, CancellationToken ct)
    {
        if (connection is not NpgsqlConnection npgsql || npgsql.State != System.Data.ConnectionState.Open)
            return;
        if (!tenantContext.IsResolved)
            return;

        await using var cmd = npgsql.CreateCommand();
        cmd.CommandText = $"""
            SET LOCAL app.current_tenant_id = '{tenantContext.OrganizationId}';
            SET LOCAL app.current_user_id   = '{currentUser.UserId?.ToString() ?? Guid.Empty.ToString()}';
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
