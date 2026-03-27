/**
 * PDF keyword search and canvas-based highlight drawing.
 *
 * Highlights are rendered directly on the PDF canvas so they are
 * non-destructive (the original file is never modified).  Each keyword
 * receives a different colour from the HighlightColors palette, cycling
 * when more keywords are added than there are colours.
 */

import { normalize } from "./pdfMozilla.js";
import { HighlightColors, HIGHLIGHT_COLOR_COUNT } from "./pdfClasses.js";
import { getAllPdfText, renderPdfPage } from "./pdfRendering.js";
import { PDFDocument, rgb } from "pdf-lib";

// ── Internal helpers ──────────────────────────────────────────────────────────

/**
 * Convert the HighlightColors palette into an array of CSS rgba() strings,
 * one per colour in palette order.
 *
 * @returns {string[]}
 */
function buildColorArray() {
  return Object.values(HighlightColors).map(
    ([r, g, b, a]) => `rgba(${r},${g},${b},${a})`
  );
}

/**
 * Find the nearest page that contains an occurrence of the keyword,
 * preferring pages at or after `currentPage`, wrapping to the first
 * occurrence if none is found ahead.
 *
 * @param {import('./pdfClasses.js').HighlightRectangle[]} rectangles
 * @param {number} currentPage
 * @returns {number} The nearest page number (1-based).
 */
function findNearestPage(rectangles, currentPage) {
  const pages = [...new Set(rectangles.map(r => r.pageNumber))].sort((a, b) => a - b);
  if (pages.length === 0) return currentPage;

  // If the current page already has the keyword, stay on it.
  if (pages.includes(currentPage)) return currentPage;

  // Otherwise jump to the first page after the current one (wrap if needed).
  const next = pages.find(p => p > currentPage);
  return next ?? pages[0];
}

/**
 * Round a number to the requested decimal places.
 *
 * @param {number} value
 * @param {number} decimalPlaces
 * @returns {number}
 */
function round(value, decimalPlaces) {
  return Math.round(value * Math.pow(10, decimalPlaces)) / Math.pow(10, decimalPlaces);
}

/**
 * Convert HighlightColors to pdf-lib compatible normalized RGBA values.
 *
 * @returns {Record<string, [number, number, number, number]>}
 */
function normalizeColorValues() {
  const colorMap = {};
  const names = Object.keys(HighlightColors);
  const arrays = Object.values(HighlightColors);

  for (let i = 0; i < arrays.length; i++) {
    const [r, g, b, a] = arrays[i];
    colorMap[names[i]] = [
      round(r / 255, 2),
      round(g / 255, 2),
      round(b / 255, 2),
      a,
    ];
  }

  return colorMap;
}

/**
 * Draw all active highlight rectangles into a pdf-lib document.
 *
 * @param {import('pdf-lib').PDFDocument} pdfDoc
 * @param {import('./pdfClasses.js').ViewerState} viewerState
 * @returns {Promise<void>}
 */
async function exportHighlightsToPdfLib(pdfDoc, viewerState) {
  const colorMap = normalizeColorValues();
  const colorNames = Object.keys(colorMap);
  let colorIndex = 0;

  for (const highlight of viewerState.highlights) {
    const colorName = colorNames[colorIndex % colorNames.length];
    const [r, g, b, opacity] = colorMap[colorName];

    for (const rect of highlight.rectangleArrays) {
      const page = pdfDoc.getPage(rect.pageNumber - 1);
      const { height } = page.getSize();
      const pdfY = height - rect.canvasY - rect.canvasHeight;

      page.drawRectangle({
        x: rect.canvasX,
        y: pdfY,
        width: rect.canvasWidth,
        height: rect.canvasHeight,
        color: rgb(r, g, b),
        opacity,
      });
    }

    colorIndex++;
  }
}

/**
 * Return a new PDF containing only pages with highlights.
 *
 * @param {import('pdf-lib').PDFDocument} pdfDocument
 * @param {import('./pdfClasses.js').ViewerState} viewerState
 * @returns {Promise<import('pdf-lib').PDFDocument>}
 */
async function saveHighlightedOnlyPages(pdfDocument, viewerState) {
  let pageNumbers = viewerState.highlights.flatMap((h) =>
    h.rectangleArrays.map((r) => r.pageNumber - 1)
  );

  pageNumbers = [...new Set(pageNumbers)].sort((a, b) => a - b);
  return getSpecificPageNumbers(pdfDocument, pageNumbers);
}

/**
 * Copy selected pages into a new document.
 *
 * @param {import('pdf-lib').PDFDocument} originalDocument
 * @param {number[]} pageNumbers - Zero-based page indexes.
 * @returns {Promise<import('pdf-lib').PDFDocument>}
 */
