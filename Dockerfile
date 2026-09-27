# ---------- 1. Frontend (React + Vite) ----------
FROM node:22-alpine AS client
WORKDIR /client
COPY client/package*.json ./
RUN npm install --no-audit --no-fund
COPY client/ ./
RUN npm run build

# ---------- 2. Backend (ASP.NET Core) ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json Prismarket.sln ./
COPY src/Prismarket.Domain/*.csproj src/Prismarket.Domain/
COPY src/Prismarket.Application/*.csproj src/Prismarket.Application/
COPY src/Prismarket.Infrastructure/*.csproj src/Prismarket.Infrastructure/
COPY src/Prismarket.Api/*.csproj src/Prismarket.Api/
COPY tests/Prismarket.UnitTests/*.csproj tests/Prismarket.UnitTests/
RUN dotnet restore src/Prismarket.Api/Prismarket.Api.csproj
COPY src/ src/
RUN dotnet publish src/Prismarket.Api/Prismarket.Api.csproj -c Release -o /app --no-restore

# ---------- 3. Runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
# pg_dump for the backup automation, fonts with Cyrillic for PDF reports
RUN apt-get update \
 && apt-get install -y --no-install-recommends postgresql-client fonts-dejavu-core curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY --from=client /client/dist ./wwwroot
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    App__UploadsPath=/data/uploads \
    App__BackupsPath=/data/backups
VOLUME ["/data"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=40s CMD curl -fs http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Prismarket.Api.dll"]
