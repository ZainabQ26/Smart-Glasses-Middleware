import { type PageViewport, type PDFDocumentProxy } from "pdfjs-dist"
import { normalize } from "./mozilla/pdf_find_controller.ts"
import type { OrderedText, HighlightRectangles, HighlightWord} from "./interfaces_constants_classes.ts"
import { HighlightColors, ViewerState } from "./interfaces_constants_classes.ts"
import { get_all_pdf_text } from "./pdf.ts"
import { get_page } from "./pdf_navigation.ts";

export async function create_keyword_input_box_event (view_context: ViewerState): Promise<void> {
    const key_word_text_box = document.getElementById("keyword-search-box") as HTMLInputElement | null;
    const canvas = document.getElementById("pdf-container") as HTMLCanvasElement;
    const context = canvas?.getContext("2d");
    const viewports : PageViewport [] = [];
    let key_word : string;

    // Check to see if it worked correctly
    if (!key_word_text_box) {
        console.error("The search box did not load");
        return;
    }
    // Check to see if the context is working
    if (!context) {
        console.error("Error when dealing with the context");
        return;
    }

    // Create the viewports
    for (let i : number = 1; i <= view_context.pdf.numPages; ++i) {
        viewports.push((await view_context.pdf.getPage(i)).getViewport({scale: 1}));
    } 

    // Create the event
    key_word_text_box.addEventListener('keydown', async (key) => {
        if (key.key === "Enter") {
            key_word = key_word_text_box.value;
            key_word_text_box_helper(view_context, key_word, viewports, context);
        }
    })

    // Removal Section
    const removal_text = document.getElementById("key-word-removal-box") as HTMLInputElement;
    removal_text?.addEventListener('keydown', async (key) => {
        if (key.key === "Enter") {
            const to_be_deleted : string = removal_text.value;
            delete_key_word(view_context, to_be_deleted, context);
        }
    });
}

async function key_word_text_box_helper (viewer_context: ViewerState, key_word: string, viewports: PageViewport[], context: CanvasRenderingContext2D) : Promise<void> {
    // make it lowercase, so it matches the text
    key_word = key_word.toLowerCase();
    
    // First check if the list includes the word, if it does, return early
    if (viewer_context.getKeyWords().includes(key_word)) {
        // Since we found the word, we will return early
        return;
    }
    // Check to see if we have not exceed the amount of allowed highlights, where the limit is based off the amount of colors we have
    else if (viewer_context.highlights.length >= Object.keys(HighlightColors).length) {
        // Since all the colors have been chosen, return early
        return;
    }
    // Check to see if it is an empty sting
    else if (key_word.trim() === "") {
        return;
    }
    
    let search_result: HighlightWord;
    // Search for the keyword. If none is found, then don't append it. If the key word is found, then append it to the list
    search_result = await search_for_keyword(key_word, viewer_context.pdf, viewports)
    if (search_result.rectangle_arrays.length !== 0) {
        let nearest_location: number;
        const locations : HighlightRectangles [] = search_result.rectangle_arrays;
        const page_number = viewer_context.page_number;
        // Find out what the closest page is that has the keyword
        nearest_location = find_nearest_page(locations, page_number);
        // Update the list of keywords and the current_page
        viewer_context.addHighlight(search_result);
        viewer_context.page_number = nearest_location;
        // Check to see if the closest page is the nearest page, if it is not, then we need to reload the page. If it is, then no need to reload the page
        if (page_number !== nearest_location) {
            // Jump to the closest page
            get_page(viewer_context, viewer_context.page_number, context);
        }
        else {
            // Because we are on the page with the closest word, then there is no need to render a new page, or re-highlight everything. So just do a single highlight
            const color_index = viewer_context.highlights.length - 1;                           // Because key word index and the color are directly related
            draw_single_highlights(locations, color_index, page_number, context);
        }
    }
}

