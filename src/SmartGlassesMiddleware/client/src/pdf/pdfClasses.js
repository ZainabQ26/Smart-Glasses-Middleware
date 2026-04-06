/**
 * PDF viewer state and constants.
 *
 * ViewerState tracks the currently loaded PDF document, the active page
 * number, and the list of keyword highlights that have been applied.
 *
 * HighlightColors defines the ordered palette used when cycling through
 * highlight colours – the first highlight gets orange, the second sky-blue,
 * and so on.  Once all colours are used the cycle wraps back to the start
 * (replacing the oldest highlight).
 */

/** Ordered colour palette for highlights.  Each entry is [r, g, b, alpha]. */
export const HighlightColors = {
  orange:      [249, 115, 22,  0.55],
  sky_blue:    [0,   191, 255, 0.45],
  soft_green:  [144, 238, 144, 0.45],
  light_coral: [240, 128, 128, 0.45],
  lavender:    [230, 190, 255, 0.45],
  grey:        [128, 128, 128, 0.45],
};

/** Number of available highlight colours. */
export const HIGHLIGHT_COLOR_COUNT = Object.keys(HighlightColors).length;

/**
 * Tracks all runtime state for the PDF viewer:
 * - The loaded PDFDocumentProxy
 * - The current page number
 * - The list of active keyword highlights (each with colour and location data)
 */
export class ViewerState {
  /**
   * @param {import('pdfjs-dist').PDFDocumentProxy} pdf
   */
  constructor(pdf) {
    this._pdf = pdf;
    this._pageNumber = 1;
    /** @type {Array<{keyWord: string, rectangleArrays: HighlightRectangle[]}>} */
    this._highlights = [];
    this._currentIndexOfHighlight = 0;
  }

  get pdf() { return this._pdf; }

  get pageNumber() { return this._pageNumber; }
  set pageNumber(n) { this._pageNumber = n; }

  /** 
  @returns {Number} 
  */
  get currentIndexOfHighlight () {
    return this._currentIndexOfHighlight;
  }
  /**
  @param {string} currentIndex
  @returns {Number}
  */
  set currentIndexOfHighlight (currentIndex) {
    this._currentIndexOfHighlight = currentIndex;
  }

  /** All active highlights. */
  get highlights() { return this._highlights; }

  /** Returns the list of highlighted keywords (lowercase). */
  getKeyWords() {
    return this._highlights.map(h => h.keyWord);
  }

  /**
   * Returns all highlight rectangles for a given keyword.
   * @param {string} keyWord
   * @returns {HighlightRectangle[]}
   */
  getRectangleArray(keyWord) {
    return this._highlights
      .filter(h => h.keyWord === keyWord)
      .flatMap(h => h.rectangleArrays);
  }

  /**
   * Returns an array of per-keyword rectangle lists for the given page.
   * The outer index corresponds to the highlight's colour slot.
   * @param {number} pageNumber
   * @returns {HighlightRectangle[][]}
   */
  getLocationsForPage(pageNumber) {
    return this._highlights.map(h =>
      h.rectangleArrays.filter(r => r.pageNumber === pageNumber)
    );
  }

  /**
   * Add a new keyword highlight record.
   * @param {{keyWord: string, rectangleArrays: HighlightRectangle[]}} highlight
   */
  addHighlight(highlight) {
    this._highlights.push(highlight);
  }

  /**
   * Remove all highlight records for the given keyword (case-sensitive on the
   * already-lowercased keyWord stored internally).
   * @param {string} keyWord
   */
  removeKeyWord(keyWord) {
    this._highlights = this._highlights.filter(h => h.keyWord !== keyWord);
  }

  /** Remove every highlight. */
  clearAll() {
    this._highlights = [];
  }
}

/**
 * @typedef {Object} HighlightRectangle
 * @property {number} canvasX      - Left edge in canvas/PDF-space pixels
 * @property {number} canvasY      - Top edge in canvas/PDF-space pixels
 * @property {number} canvasWidth  - Width in canvas pixels
 * @property {number} canvasHeight - Height in canvas pixels
 * @property {number} pageNumber   - 1-based page number
 */
