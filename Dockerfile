# Backend EAIOS — image de déploiement en deux étapes.
#
# La première compile et publie en Release ; la seconde n'embarque que le
# runtime ASP.NET et le résultat de la publication. Les migrations s'appliquent
# au démarrage (Program.cs), la configuration passe par variables d'environnement
# (`ConnectionStrings__DefaultConnection`, `Security__TokenSigningKey`, …).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restauration d'abord : la couche est mise en cache tant que le csproj ne bouge pas.
COPY backend.csproj ./
RUN dotnet restore backend.csproj

COPY . .
RUN dotnet publish backend.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Utilisateur non privilégié : l'API n'a besoin d'aucun droit sur l'hôte.
RUN useradd --create-home --uid 10001 eaios \
    && mkdir -p /app/uploads && chown -R eaios:eaios /app
USER eaios

COPY --from=build --chown=eaios:eaios /app/publish ./

ENV ASPNETCORE_URLS=http://0.0.0.0:5257 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 5257

HEALTHCHECK --interval=30s --timeout=5s --start-period=40s --retries=3 \
    CMD curl -fsS http://localhost:5257/health || exit 1

ENTRYPOINT ["dotnet", "EAIOS.Api.dll"]
