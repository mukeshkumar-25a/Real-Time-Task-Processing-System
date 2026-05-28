## Build & Run

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (optional, recommended)

### Quick Start with Docker

```bash
docker compose up --build
```

API: `http://localhost:8080`  
SignalR hub: `ws://localhost:8080/hubs/tasks`

### Local Development (without Docker API container)

Start dependencies only:

```bash
docker compose up sqlserver redis -d
```

Run the API:

```bash
cd src/TaskManager.Api
dotnet run
```

Default local URL: `https://localhost:7xxx` (see `launchSettings.json`)

---

## Architecture

```
┌─────────────┐     REST API      ┌──────────────────┐
│   Client    │ ───────────────►  │  TaskManager.Api │
│  (Postman)  │ ◄── WebSocket ──  │   (Controllers)  │
└─────────────┘                   └────────┬─────────┘
                                           │
                    ┌──────────────────────┼──────────────────────┐
                    ▼                      ▼                      ▼
            ┌──────────────┐      ┌──────────────┐      ┌──────────────┐
            │  TaskService │      │  SignalR Hub │      │ Background   │
            │   (Core)     │      │  (real-time) │      │   Worker     │
            └──────┬───────┘      └──────────────┘      └──────┬───────┘
                   │                                            │
         ┌─────────┴─────────┐                         ┌────────┴────────┐
         ▼                   ▼                         ▼                 ▼
  ┌─────────────┐    ┌─────────────┐           ┌─────────────┐   ┌─────────────┐
  │  MS SQL     │    │   Redis     │           │ TaskProcessor│   │ Redis Queue │
  │  (persist)  │    │  (cache)    │           │  (retry)     │   │  (async)    │
  └─────────────┘    └─────────────┘           └─────────────┘   └─────────────┘
```

### Project Structure

| Project | Responsibility |
|---------|----------------|
| `TaskManager.Api` | HTTP endpoints, SignalR hub, DI composition |
| `TaskManager.Core` | Domain entities, DTOs, interfaces, business services |
| `TaskManager.Infrastructure` | Dapper (SQL Server), Redis cache & queue, background worker |

---

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/tasks` | Create a new task |
| `GET` | `/tasks/{id}` | Get task status by ID |
| `GET` | `/tasks` | List all tasks |

### Create Task

```http
POST /tasks
Content-Type: application/json
Idempotency-Key: unique-key-123

{
  "type": "email-send"
}
```

**Response:** `201 Created` (new task) or `200 OK` (idempotent replay)

### Task Response

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "type": "email-send",
  "status": "pending",
  "attempts": 0,
  "result": null,
  "errorMessage": null,
  "createdAt": "2026-05-28T10:00:00Z"
}
```

---

## State Transitions

```
                    ┌──────────┐
                    │ pending  │
                    └────┬─────┘
                         │ worker picks up
                         ▼
                  ┌─────────────┐
                  │ processing  │
                  └──────┬──────┘
                         │
           ┌─────────────┼─────────────┐
           ▼             ▼             ▼
    ┌──────────┐  ┌──────────┐  ┌──────────┐
    │ pending  │  │completed │  │  failed  │
    │ (retry)  │  └──────────┘  └──────────┘
    └──────────┘
```

| From | To | Condition |
|------|----|-----------|
| `pending` | `processing` | Background worker dequeues task |
| `processing` | `completed` | Processing succeeds (~70%) |
| `processing` | `pending` | Simulated failure, attempts < 3 |
| `processing` | `failed` | Simulated failure, attempts ≥ 3 |

---

## Retry Logic

- Each processing attempt increments the `attempts` counter.
- Processing takes a random **2–5 second** delay.
- ~**30%** of attempts fail randomly (configurable via `FailureProbability`).
- On failure:
  - If `attempts < MaxRetryAttempts` (default **3**): status returns to `pending`, task is re-enqueued.
  - If max attempts reached: status becomes `failed` with an error message.

Configuration (`appsettings.json` → `TaskProcessing`):

```json
{
  "MinProcessingDelaySeconds": 2,
  "MaxProcessingDelaySeconds": 5,
  "MaxRetryAttempts": 3,
  "FailureProbability": 0.30
}
```

---

## Idempotency

Duplicate task creation is prevented using the **`Idempotency-Key`** HTTP header.

1. Client sends `POST /tasks` with a unique `Idempotency-Key`.
2. The key is stored in `IdempotencyRecords` with a **unique index** in SQL Server.
3. If the same key is sent again, the existing task is returned with **`200 OK`** (no duplicate created).
4. Concurrent duplicate requests are handled safely: the database unique constraint ensures only one task is created; the loser reads and returns the winner's task.

---

## Redis Caching

- Task status is cached in Redis with a random TTL between **30–60 seconds**.
- `GET /tasks/{id}` checks cache first, then falls back to SQL Server.
- Cache is invalidated and refreshed on every status change.
- The background worker pushes updates via SignalR after each transition.

---

## Real-Time Updates (WebSockets / SignalR)

Connect to the hub at `/hubs/tasks` and subscribe to a task:

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:8080/hubs/tasks")
  .build();

await connection.start();
await connection.invoke("SubscribeToTask", taskId);

connection.on("TaskUpdated", (task) => {
  console.log("Status:", task.status);
});
```

---

## Queue System

Tasks are enqueued in a **Redis list** (`taskmanager:queue`) when created or retried. A `BackgroundService` worker dequeues and processes tasks asynchronously — similar in spirit to BullMQ but implemented with Redis primitives native to the .NET stack.

---

## Concurrency Safety

- **Idempotency:** SQL unique index on `IdempotencyKey`.
- **State transitions:** Atomic SQL `UPDATE ... WHERE Status = pending`.
- **Optimistic concurrency:** SQL Server `ROWVERSION` checked on every Dapper update.
- **Queue:** Redis list operations are atomic.

---

## Data Access (Dapper)

Persistence uses **Dapper** with raw SQL against MS SQL Server — no EF Core.

| Component | Purpose |
|-----------|---------|
| `ISqlConnectionFactory` | Creates `SqlConnection` instances from config |
| `TaskRepository` | All CRUD via parameterized Dapper queries |
| `DatabaseInitializer` | Creates `Tasks` and `IdempotencyRecords` tables on startup |

Tables are created automatically at startup if they do not exist. Schema is defined in `DatabaseInitializer.cs`.

---

## Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectionStrings:DefaultConnection` | — | MS SQL Server connection |
| `ConnectionStrings:Redis` | — | Redis connection |
| `TaskProcessing:MaxRetryAttempts` | 3 | Max processing attempts |
| `TaskProcessing:FailureProbability` | 0.30 | Simulated failure rate |
| `TaskProcessing:CacheTtlMinSeconds` | 30 | Min Redis cache TTL |
| `TaskProcessing:CacheTtlMaxSeconds` | 60 | Max Redis cache TTL |

---

## Postman Collection

Import `postman/TaskManager.postman_collection.json` into Postman.

Set the `baseUrl` variable to `http://localhost:8080` (Docker) or your local dev URL.

---

## Tech Stack

- **ASP.NET Core 9** — Web API
- **MS SQL Server 2022** — Persistence
- **Redis 7** — Cache & task queue
- **SignalR** — Real-time WebSocket updates
- **Dapper** — lightweight SQL micro-ORM for MS SQL Server
- **Docker Compose** — Local infrastructure
