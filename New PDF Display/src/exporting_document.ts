import { PDFDocument, rgb } from "pdf-lib";
import { HighlightColors, type ViewerState } from "./interfaces_constants_classes";

 
export async function set_up_exporting_button(viewer: ViewerState, pdf_address: string) {
    const originalPdfBytes: Uint8Array = await fetch(pdf_address).then(res => res.arrayBuffer()).then(buf => new Uint8Array(buf));
    const exportBtn = document.getElementById("export-pdf-btn");
    if (!exportBtn) {
        console.error("Export button not found");
        return;
    }
    exportBtn.addEventListener("click", async () => {
        exportBtn.setAttribute("disabled", "true");
        try {
            const exportedPdfBytes = await export_highlights_to_pdf_lib(originalPdfBytes, viewer);
            const safePdfBytes = Uint8Array.from(exportedPdfBytes);
            const blob = new Blob([safePdfBytes], { type: "application/pdf" });
            const url = URL.createObjectURL(blob);
            const a = document.createElement("a");
            a.href = url;
            a.download = "highlighted.pdf";
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        } catch (err) {
            console.error("Failed to export PDF:", err);
        } finally {
            exportBtn.removeAttribute("disabled");
        }
    });
}

function round(value: number, decimal_place: number) {
    return Math.round(value * 100) / Math.pow(10, decimal_place);
}

function normalize_color_values (): { [key: string]: [number, number, number, number]} {
    const array_of_arrays = Object.values(HighlightColors);
    const color_map: { [key: string]: [number, number, number, number] } = {};
    const array_of_names = Object.keys(HighlightColors);
    const rgb_array: number []  = new Array(4);
    const decimal_place_rounding = 2;

    for (let i: number = 0; i < array_of_arrays.length; ++i) {
        const color_array = array_of_arrays[i];
        rgb_array[0] = round((color_array[0] / 255), decimal_place_rounding);
        rgb_array[1] = round((color_array[1] / 255), decimal_place_rounding);
        rgb_array[2] = round((color_array[2] / 255), decimal_place_rounding);
        // Opacity is already on 0 - 1 scale
        rgb_array[3] = color_array[3];


        // Then add this array to the color map
        color_map[array_of_names[i]] = [rgb_array[0], rgb_array[1], rgb_array[2], rgb_array[3]];
    }
    
    return color_map;
}

export async function export_highlights_to_pdf_lib (originalPdfBytes: Uint8Array, viewer_context: ViewerState): Promise<Uint8Array> {
    const pdfDoc = await PDFDocument.load(originalPdfBytes);
    
    // Convert the highlighter colors to a 0 to 1 normalization. I.e divide the values by 255
    const color_map: { [key: string]: [number, number, number, number] } = normalize_color_values();

    const highlight_colors: string [] = Object.keys(color_map);

    // Loop throught the highlights
    let index = 0;
    for (const highlight of viewer_context.highlights) {
        const colorName: string = highlight_colors[index % highlight_colors.length];
        const color_array = color_map[colorName];
        const color: [number, number, number] = [color_array[0], color_array[1], color_array[2]];
        for (const rect of highlight.rectangle_arrays) {
            const page = pdfDoc.getPage(rect.page_number - 1);
            // pdf-lib uses bottom-left origin, so y needs adjustment
            const { height } = page.getSize();
            const pdfY = height - rect.canvas_y - rect.canvas_height;
            page.drawRectangle({
                x: rect.canvas_x,
                y: pdfY,
                width: rect.canvas_width,
                height: rect.canvas_height,
                color: rgb(...color),
                opacity: color_array[3]
            });
        }
        index++;
    }
    return await pdfDoc.save();
}
