# School Admission System

A take-home implementation using .NET 8, PostgreSQL, Entity Framework Core, React 18, Vite, and Docker Compose.

## Run the complete project

Requirements: Docker Desktop / Docker Engine with the Compose plugin. No local .NET, Node, or PostgreSQL installation is needed.

```bash
docker compose up --build
```

Open **http://localhost:8080**. The first build downloads the base images and dependencies. Keep the terminal open. Stop with `Ctrl+C`, then `docker compose down`. PostgreSQL data persists in the `school_data` volume. Do not use `docker compose down -v` unless you want to delete all admission data.

Optional configuration: copy `.env.example` to `.env` and set `POSTGRES_PASSWORD` and `HOLD_DURATION_SECONDS`. Default hold time is 600 seconds (10 minutes).

## API base URL

- Browser UI: `http://localhost:8080`
- API through UI proxy: `http://localhost:8080/api`
- Swagger (backend container is internal by default): to expose it for local API development, temporarily add `ports: ["8081:8080"]` to the backend service; then open `http://localhost:8081/swagger`.

The frontend API URL is a Vite build-time environment variable (`VITE_API_URL`). The Docker build sets it to `/api`; for another deployment, rebuild with a different value.

## API contract

| Method | Path | Purpose |
|---|---|---|
| POST | `/classes` | Create class/shift |
| GET | `/classes` | List classes with seat counts |
| GET | `/classes/{classId}` | Class seat summary |
| POST | `/classes/{classId}/holds` | Create a seat hold; requires `X-Student-Id` |
| GET | `/holds/{holdId}` | Read hold status |
| POST | `/holds/{holdId}/confirm` | Confirm admission; requires `X-Student-Id` |
| DELETE | `/holds/{holdId}` | Cancel a held application; requires `X-Student-Id` |
| GET | `/me/holds` | Current student's applications; requires `X-Student-Id` |

A successful hold returns 201. Full capacity, duplicate active/confirmed application, expired confirmation, and concurrency conflicts return 409. Invalid input returns 400. Hold statuses: `HELD`, `CONFIRMED`, `EXPIRED`, `CANCELLED`.

### Example requests

```bash
curl -X POST http://localhost:8080/api/classes \
  -H 'Content-Type: application/json' \
  -d '{"name":"Class 6 - Morning Shift","totalSeats":120}'

curl -X POST http://localhost:8080/api/classes/CLASS_UUID/holds \
  -H 'X-Student-Id: 20152691234567890'

curl -X POST http://localhost:8080/api/holds/HOLD_UUID/confirm \
  -H 'X-Student-Id: 20152691234567890'
```

## Automated tests

With the stack running, install Python's `requests` package and run:

```bash
python -m pip install requests
python -m unittest discover -s tests -v
```

The suite checks class creation and seat totals, hold + idempotent confirmation, cancellation releasing a seat, the one-active-application rule across different classes (including simultaneous requests), and a concurrency test that sends 25 simultaneous requests to a three-seat class and asserts that the system never overbooks.

## Project structure

```text
backend/SchoolAdmission.Api/   .NET 8 API, EF Core models, PostgreSQL
frontend/                      React 18 + Vite UI + nginx
 tests/concurrency_test.py     Concurrent request smoke test
docker-compose.yml             PostgreSQL + API + UI orchestration
ASSUMPTIONS.md                 Decisions where the brief is silent
AI_USAGE.md                    AI assistance disclosure
```

