# End-to-End Tests

This directory contains comprehensive end-to-end tests for the Smart Glasses Middleware application using Playwright 1.58.2.

## Overview

The E2E test suite covers all major features of the application including:

- Health and basic API endpoints
- Admin page functionality (file, prompt, and group management)
- Monitoring page with real-time updates via SignalR
- Navigation and page reloading
- Responsive design across different viewports
- Error handling and edge cases
- Performance and security testing
- Full user journey integration tests

## Test Files

- `01-health.spec.ts` - Health endpoint and basic connectivity tests
- `02-admin-page.spec.ts` - Admin page UI and functionality tests
- `03-monitoring-page.spec.ts` - Monitoring page UI and real-time features
- `04-api-endpoints.spec.ts` - API endpoint validation tests
- `05-navigation.spec.ts` - Page navigation and reload tests
- `06-admin-features.spec.ts` - Detailed admin feature tests (files, prompts, groups)
- `07-monitoring-features.spec.ts` - Detailed monitoring and SignalR tests
- `08-error-handling.spec.ts` - Error handling, performance, and security tests
- `09-integration.spec.ts` - Full user journey integration tests

## Prerequisites

- Node.js (v18 or higher recommended)
- Docker and Docker Compose
- The Smart Glasses Middleware application

## Installation

Install Playwright and its dependencies:

```bash
npm install
npx playwright install
```

## Running Tests

### Start the Application

The tests are configured to automatically start the application using Docker Compose. However, you can also start it manually:

```bash
docker-compose up
```

Wait for all services to be healthy before running tests.

### Run All Tests

```bash
npm test
```

### Run Tests in Headed Mode

To see the browser while tests run:

```bash
npm run test:headed
```

### Run Tests in UI Mode

To use Playwright's interactive UI:

```bash
npm run test:ui
```

### Run Tests in Debug Mode

To debug tests:

```bash
npm run test:debug
```

### Run Specific Test File

```bash
npx playwright test e2e-tests/01-health.spec.ts
```

### Run Tests for Specific Browser

```bash
npx playwright test --project=chromium
npx playwright test --project=firefox
npx playwright test --project=webkit
```

## Test Reports

After running tests, view the HTML report:

```bash
npm run test:report
```

The report will be available at `playwright-report/index.html`.

## Test Artifacts

The following artifacts are generated during test runs and are excluded from Git:

- `node_modules/` - NPM dependencies
- `playwright-report/` - HTML test reports
- `test-results/` - Test screenshots, videos, and traces
- `playwright/.cache/` - Playwright browser binaries

All artifacts are properly configured in `.gitignore`.

## Configuration

Test configuration is in `playwright.config.ts`:

- Base URL: `http://localhost:5000`
- Retries: 2 on CI, 0 locally
- Browsers: Chromium, Firefox, WebKit
- Screenshots: Captured on failure
- Videos: Retained on failure
- Traces: Captured on first retry

## Writing New Tests

When adding new tests:

1. Create a new `.spec.ts` file in the `e2e-tests/` directory
2. Follow the existing test structure and patterns
3. Use descriptive test names that explain what is being tested
4. Group related tests using `test.describe()`
5. Clean up after tests if they create data
6. Ensure tests are idempotent and can run in any order

## CI/CD Integration

The tests are designed to run in CI environments:

- Tests run sequentially on CI (`workers: 1`)
- Automatic retries on CI (`retries: 2`)
- Fail fast if `test.only` is left in code
- Web server starts automatically with health check

## Troubleshooting

### Tests Timeout

If tests timeout waiting for the application:
- Ensure Docker Compose is running
- Check all services are healthy: `docker-compose ps`
- Increase the `webServer.timeout` in `playwright.config.ts`

### Flaky Tests

If tests are flaky:
- Check if the application is fully loaded before assertions
- Use `waitForLoadState('networkidle')` for dynamic content
- Increase timeouts for specific assertions if needed

### Browser Not Installed

If you see "browser not installed" errors:

```bash
npx playwright install chromium
```

## Test Coverage

The test suite covers:

✅ All public pages (admin, monitoring, health)
✅ All API endpoints
✅ Page reloading and navigation
✅ Responsive design (mobile, tablet, desktop)
✅ Error handling and edge cases
✅ SignalR real-time connections
✅ Browser back/forward navigation
✅ Network failure recovery
✅ Performance benchmarks
✅ Security validation

## Maintenance

- Review test failures promptly
- Update tests when application features change
- Keep Playwright version up to date
- Monitor test execution time and optimize slow tests
- Remove obsolete tests when features are deprecated
