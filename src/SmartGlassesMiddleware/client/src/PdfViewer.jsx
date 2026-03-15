/**
 * PdfViewer — inline PDF display component.
 *
 * This component loads a PDF from the backend preview endpoint and renders it
 * onto an HTML <canvas> element using PDF.js.  It is hidden when no document is
 * open and visible as an overlay when one is loaded.
 *
 * An imperative handle is exposed via `forwardRef` so that the parent (App.jsx)
 * can drive navigation and highlighting in response to voice commands without
 * requiring prop-drilling through the full component tree.
 *
 * The parent MUST give this component a stable `key` equal to the documentId
 * (e.g. <PdfViewer key={openPdf.id} …>).  This guarantees that switching
 * documents causes a full remount — clearing all state including highlights —
 * while re-renders caused by other parent state changes (voice command status,
 * logs, etc.) leave the component untouched.
 *
 * Usage:
 *   const pdfViewerRef = useRef(null);
 *   <PdfViewer key={id} ref={pdfViewerRef} documentId={id} documentName={name} onClose={fn} />
 *
 *   // Then call imperatively:
 *   pdfViewerRef.current.nextPage();
 *   pdfViewerRef.current.prevPage();
 *   pdfViewerRef.current.goToPage(5);
 *   pdfViewerRef.current.highlightWord("example");
 *   pdfViewerRef.current.goToLoincCode("8480-6");
 *   pdfViewerRef.current.removeHighlights();
 */

import React, {
  useState,
  useEffect,
  useRef,
  useImperativeHandle,
  forwardRef,
  useCallback,
  memo,
} from "react";

import { getPdf, renderPdfPage } from "./pdf/pdfRendering.js";
import {
  renderPageWithHighlights,
  addHighlight,
  removeAllHighlights,
  searchForKeyword,
} from "./pdf/pdfHighlighting.js";
import { ViewerState, HighlightColors } from "./pdf/pdfClasses.js";

/**
 * @typedef {Object} PdfViewerHandle
 * @property {() => void} nextPage                    - Navigate to the next page.
 * @property {() => void} prevPage                    - Navigate to the previous page.
 * @property {(n: number) => void} goToPage           - Navigate to a specific 1-based page number.
 * @property {(word: string) => void} highlightWord   - Highlight a word (cycles color on repeat).
 * @property {(code: string) => void} goToLoincCode   - Navigate to and highlight a LOINC code.
 * @property {() => void} removeHighlights            - Clear all highlights.
 */

// Color names in the same order as the HighlightColors palette, used for the legend.
const COLOR_NAMES = Object.keys(HighlightColors);
// CSS colors for the legend swatches, matching the canvas-draw RGBA values.
const SWATCH_COLORS = Object.values(HighlightColors).map(
  ([r, g, b]) => `rgb(${r},${g},${b})`
);

