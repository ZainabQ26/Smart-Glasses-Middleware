import { useEffect } from "react";
import { pdf_js_main } from "./run_program";
import { PageNavigationControls } from "./components/PageNavigationControls";
import { PdfCanvas } from "./components/PdfCanvas";
import { KeywordControls } from "./components/KeywordControls";
import { ExportButton } from "./components/ExportButton";

export function App() {
  useEffect(() => {
    void pdf_js_main();
  }, []);

  return (
    <div id="app">
      <h1>PDF.js Test</h1>
      <PageNavigationControls />
      <PdfCanvas />
      <KeywordControls />
      <ExportButton />
    </div>
  );
}
