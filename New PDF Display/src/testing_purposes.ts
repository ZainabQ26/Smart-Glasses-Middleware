/* WARNING: This file is only for testing purposes. Most, if not all of the functions/actions in this file
will be swapped out for proper fitting things */

// Types to import

// For getting the file address
export function TESTING_get_pdf_address () : string {
    // Current testing pdf address. It is located in public
    // let pdf_address : string = "dev-example.pdf";
    let pdf_address : string = "epidiolex.pdf";

    return pdf_address;
}

// For getting the any HTML
export function TESTING_get_element_by_id <T extends HTMLElement> (id : string) : T | null {
    return document.getElementById(id) as T | null;
}


