# Build context: repository root. For Raspberry Pi 32-bit, build with:
#   docker buildx build --platform linux/arm/v7 -f agent/downloader-agent/Dockerfile .
# Confluent.Kafka requires native librdkafka; we install via apt on amd64/arm64 and build from source on arm (32-bit).

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Provides librdkafka for the current platform: apt on amd64/arm64, build from source on arm (32-bit Pi).
FROM debian:bookworm-slim AS librdkafka-native
RUN apt-get update && apt-get install -y --no-install-recommends \
    ca-certificates curl xz-utils \
    && rm -rf /var/lib/apt/lists/*
ARG TARGETARCH
RUN mkdir -p /out \
    && ARCH=$(dpkg --print-architecture) \
    && if [ "$ARCH" = "amd64" ] || [ "$ARCH" = "arm64" ]; then \
        apt-get update && apt-get install -y --no-install-recommends librdkafka1 \
        && cp -P /usr/lib/$(dpkg --print-architecture)-linux-gnu/librdkafka.so* /out/ \
        && rm -rf /var/lib/apt/lists/*; \
    else \
        apt-get update && apt-get install -y --no-install-recommends build-essential git cmake libssl-dev zlib1g-dev \
        && rm -rf /var/lib/apt/lists/* \
        && git clone --depth 1 -b v2.4.0 https://github.com/confluentinc/librdkafka.git /tmp/librdkafka \
        && cd /tmp/librdkafka \
        && ./configure --prefix=/out \
        && make -j$(nproc) \
        && make install \
        && cp -P /out/lib/librdkafka.so* /out/ \
        && rm -rf /tmp/librdkafka; \
    fi

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["agent/downloader-agent/downloader-agent.csproj", "agent/downloader-agent/"]
RUN dotnet restore "./agent/downloader-agent/downloader-agent.csproj"
COPY . .
WORKDIR "/src/agent/downloader-agent"
RUN dotnet build "./downloader-agent.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./downloader-agent.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root
# Install librdkafka: system path (ldconfig) + app dir so .NET loader finds it reliably
COPY --from=librdkafka-native /out/librdkafka.so* /usr/local/lib/
RUN ldconfig
WORKDIR /app
COPY --from=publish /app/publish .
COPY --from=librdkafka-native /out/librdkafka.so* /app/
ENV LD_LIBRARY_PATH=/app
USER $APP_UID
ENTRYPOINT ["dotnet", "downloader-agent.dll"]
