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
- Send notifications to Discord about new versions (via webhook).
- Enqueue and run updates: when you choose to update, PatchPanda edits compose/.env files and runs `docker compose pull` and `docker compose up -d` for the target stack. You can also view live log.
- Support multiple release sources per app (primary and secondary repos) and merge release notes when appropriate.
- Ability to ignore a specific version to not clutter the UI.
- Update multiple applications at once.
- Manually override the detected GitHub repo if it's incorrect.

Planned / upcoming features

- Automatic non-breaking updates: a future enhancement will be able to apply updates automatically when the new release is classified as non-breaking. This is currently not allowed due to the beta nature.
- Ollama integration for additional security when detecting breaking changes.
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

- BASE_URL - The base URL of PatchPanda, this will be used for creating clickable URLs in notifications (e.g. `http://localhost:5093`)

GitHub

- GITHUB_USERNAME - GitHub username
- GITHUB_PASSWORD - GitHub personal access token (PAT) or password

Discord

- DISCORD_WEBHOOK_URL - Full Discord webhook URL used to post notifications

Notes about the GitHub token

- Create a Personal Access Token in GitHub (Settings → Developer settings → Personal access tokens). For public repositories `public_repo` should be sufficient; for private repositories use `repo`. Put the token into `GITHUB_PASSWORD` and your GitHub username into `GITHUB_USERNAME`.

## Run with Docker Compose (recommended for hosting)

Here is an example `docker-compose.yml` that runs PatchPanda. Save this next to the repo or adapt it for production (use secrets in production, not plain env vars):

```yaml
services:
  patchpanda:
    container_name: patchpanda-app
    image: ghcr.io/dkorecko/patchpanda:latest
    environment:
      - DISCORD_WEBHOOK_URL=https://discord.com/api/webhooks/... # use your discord webhook URL here
      - GITHUB_USERNAME=yourusername # use your GitHub username here
      - GITHUB_PASSWORD=yourtoken # use your GitHub personal access token here
      - BASE_URL=http://localhost:5093 # adjust to what URL you will use to access PatchPanda
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock:rw
      - /srv/www:/srv/www:rw # This should be a path which contains the compose files as part of its subdirectories. Meaning if your compose files are at /srv/www in different folders, this is what you would use. BOTH PATHS MUST BE THE SAME.
      - ./data:/app/data:rw # persistent data storage for SQLite
    ports:
      - "5093:8080" # adjust as needed
    restart: unless-stopped
```

Notes:

- For the container to inspect your host Docker state, mount the host's `/var/run/docker.sock` into the container. That gives the container the ability to list containers and run docker commands on the host.
- The second volume is for being able to access the compose files. PatchPanda will use the paths reported by the Docker engine, meaning the paths must be the same in its file system for everything to work properly.

Once the application is running, you can access it in your browser at `http://localhost:5093` (or the host you are using and the port you configured).

## Screenshots

![Image of the homepage](screenshots/home.png)
![Image of a single line with a pending update](screenshots/line.png)
![Image of the page with release notes and ability to update](screenshots/versions.png)
![Image of the ongoing update](screenshots/terminal.png)
![Image of the Discord notification](screenshots/discord.png)

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

- Notifications are posted to the configured Discord webhook. PatchPanda will chunk long messages (Discord limits message length) and mark versions as notified once it posted them.

## Troubleshooting & tips

- If PatchPanda can't find the GitHub repo to your container, make sure to include it in one of the labels.
- Running PatchPanda inside a container with `/var/run/docker.sock` mounted gives it powerful control over the host Docker.

## Contributing

Contributions welcome. Please run tests and follow the project's coding conventions. Open an issue if you spot a bug or would like to see a feature.

## License & contact

No license is included by default. Add an appropriate LICENSE file if you intend to share this project publicly.
