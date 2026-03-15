// Import from pdfjs-dist
import type {PDFDocumentProxy, PDFPageProxy } from "pdfjs-dist";
// Import from pdf-lib

// import {getDocument, type PDFDocumentProxy, type PDFPageProxy} from "./pdfHelper";

// Import from other files
import { get_pdf, render_pdf_page} from "./pdf"
import {page_navigation} from "./pdf_navigation"
import {create_keyword_input_box_event} from "./keyword_handling_and_searching";
import { set_up_exporting_button} from "./exporting_document";

// For testing purposes
import {TESTING_get_pdf_address} from "./testing_purposes"
import { ViewerState } from "./interfaces_constants_classes";


export async function pdf_js_main () : Promise<void> {
    let pdf_address : string = "";
    let pdf : PDFDocumentProxy | null;
    let page_number : number = 1;
    let number_of_pages : number = 0;
    let page : PDFPageProxy | null;
    const canvas = document.getElementById("pdf-container") as HTMLCanvasElement;
    const context = canvas?.getContext("2d");
    
    
    // Get the pdf address
    pdf_address = TESTING_get_pdf_address();

    try {
        // Get the pdf
        pdf = await get_pdf(pdf_address);
        // Check to see if the pdf was retrieved successfully
        if (!pdf) {
            console.error("Failed to load the pdf");
            return;
        }

        // Update the page numbers
        number_of_pages = pdf.numPages;

        // Check to see if the canvas was able to be obtained
        if (!canvas) {
            console.error("The canvas was not loaded");
            return;
        }

        // Create the object
        const viewer_context = new ViewerState (pdf);

        // Display the pdf, and store the page
        if( (page = await render_pdf_page(pdf, page_number)) === null) {
            console.error("The page was not returned properly");
            return;
        }

        // Set up the next page and prev page buttons
        page_navigation(viewer_context, number_of_pages, (page as PDFPageProxy), (context as CanvasRenderingContext2D));

        // Create the search call
        create_keyword_input_box_event(viewer_context);

        // Fetch the original PDF bytes and setup export button
            await set_up_exporting_button(viewer_context, pdf_address);
    }
    catch (error) {
        console.error("An error has occurred" + error);
    }
}