function find_nearest_page(rectangle_array: HighlightRectangles [], current_page: number) : number {
    // Create a list of page locations where the key word occurs
    const key_word_page_locations: number [] = [...new Set (rectangle_array.map(item => item.page_number))];
    // Set the nearest location to the first location. Because it will be a circular page structure. Thus if it isn't found after the current page, then it will be the first page in the list.
    let nearest_location: number = 0;

    // Check to see if we are already on a page with the key word in it
    if (key_word_page_locations.includes(current_page)) {
        // Then we are already on that page, thus don't goto to any page
        nearest_location = current_page;
    }   
    else {
        // Otherwise find the nearest page
        for (const location of key_word_page_locations) {
            // The first one that passes this conditional, will be the closest location
            if (current_page < location) {
                nearest_location = location;
                break;
            }
        }
    }
    // Return the page number that the match was found on
    return nearest_location;
}

export async function search_for_keyword (key_word : string, pdf : PDFDocumentProxy, viewports: PageViewport []) : Promise<HighlightWord> {
    const [normalized_key_word] = normalize(key_word); 
    const lowercased_keyword = normalized_key_word.toLowerCase();
    const locations : HighlightRectangles [] = []; 
    const temp_canvas = document.createElement("canvas");
    const temp_ctx = temp_canvas.getContext("2d");

    if (!temp_ctx) {
        return {key_word: key_word, rectangle_arrays: locations};
    }

    // Search the pdf for the text
    const all_text : OrderedText [] = await get_all_pdf_text(pdf);

    for (let page_number = 0; page_number < all_text.length; page_number++) {
        const { normalized, item_ranges } = all_text[page_number];
        const page_text = normalized.toLowerCase();
        const view_port = viewports[page_number];
        const page = await pdf.getPage(page_number + 1);
        await page.getOperatorList();
        const font_metrics_cache = new Map<string, { ascent: number; descent: number }>();

        const get_font_metrics = async (font_name: string): Promise<{ ascent: number; descent: number }> => {
            const cached_metrics = font_metrics_cache.get(font_name);
            if (cached_metrics) {
                return cached_metrics;
            }

            const font_object = await new Promise(resolve => {
                page.commonObjs.get(font_name, resolve);
            }) as { ascent?: number; descent?: number };

            const ascent = typeof font_object?.ascent === "number" ? font_object.ascent : 0.8;
            const descent = typeof font_object?.descent === "number" ? font_object.descent : -0.2;
            const metrics = { ascent, descent };
            font_metrics_cache.set(font_name, metrics);
            return metrics;
        };

        // Search in the normalized page text
        let search_index = 0;
        while ((search_index = page_text.indexOf(lowercased_keyword, search_index)) !== -1) {
            const match_start = search_index;
            const match_end = search_index + lowercased_keyword.length;

            // Find all items that this match spans (item_ranges are now in normalized position space)
            for (const range of item_ranges) {
                // Skip items that don't overlap with the match
                if (range.end <= match_start || range.start >= match_end) {
                    continue;
                }

                const item = range.item;

                // Calculate overlap within this item
                const overlap_start = Math.max(match_start, range.start);
                const overlap_end = Math.min(match_end, range.end);
                
                // Convert to local item positions (in normalized space)
                const local_start = overlap_start - range.start;
                const local_end = overlap_end - range.start;

                // Use offscreen canvas to measure text for precise highlight
                const full_text = item.str;
                const prefix_text = full_text.substring(0, local_start);
                const match_text = full_text.substring(local_start, local_end);

                const font_size = Math.abs(item.transform[3]);
                temp_ctx.font = `${font_size}px sans-serif`;

                const full_width = temp_ctx.measureText(full_text).width;
                const prefix_width = temp_ctx.measureText(prefix_text).width;
                const match_width = temp_ctx.measureText(match_text).width;

                // Scale factor: map measured canvas width back to PDF item.width
                const scale = full_width > 0 ? item.width / full_width : 1;

                const canvas_x = item.transform[4] + (prefix_width * scale);
                // const canvas_y = (view_port.height - item.transform[5] - font_size);
                const canvas_width = match_width * scale;
                // const canvas_height = font_size;
                
                // Get the actual font object
                const fontObj = await get_font_metrics(item.fontName);

                // fontObj contains actual metrics
                const ascent = fontObj.ascent;      // value between 0-1, fraction of font size
                const descent = fontObj.descent;    // usually negative

                // const font_size = Math.abs(item.transform[3]);
                const canvas_y = (view_port.height - item.transform[5] - (ascent * font_size));
                const canvas_height = (ascent - descent) * font_size;

                // console.log(`ascent: ${ascender} and descent: ${descent}`);

                locations.push({
                    page_number: page_number + 1,
                    canvas_x,
                    canvas_y,
                    canvas_width,
                    canvas_height
                });

                // console.log(`Found "${lowercased_keyword}" in "${item.str}" [${local_start}-${local_end}] -> x:${canvas_x.toFixed(1)}, y:${canvas_y.toFixed(1)}, w:${canvas_width.toFixed(1)}, h:${canvas_height.toFixed(1)}`);
                
                // console.log(`FontName and Transformation: ${item.fontName} & ${item.transform}`);
            }
            
            search_index++;
        }
    }
    return {key_word: key_word, rectangle_arrays: locations};
}

