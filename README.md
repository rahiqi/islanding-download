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

- **Kafka** (KRaft): port 9092, healthcheck before other services start.
- **kafka-init**: one-off job that creates topics `download-queue`, `download-progress`, `agent-heartbeat` with the desired partition counts.
- **dispatcher**: API + React UI on port **8080** (open http://localhost:8080).
- **agent**: 2 replicas by default; scale with `docker compose up -d --scale agent=4`.

The dispatcher Dockerfile builds the React app in a stage, so no need to run `npm run build` first. To rebuild after code changes: `docker compose build --no-cache dispatcher` (or `agent`), then `docker compose up -d`.
