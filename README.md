# PatchPanda

PatchPanda is a self-hostable Docker Compose stack update manager built with .NET 10 and Blazor Server. It scans your existing Docker Compose stacks, monitors GitHub releases for new versions, groups related containers, and helps you review and apply updates while keeping you in control.

This README covers what PatchPanda can do, what it intentionally doesn't do, how to self-host it (locally and with Docker Compose), required environment variables (Discord webhook and GitHub credentials are mandatory), and a brief comparison to other updater tools.

## What PatchPanda can do (today)

- Discover running Docker Compose projects and list services and their current image tags.
- Extract GitHub repository information from image labels / OCI annotations and query GitHub releases.
- Build heuristics and regexes to match release tags and filter valid version candidates.
- Determine whether a release contains any breaking changes.
- Track discovered newer versions in a database and show release notes in the UI.
- Group related services into multi-container apps (for example `app-web` + `app-worker`).
- Send notifications to any Apprise URL or Discord natively about new versions (via webhook).
- Enqueue and run updates: when you choose to update, PatchPanda edits compose/.env files and runs `docker compose pull` and `docker compose up -d` for the target stack. You can also view live log.
- Support multiple release sources per app (primary and secondary repos) and merge release notes when appropriate.
- Ability to ignore a specific version to not clutter the UI.
- Update multiple applications at once.
- Manually override the detected GitHub repo if it's incorrect.
- Works with both Linux and Windows.
- Integrate with Portainer API to update its managed compose stacks.
- AI-powered release note summarization and breaking change detection using Ollama or compatible LLM API.
- Contains error handling, meaning if an update fails, it will do its best to revert to a working version. You also have a page available of all the past updates to view the stdout/stderr of each update operation.

Planned / upcoming features

- Automatic non-breaking updates: a future enhancement will be able to apply updates automatically when the new release is classified as non-breaking. This is currently not allowed due to the beta nature.
- Ability for non-technical users of your server to subscribe to updates from specific containers, which will be provided in a simple and understandable manner.

Why this is different from Watchtower / DockGe / similar

- PatchPanda is release-oriented: it reads GitHub releases and their notes, not just the latest image on a registry. That gives you release notes, pre-release awareness, and a chance to inspect breaking-change markers.
- It understands multi-container apps and groups related services for coordinated updates.
- It edits your existing docker-compose files (or .env) to update tags and then runs the regular compose workflow, so you keep full control and can still edit your compose files manually at any time.
- Tools like Watchtower focus on automatically pulling new images and restarting containers when an image changes. PatchPanda aims to be more conservative and review-first - designed for environments where release notes and coordinated updates matter.

## Important: Beta & safety warning

> WARNING - BETA SOFTWARE
>
> PatchPanda is beta software. It is provided as-is with no guarantees. Do not rely on it for critical production automation without testing. You should always have backups and a recovery plan when using any automated update tool. It's also rough around the edges and not specifically designed for responsiveness. The design will change.
>
> This software DOES NOT cover all edge cases and all possible scenarios. Care should be taken when allowing an upgrade. The app shows the update plan which you can verify before it's actually executed. It's being released to get feedback from potential users.
>
> That said: PatchPanda operates on your existing docker-compose files - it does not invent or replace your deployment manifests. If anything goes wrong you can still manually edit your compose files or roll back to previous images.

## Environment variables

PatchPanda requires the following env vars to function.

General

