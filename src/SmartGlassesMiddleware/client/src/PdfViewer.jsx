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
 * Usage:
 *   const pdfViewerRef = useRef(null);
 *   <PdfViewer ref={pdfViewerRef} documentId={id} documentName={name} onClose={fn} />
 *
 *   // Then call imperatively:
 *   pdfViewerRef.current.nextPage();
 *   pdfViewerRef.current.prevPage();
 *   pdfViewerRef.current.highlightWord("example");
 *   pdfViewerRef.current.removeHighlights();
 */

import React, {
  useState,
  useEffect,
  useRef,
  useImperativeHandle,
  forwardRef,
  useCallback,
} from "react";

import { getPdf, renderPdfPage } from "./pdf/pdfRendering.js";
import {
  renderPageWithHighlights,
  addHighlight,
  removeAllHighlights,
} from "./pdf/pdfHighlighting.js";
import { ViewerState } from "./pdf/pdfClasses.js";

/**
 * @typedef {Object} PdfViewerHandle
 * @property {() => void} nextPage          - Navigate to the next page.
 * @property {() => void} prevPage          - Navigate to the previous page.
 * @property {(word: string) => void} highlightWord  - Highlight a word (cycles colour on repeat).
 * @property {() => void} removeHighlights  - Clear all highlights.
 */

const PdfViewer = forwardRef(function PdfViewer(
  { documentId, documentName, onClose },
  ref
) {
  const [currentPage, setCurrentPage] = useState(1);
  const [totalPages, setTotalPages]   = useState(0);
  const [loading, setLoading]         = useState(true);
  const [error, setError]             = useState(null);
  const [highlightStatus, setHighlightStatus] = useState(null); // brief feedback

  const canvasRef      = useRef(null);
  const viewerStateRef = useRef(null); // ViewerState instance
  const viewportsRef   = useRef([]);   // PageViewport[] – one per page

  // ── Load PDF whenever documentId changes ────────────────────────────────────
  useEffect(() => {
    if (!documentId) return;

    let cancelled = false;

    async function load() {
      setLoading(true);
      setError(null);
      setCurrentPage(1);
      setTotalPages(0);
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

      const numPages = pdf.numPages;
      setTotalPages(numPages);

      // Pre-build viewports (needed for highlight coordinate calculations).
      const vps = [];
      for (let i = 1; i <= numPages; i++) {
        const page = await pdf.getPage(i);
        vps.push(page.getViewport({ scale: 1.0 }));
      }
      if (cancelled) return;
      viewportsRef.current = vps;

      const state = new ViewerState(pdf);
      viewerStateRef.current = state;

      // Render the first page.
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

    return () => { cancelled = true; };
  }, [documentId]);

  // ── Page navigation ──────────────────────────────────────────────────────────

  const goToPage = useCallback(async (pageNumber) => {
    const state = viewerStateRef.current;
    const canvas = canvasRef.current;
    if (!state || !canvas) return;
    if (pageNumber < 1 || pageNumber > state.pdf.numPages) return;

    await renderPageWithHighlights(state, pageNumber, canvas);
    setCurrentPage(pageNumber);
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
   * Highlight a keyword.  If the word is already highlighted it cycles to the
   * next colour slot.  Calling this again on the same word keeps cycling.
   * @param {string} word
   */
  const highlightWord = useCallback(async (word) => {
    const state  = viewerStateRef.current;
    const canvas = canvasRef.current;
    if (!state || !canvas) return;

    const trimmed = word?.trim();
    if (!trimmed) return;

    setHighlightStatus(`Searching for "${trimmed}"…`);
    await addHighlight(state, trimmed, viewportsRef.current, canvas);
    setCurrentPage(state.pageNumber); // sync page indicator after possible jump
    setHighlightStatus(`Highlighted "${trimmed}"`);
    setTimeout(() => setHighlightStatus(null), 2500);
  }, []);

  /** Remove all keyword highlights from the viewer. */
  const removeHighlights = useCallback(async () => {
    const state  = viewerStateRef.current;
    const canvas = canvasRef.current;
    if (!state || !canvas) return;

    await removeAllHighlights(state, canvas);
    setHighlightStatus("All highlights removed");
    setTimeout(() => setHighlightStatus(null), 2000);
  }, []);

  // ── Expose imperative handle to parent ──────────────────────────────────────
  useImperativeHandle(ref, () => ({ nextPage, prevPage, highlightWord, removeHighlights }), [
    nextPage, prevPage, highlightWord, removeHighlights,
  ]);

  // ── Render ───────────────────────────────────────────────────────────────────
  return (
    <div style={styles.overlay}>
      {/* Header bar */}
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

      {/* Navigation controls */}
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

      {/* Highlight status toast */}
      {highlightStatus && (
        <div style={styles.toast}>{highlightStatus}</div>
      )}

      {/* Loading / error states */}
      {loading && <div style={styles.statusMsg}>⏳ Loading document…</div>}
      {error   && <div style={styles.errorMsg}>❌ {error}</div>}

      {/* PDF canvas */}
      <div style={styles.canvasWrapper}>
        <canvas
          ref={canvasRef}
          style={{ ...styles.canvas, display: loading || error ? "none" : "block" }}
        />
      </div>
    </div>
  );
});

export default PdfViewer;

// ── Styles ────────────────────────────────────────────────────────────────────
const styles = {
  overlay: {
    position: "fixed",
    inset: 0,
    background: "#111",
    zIndex: 100,
    display: "flex",
    flexDirection: "column",
    overflow: "hidden",
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
  toast: {
    position: "absolute",
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
};
