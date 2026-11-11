# Assumptions and decisions

1. **Student identity:** The brief says the birth-registration number is the student identity and explicitly says no login is needed. The UI stores the entered number in browser local storage and sends it as `X-Student-Id` on student-specific endpoints. The backend accepts 5–32 numeric characters because the exact validation format was not specified; replace this with the school's official format if supplied.
2. **Class/shift model:** Each row is one admission option, e.g. `Class 6 - Morning Shift`. This follows the brief's example that each class/shift has its own seat capacity.
3. **Admin access:** The assignment requests a School Admin UI but does not specify authentication, roles, or an admin account. The create-class panel is therefore open in this take-home implementation. Production deployment must add authentication/authorization.
4. **Payment:** The assignment says to assume payment succeeded when the guardian clicks “ভর্তি ফি দিন”; no real payment gateway is required. Confirm endpoint changes a valid HELD record to CONFIRMED.
5. **Hold duration:** `HOLD_DURATION_SECONDS` controls the hold time and defaults to 600 seconds. The value is clamped to 30–86400 seconds to avoid accidental zero/negative or excessively long holds.
6. **One application per student/class:** A student may have only one active HELD or one CONFIRMED application for a particular class/shift. A cancelled or expired hold does not prevent a later application.
7. **Cancellation:** DELETE cancels a HELD application. A CONFIRMED admission cannot be cancelled through this endpoint because the brief does not define a refund/reversal process.
8. **Concurrency:** Hold creation locks the target class row and runs in a PostgreSQL SERIALIZABLE transaction before counting occupied seats and inserting a hold. Conflicts are returned as 409 and the client can retry. The database is the source of truth.
9. **Expiry:** Expired HELD rows are marked EXPIRED when classes, hold details, or a student's applications are read. No background worker is required; the UI polls every five seconds and the API computes availability from persisted rows.
10. **IDs:** Database IDs are UUIDs. API examples use `c_123` and `h_789` only as examples; actual response IDs are UUID strings.
11. **Persistence:** PostgreSQL data is stored in the named Docker volume `school_data`, so container restarts and `docker compose down` do not erase it. `docker compose down -v` intentionally deletes it.
12. **Ports:** The backend listens on port 8080 inside the Docker network. The frontend nginx serves the complete app on host `http://localhost:8080` and proxies `/api/*` to the backend. This avoids a host-port collision while preserving the required public base URL.
13. **No lottery:** Seats are allocated strictly by successful transaction order (first accepted request wins); there is no lottery or waitlist.