async function getSpecificPageNumbers(originalDocument, pageNumbers) {
  const newDocument = await PDFDocument.create();

  if (pageNumbers.length > 0) {
    const copiedPages = await newDocument.copyPages(originalDocument, pageNumbers);
    for (const page of copiedPages) {
      newDocument.addPage(page);
    }
  }

  return newDocument;
}

/**
 * Export a downloadable PDF with highlights burned into the file.
 *
 * This is voice-command friendly and does not require any UI buttons.
 *
 * @param {import('./pdfClasses.js').ViewerState} viewerState
 * @param {string} pdfAddress
 * @param {{isAllPages?: boolean, downloadName?: string}} [options]
 * @returns {Promise<boolean>}
 */
export async function exportHighlightedPdf(viewerState, pdfAddress, options = {}) {
  const { isAllPages = true, downloadName = "highlighted.pdf" } = options;

  try {
    const originalPdfBytes = await fetch(pdfAddress)
      .then((result) => result.arrayBuffer())
      .then((buffer) => new Uint8Array(buffer));

    let pdfDocument = await PDFDocument.load(originalPdfBytes);

    await exportHighlightsToPdfLib(pdfDocument, viewerState);

    if (!isAllPages) {
      pdfDocument = await saveHighlightedOnlyPages(pdfDocument, viewerState);
    }

    const exportedPdfBytes = await pdfDocument.save();
    const safePdfBytes = Uint8Array.from(exportedPdfBytes);
    const blob = new Blob([safePdfBytes], { type: "application/pdf" });
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = downloadName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
    URL.revokeObjectURL(url);
    return true;
  } catch (error) {
    console.error("Error exporting highlighted PDF:", error);
    return false;
  }
}


// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Search a PDF for every occurrence of `keyword` and return the canvas-space
 * bounding rectangles of all matches across all pages.
 *
 * @param {string} keyword - The word or phrase to search for.
 * @param {import('pdfjs-dist').PDFDocumentProxy} pdf
 * @param {import('pdfjs-dist').PageViewport[]} viewports - Pre-built viewport for each page (index 0 = page 1).
 * @returns {Promise<import('./pdfClasses.js').HighlightRectangle[]>}
 */
export async function searchForKeyword(keyword, pdf, viewports) {
  const [normalizedKeyword] = normalize(keyword);
  const lowercasedKeyword = normalizedKeyword.toLowerCase();
  const locations = [];
  const tempCanvas = document.createElement("canvas");
  const tempCtx = tempCanvas.getContext("2d");

  if (!tempCtx) return locations;

  const allText = await getAllPdfText(pdf);

  for (let pageIndex = 0; pageIndex < allText.length; pageIndex++) {
    const { normalized: pageText, itemRanges } = allText[pageIndex];
    const lowerPageText = pageText.toLowerCase();
    const viewport = viewports[pageIndex];

    // Default font metrics used for all pages.
    // Fetching real metrics via page.commonObjs.get() requires the page to have
    // been fully rendered first (fonts are only loaded into commonObjs after
    // page.render() completes).  Using getOperatorList() alone is not sufficient
    // for non-current pages, causing the promise to hang indefinitely.
    // These constants are the same fallback values the previous code used and
    // produce well-positioned highlights for typical Latin fonts.
    const ASCENT  = 0.8;
    const DESCENT = -0.2;

    let searchIndex = 0;
    while ((searchIndex = lowerPageText.indexOf(lowercasedKeyword, searchIndex)) !== -1) {
      const matchStart = searchIndex;
      const matchEnd   = searchIndex + lowercasedKeyword.length;

      for (const range of itemRanges) {
        // Skip items that don't overlap with this match.
        if (range.end <= matchStart || range.start >= matchEnd) continue;

        const item = range.item;

        // Character positions within this item.
        const overlapStart = Math.max(matchStart, range.start) - range.start;
        const overlapEnd   = Math.min(matchEnd,   range.end)   - range.start;

        const fullText   = item.str;
        const prefixText = fullText.substring(0, overlapStart);
        const matchText  = fullText.substring(overlapStart, overlapEnd);

        const fontSize = Math.abs(item.transform[3]);
        tempCtx.font = `${fontSize}px sans-serif`;

        const fullWidth   = tempCtx.measureText(fullText).width;
        const prefixWidth = tempCtx.measureText(prefixText).width;
        const matchWidth  = tempCtx.measureText(matchText).width;

        // Scale measured canvas width back to PDF item.width.
        const scale = fullWidth > 0 ? item.width / fullWidth : 1;

        const canvasX      = item.transform[4] + prefixWidth * scale;
        const canvasWidth  = matchWidth * scale;
        const canvasY      = viewport.height - item.transform[5] - ASCENT * fontSize;
        const canvasHeight = (ASCENT - DESCENT) * fontSize;

        locations.push({
          pageNumber: pageIndex + 1,
          canvasX,
          canvasY,
          canvasWidth,
          canvasHeight,
        });
      }

      searchIndex++;
    }
  }

  return locations;
}

