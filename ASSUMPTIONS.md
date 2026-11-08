# Assumptions & Architecture Decisions

This document details the architectural decisions, assumptions, and solutions implemented for Section 1.1, 1.5, and the 1.6 Bonus requirement.

---

## 1. Concurrency Safety Across Multiple Processes (Section 1.6 Bonus)
**Requirement:** Even if two or more backend processes run concurrently on different ports using the same database, `held + confirmed` must never exceed `totalSeats`.

### How it was guaranteed:
1. **PostgreSQL Row-Level Exclusive Lock (`SELECT ... FOR UPDATE`):**
   When a hold request is received on `POST /classes/{classId}/holds`, the transaction acquires an exclusive row lock on the class record:
   ```sql
   SELECT * FROM classes WHERE id = @classId FOR UPDATE;
   ```
   Because row-level locks are managed directly by PostgreSQL's lock manager, any secondary backend process attempting to process a hold on the same `classId` will block until the first transaction commits or aborts.
2. **Transaction Isolation:**
   The seat availability check (`confirmedSeats + activeHeldCount < totalSeats`) and insertion into `seat_holds` occur atomically inside the locked transaction.
3. **Partial Unique Database Index:**
   To enforce Rule 3 (*"একই সময়ে একটার বেশি active আবেদন বা নিশ্চিত ভর্তি রাখতে পারবে না"*), a unique partial index is applied:
   ```sql
   CREATE UNIQUE INDEX idx_unique_active_student_hold 
   ON seat_holds (class_id, student_id) 
   WHERE status IN ('HELD', 'CONFIRMED');
   ```
   This guarantees at the database engine level that no race condition can ever insert a duplicate active hold for the same student in the same class.

---

## 2. Automatic Hold Expiration (Rule 2)
- **Background Worker (`HoldExpirationWorker`):** Runs every 1 second in the background. It finds all `status = 'HELD'` records where `expires_at <= NOW()`, updates them to `EXPIRED`, and purges the corresponding Redis cache keys.
- **On-Read Real-Time Precision:** To guarantee zero lag between 1-second ticks, all `GET` and `POST` queries additionally filter by `expires_at > NOW()`. If an expired record is read before the worker cleans it up, the API dynamically renders its status as `EXPIRED`.

---

## 3. High Concurrency Payload Buffering & Redis Caching
- **GET API Caching:** All `GET /classes`, `GET /classes/{classId}`, and `GET /me/holds` endpoints utilize a fast Redis cache with short TTL (2 seconds) and targeted write-invalidation.
- **POST Rate Limiting:** Enforced via ASP.NET Core RateLimiter partitioned by `X-Student-Id` to prevent single-user automated bot spam while queueing legitimate bursts.
- **POST Redis Enqueueing:** Every incoming hold request is enqueued into Redis (`hold_queue:{classId}`) for audit trail and ingestion buffering before committing to PostgreSQL.

---

## 4. API Contract & Pagination Strategy
- To strictly maintain automated test compliance, `GET /classes` and `GET /me/holds` return root JSON arrays `[...]` as defined in the specification.
- Pagination is supported via optional `?page=1&pageSize=10` query parameters, with total count metadata returned cleanly in the `X-Total-Count` HTTP response header.

