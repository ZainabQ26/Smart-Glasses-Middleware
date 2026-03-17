import React, { useState, useEffect, useRef } from "react";

const POLL_INTERVAL_MS   = 4_000;   // how often to probe /health while sleeping
const WAKE_TIMEOUT_MS    = 600_000; // allow up to 10 min when ECS + RDS are cold
const WAKE_RETRY_MS      = 15_000;  // retry /wake if initial request is dropped
// Use a longer timeout for the health check so a slow cold-start response
// is not mistaken for a network failure.
const HEALTH_CHECK_TIMEOUT_MS = 10_000;

/**
 * WakeupGate — polls /health before mounting the main App.
 *
 * On first access the ECS service may have 0 running tasks (scaled to zero
 * when idle).  On mount this component immediately fires a POST to /wake which
 * invokes the ECS idle-scaler Lambda directly — no CloudWatch metric pipeline
 * delay.  It then polls /health every few seconds and renders children once
 * the service responds with 2xx.  The task takes ~60-90 s to pass its health
 * check.
 *
 * States
 *   checking  – first probe, transitions quickly to "ready" if already up
 *   waking    – got a non-200 / network error; server is cold-starting
 *   ready     – /health returned 2xx; renders children
 *   error     – timed out or unrecoverable failure
 */
export default function WakeupGate({ children }) {
  const [phase, setPhase]     = useState("checking"); // checking | waking | ready | error
  const [elapsed, setElapsed] = useState(0);
  const [dots, setDots]       = useState("");

  const startRef     = useRef(Date.now());
  const timerRef     = useRef(null);
  const dotsRef      = useRef(null);
  const cancelledRef = useRef(false);
  const lastWakeAttemptRef = useRef(0);

  useEffect(() => {
    cancelledRef.current = false;

    // Animated ellipsis
    dotsRef.current = setInterval(() => {
      setDots(d => d.length >= 3 ? "" : d + ".");
    }, 500);

    // Elapsed seconds counter
    timerRef.current = setInterval(() => {
      setElapsed(Math.floor((Date.now() - startRef.current) / 1000));
    }, 1000);

    let timeout;

    function triggerWake() {
      const now = Date.now();
      if (now - lastWakeAttemptRef.current < WAKE_RETRY_MS) return;
      lastWakeAttemptRef.current = now;
      // Fire-and-forget — /wake routes to Lambda and scales ECS immediately.
      fetch("/wake", { method: "POST" }).catch(() => {});
    }

    // ── Immediately attempt to wake on page load ─────────────────────────
    // This fires the wake signal before the first health check completes so
    // the ECS Lambda is invoked as early as possible.
    triggerWake();

    async function poll() {
      if (cancelledRef.current) return;

      const overallDeadline = startRef.current + WAKE_TIMEOUT_MS;
      if (Date.now() > overallDeadline) {
        if (!cancelledRef.current) setPhase("error");
        return;
      }

      try {
        const controller = new AbortController();
        const t = setTimeout(() => controller.abort(), HEALTH_CHECK_TIMEOUT_MS);
        const res = await fetch("/health", { signal: controller.signal });
        clearTimeout(t);

        if (!cancelledRef.current) {
          if (res.ok) {
            setPhase("ready");
            return; // done — stop polling
          }
          // 503 / 5xx -> service is cold or still booting.
          // Retry wake periodically in case the first wake request is lost.
          triggerWake();
          setPhase("waking");
        }
      } catch {
        if (!cancelledRef.current) {
          triggerWake();
          setPhase("waking");
        }
      }

      if (!cancelledRef.current) {
        timeout = setTimeout(poll, POLL_INTERVAL_MS);
      }
    }

    poll();

    return () => {
      cancelledRef.current = true;
      clearInterval(dotsRef.current);
      clearInterval(timerRef.current);
      clearTimeout(timeout);
    };
  }, []);

  if (phase === "ready") return children;

  const isError = phase === "error";

  return (
    <div style={s.overlay}>
      <div style={s.card}>
        {isError ? (
          <>
            <div style={s.icon}>⚠️</div>
            <h1 style={s.title}>Server unavailable</h1>
            <p style={s.sub}>
              The server didn't respond after {Math.floor(WAKE_TIMEOUT_MS / 1000)} seconds.
            </p>
            <button style={s.btn} onClick={() => {
              window.location.reload();
            }}>
              Try again
            </button>
          </>
        ) : (
          <>
            <Spinner />
            <h1 style={s.title}>
              {phase === "checking" ? "Connecting" + dots : "Waking server" + dots}
            </h1>
            <p style={s.sub}>
              {phase === "checking"
                ? "Checking server status…"
                : "The server and database are starting up — this can take several minutes when sleeping."}
            </p>
            {phase === "waking" && elapsed > 0 && (
              <p style={s.timer}>{elapsed}s</p>
            )}
            <p style={s.hint}>
              The service scales to zero when idle to save cost.
            </p>
          </>
        )}
      </div>
    </div>
  );
}

function Spinner() {
  return (
    <div style={s.spinnerWrap}>
      <div style={s.spinner} />
    </div>
  );
}

const s = {
  overlay: {
    position: "fixed",
    inset: 0,
    background: "#000",
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
    zIndex: 9999,
  },
  card: {
    display: "flex",
    flexDirection: "column",
    alignItems: "center",
    gap: 16,
    padding: "48px 40px",
    maxWidth: 400,
    width: "90%",
    background: "#111",
    borderRadius: 16,
    boxShadow: "0 8px 40px rgba(0,0,0,0.8)",
    textAlign: "center",
  },
  icon: { fontSize: "3rem" },
  title: {
    fontSize: "1.4rem",
    fontWeight: 600,
    color: "#e0e0e0",
    margin: 0,
  },
  sub: {
    fontSize: "0.9rem",
    color: "#aaa",
    margin: 0,
    lineHeight: 1.6,
  },
  timer: {
    fontSize: "2rem",
    fontWeight: 700,
    color: "#00b894",
    margin: 0,
    fontVariantNumeric: "tabular-nums",
  },
  hint: {
    fontSize: "0.75rem",
    color: "#555",
    margin: "8px 0 0",
  },
  btn: {
    marginTop: 8,
    padding: "10px 28px",
    background: "#00b894",
    color: "#000",
    border: "none",
    borderRadius: 8,
    fontSize: "0.95rem",
    fontWeight: 600,
    cursor: "pointer",
  },
  spinnerWrap: {
    width: 56,
    height: 56,
    display: "flex",
    alignItems: "center",
    justifyContent: "center",
  },
  spinner: {
    width: 48,
    height: 48,
    border: "4px solid #333",
    borderTop: "4px solid #00b894",
    borderRadius: "50%",
    animation: "spin 0.9s linear infinite",
  },
};

// Inject keyframe once
if (typeof document !== "undefined" && !document.getElementById("wakeup-spin-style")) {
  const style = document.createElement("style");
  style.id = "wakeup-spin-style";
  style.textContent = "@keyframes spin { to { transform: rotate(360deg); } }";
  document.head.appendChild(style);
}
