# PatchPanda.Web - Multi-Stage Docker Build (2026 Standards)
# Support: amd64, arm64, arm/v7
# Features: Non-root user, Security Patches, Docker-in-Docker CLI, OIDC Ready, Health Checks

ARG BUILDPLATFORM

# ============================================================================
# STAGE 1: Base Runtime (Hardened)
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

WORKDIR /app
EXPOSE 8080

# Patch OS vulnerabilities and install basic dependencies
USER root
RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y --no-install-recommends curl ca-certificates gnupg && \
    rm -rf /var/lib/apt/lists/*
USER app

# ============================================================================
# STAGE 2: Build (Cross-Platform)
# ============================================================================
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGETARCH
ARG TARGETVARIANT
WORKDIR /src

# Copy project file and restore specifically for the target architecture
COPY ["PatchPanda.Web/PatchPanda.Web.csproj", "PatchPanda.Web/"]

RUN export DOTNET_ARCH=$(case ${TARGETARCH} in \
    "amd64") echo "x64" ;; \
    "arm64") echo "arm64" ;; \
    "arm") [ "${TARGETVARIANT}" = "v7" ] && echo "arm" || { echo "ERROR: Unsupported ARM variant '${TARGETVARIANT}'"; exit 1; } ;; \
    *) echo "ERROR: Unsupported architecture '${TARGETARCH}'"; exit 1 ;; \
    esac) && \
    dotnet restore "PatchPanda.Web/PatchPanda.Web.csproj" \
    --runtime "linux-${DOTNET_ARCH}"

# Copy remaining source code
COPY . .

# Publish the Release binary with strict ARMv7 validation
RUN export DOTNET_ARCH=$(case ${TARGETARCH} in \
    "amd64") echo "x64" ;; \
    "arm64") echo "arm64" ;; \
    "arm") [ "${TARGETVARIANT}" = "v7" ] && echo "arm" || { echo "ERROR: Unsupported ARM variant '${TARGETVARIANT}'"; exit 1; } ;; \
    *) echo "ERROR: Unsupported architecture '${TARGETARCH}'"; exit 1 ;; \
    esac) && \
    dotnet publish "PatchPanda.Web/PatchPanda.Web.csproj" \
    --configuration Release \
    --output /app/publish \
    --runtime "linux-${DOTNET_ARCH}" \
    --no-restore \
    --self-contained false

# ============================================================================
# STAGE 3: Final Production Image (Ubuntu 26.04 LTS "Resolute")
# ============================================================================
FROM base AS final

# Set up environment and diagnostics
ARG RELEASE_VERSION
ARG ENABLE_DIAGNOSTICS=0
ENV APP_VERSION=$RELEASE_VERSION
ENV DOTNET_EnableDiagnostics=${ENABLE_DIAGNOSTICS}

# Install Docker CLI using Ubuntu 'resolute' repo
USER root
RUN mkdir -m 0755 -p /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg && \
    chmod a+r /etc/apt/keyrings/docker.gpg && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu resolute stable" > /etc/apt/sources.list.d/docker.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends docker-ce-cli && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
# Copy artifacts first (owned by root for security)
COPY --from=build /app/publish .

# Create data directory and fix permissions AFTER artifacts are copied
# Narrowed chown ensures binaries stay read-only while data is writable
RUN mkdir -p /app/data && \
    chown -R app:app /app/data

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:8080/ || exit 1

LABEL version=$RELEASE_VERSION \
    description="PatchPanda Web Application" \
    maintainer="dkorecko"

USER app
ENTRYPOINT ["dotnet", "PatchPanda.Web.dll"]