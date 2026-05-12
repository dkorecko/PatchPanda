# PatchPanda.Web - Multi-Stage Docker Build
# Multi-architecture support (amd64, arm64, arm)
# Security: Non-root user, security patches, GPG verification

ARG BUILDPLATFORM

# ============================================================================
# STAGE 1: Base Runtime
# ============================================================================

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

WORKDIR /app
EXPOSE 8080

USER root
RUN apt-get update && \
    apt-get upgrade -y && \
    apt-get install -y curl && \
    rm -rf /var/lib/apt/lists/*
USER app

# ============================================================================
# STAGE 2: Build
# ============================================================================

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGETARCH
WORKDIR /src

COPY ["PatchPanda.Web/PatchPanda.Web.csproj", "PatchPanda.Web/"]

# Restore dependencies for target architecture (amd64→x64, arm64→arm64, arm→arm)
RUN export DOTNET_ARCH=$(case ${TARGETARCH} in \
    "amd64") echo "x64" ;; \
    "arm64") echo "arm64" ;; \
    "arm") echo "arm" ;; \
    *) echo "ERROR: Unsupported architecture '${TARGETARCH}'"; exit 1 ;; \
    esac) && \
    dotnet restore "PatchPanda.Web/PatchPanda.Web.csproj" \
    --runtime "linux-${DOTNET_ARCH}"

COPY . .

# Publish Release build for target architecture
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
# STAGE 3: Production Runtime with Docker CLI
# ============================================================================

FROM base AS final

USER root

# Install Docker CLI with GPG verification
RUN apt-get update && \
    apt-get install -y \
    ca-certificates \
    curl \
    gnupg \
    lsb-release && \
    mkdir -m 0755 -p /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/debian/gpg | \
    gpg --dearmor -o /etc/apt/keyrings/docker.gpg && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian bookworm stable" | \
    tee /etc/apt/sources.list.d/docker.list > /dev/null && \
    apt-get update && \
    apt-get install -y docker-ce-cli && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

USER app

ARG RELEASE_VERSION
ENV APP_VERSION=$RELEASE_VERSION
LABEL version=$RELEASE_VERSION \
    description="PatchPanda Web Application" \
    maintainer="dkorecko"

ENTRYPOINT ["dotnet", "PatchPanda.Web.dll"]
# action trigger...