function form_color_array_for_context () {
    const array_of_arrays = Object.values(HighlightColors);
    const array_of_colors: string[] = [];
    
    for (const color_array of array_of_arrays) {
        let color_string: string = "rgba(";
        color_string += `${color_array[0]},${color_array[1]},${color_array[2]},${color_array[3]}`;
        array_of_colors.push(color_string);
    }

    return array_of_colors;
}

// For drawing a single word on the page (When we haven't moved to a new page since the last highlight)
export function draw_single_highlights (key_word_locations: HighlightRectangles [], color_index: number, page_number: number, context: CanvasRenderingContext2D) {
    // Pick the current color for the context
    
    context.fillStyle = form_color_array_for_context()[color_index];

    // Loop over all the keywords and then highlight those keywords
    for (const location of key_word_locations) {
        if (location.page_number === page_number) {
            context.fillRect(location.canvas_x, location.canvas_y, location.canvas_width, location.canvas_height);
            // console.log(`Drawing highlight at (${location.canvas_x.toFixed(1)}, ${location.canvas_y.toFixed(1)}) size ${location.canvas_width.toFixed(1)}x${location.canvas_height.toFixed(1)}`);
        }
    }
}   

export function draw_multi_highlights (viewer_context: ViewerState, context: CanvasRenderingContext2D) {
    const list_of_key_words_locations = viewer_context.getLocationsForPage(viewer_context.page_number);                         // Array of the HighlightRectangles
    const color_array: string [] = form_color_array_for_context();                


    // Loop over all of the key words
    for (let outer_array_index: number = 0; outer_array_index < list_of_key_words_locations.length; ++outer_array_index) {
        // Get the array of a key words locations
        const array_of_locations: HighlightRectangles[] = list_of_key_words_locations[outer_array_index];

        // Set the color based off the index of the outer array. Every keyword has a different color
        context.fillStyle = color_array[outer_array_index];
        
        // Loop over each location of the key word that appears on the page
        for (const location of array_of_locations) {
            context.fillRect(location.canvas_x, location.canvas_y, location.canvas_width, location.canvas_height);
        }
    }
}

export async function delete_key_word (view_context: ViewerState, key_word: string, context: CanvasRenderingContext2D) {
    // Search for the key word in the list, if the word exists in the list delete it. If it doesn't, then don't do anything
    
    // Check to see if it is white space
    if (key_word.trim() === "") {
        return;
    }

    // make the word lowercase
    key_word = key_word.toLowerCase();

    if (view_context.getKeyWords().includes(key_word)) {

        // Remove it from the list of key words
        view_context.removeKeyWord(key_word);
        
        // update the page
        await get_page(view_context, view_context.page_number, context);
    }
}