/**
 * Core PDF.js rendering helpers.
 *
 * These functions wrap pdfjs-dist to load a PDF document and render
 * individual pages onto an HTML <canvas> element.
 */

import { getDocument, GlobalWorkerOptions } from "pdfjs-dist";
import { normalize } from "./pdfMozilla.js";

// Point the PDF.js web worker at the bundled worker script supplied by pdfjs-dist.
GlobalWorkerOptions.workerSrc = new URL(
  "pdfjs-dist/build/pdf.worker.mjs",
  import.meta.url
).toString();

/**
 * Load a PDF document from a URL.
 *
 * @param {string} url - The URL of the PDF file (e.g. `/api/admin/files/{id}/preview`).
 * @returns {Promise<import('pdfjs-dist').PDFDocumentProxy|null>}
 */
export async function getPdf(url) {
  try {
    return await getDocument(url).promise;
  } catch (error) {
    console.error("Error loading PDF document:", error);
    return null;
  }
}

/**
 * Render a single page of a PDF document to the given canvas.
 *
 * @param {import('pdfjs-dist').PDFDocumentProxy} pdf
 * @param {number} pageNumber - 1-based page number.
 * @param {HTMLCanvasElement} canvas - The canvas element to render into.
 * @param {number} [scale=1.0] - Render scale (1.0 = 100%).
 * @returns {Promise<import('pdfjs-dist').PDFPageProxy|null>}
 */
export async function renderPdfPage(pdf, pageNumber, canvas, scale = 1.0) {
  const page = await pdf.getPage(pageNumber);
  if (!page) {
    console.error("Failed to load page", pageNumber);
    return null;
  }

  const viewport = page.getViewport({ scale });
  const context = canvas.getContext("2d");

  canvas.height = viewport.height;
  canvas.width = viewport.width;

  await page.render({ canvasContext: context, viewport }).promise;
  return page;
}

/**
 * Sort text items on a page from top-to-bottom, left-to-right so that
 * keyword searching produces natural reading-order matches.
 *
 * @param {any} textContent - Raw text content from page.getTextContent().
 * @returns {any} The same object with items sorted in reading order.
 */
function rearrangePageText(textContent) {
  textContent.items.sort((a, b) => {
    const aY = a.transform[5];
    const bY = b.transform[5];
    const tolerance = Math.max(a.transform[3], b.transform[3]) * 0.5;

    if (Math.abs(aY - bY) <= tolerance) {
      return a.transform[4] - b.transform[4]; // same line → left to right
    }
    return bY - aY; // top to bottom
  });
  return textContent;
}

/**
 * Extract the normalised text content of a single PDF page together with
 * per-item character-range metadata needed for precise highlight positioning.
 *
 * @param {import('pdfjs-dist').PDFPageProxy} page
 * @returns {Promise<{normalized: string, diffs: any, itemRanges: Array<{start: number, end: number, item: any}>}>}
 */
export async function extractPageText(page) {
  let textContent = await page.getTextContent({ disableNormalization: true });
  textContent = rearrangePageText(textContent);

  const normalizedParts = [];
  const itemRanges = [];
  let normalizedPosition = 0;

  for (const item of textContent.items) {
    if (!("str" in item)) continue; // skip TextMarkedContent

    const [normalizedItemText] = normalize(item.str);

    itemRanges.push({
      start: normalizedPosition,
      end: normalizedPosition + normalizedItemText.length,
      item,
    });
    normalizedParts.push(normalizedItemText);
    normalizedPosition += normalizedItemText.length;

    if (item.hasEOL) {
      normalizedParts.push("\n");
      normalizedPosition += 1;
    }
  }

  const normalizedText = normalizedParts.join("");
  return { normalized: normalizedText, itemRanges };
}

/**
 * Extract the normalised text content for every page in a PDF.
 *
 * @param {import('pdfjs-dist').PDFDocumentProxy} pdf
 * @returns {Promise<Array<{normalized: string, itemRanges: Array<{start: number, end: number, item: any}>}>>}
 */
export async function getAllPdfText(pdf) {
  const results = [];
  for (let i = 1; i <= pdf.numPages; i++) {
    const page = await pdf.getPage(i);
    results.push(await extractPageText(page));
  }
  return results;
}
