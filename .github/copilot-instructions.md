# PatchPanda Copilot Instructions

## Project Overview

PatchPanda is a Docker container update management system built with .NET 10 and Blazor Server. It monitors Docker Compose stacks, detects available updates via GitHub releases, and automates version upgrades.

## Architecture

### Core Components

- **DockerService**: Interfaces with Docker daemon via Docker.DotNet to list containers and execute updates
- **VersionService**: Queries GitHub releases using Octokit to check for new versions
- **UpdateService**: Orchestrates container updates by modifying compose files and env files
- **Background Services**: Two hosted services run continuously:
  - `VersionCheckHostedService`: Polls for updates every 2 hours
  - `UpdateBackgroundService`: Processes update queue using Channel-based async pattern

### Data Model

- **ComposeStack**: Represents a docker-compose project with multiple containers
- **Container**: Individual service within a stack, tracks version, GitHub repo, and update metadata
- **MultiContainerApp**: Groups related containers (e.g., `app-web`, `app-worker`) detected by naming patterns
- **AppVersion**: Represents available versions from GitHub releases

### Update Flow

1. Background service discovers containers from Docker daemon
2. Extracts GitHub repo from image labels or OCI annotations
3. Queries GitHub API for releases matching regex patterns
4. User triggers update → enqueued to `UpdateQueue` (Channel-based)
5. Background worker processes updates by modifying compose/env files
6. Executes `docker compose pull && docker compose up -d`

## Code Conventions

### GlobalUsings Pattern

Always add namespace imports to `PatchPanda.Web/GlobalUsings.cs` instead of per-file usings. Never use deep references like `Microsoft.EntityFrameworkCore.Storage.ValueConversion` in code - add to GlobalUsings.

### No Comments Policy

Use self-documenting variable/method names instead of comments. For example: `cleanedVersion1` instead of `version1 // cleaned`.

### Conditional Compilation

Use `#if DEBUG` for development-specific behavior:

- Docker socket: `npipe://./pipe/docker_engine` (Windows dev) vs `unix:///var/run/docker.sock` (production)
- App name suffix: `[DEV]` appended in debug builds

### Entity Framework Patterns

- Use `IDbContextFactory<DataContext>` for scoped/background service contexts
- All entities inherit from `AbstractEntity` (provides `Id` property)
- Complex types like `Tuple<string, string>` stored via custom `ValueConverter` (see `DataContext.OnModelCreating`)
- Migrations live in `Migrations/` folder

### Service Registration

Services follow consistent DI patterns in `Program.cs`:

- Scoped: `DockerService`, `VersionService`, `UpdateService`, `DiscordService`
- Singleton: `UpdateRegistry`, `UpdateQueue`
- Hosted: Background processing services

## Development Workflows

### Running Locally

1. Ensure MySQL/MariaDB running (connection string from env vars: `DB_HOST`, `DB_NAME`, `DB_USERNAME`, `DB_PASSWORD`)
2. Run in DEBUG mode - automatically uses Windows named pipe for Docker
3. EF migrations auto-apply on startup via `dbContext.Database.Migrate()`

### Adding New Background Services

Implement `IHostedService`, inject `IServiceScopeFactory` (not direct scoped services), create scope in `DoWork` methods. See `VersionCheckHostedService` pattern.

### Version Detection Logic

Version comparison uses `VersionHelper` static methods:

- `BuildRegexFromVersion()`: Generates regex from example version (e.g., `v1.2.3` → `^v\d+\.\d+\.\d+$`)
- `IsNewerThan()`: Semantic version comparison by parsing numeric segments
- Handles special formats: `@sha256:...`, `-r123`, `-ls456` suffixes

### Update Regex Patterns

Two regex types per container:

- **Regex**: Matches docker image tags to determine current version
- **GitHubVersionRegex**: Filters GitHub release tags/names for valid versions

## Key Files

- `Program.cs`: DI setup, EF migration, service registration
- `Services/DockerService.cs`: Docker socket interaction, container discovery
- `Services/VersionService.cs`: GitHub API integration with rate limit handling
- `Helpers/MultiContainerAppDetector.cs`: Logic for grouping related containers by naming patterns
- `Helpers/UpdateQueue.cs`: Channel-based async queue for background processing
- `Db/DataContext.cs`: EF context with custom value converters for complex types

## Common Patterns

- Use `await using var db = _dbContextFactory.CreateDbContext()` for database access in async methods
- GitHub rate limiting handled via `RateLimitException` - credentials optional via env vars
- Container updates modify compose YAML or `.env` files in-place using regex replacement
- Multi-container apps detected by: shared prefix (`app-web`, `app-db`), shared GitHub repo, or substring matching
