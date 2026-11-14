# One container: the ASP.NET Core API also serves the built React app.

# ---- 1. Frontend: type-check + build into /src/frontend/dist
FROM node:22-bookworm-slim AS web
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ---- 2. Backend: publish (ReadyToRun → faster cold start on the small free instance)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY backend/Tasklog.Api/Tasklog.Api.csproj backend/Tasklog.Api/
RUN dotnet restore backend/Tasklog.Api/Tasklog.Api.csproj -r linux-x64
COPY backend/Tasklog.Api/ backend/Tasklog.Api/
RUN dotnet publish backend/Tasklog.Api/Tasklog.Api.csproj \
      -c Release -r linux-x64 --self-contained false --no-restore \
      -p:PublishReadyToRun=true \
      -o /app

# ---- 3. Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/frontend/dist ./wwwroot

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Non-root user provided by the .NET images.
USER $APP_UID
ENTRYPOINT ["dotnet", "Tasklog.Api.dll"]
