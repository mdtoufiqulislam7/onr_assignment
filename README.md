# School Admission System (অনলাইন স্কুল ভর্তি সিস্টেম)

A high-concurrency School Admission System designed for first-come-first-served (FCFS) limited-seat admissions with automatic hold expirations, Redis caching and queue buffering, PostgreSQL row-level concurrency locking, and a modern React UI.

---

## Tech Stack
- **Backend:** C# (.NET 8 Web API)
- **Database:** PostgreSQL 16
- **Caching & Buffer:** Redis 7
- **Frontend:** React 18, TypeScript, Tailwind CSS, Vite
- **Containerization:** Docker & Docker Compose

---

## 🚀 How to Run the Project with Docker

Only Docker is needed. Run the following command from the repository root:

```bash
docker compose up --build
```

### Services & URLs:
- **Frontend UI:** [http://localhost:3000](http://localhost:3000)
- **Backend Web API:** [http://localhost:8080](http://localhost:8080)
- **API Swagger Documentation:** [http://localhost:8080/swagger](http://localhost:8080/swagger)
- **PostgreSQL Database:** `localhost:5432` (`admission_db`)
- **Redis Server:** `localhost:6379`

To stop the containers:
```bash
docker compose down
```
*(Data persists across restarts inside the `pgdata` docker volume).*

---

## 🧪 How to Run Automated & Concurrency Tests

### Backend Tests:
To run the backend integration and high-concurrency automated tests using Docker (no local .NET SDK required):

```bash
docker run --rm -v "${PWD}/backend:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test
```

Or if you have the .NET 8 SDK installed locally:
```bash
cd backend
dotnet test
```

#### Backend Tests Included:
1. **Class Creation & Available Seats Calculation:** Verifies accurate seat counting and cache synchronization.
2. **Hold Confirmation & Idempotency:** Asserts that only a student's own active hold can be confirmed and repeated confirms increase `confirmedSeats` only once.
3. **High Concurrency Test:** Dispatches 30+ simultaneous hold requests for a class with only 5 seats. Verifies that `held + confirmed` **never** exceeds `totalSeats`, returning `409 Conflict` to subsequent applicants.

### Frontend Tests (Section 1.6 Bonus):
To run the frontend component and UI workflow tests:

```bash
cd frontend
npm test
```

---

## ⚙️ Environment Variables (`.env` & `.env.example`)

Environment configuration files (`.env` and `.env.example`) are provided in the root, `backend/`, and `frontend/` directories:
- `DATABASE_URL`: Connection string for PostgreSQL (default: `Host=postgres;Port=5432;Database=admission_db;Username=postgres;Password=postgres`).
- `REDIS_URL`: Host and port for Redis (default: `redis:6379`).
- `HOLD_DURATION_SECONDS`: Seat hold duration before automatic release (default: `600` seconds / 10 minutes).
- `VITE_API_URL`: Backend base URL consumed by the React UI (default: `http://localhost:8080`).

---

## 📱 User Interface Features
- **Student ID Header Bar:** Persists student's 17-digit Birth Registration Number in `localStorage` across reloads and tab closures.
- **School Admin Screen:** Allows creating classes with name, shift, and total seat limits.
- **Live Class Seat Board:** Auto-polls seat changes every 3 seconds without page reload.
- **Active Hold Countdown & Auto-Expire:** Real-time countdown timer that changes status to `EXPIRED` and disables confirmation the second time expires without requiring a page refresh.

---

## 🌐 Production VPS Deployment Architecture (Cloudflare + Caddy + Multi-Port Scaling)

For high-traffic admission windows on a production VPS, this system is architected to scale horizontally across multiple ports:
1. **Cloudflare Edge Layer:** Protects the origin server with Bot Fight Mode, DDoS mitigation, and Edge Rate Limiting against automated scripts.
2. **Caddy Reverse Proxy & Load Balancer:** Runs on the VPS (`:80` / `:443` with auto-SSL) and load-balances incoming requests across multiple isolated .NET 8 backend instances running on separate ports (e.g., `localhost:8081`, `localhost:8082`, `localhost:8083`).
3. **Multi-Instance Concurrency Safety:** Because all backend instances share the same PostgreSQL (`SELECT ... FOR UPDATE` row-level locks) and Redis cluster, scaling across multiple ports maintains 100% seat count accuracy (`held + confirmed <= totalSeats`).

