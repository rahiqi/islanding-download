# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Fix TLS / cert issues
RUN apt-get update && apt-get install -y ca-certificates curl && update-ca-certificates

# Optional: debug connectivity
# RUN curl  https://api.nuget.org/v3/index.json

# Force stable HTTP handling
ENV DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0

COPY ["agent/downloader-agent/downloader-agent.csproj", "agent/downloader-agent/"]

RUN dotnet restore "./agent/downloader-agent/downloader-agent.csproj" --disable-parallel

COPY . .
WORKDIR "/src/agent/downloader-agent"

RUN dotnet build "./downloader-agent.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./downloader-agent.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
USER root
RUN apt-get update && apt-get install -y --no-install-recommends gosu && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
COPY agent/downloader-agent/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "downloader-agent.dll"]
