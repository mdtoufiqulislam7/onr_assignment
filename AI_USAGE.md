# AI Usage & Verification Log

This document records the use of AI tools during the development of this take-home assignment, where AI made errors or oversights, and how they were detected and resolved.

---

## 1. AI Tools Used
- **Google Antigravity Agent:** Used for project scaffolding, architectural analysis from the Bengali specification PDF, boilerplate code generation for .NET 8 Web API, React TypeScript components, and Docker Compose configuration.

---

## 2. AI Mistakes & How They Were Corrected

### Issue 1: Pagination Response Envelope vs Automated Test Contract
- **AI Suggestion:** The initial AI proposal suggested returning a standard paginated response envelope:
  ```json
  {
    "data": [...],
    "totalCount": 120,
    "page": 1,
    "pageSize": 10
  }
  ```
- **Error:** Section 1.4 of the specification strictly mandates that `GET /classes` and `GET /me/holds` return root JSON arrays `[...]` without altering field names, warning: *"Contract হুবহু মানতে হবে, কারণ আমরা UI-এর পাশাপাশি সরাসরি API-এর উপরেও automated test চালাব।"* If the response root was an object instead of an array, the evaluator's automated tests would crash.
- **Correction:** The response was kept strictly as a JSON array `[...]`, while pagination metadata (`X-Total-Count`) was placed in the HTTP response headers.

---

### Issue 2: Returning `202 Accepted` for Queue-Buffered Seat Holds
- **AI Suggestion:** When asked for Redis queueing for high traffic, the AI initially proposed an asynchronous job model returning `202 Accepted` with a job ID for `POST /classes/{classId}/holds`.
- **Error:** The automated concurrency test expects an immediate synchronous `201 Created` with a `holdId` or `409 Conflict` when seats run out. A `202 Accepted` would immediately fail the test.
- **Correction:** Designed a hybrid architecture: the payload is enqueued into Redis for audit/streaming, while the reservation completes synchronously with PostgreSQL row-level locking (`SELECT ... FOR UPDATE`), immediately returning `201 Created` or `409 Conflict`.

---

### Issue 3: In-Memory Locking vs Multi-Process Bonus
- **AI Suggestion:** Using a C# `SemaphoreSlim` in memory to guard seat reservations.
- **Error:** While `SemaphoreSlim` works within a single process, Section 1.6 Bonus specifies that multiple backend processes running on different ports against the same database must still guarantee `held + confirmed <= totalSeats`. An in-memory semaphore does not cross process boundaries.
- **Correction:** Implemented database engine row locking (`SELECT * FROM classes WHERE id = @classId FOR UPDATE`) and a partial unique database index (`idx_unique_active_student_hold`), making concurrency safety mathematically bulletproof across any number of server processes or containers.

