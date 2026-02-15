# Build and run Dispatcher (non-AOT for Kafka compatibility).
# Build context: repository root (see DockerfileContext in .csproj).
# Includes React UI built into wwwroot.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

# Frontend (React) build
FROM node:22-alpine AS frontend
WORKDIR /app
COPY dispatcher/frontend/package.json dispatcher/frontend/package-lock.json* ./
RUN npm install
COPY dispatcher/frontend/ .
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["dispatcher/dispatcher/dispatcher.csproj", "dispatcher/dispatcher/"]
RUN dotnet restore "dispatcher/dispatcher/dispatcher.csproj"
COPY . .
COPY --from=frontend /dispatcher/wwwroot ./dispatcher/dispatcher/wwwroot
WORKDIR /src/dispatcher/dispatcher
RUN dotnet build "dispatcher.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "dispatcher.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "dispatcher.dll"]
