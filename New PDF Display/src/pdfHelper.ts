// Import from pdfjsLib node-module, DO I NEED THIS?
import { getDocument, GlobalWorkerOptions, type PDFDocumentProxy, type PDFPageProxy } from 'pdfjs-dist';

// Worker setup - point to the worker script from pdfjs-dist
GlobalWorkerOptions.workerSrc = new URL(
    'pdfjs-dist/build/pdf.worker.mjs',
    import.meta.url
).toString();

// Export specific functions and types for use in other modules
export { getDocument, type PDFDocumentProxy, type PDFPageProxy };