/**
 * Draw a single set of highlight rectangles (one keyword) onto `context`.
 *
 * @param {import('./pdfClasses.js').HighlightRectangle[]} rectangles - All rectangles for the keyword.
 * @param {number} colorIndex - Index into the HighlightColors palette.
 * @param {number} pageNumber - Only draw rectangles on this page.
 * @param {CanvasRenderingContext2D} context
 */
export function drawSingleHighlights(rectangles, colorIndex, pageNumber, context) {
  const colors = buildColorArray();
  context.fillStyle = colors[colorIndex % colors.length];

  for (const rect of rectangles) {
    if (rect.pageNumber === pageNumber) {
      context.fillRect(rect.canvasX, rect.canvasY, rect.canvasWidth, rect.canvasHeight);
    }
  }
}

/**
 * Re-draw all active highlights from `viewerState` onto `context` for the
 * current page stored in `viewerState.pageNumber`.
 *
 * @param {import('./pdfClasses.js').ViewerState} viewerState
 * @param {CanvasRenderingContext2D} context
 */
export function drawAllHighlights(viewerState, context) {
  const colors = buildColorArray();
  const locationsPerKeyword = viewerState.getLocationsForPage(viewerState.pageNumber);

  locationsPerKeyword.forEach((rects, colorIndex) => {
    context.fillStyle = colors[colorIndex % colors.length];
    for (const rect of rects) {
      context.fillRect(rect.canvasX, rect.canvasY, rect.canvasWidth, rect.canvasHeight);
    }
  });
}

/**
 * Add a highlight for `keyword` in `viewerState`, then re-render the current
 * page with all highlights applied.
 *
 * If the keyword already has a colour slot, its colour cycles to the next
 * available slot (up to HIGHLIGHT_COLOR_COUNT slots total, then wraps).
 * If the keyword is brand-new and the palette is full, the oldest keyword is
 * replaced.
 *
 * @param {import('./pdfClasses.js').ViewerState} viewerState
 * @param {string} keyword
 * @param {import('pdfjs-dist').PageViewport[]} viewports
 * @param {HTMLCanvasElement} canvas
 * @returns {Promise<boolean>} `true` if the keyword was found and highlighted, `false` otherwise.
 */
export async function addHighlight(viewerState, keyword, viewports, canvas) {
  const context = canvas.getContext("2d");
  if (!context) return false;

  const lowerKeyword = keyword.toLowerCase().trim();
  if (!lowerKeyword) return false;

  // Search for the keyword across all pages.
  const rectangles = await searchForKeyword(lowerKeyword, viewerState.pdf, viewports);
  if (rectangles.length === 0) return false; // keyword not found in document

  // If keyword already highlighted, remove it so we can re-add with next colour.
  // (This implements the "say again to cycle colour" behaviour.)
  const existingIndex = viewerState.getKeyWords().indexOf(lowerKeyword);
  if (existingIndex !== -1) {
    viewerState.removeKeyWord(lowerKeyword);
  }

  // If the palette is full, drop the oldest keyword to make room.
  if (viewerState.highlights.length >= HIGHLIGHT_COLOR_COUNT) {
    const oldest = viewerState.highlights[0]?.keyWord;
    if (oldest) viewerState.removeKeyWord(oldest);
  }

  viewerState.addHighlight({ keyWord: lowerKeyword, rectangleArrays: rectangles });

  // Navigate to the nearest page containing the keyword.
  const nearestPage = findNearestPage(rectangles, viewerState.pageNumber);
  if (nearestPage !== viewerState.pageNumber) {
    viewerState.pageNumber = nearestPage;
    await renderPdfPage(viewerState.pdf, nearestPage, canvas);
  }

  // Draw all highlights (including the new one) on the current page.
  drawAllHighlights(viewerState, context);
  return true;
}

/**
 * Remove all highlights and re-render the current page without any overlays.
 *
 * @param {import('./pdfClasses.js').ViewerState} viewerState
 * @param {HTMLCanvasElement} canvas
 * @returns {Promise<void>}
 */
export async function removeAllHighlights(viewerState, canvas) {
  viewerState.clearAll();
  // Re-render the page to wipe the highlight overlay.
  await renderPdfPage(viewerState.pdf, viewerState.pageNumber, canvas);
}

/**
 * Render a page and then re-apply all active highlights.
 *
 * @param {import('./pdfClasses.js').ViewerState} viewerState
 * @param {number} pageNumber
 * @param {HTMLCanvasElement} canvas
 * @returns {Promise<import('pdfjs-dist').PDFPageProxy|null>}
 */
export async function renderPageWithHighlights(viewerState, pageNumber, canvas) {
  const page = await renderPdfPage(viewerState.pdf, pageNumber, canvas);
  viewerState.pageNumber = pageNumber;

  if (viewerState.highlights.length > 0) {
    const context = canvas.getContext("2d");
    if (context) drawAllHighlights(viewerState, context);
  }

  return page;
}
