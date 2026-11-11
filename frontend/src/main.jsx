
import React, { useCallback, useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import './style.css';

const API = (
  import.meta.env.VITE_API_URL || 'http://localhost:8080'
).replace(/\/$/, '');

function App() {
  const [classes, setClasses] = useState([]);
  const [studentId, setStudentId] = useState(
    localStorage.getItem('studentId') || ''
  );
  const [myHolds, setMyHolds] = useState([]);
  const [name, setName] = useState('');
  const [seats, setSeats] = useState(40);
  const [message, setMessage] = useState('');
  const [busy, setBusy] = useState(false);

  const headers = {
    'Content-Type': 'application/json',
    'X-Student-Id': studentId,
  };

  const call = async (path, options = {}) => {
    const r = await fetch(`${API}${path}`, {
      ...options,
      headers: {
        ...headers,
        ...options.headers,
      },
    });

    const body =
      r.status === 204 ? null : await r.json().catch(() => null);

    if (!r.ok) {
      throw new Error(body?.message || `Request failed (${r.status})`);
    }

    return body;
  };

  const refresh = useCallback(async () => {
    try {
      const c = await call('/classes', {
        headers: {},
      });

      setClasses(c);

      if (studentId) {
        const h = await call('/me/holds');
        setMyHolds(h);
      } else {
        setMyHolds([]);
      }
    } catch (e) {
      setMessage(e.message);
    }
  }, [studentId]);

  useEffect(() => {
    refresh();

    const t = setInterval(refresh, 5000);

    return () => clearInterval(t);
  }, [refresh]);

  const saveStudent = () => {
    const v = studentId.trim();

    if (!/^\d{5,32}$/.test(v)) {
      setMessage('Please enter a valid student ID (5–32 digits).');
      return;
    }

    localStorage.setItem('studentId', v);
    setStudentId(v);
    setMessage('Student ID saved successfully.');
  };

  const createClass = async (e) => {
    e.preventDefault();
    setBusy(true);

    try {
      await call('/classes', {
        method: 'POST',
        body: JSON.stringify({
          name,
          totalSeats: Number(seats),
        }),
      });

      setName('');
      setMessage('Class created successfully.');

      await refresh();
    } catch (e) {
      setMessage(e.message);
    } finally {
      setBusy(false);
    }
  };

  const apply = async (id) => {
    if (!studentId) {
      setMessage('Please enter your student ID before applying.');
      return;
    }

    setBusy(true);

    try {
      const h = await call(`/classes/${id}/holds`, {
        method: 'POST',
      });

      setMessage(`Seat reserved successfully. Hold ID: ${h.holdId}`);

      await refresh();
    } catch (e) {
      setMessage(e.message);
    } finally {
      setBusy(false);
    }
  };

  const confirm = async (id) => {
    setBusy(true);

    try {
      await call(`/holds/${id}/confirm`, {
        method: 'POST',
      });

      setMessage('Admission confirmed successfully.');

      await refresh();
    } catch (e) {
      setMessage(e.message);
    } finally {
      setBusy(false);
    }
  };

  const cancel = async (id) => {
    setBusy(true);

    try {
      await call(`/holds/${id}`, {
        method: 'DELETE',
      });

      setMessage('Application cancelled successfully.');

      await refresh();
    } catch (e) {
      setMessage(e.message);
    } finally {
      setBusy(false);
    }
  };

  const timeLeft = (iso) =>
    Math.max(
      0,
      Math.ceil((new Date(iso) - Date.now()) / 1000)
    );

  return (
    <main className="shell">

      <header>
        <div className="brand">
          SA<span>•</span>
        </div>

        <div>
          <p className="eyebrow">SCHOOL ADMISSION PORTAL</p>
          <h1>Admission, made simple.</h1>
          <p className="sub">
            Apply early — seats are limited.
          </p>
        </div>

        <div className="session">
          ONLINE ADMISSION
          <br />
          <b>2026–27</b>
        </div>
      </header>

      {message && (
        <div className="notice" role="status">
          {message}

          <button onClick={() => setMessage('')}>
            ×
          </button>
        </div>
      )}

      <section className="panel identity">
        <div>
          <p className="eyebrow">STUDENT IDENTITY</p>
          <h2>Student ID</h2>
        </div>

        <div className="inline">
          <input
            value={studentId}
            onChange={(e) => setStudentId(e.target.value)}
            placeholder="Enter student ID"
            inputMode="numeric"
          />

          <button
            className="primary"
            onClick={saveStudent}
          >
            Save ID
          </button>
        </div>
      </section>

      <section className="section-head">
        <div>
          <p className="eyebrow">AVAILABLE SEATS</p>
          <h2>Class & Shift List</h2>
        </div>

        <span className="live">
          ● LIVE · Updates every 5 seconds
        </span>
      </section>

      <section className="class-grid">
        {classes.length === 0 ? (
          <div className="empty">
            No classes available.
          </div>
        ) : (
          classes.map((c) => (
            <article className="class-card" key={c.id}>

              <div className="card-top">
                <span className="tag">
                  ADMISSION OPEN
                </span>

                <span className="available">
                  {c.availableSeats} seats left
                </span>
              </div>

              <h3>{c.name}</h3>

              <div className="stats">
                <div>
                  <b>{c.totalSeats}</b>
                  <small>Total</small>
                </div>

                <div>
                  <b>{c.heldSeats}</b>
                  <small>On hold</small>
                </div>

                <div>
                  <b>{c.confirmedSeats}</b>
                  <small>Confirmed</small>
                </div>
              </div>

              <div className="progress">
                <i
                  style={{
                    width: `${
                      c.totalSeats > 0
                        ? Math.min(
                            100,
                            ((c.heldSeats + c.confirmedSeats) /
                              c.totalSeats) *
                              100
                          )
                        : 0
                    }%`,
                  }}
                />
              </div>

              <button
                disabled={busy || c.availableSeats === 0}
                className="primary full"
                onClick={() => apply(c.id)}
              >
                {c.availableSeats === 0
                  ? 'No Seats Available'
                  : 'Apply Now →'}
              </button>

            </article>
          ))
        )}
      </section>

      <section className="panel">

        <div className="section-head compact">
          <div>
            <p className="eyebrow">APPLICATION TRACKER</p>
            <h2>My Applications</h2>
          </div>

          <button
            className="secondary"
            onClick={refresh}
          >
            ↻ Refresh
          </button>
        </div>

        {!studentId ? (
          <p className="muted">
            Please save your student ID to view applications.
          </p>
        ) : myHolds.length === 0 ? (
          <p className="muted">
            No applications found.
          </p>
        ) : (
          <div className="table-wrap">

            <table>
              <thead>
                <tr>
                  <th>Class</th>
                  <th>Status</th>
                  <th>Hold Expires</th>
                  <th>Action</th>
                </tr>
              </thead>

              <tbody>
                {myHolds.map((h) => (
                  <tr key={h.holdId}>

                    <td>{h.className}</td>

                    <td>
                      <span
                        className={`status ${h.status.toLowerCase()}`}
                      >
                        {h.status}
                      </span>
                    </td>

                    <td>
                      {h.status === 'HELD'
                        ? `${timeLeft(h.expiresAt)} sec`
                        : new Date(h.expiresAt).toLocaleString()}
                    </td>

                    <td>
                      {h.status === 'HELD' ? (
                        <>
                          <button
                            className="primary small"
                            disabled={busy}
                            onClick={() => confirm(h.holdId)}
                          >
                            Confirm Admission
                          </button>

                          {' '}

                          <button
                            className="danger small"
                            disabled={busy}
                            onClick={() => cancel(h.holdId)}
                          >
                            Cancel
                          </button>
                        </>
                      ) : h.status === 'CONFIRMED' ? (
                        '✓ Confirmed'
                      ) : (
                        '—'
                      )}
                    </td>

                  </tr>
                ))}
              </tbody>
            </table>

          </div>
        )}
      </section>

      <section className="panel admin">

        <div>
          <p className="eyebrow">SCHOOL ADMIN</p>
          <h2>Create New Class / Shift</h2>
        </div>

        <form onSubmit={createClass}>

          <label>
            Class / Shift Name

            <input
              required
              maxLength="160"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="Enter class or shift name"
            />
          </label>

          <label>
            Total Seats

            <input
              required
              type="number"
              min="1"
              max="10000"
              value={seats}
              onChange={(e) => setSeats(e.target.value)}
            />
          </label>

          <button
            className="primary"
            disabled={busy}
          >
            {busy ? 'Processing...' : 'Create Class'}
          </button>

        </form>
      </section>

      <footer>
        School Admission System
        <span>
          · .NET 8 · PostgreSQL · React · Docker
        </span>
      </footer>

    </main>
  );
}

createRoot(document.getElementById('root')).render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
