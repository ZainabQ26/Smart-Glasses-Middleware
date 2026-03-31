#!/bin/bash
set -e

# Get the directory of this script and then the project root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
cd "$PROJECT_ROOT"

echo "Cleaning old test results..."
rm -rf ./TestResults
rm -rf ./CoverageReport

echo "Running tests with coverage..."
dotnet test Smart-Glasses-Middleware.sln --settings tests/SmartGlassesMiddleware.Tests/coverage.runsettings --collect:"XPlat Code Coverage" --results-directory ./TestResults

# Find the generated coverage file
COVERAGE_FILE=$(find ./TestResults -name "coverage.cobertura.xml" | sort -r | head -n 1)

if [ -z "$COVERAGE_FILE" ]; then
    echo "Error: Coverage file not found."
    sh -c 'exit 1'
fi

echo "Generating reports from $COVERAGE_FILE..."
# Ensure the reportgenerator tool is installed
if ! command -v reportgenerator &> /dev/null
then
    dotnet tool install -g dotnet-reportgenerator-globaltool || true
    export PATH="$PATH:$HOME/.dotnet/tools"
fi

reportgenerator -reports:"$COVERAGE_FILE" -targetdir:"./CoverageReport" -reporttypes:"HtmlSummary;TextSummary"

# Extract coverage percentage from TextSummary
COVERAGE=$(grep "Line coverage" ./CoverageReport/Summary.txt | head -n 1 | awk -F': ' '{print $2}' | tr -d '%')

echo "Total Line Coverage: $COVERAGE%"

# Generate PDF from HTML using puppeteer
echo "Generating PDF summary..."

cat << 'JS_EOF' > ./CoverageReport/html2pdf.js
const puppeteer = require('puppeteer');

(async () => {
  try {
    const browser = await puppeteer.launch({ args: ['--no-sandbox', '--disable-setuid-sandbox'] });
    const page = await browser.newPage();
    const url = 'file://' + process.argv[2];
    await page.goto(url, {waitUntil: 'networkidle0'});
    await page.pdf({path: process.argv[3], format: 'A4', printBackground: true});
    await browser.close();
  } catch (e) {
    console.error("Puppeteer error:", e);
    process.exitCode = 1;
  }
})();
JS_EOF

# Install puppeteer temporarily if not present in the current node_modules or globally
# Avoid modifying package-lock.json or package.json
if ! node -e "require('puppeteer');" >/dev/null 2>&1; then
    echo "Puppeteer not found. Installing temporarily without saving..."
    # install in a temp dir to completely avoid messing with project node_modules/package-lock.json
    export TEMP_NPM_DIR=$(mktemp -d)
    cd "$TEMP_NPM_DIR"
    npm init -y > /dev/null 2>&1
    npm install puppeteer > /dev/null 2>&1
    cd "$PROJECT_ROOT"
    export NODE_PATH="$TEMP_NPM_DIR/node_modules:$NODE_PATH"
fi

# Run the puppeteer script with absolute paths
node ./CoverageReport/html2pdf.js "$(realpath ./CoverageReport/summary.html)" "$(realpath ./CoverageReport)/Summary.pdf"

# Clean up
rm ./CoverageReport/html2pdf.js
if [ -n "$TEMP_NPM_DIR" ] && [ -d "$TEMP_NPM_DIR" ]; then
    rm -rf "$TEMP_NPM_DIR"
fi

# Check coverage threshold
INT_COVERAGE=${COVERAGE%.*}
if [ -z "$INT_COVERAGE" ]; then
   INT_COVERAGE=0
fi

if [ "$INT_COVERAGE" -lt 10 ]; then
    echo "Coverage is below 10%. Failing."
    sh -c 'exit 1'
fi

echo "Coverage is over 10%. Success."
