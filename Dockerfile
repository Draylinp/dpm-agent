# syntax=docker/dockerfile:1

# 1. Compilación
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/DpmAgent.Api/DpmAgent.Api.csproj src/DpmAgent.Api/
RUN dotnet restore src/DpmAgent.Api/DpmAgent.Api.csproj
COPY src/ src/
RUN dotnet publish src/DpmAgent.Api/DpmAgent.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# 2. Imagen final: solo el runtime de ASP.NET Core, con usuario sin privilegios
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Logging__Formato=json
COPY --from=build /app .
# Carpeta escribible para SQLite y las claves de Data Protection si no se usa PostgreSQL.
RUN mkdir -p /data && chown "$APP_UID" /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DpmAgent.Api.dll"]
