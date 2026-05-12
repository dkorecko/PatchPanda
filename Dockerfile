# PatchPanda.Web - Multi-Stage Docker Build (2026 Standards)
# Support: amd64, arm64, arm/v7
# Features: Non-root user, Security Patches, Docker-in-Docker CLI, OIDC Ready

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
    apt-get install -y --no-install-recommends curl ca-certificates && \
    rm -rf /var/lib/apt/lists/*
USER app

# ============================================================================
# STAGE 2: Build (Cross-Platform)
# ============================================================================
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGETARCH
WORKDIR /src

# Copy project file and restore specifically for the target architecture
COPY ["PatchPanda.Web/PatchPanda.Web.csproj", "PatchPanda.Web/"]

RUN export DOTNET_ARCH=$(case ${TARGETARCH} in \
    "amd64") echo "x64" ;; \
    "arm64") echo "arm64" ;; \
    "arm") echo "arm" ;; \
    *) echo "ERROR: Unsupported architecture '${TARGETARCH}'"; exit 1 ;; \
    esac) && \
    dotnet restore "PatchPanda.Web/PatchPanda.Web.csproj" \
    --runtime "linux-${DOTNET_ARCH}"

# Copy remaining source code
COPY . .

# Publish the Release binary
RUN export DOTNET_ARCH=$(case ${TARGETARCH} in \
    "amd64") echo "x64" ;; \
    "arm64") echo "arm64" ;; \
    "arm") echo "arm" ;; \
    esac) && \
    dotnet publish "PatchPanda.Web/PatchPanda.Web.csproj" \
    --configuration Release \
    --output /app/publish \
    --runtime "linux-${DOTNET_ARCH}" \
    --no-restore \
    --self-contained false

# ============================================================================
# STAGE 3: Final Production Image
# ============================================================================
FROM base AS final

# Install Docker CLI with GPG verification (Required for PatchPanda functionality)
USER root
RUN apt-get update && \
    apt-get install -y gnupg lsb-release && \
    mkdir -m 0755 -p /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/debian/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian bookworm stable" | \
    tee /etc/apt/sources.list.d/docker.list > /dev/null && \
    apt-get update && \
    apt-get install -y docker-ce-cli && \
    # Create data directory and fix permissions for non-root 'app' user
    mkdir -p /app/data && \
    chown -R app:app /app/data && \
    # Cleanup
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Metadata and Environment
ARG RELEASE_VERSION
ENV APP_VERSION=$RELEASE_VERSION
ENV DOTNET_EnableDiagnostics=0
LABEL version=$RELEASE_VERSION \
    description="PatchPanda Web Application" \
    maintainer="dkorecko"

# Ensure we run as the non-root user
USER app

ENTRYPOINT ["dotnet", "PatchPanda.Web.dll"]