#!/bin/bash

# E2E Test Execution Script
# This script provides various options for running Playwright E2E tests

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Function to print colored output
print_info() {
    echo -e "${GREEN}[INFO]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Check if Node.js is installed
if ! command -v node &> /dev/null; then
    print_error "Node.js is not installed. Please install Node.js v18 or higher."
    exit 1
fi

# Check if npm is installed
if ! command -v npm &> /dev/null; then
    print_error "npm is not installed. Please install npm."
    exit 1
fi

# Check if dependencies are installed
if [ ! -d "node_modules" ]; then
    print_warning "Dependencies not found. Installing..."
    npm install
fi

# Check if Playwright browsers are installed
if [ ! -d "$HOME/.cache/ms-playwright" ] && [ ! -d "$HOME/Library/Caches/ms-playwright" ]; then
    print_warning "Playwright browsers not found. Installing Chromium..."
    npx playwright install chromium
fi

# Parse command line arguments
TEST_MODE=${1:-all}

case $TEST_MODE in
    setup)
        print_info "Running setup validation tests..."
        SKIP_WEB_SERVER=1 npx playwright test e2e-tests/00-setup-validation.spec.ts
        ;;
    
    quick)
        print_info "Running quick tests (health and setup)..."
        SKIP_WEB_SERVER=1 npx playwright test e2e-tests/00-setup-validation.spec.ts
        npx playwright test e2e-tests/01-health.spec.ts --project=chromium
        ;;
    
    admin)
        print_info "Running admin page tests..."
        npx playwright test e2e-tests/02-admin-page.spec.ts e2e-tests/06-admin-features.spec.ts --project=chromium
        ;;
    
    monitoring)
        print_info "Running monitoring page tests..."
        npx playwright test e2e-tests/03-monitoring-page.spec.ts e2e-tests/07-monitoring-features.spec.ts --project=chromium
        ;;
    
    api)
        print_info "Running API endpoint tests..."
        npx playwright test e2e-tests/04-api-endpoints.spec.ts --project=chromium
        ;;
    
    navigation)
        print_info "Running navigation tests..."
        npx playwright test e2e-tests/05-navigation.spec.ts --project=chromium
        ;;
    
    errors)
        print_info "Running error handling tests..."
        npx playwright test e2e-tests/08-error-handling.spec.ts --project=chromium
        ;;
    
    integration)
        print_info "Running integration tests..."
        npx playwright test e2e-tests/09-integration.spec.ts --project=chromium
        ;;
    
    chromium)
        print_info "Running all tests on Chromium only..."
        npx playwright test --project=chromium
        ;;
    
    headed)
        print_info "Running tests in headed mode..."
        npx playwright test --headed --project=chromium
        ;;
    
    debug)
        print_info "Running tests in debug mode..."
        npx playwright test --debug
        ;;
    
    ui)
        print_info "Opening Playwright UI mode..."
        npx playwright test --ui
        ;;
    
    report)
        print_info "Opening test report..."
        npx playwright show-report
        ;;
    
    all)
        print_info "Running all E2E tests..."
        npm test
        ;;
    
    help|--help|-h)
        echo "E2E Test Execution Script"
        echo ""
        echo "Usage: ./run-e2e-tests.sh [mode]"
        echo ""
        echo "Modes:"
        echo "  setup       - Run setup validation tests only (no app required)"
        echo "  quick       - Run quick tests (setup + health)"
        echo "  admin       - Run admin page tests"
        echo "  monitoring  - Run monitoring page tests"
        echo "  api         - Run API endpoint tests"
        echo "  navigation  - Run navigation tests"
        echo "  errors      - Run error handling tests"
        echo "  integration - Run integration tests"
        echo "  chromium    - Run all tests on Chromium only"
        echo "  headed      - Run tests with visible browser"
        echo "  debug       - Run tests in debug mode"
        echo "  ui          - Open Playwright UI mode"
        echo "  report      - Show test report from last run"
        echo "  all         - Run all tests (default)"
        echo "  help        - Show this help message"
        echo ""
        echo "Examples:"
        echo "  ./run-e2e-tests.sh setup      # Quick validation"
        echo "  ./run-e2e-tests.sh admin      # Test admin page"
        echo "  ./run-e2e-tests.sh headed     # See browser"
        echo "  ./run-e2e-tests.sh all        # Full test suite"
        ;;
    
    *)
        print_error "Unknown mode: $TEST_MODE"
        echo "Run './run-e2e-tests.sh help' for usage information"
        exit 1
        ;;
esac

print_info "Test execution complete!"