- BASE_URL - The base URL of PatchPanda, this will be used for creating clickable URLs in notifications (e.g. http://localhost:5093)

GitHub

- GITHUB_USERNAME - GitHub username
- GITHUB_PASSWORD - GitHub personal access token (PAT) or password

Notifications

- DISCORD_WEBHOOK_URL - (optional) Full Discord webhook URL used to post notifications
- APPRISE_API_URL - (optional) An Apprise API URL to send notifications to any services Apprise supports (e.g. email, Telegram, etc).
- APPRISE_NOTIFICATION_URLS - (optional) A comma-separated list of Apprise notification URLs to send notifications to any services Apprise supports (e.g. email, Telegram, etc). For specific formats, take a look at the [Apprise documentation](https://github.com/caronc/apprise/wiki#notification-services).

AI Summarization (Ollama or compatible LLM API)

- OLLAMA_URL - (optional) Base URL to your Ollama or compatible LLM API (example: `http://localhost:11434`). If set, PatchPanda will use this endpoint to generate AI-powered summaries and breaking change detection for release notes. If not set, the feature is disabled and the app will work as usual.
- OLLAMA_MODEL - (optional) Model name to use for summarization.

Portainer

- PORTAINER_URL - (optional) Base URL to your Portainer instance (example: `http://portainer:9000`). If provided and a stack does not expose a local compose config path, PatchPanda will assume the stack is Portainer-managed and use the Portainer API to read/update the compose file.
- PORTAINER_USERNAME - (optional) Username for Portainer API authentication, must be provided if PORTAINER_URL is provided.
- PORTAINER_PASSWORD - (optional) Password for Portainer API authentication, must be provided if PORTAINER_URL is provided.

If you set the `OLLAMA_URL` environment variable, PatchPanda will use an Ollama-compatible LLM API to generate a short, user-friendly summary and breaking change detection for each new version's release notes. This summary is shown in the UI and included in notifications. If the variable is not set, the feature is disabled and PatchPanda will work as usual.

You can use any LLM API that supports the Ollama API standard for text generation. The model used can be set with `OLLAMA_MODEL`.

When Portainer vars are present PatchPanda will authenticate to Portainer and use the Portainer API to fetch and update stack files for stacks that do not expose a `ConfigFile` path via Docker labels. The service stores and re-uses the JWT returned by Portainer for API requests.

Notes about the GitHub token

- Create a Personal Access Token in GitHub (Settings → Developer settings → Personal access tokens). For public repositories `public_repo` should be sufficient; for private repositories use `repo`. Put the token into `GITHUB_PASSWORD` and your GitHub username into `GITHUB_USERNAME`.

## Run with Docker Compose (recommended for hosting)

Here is an example `docker-compose.yml` that runs PatchPanda and Apprise. Save this next to the repo or adapt it for production (use secrets in production, not plain env vars, correct paths for volumes, etc...):

```yaml
services:
  ### THIS IS OPTIONAL
  apprise-api: # You can bring your own Apprise API (or completely not use it), just make sure to update the environment variable for PatchPanda
    image: caronc/apprise:latest
    container_name: apprise-api
    environment:
      - APPRISE_STATEFUL_MODE=disabled
    ports:
      - 6603:8000
  ### OPTIONAL ENDS HERE

  patchpanda:
    container_name: patchpanda-app
    image: ghcr.io/dkorecko/patchpanda:latest
    environment:
      - APPRISE_API_URL=http://apprise-api:8000 # optional, if you run an Apprise API and want to use it for notifications
      - APPRISE_NOTIFICATION_URLS=discord://webhook_id/webhook_token,mailto://user:password@gmail.com # optional, comma-separated list of Apprise notification URLs
      - DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/... # optional, use your discord webhook URL here for the direct discord integration
      - GITHUB_USERNAME=yourusername # use your GitHub username here
      - GITHUB_PASSWORD=yourtoken # use your GitHub personal access token here
      - BASE_URL=http://localhost:5093 # adjust to what URL you will use to access PatchPanda
      # - PORTAINER_URL=http://portainer:9000 # if you wish to include stacks fully managed by Portainer
      # - PORTAINER_USERNAME=admin # if you wish to include stacks fully managed by Portainer
      # - PORTAINER_PASSWORD=CHANGEME # if you wish to include stacks fully managed by Portainer
      # - OLLAMA_URL=http://localhost:11434 # optional, if you wish to use Ollama or compatible LLM API for release note summarization
      # - OLLAMA_MODEL=llama3 # optional, model name to use for summarization
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:rw # This can remain, no matter whether you're using Docker on Linux or Windows
      # DOCKER ON LINUX VARIANT
      - /srv/www:/srv/www:rw # This should be a path which contains the compose files as part of its subdirectories. For PatchPanda to discover compose files, the container path should match the host path. This is not a general Docker Compose requirement.
      # DOCKER ON WINDOWS VARIANT
      # /c/Users/PC/testing-compose:/c/Users/PC/testing-compose:rw # This should be a path which contains the compose files as part of its subdirectories. For PatchPanda to discover compose files, the container path should match the host path. This is not a general Docker Compose requirement. Make sure to format the paths like this, given that your initial path was C:\Users\PC\testing-compose.
      - ./data:/app/data:rw # persistent data storage for SQLite
    ports:
      - "5093:8080" # adjust as needed
    restart: unless-stopped
```

IMPORTANT NOTES:

- For the container to inspect your host Docker state, mount the host's `/var/run/docker.sock` into the container. That gives the container the ability to list containers and run docker commands on the host.
- The second volume is for being able to access the compose files. PatchPanda will use the paths reported by the Docker engine, meaning the paths must be the same in its file system for everything to work properly.
- The Apprise API container is completely removable if you either don't wish to use Apprise or want to use the native Discord notifications. It's included here to make it as simple as possible for people to setup the app.

Once the application is running, you can access it in your browser at `http://localhost:5093` (or the host you are using and the port you configured).

## Screenshots

![Image of the homepage](screenshots/home.png)
![Image of a single line with a pending update](screenshots/line.png)
![Image of the page with release notes and ability to update](screenshots/versions.png)
![Image of the ongoing update](screenshots/terminal.png)
![Image of the Discord notification](screenshots/discord.png)

## Homepage widget

Here's the example configuration to add a PatchPanda widget to your Homepage instance:

```yaml
- PatchPanda:
    container: patchpanda-app
    href: http://your-url:5091 # Change this based on your setup
    description: Compose updater and monitoring
    widget:
      type: customapi
      url: http://patchpanda-app:8080/api/info # You may need to change the hostname/port based on your setup
      refreshInterval: 600000 # 10 minutes
      mappings:
        - field: stackCount
          label: Stack count
          format: number
        - field: containerCount
          label: Container count
          format: number
        - field: toBeUpdatedContainersCount
          label: Containers with updates
          format: number
```

## Run locally (development)

Set env vars in PowerShell and run:

```powershell
$env:DISCORD_WEBHOOK_URL = "https://discord.com/api/webhooks/..."
$env:GITHUB_USERNAME = "yourusername"
$env:GITHUB_PASSWORD = "your_personal_access_token"
dotnet watch --project PatchPanda.Web
```

On Windows development builds PatchPanda uses a named pipe (`npipe://./pipe/docker_engine`) to talk to Docker; make sure Docker Desktop is running.

## Database / Migrations

PatchPanda runs EF Core migrations on startup when the DB is relational. To work with migrations locally:

```powershell
dotnet ef migrations add <Name> --project PatchPanda.Web --startup-project PatchPanda.Web
dotnet ef database update --project PatchPanda.Web --startup-project PatchPanda.Web
```

## How PatchPanda detects repos and versions

- It extracts GitHub repo info from image labels / OCI annotations if available, or from the image name where possible.
- For each container it builds a version regex from the currently used tag, and queries GitHub releases using a configured regex (per-container) to pick valid releases.
- Secondary repos: some apps publish release notes or build artifacts in a second repository; PatchPanda automatically looks up secondary repos and merges additional release notes into the primary app's release notes.

## Notifications

- Notifications are posted to the configured Discord webhook and Apprise notification URLs. PatchPanda will chunk long messages (Discord limits message length) and mark versions as notified once it posted them.

## Troubleshooting & tips

- If PatchPanda can't find the GitHub repo to your container, make sure to include it in one of the labels.
- Running PatchPanda inside a container with `/var/run/docker.sock` mounted gives it powerful control over the host Docker.

## Community

If you want to participate more actively, provide more feedback, contribute, have a discussion, questions or need help, you can also reach out via our [Discord](https://discord.gg/9Vezk3ve8q).

## Contributing

Contributions welcome. Please run tests and follow the project's coding conventions. Open an issue if you spot a bug or would like to see a feature.

## License & contact

Released under the MIT license.
