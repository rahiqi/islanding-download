# Islanding Download Portal

Scalable download portal: **Dispatcher** (entry point + UI) enqueues URLs to Kafka; **Download agents** (islands) consume the queue and download, reporting progress back via Kafka. SSE streams progress to the React UI in real time.

## Architecture

- **Dispatcher** (.NET 10, Web API + React): accepts download URLs, produces to `download-queue`, consumes `download-progress` and `agent-heartbeat`, serves the UI and exposes SSE for live progress.
- **Agents** (.NET 10): consume `download-queue`, download files, produce progress to `download-progress` and heartbeats to `agent-heartbeat`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) (for frontend build/dev)
- [Kafka](https://kafka.apache.org/) (e.g. local or Docker)

### Create Kafka topics (one-time)

```bash
# Example with Kafka in Docker or local install
kafka-topics --create --topic download-queue       --bootstrap-server localhost:9092 --partitions 4
kafka-topics --create --topic download-progress   --bootstrap-server localhost:9092 --partitions 4
kafka-topics --create --topic agent-heartbeat     --bootstrap-server localhost:9092 --partitions 2
```

## Run

1. **Start Kafka** (e.g. `docker run -d -p 9092:9092 apache/kafka` or your existing cluster).

2. **Start the Dispatcher** (API + built React UI):

   ```bash
   cd dispatcher/dispatcher
   dotnet run
   ```

   - API + UI: http://localhost:5062  
   - Swagger: http://localhost:5062/openapi/v1.json

3. **Start one or more Agents** (each can run on a different machine/location):

   ```bash
   cd agent/downloader-agent
   dotnet run
   ```

4. Open http://localhost:5062, add a download URL, and watch an agent pick it up and report progress.

## Frontend development (optional)

- **Build into dispatcher wwwroot** (so the dispatcher serves the latest UI):

  ```bash
  cd dispatcher/frontend
  npm install
  npm run build
  ```

- **Dev with hot reload** (Vite proxies `/api` and `/openapi` to the dispatcher):

  ```bash
  cd dispatcher/frontend
  npm run dev
  ```

  Then open the URL Vite prints (e.g. http://localhost:5173). Ensure the dispatcher is running on port 5062.

## Configuration

- **Dispatcher**: `dispatcher/dispatcher/appsettings.json` — `Kafka`, `KafkaConsumer`, `AgentHeartbeat` (bootstrap servers, topic names, group ids).
- **Agent**: `agent/downloader-agent/appsettings.json` — `DownloadWorker`, `Heartbeat` (bootstrap servers, topics, `ProgressIntervalMs`, `IntervalSeconds`).

Override with environment variables or `appsettings.Development.json` per environment.

## Docker deployment

From the repository root:

```bash
docker compose up -d
```

- **Redpanda** (Kafka API–compatible): internal port 9092 for other containers, external **19092** if you need to connect from the host (e.g. CLI tools).
- **redpanda-init**: one-off job that creates topics `download-queue`, `download-progress`, `agent-heartbeat` with the desired partition counts.
- **redpanda-console**: Web UI for Redpanda (topics, messages, consumer groups) at **http://localhost:8080**.
- **dispatcher**: API + React UI (download portal) on port **8084** (open http://localhost:8084).
- **agent**: 2 replicas by default; scale with `docker compose up -d --scale agent=4`.

The dispatcher Dockerfile builds the React app in a stage, so no need to run `npm run build` first. To rebuild after code changes: `docker compose build --no-cache dispatcher` (or `agent`), then `docker compose up -d`.

### Raspberry Pi 32-bit (armhf) — agent only

The **dispatcher** runs on **64-bit hosts only** (amd64/arm64). On 32-bit Raspberry Pi, run only the **agent** as a download island.

Use the same compose entry point and set **`BUILD_ARM32=1`** to use the Pi Dockerfile and platform:

- **Main (64-bit):**  
  `./agent/compose.sh up -d`  
  or  
  `docker compose -f agent/docker-compose.yml up -d`

- **Raspberry Pi 32-bit:**  
  `BUILD_ARM32=1 ./agent/compose.sh up -d`  
  or  
  `docker compose -f agent/docker-compose.yml -f agent/docker-compose.arm32.yml up -d`

On Windows (PowerShell):  
`$env:BUILD_ARM32=1; .\agent\compose.ps1 up -d`

When `BUILD_ARM32=1`, the agent uses `DockerfileRaspberryPi2` and `platform: linux/arm/v7`. The main compose uses `Dockerfile` and the host platform.

**Portainer (stack from GitHub):** Use a single compose file and set stack env vars for 32-bit:

1. Add stack → Repository URL: your repo, Compose path: **`agent/docker-compose.yml`**.  
   (Build context is the repo root, so the path must be the path *inside* the repo; Portainer usually clones the repo and uses the path relative to root.)

2. For **Raspberry Pi 32-bit**, add these **Environment variables** in the stack (before deploy):
   - `AGENT_PLATFORM` = `linux/arm/v7`
   - `AGENT_DOCKERFILE` = `RaspberryPi2`

3. Also set required vars: `DISPATCHER_URL`, `KAFKA_BOOTSTRAP_SERVERS`, and optionally `AGENT_NAME`, `AGENT_LOCATION`, `AGENT_ID`.

4. Deploy. The same compose file builds and runs the 32-bit image when those two vars are set.
