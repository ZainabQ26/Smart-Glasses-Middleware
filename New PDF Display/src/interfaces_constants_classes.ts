import type { PDFDocumentProxy } from "pdfjs-dist";

export class ViewerState {
    private _pdf: PDFDocumentProxy;
    private _page_number: number;
    private _highlights: HighlightWord[] = new Array(Object.keys(HighlightColors).length);      // The length is limited based off the number of colors

    constructor (pdf: PDFDocumentProxy) {
        this._pdf = pdf;
        this._page_number = 1;
        this._highlights = [];
    }

    get pdf (): PDFDocumentProxy {
        return this._pdf;
    }
    
    set page_number (page_number: number) {
        this._page_number = page_number;
    }
    get page_number () {
        return this._page_number;
    }
    


    get highlights () {
        return this._highlights;
    }
    
    removeKeyWord(key_word: string): void {
        this._highlights = this._highlights.filter(highlight => highlight.key_word !== key_word);
    }
    getKeyWords (): String [] {
        return this._highlights
            .flatMap(items => items.key_word);
    }
    getRectangleArray (key_word: string): HighlightRectangles [] {
        return this._highlights
            .filter(word_and_locations => word_and_locations.key_word === key_word)                 // Filters out all the ones that don't have the same keyword
            .flatMap(item => item.rectangle_arrays);                                                 // Makes it so that it returns only the rectangle array part
    }
    addHighlight (highlight: HighlightWord): void {
        this._highlights.push(highlight);
    }
    // Returns an a 2-D array (Array of arrays) of Highlight Rectangles (where it is an array, of an array where each sub-array keeps the location of a word)
    getLocationsForPage (page_number: number): HighlightRectangles [][] {
        return this._highlights
            .map(highlight_items =>
                highlight_items.rectangle_arrays.filter(item => item.page_number === page_number)
            );
    }
}

export interface TextContent {
    items: PdfTextItem[],
    lang: string,
    styles: object,
};
export interface PdfTextItem {
    str: string,
    dir: string,
    width: number,
    height: number,
    transform: [number, number, number, number, number, number];
    fontName: string,
    hasEOL: boolean
};

export interface ItemRange {
    start: number;
    end: number;
    item: PdfTextItem;
}
export interface OrderedText {
    normalized: string,
    diffs: [Uint32Array, Int32Array],  // [starts, shifts] from normalize()
    item_ranges: ItemRange[]
}

export interface HighlightWord {
    key_word: string,
    rectangle_arrays: HighlightRectangles [];
}
// For canvas Use
export interface HighlightRectangles {
    canvas_x: number;
    canvas_y: number;
    canvas_width: number;
    canvas_height: number;
    page_number: number;
}

// For pdf-lib use
export interface HighlightRectangleLib {
    page: number,
    x: number,
    y: number,
    width: number,
    height: number
};

export const HighlightColors = {
    yellow:         [255, 220, 0, 0.45],
    sky_blue:       [0, 191, 255, 0.45],
    soft_green:     [144, 238, 144, 0.45],
    light_coral:    [240, 128, 128, 0.45],
    lavender:       [230, 190, 255, 0.45],
    grey:           [128, 128, 128, 0.45]    
} as const;