const PdfViewer = memo(forwardRef(function PdfViewerComponent(
  { documentId, documentName, onClose },
  ref
) {
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages]   = useState(0);
  const [loading, setLoading]         = useState(true);
  const [error, setError]             = useState(null);
  const [highlightStatus, setHighlightStatus] = useState(null); // brief toast feedback

  /**
   * Summary of active highlights for the legend panel.
   * Each entry: { keyWord, colorName, swatchColor, pages: number[] }
   */
  const [highlightSummary, setHighlightSummary] = useState([]);

  const canvasRef         = useRef(null);
  const canvasWrapperRef  = useRef(null); // for scroll-position preservation
  const viewerStateRef    = useRef(null); // ViewerState instance
  const viewportsRef      = useRef([]);   // PageViewport[] – one per page (index 0 = page 1)
  const toastTimerRef     = useRef(null); // debounce toast clearing

  // ── Helpers ──────────────────────────────────────────────────────────────────

  /**
   * Rebuild the highlight legend from the current ViewerState and push it into
   * React state so the legend panel re-renders automatically.
   */
  const refreshHighlightSummary = useCallback(() => {
    const state = viewerStateRef.current;
    if (!state || state.highlights.length === 0) {
      setHighlightSummary([]);
      return;
    }
    setHighlightSummary(
      state.highlights.map((h, i) => ({
        keyWord:     h.keyWord,
        colorName:   COLOR_NAMES[i % COLOR_NAMES.length],
        swatchColor: SWATCH_COLORS[i % SWATCH_COLORS.length],
        pages: [...new Set(h.rectangleArrays.map(r => r.pageNumber))].sort((a, b) => a - b),
      }))
    );
  }, []);

  /**
   * Show a brief status toast, replacing any existing one.
   * @param {string} message
   * @param {number} [ms=2500] - Auto-dismiss delay in milliseconds.
   */
  const showToast = useCallback((message, ms = 2500) => {
    if (toastTimerRef.current) clearTimeout(toastTimerRef.current);
    setHighlightStatus(message);
    toastTimerRef.current = setTimeout(() => setHighlightStatus(null), ms);
  }, []);

  // ── Load PDF on mount (documentId never changes during a component's lifetime
  //    because the parent uses key={documentId} to force a remount) ─────────────
  useEffect(() => {
    if (!documentId) return;

    let cancelled = false;

    async function load() {
      setLoading(true);
      setError(null);
      viewerStateRef.current = null;
      viewportsRef.current = [];

      const url = `/api/admin/files/${documentId}/preview`;
      const pdf = await getPdf(url);

      if (cancelled) return;

      if (!pdf) {
        setError("Failed to load the document. Please try again.");
        setLoading(false);
        return;
      }

      setTotalPages(pdf.numPages);

      // Pre-build viewports for every page (needed for highlight coordinate math).
      const vps = [];
      for (let i = 1; i <= pdf.numPages; i++) {
        const page = await pdf.getPage(i);
        vps.push(page.getViewport({ scale: 1.0 }));
      }
      if (cancelled) return;
      viewportsRef.current = vps;

      viewerStateRef.current = new ViewerState(pdf);

      if (canvasRef.current) {
        await renderPdfPage(pdf, 1, canvasRef.current);
      }

      if (!cancelled) {
        setCurrentPage(1);
        setLoading(false);
      }
    }

    load().catch(err => {
      if (!cancelled) {
        console.error("PdfViewer load error:", err);
        setError("An error occurred while loading the document.");
        setLoading(false);
      }
    });

    return () => {
      cancelled = true;
      if (toastTimerRef.current) clearTimeout(toastTimerRef.current);
    };
  }, [documentId]);

  // ── Page navigation ──────────────────────────────────────────────────────────

  /**
   * Navigate to a specific 1-based page, preserving all active highlights.
   * The canvas scroll position is intentionally NOT reset — we let the user's
   * last scroll position carry over when flipping pages.
   * @param {number} pageNumber
   */
  const goToPage = useCallback(async (pageNumber) => {
    const state  = viewerStateRef.current;
    const canvas = canvasRef.current;
    if (!state || !canvas) return;
    if (pageNumber < 1 || pageNumber > state.pdf.numPages) return;

    // Preserve the canvas wrapper's scroll position across the re-render so
    // the viewer does not "jump" when voice commands trigger parent re-renders.
    const wrapper = canvasWrapperRef.current;
    const savedScrollTop  = wrapper?.scrollTop  ?? 0;
    const savedScrollLeft = wrapper?.scrollLeft ?? 0;

    await renderPageWithHighlights(state, pageNumber, canvas);
    setCurrentPage(pageNumber);

    // Restore scroll after the canvas content has been repainted.
    if (wrapper) {
      requestAnimationFrame(() => {
        wrapper.scrollTop  = savedScrollTop;
        wrapper.scrollLeft = savedScrollLeft;
      });
    }
  }, []);

  const nextPage = useCallback(() => {
    const state = viewerStateRef.current;
    if (!state) return;
    goToPage(state.pageNumber + 1);
  }, [goToPage]);

  const prevPage = useCallback(() => {
    const state = viewerStateRef.current;
    if (!state) return;
    goToPage(state.pageNumber - 1);
  }, [goToPage]);

  // ── Highlighting ─────────────────────────────────────────────────────────────

  /**
   * Highlight a keyword across the whole document.
   * If the word is already highlighted its color cycles to the next slot.
   * Saying the same word a third time cycles again, and so on.
   * @param {string} word
   */
  const highlightWord = useCallback(async (word) => {
    const state  = viewerStateRef.current;
    const canvas = canvasRef.current;
    if (!state || !canvas) return;

    const trimmed = word?.trim();
    if (!trimmed) return;

    showToast(`Searching for "${trimmed}"…`);
    await addHighlight(state, trimmed, viewportsRef.current, canvas);
    setCurrentPage(state.pageNumber); // sync indicator if addHighlight jumped pages
    refreshHighlightSummary();
    showToast(`Highlighted "${trimmed}"`);
  }, [showToast, refreshHighlightSummary]);

  /**
   * Navigate to and highlight a LOINC code within the document.
   * The code is searched as plain text (e.g. "8480-6").  If found it is
   * highlighted and the viewer jumps to the first matching page.
   * @param {string} code - A LOINC code string such as "8480-6".
   */
  const goToLoincCode = useCallback(async (code) => {
    const state  = viewerStateRef.current;
    const canvas = canvasRef.current;
    if (!state || !canvas) return;

    const trimmedCode = code?.trim();
    if (!trimmedCode) return;

    showToast(`Searching for LOINC code "${trimmedCode}"…`);

    // Reuse the highlight path which both navigates to the first matching page
    // and draws the highlight overlay.
    await addHighlight(state, trimmedCode, viewportsRef.current, canvas);
    setCurrentPage(state.pageNumber);
    refreshHighlightSummary();

    const pages = state.getRectangleArray(trimmedCode.toLowerCase())
      .map(r => r.pageNumber);
    if (pages.length === 0) {
      showToast(`LOINC code "${trimmedCode}" not found in this document.`);
    } else {
      const uniquePages = [...new Set(pages)].sort((a, b) => a - b);
      showToast(`LOINC "${trimmedCode}" found on page${uniquePages.length > 1 ? "s" : ""} ${uniquePages.join(", ")}`);
    }
  }, [showToast, refreshHighlightSummary]);

  /** Remove all keyword highlights from the viewer. */
  const removeHighlights = useCallback(async () => {
    const state  = viewerStateRef.current;
    const canvas = canvasRef.current;
    if (!state || !canvas) return;

    await removeAllHighlights(state, canvas);
    setHighlightSummary([]);
    showToast("All highlights removed", 2000);
  }, [showToast]);

  // ── Expose imperative handle to parent ──────────────────────────────────────
  useImperativeHandle(ref, () => ({
    nextPage,
    prevPage,
    goToPage,
    highlightWord,
    goToLoincCode,
    removeHighlights,
  }), [nextPage, prevPage, goToPage, highlightWord, goToLoincCode, removeHighlights]);

  // ── Render ───────────────────────────────────────────────────────────────────
  return (
    <div style={styles.overlay}>
      {/* ── Header bar ─────────────────────────────────────────────────────── */}
      <div style={styles.header}>
        <span style={styles.docName} title={documentName}>
          📄 {documentName || "Document"}
        </span>
        <span style={styles.pageIndicator}>
          {loading ? "Loading…" : totalPages > 0 ? `${currentPage} / ${totalPages}` : ""}
        </span>
        <button style={styles.closeBtn} onClick={onClose} title="Close document">
          ✕ Close
        </button>
      </div>

      {/* ── Navigation controls ─────────────────────────────────────────────── */}
      {!loading && !error && (
        <div style={styles.navBar}>
          <button
            style={{ ...styles.navBtn, opacity: currentPage <= 1 ? 0.4 : 1 }}
            onClick={prevPage}
            disabled={currentPage <= 1}
          >
            ◀ Prev
          </button>
          <button
            style={{ ...styles.navBtn, opacity: currentPage >= totalPages ? 0.4 : 1 }}
            onClick={nextPage}
            disabled={currentPage >= totalPages}
          >
            Next ▶
          </button>
        </div>
      )}

      {/* ── Highlight status toast (fixed so it never shifts the layout) ──── */}
      {highlightStatus && (
        <div style={styles.toast}>{highlightStatus}</div>
      )}

      {/* ── Loading / error states ──────────────────────────────────────────── */}
      {loading && <div style={styles.statusMsg}>⏳ Loading document…</div>}
      {error   && <div style={styles.errorMsg}>❌ {error}</div>}

      {/* ── PDF canvas — scrollable area ────────────────────────────────────── */}
      <div ref={canvasWrapperRef} style={styles.canvasWrapper}>
        <canvas
          ref={canvasRef}
          style={{ ...styles.canvas, display: loading || error ? "none" : "block" }}
        />
      </div>

      {/* ── Highlight legend — shows pages per keyword/color ────────────────── */}
      {highlightSummary.length > 0 && (
        <div style={styles.legend}>
          <span style={styles.legendTitle}>Highlights</span>
          <div style={styles.legendRows}>
            {highlightSummary.map(({ keyWord, swatchColor, pages }) => (
              <div key={keyWord} style={styles.legendRow}>
                <span style={{ ...styles.legendSwatch, background: swatchColor }} />
                <span style={styles.legendKeyword}>&ldquo;{keyWord}&rdquo;</span>
                <span style={styles.legendPages}>
                  {pages.length === 1
                    ? `page ${pages[0]}`
                    : `pages ${pages.join(", ")}`}
                </span>
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}));

export default PdfViewer;

// ── Styles ────────────────────────────────────────────────────────────────────
const styles = {
  /**
   * position: fixed + inset: 0 pins the overlay to the viewport.
   * will-change: transform creates an independent compositing layer so that
   * parent re-renders (voice status, logs, etc.) do not cause any visual
   * layout recalculation inside the overlay.
   */
  overlay: {
    position: "fixed",
    inset: 0,
    background: "#111",
    zIndex: 100,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
    willChange: "transform",
  },
  header: {
    display: "flex",
    alignItems: "center",
    gap: 12,
    padding: "10px 16px",
    background: "#1a1a2e",
    borderBottom: "1px solid #333",
    flexShrink: 0,
  },
  docName: {
    flex: 1,
    fontSize: "0.9rem",
    color: "#7ec8e3",
    fontWeight: 600,
    whiteSpace: "nowrap",
    overflow: "hidden",
    textOverflow: "ellipsis",
  },
  pageIndicator: {
    fontSize: "0.85rem",
    color: "#aaa",
    whiteSpace: "nowrap",
  },
  closeBtn: {
    background: "transparent",
    border: "1px solid #555",
    color: "#ccc",
    borderRadius: 6,
    padding: "4px 12px",
    cursor: "pointer",
    fontSize: "0.82rem",
    flexShrink: 0,
  },
  navBar: {
    display: "flex",
    gap: 8,
    padding: "8px 16px",
    background: "#161625",
    borderBottom: "1px solid #2a2a3a",
    flexShrink: 0,
  },
  navBtn: {
    background: "#2a3a5a",
    border: "none",
    color: "#fff",
    borderRadius: 6,
    padding: "6px 16px",
    cursor: "pointer",
    fontSize: "0.85rem",
    transition: "background 0.15s",
  },
  // The toast is position: fixed so it never contributes to layout height and
  // the rest of the overlay stays perfectly still when it appears/disappears.
  toast: {
    position: "fixed",
    top: 70,
    left: "50%",
    transform: "translateX(-50%)",
    background: "rgba(0,150,100,0.92)",
    color: "#fff",
    borderRadius: 8,
    padding: "8px 20px",
    fontSize: "0.88rem",
    zIndex: 110,
    pointerEvents: "none",
    whiteSpace: "nowrap",
  },
  statusMsg: {
    margin: "32px auto",
    color: "#aaa",
    fontSize: "0.95rem",
  },
  errorMsg: {
    margin: "32px auto",
    color: "#f88",
    fontSize: "0.95rem",
  },
  // flex: 1 lets the canvas area take all remaining vertical space between the
  // nav bar and the legend, keeping both anchored at all times.
  canvasWrapper: {
    flex: 1,
    overflow: "auto",
    display: "flex",
    justifyContent: "center",
    alignItems: "flex-start",
    padding: "16px",
    background: "#1e1e1e",
  },
  canvas: {
    maxWidth: "100%",
    boxShadow: "0 4px 24px rgba(0,0,0,0.6)",
    background: "#fff",
  },
  // Legend panel sits below the canvas, above the bottom of the viewport.
  // flexShrink: 0 ensures it never collapses even when the canvas area is large.
  legend: {
    flexShrink: 0,
    background: "#181828",
    borderTop: "1px solid #2a2a3a",
    padding: "8px 16px",
    display: "flex",
    alignItems: "flex-start",
    gap: 12,
  },
  legendTitle: {
    fontSize: "0.78rem",
    color: "#888",
    whiteSpace: "nowrap",
    marginTop: 2,
    flexShrink: 0,
  },
  legendRows: {
    display: "flex",
    flexWrap: "wrap",
    gap: "6px 20px",
  },
  legendRow: {
    display: "flex",
    alignItems: "center",
    gap: 6,
  },
  legendSwatch: {
    width: 12,
    height: 12,
    borderRadius: 3,
    flexShrink: 0,
    border: "1px solid rgba(255,255,255,0.2)",
  },
  legendKeyword: {
    fontSize: "0.82rem",
    color: "#ddd",
  },
  legendPages: {
    fontSize: "0.78rem",
    color: "#aaa",
  },
};
