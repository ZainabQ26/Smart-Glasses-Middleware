import type { PDFPageProxy } from "pdfjs-dist";

import { render_pdf_page } from "./pdf"
import { ViewerState } from "./interfaces_constants_classes";
import { draw_multi_highlights } from "./keyword_handling_and_searching";

interface PageNavigationResult {
    page: PDFPageProxy | null,
    page_number: number | null
};

export type PageChangeCallback = (page: PDFPageProxy, pageNumber: number) => void;

async function prev_page (viewer_context : ViewerState, current_page : number, page : PDFPageProxy, context: CanvasRenderingContext2D) : Promise<PageNavigationResult> {
    // First check if we can go back a page or not
    if (current_page <= 1) {
        return {page, page_number: current_page}
    }
    // Otherwise we can goto the previous page
    current_page--;                                             // Update the current page

    return {page: await get_page(viewer_context, current_page, context), page_number: current_page};
}

async function next_page (viewer_context: ViewerState, current_page : number, number_of_pages : number, page : PDFPageProxy, context: CanvasRenderingContext2D) : Promise<PageNavigationResult> {
    // First check if we can advance the page or not
    if (number_of_pages <= current_page) {
        return {page: page, page_number: current_page};
    }
    // Otherwise, we can goto the next page
    current_page++;

    return {page: await get_page(viewer_context, current_page, context), page_number: current_page};
} 

async function goto_page (viewer_context: ViewerState, input_text : string, number_of_pages : number, page : PDFPageProxy, context: CanvasRenderingContext2D) : Promise<PageNavigationResult | null> {
    let page_number : number

    // First cut it down to the first "word"
    input_text = input_text.trim();                     // Gets rid of the leading or trailing white space
    input_text = input_text.split(" ")[0];              // Gets only the first word
    
    // Check to see if the string is not a number
    if (isNaN(Number(input_text))) {
        return {page: page, page_number: null};
    }
    

    // Since it is a number, we can check if it is not a valid page, by making sure it is within the range, and is not a floating point number
    page_number = Number(input_text);
    if (page_number < 1 || number_of_pages < page_number || page_number % 1 !== 0) {
        return {page: page, page_number: null};
    }

    // Since it is a valid page, then we can goto it
    return {page: await get_page(viewer_context, page_number, context), page_number: page_number};
}   

export async function get_page (viewer_context : ViewerState, current_page : number, context : CanvasRenderingContext2D) : Promise<PDFPageProxy | null> {
    let page : PDFPageProxy | null;
    
    // Render the page
    if ((page = await render_pdf_page(viewer_context.pdf, current_page)) === null) {
        console.error("Page failed to load");
        return null;
    }

    // Update the global viewer state tracker
    viewer_context.page_number = current_page;

    // Check to see if we need to apply any highlighting, and if we do, then apply it to the page
    if (viewer_context.highlights.length > 0) {                                 // It's one because we already update viewer_context, so I made the limit higher then one
        draw_multi_highlights(viewer_context, (context as CanvasRenderingContext2D));
    }

    // Either way return the page
    return page;
}

export function page_navigation (viewer_context : ViewerState, number_of_pages : number, page: PDFPageProxy, context: CanvasRenderingContext2D, onPageChange?: PageChangeCallback) : void {
    let current_page : number = 1;                                              // This represents the current page that we are on.
    const prev_button = document.getElementById("prev-page-button") as HTMLButtonElement | null;
    const next_button = document.getElementById("next-page-button") as HTMLButtonElement | null;
    const goto_page_input_box = document.getElementById("jump-to-page-number-box") as HTMLInputElement | null;
    let page_nav_result : PageNavigationResult | null;

    // If either, the previous button,next button, or the page input box were retrieved incorrectly
    if (!prev_button || !next_button || !goto_page_input_box) {
        return;     
    }

    prev_button.addEventListener('click', async () => {
        page_nav_result = await prev_page(viewer_context, current_page, page, context);
        page = (page_nav_result.page as PDFPageProxy);
        current_page = (page_nav_result.page_number as number);
        onPageChange?.(page, current_page);
    });
    next_button.addEventListener('click', async () => {
        page_nav_result = await next_page(viewer_context, current_page, number_of_pages, page, context);
        page = (page_nav_result.page as PDFPageProxy);
        current_page = (page_nav_result.page_number as number);
        onPageChange?.(page, current_page);
    });
    goto_page_input_box.addEventListener('keydown', async (key) => {
        if (key.key === "Enter") {
            const input_text : string = goto_page_input_box.value;
            page_nav_result = await goto_page(viewer_context, input_text, number_of_pages, page, context);
            if (page_nav_result && page_nav_result.page_number) {
                current_page = (page_nav_result.page_number as number);
                page = (page_nav_result.page as PDFPageProxy);
                onPageChange?.(page, current_page);
            }
        }
    });
    
}   