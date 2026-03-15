import type { PageViewport, PDFDocumentProxy, PDFPageProxy } from "pdfjs-dist";
import { getDocument } from 'pdfjs-dist';

import type { TextContent, PdfTextItem, ItemRange, OrderedText } from "./interfaces_constants_classes";

// TESTING
import {TESTING_get_element_by_id} from "./testing_purposes";
import { normalize } from "./mozilla/pdf_find_controller";

export async function get_pdf (url : string) : Promise<PDFDocumentProxy | null> {
    // return await getDocument(url);
    try {
        return getDocument(url).promise;
    }
    catch (error) {
        console.error(`Error when getting the document: ${error}`);
        return null;
    }
}

export async function render_pdf_page (pdf : PDFDocumentProxy, page_number : number) : Promise<PDFPageProxy | null> {
    let page : PDFPageProxy | null;                                // Stores the pdf, but only a single page of it
    let canvas = TESTING_get_element_by_id("pdf-container") as HTMLCanvasElement | null;
    let context : CanvasRenderingContext2D | null;
    const scale : number = 1.0;
    let viewport : PageViewport;
    let render_context : any;
    let render_task : any;

    if (!canvas) {
        console.error("Canvas failed to be obtained");
        return null;
    }

    // Load the page
    page = await pdf.getPage(page_number);
    if (!page) {
        console.error("Page has failed");
        return null;
    }

    // Scale and stuff
    viewport = page.getViewport({scale: scale});

    // Prepare the canvas using PDF page dimensions
    context = (canvas as HTMLCanvasElement).getContext("2d");
    canvas.height = viewport.height;
    canvas.width = viewport.width;

    // Render the PDF page into the canvas context
    render_context = {
        canvasContext: context,
        viewport: viewport
    };

    render_task = page.render(render_context);
    
    // Print out an a-ok
    await render_task.promise;
    console.log("The page has been rendered");

    return page; // At this rate, it is guaranteed to be fine, and of PDFPageProxy type
}

export async function extract_page_text (page : PDFPageProxy) : Promise<OrderedText> {
    // First get the text context
    let text_content = await page.getTextContent({disableNormalization: true}) as unknown as TextContent;
    
    const normalized_parts : string[] = [];
    const item_ranges : ItemRange[] = [];
    let normalized_position : number = 0;

    text_content = rearrange_page_text(text_content);

    for (const item of text_content.items) {
        // Skip non-text items (TextMarkedContent)
        if (!("str" in item)) {
            continue;
        }

        // Normalize this item's text
        const [normalized_item_text] = normalize(item.str);
        
        item_ranges.push({
            start: normalized_position,
            end: normalized_position + normalized_item_text.length,
            item
        });
        normalized_parts.push(normalized_item_text);
        normalized_position += normalized_item_text.length;

        if (item.hasEOL) {
            normalized_parts.push("\n");
            normalized_position += 1;
        }
    }
    
    const normalized = normalized_parts.join("");
    // diffs not needed since item_ranges are now in normalized space
    const diffs: [Uint32Array, Int32Array] = [new Uint32Array(0), new Int32Array(0)];
    
    return {normalized, diffs, item_ranges} as OrderedText;
}

function rearrange_page_text(text_content : any) : any {
    let a_y : number;
    let b_y : number;
    let tolerance : number;

    text_content.items.sort((a: PdfTextItem, b: PdfTextItem) => {
        // Get the y-axis values from the two elements
        a_y = a.transform[5];                 // the 5th element in the transform array is the y-axis
        b_y = b.transform[5];

        // Figure out how to handel the font size
        tolerance = Math.max(a.transform[3], b.transform[3]) * 0.5;          // the third element represents the vertical scaling (aka, most of the time, the letter scaling, since usually horizontal scaling = vertical)

        // If they are on the same line (with a tolerable y-distance)
        if (Math.abs(a_y - b_y) <= tolerance) {     // Checks to see if the difference in y-level between the two, is within the tolerance level
            // If this is the case, then sort it left to right
            return a.transform[4] - b.transform[4];
        }

        // Sorting from top to bottom
        return b_y - a_y;
    });

    // Once all the sorting has finished, return the newly sorted text_content
    return text_content
}

export async function get_all_pdf_text (pdf : PDFDocumentProxy) : Promise<OrderedText []> {
    const number_of_pages : number = pdf.numPages;
    let page : PDFPageProxy;
    let all_pdf_text : OrderedText[] = [];

    // Loop over all of the pages in the pdf. For each page, extract, and organize the text. Then store it into an array
    for (let page_number : number = 1; page_number <= number_of_pages; ++page_number) {
        // Get the page
        page = await pdf.getPage(page_number);

        // Extract the text from that page
        all_pdf_text.push(await extract_page_text(page));
    }
    
    return all_pdf_text;
}