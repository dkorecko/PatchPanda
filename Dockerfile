FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["PatchPanda.Web/PatchPanda.Web.csproj", "PatchPanda.Web/"]
RUN dotnet restore "PatchPanda.Web/PatchPanda.Web.csproj"
COPY . .
RUN dotnet build "PatchPanda.Web/PatchPanda.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PatchPanda.Web/PatchPanda.Web.csproj" -c Release -o /app/publish

FROM base AS final
# Install utilities needed for installing the Docker CLI
RUN apt-get update && \
    apt-get install -y \
        ca-certificates \
        curl \
        gnupg \
        lsb-release && \
    # Add Docker's official GPG key
    mkdir -m 0755 -p /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/debian/gpg | gpg --dearmor -o /etc/apt/keyrings/docker.gpg && \
    # Set up the stable repository
    echo \
      "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/debian \
      $(lsb_release -cs) stable" | tee /etc/apt/sources.list.d/docker.list > /dev/null && \
    # Install the Docker CLI only
    apt-get update && \
    apt-get install -y docker-ce-cli && \
    # Clean up
    rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PatchPanda.Web.dll"]