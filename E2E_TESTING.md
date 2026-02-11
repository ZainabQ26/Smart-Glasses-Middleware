# End-to-End Testing with Playwright

This repository includes comprehensive end-to-end tests using **Playwright 1.58.2** to ensure all features of the Smart Glasses Middleware are working correctly.

## Quick Start

### Prerequisites

- Node.js v18 or higher
- Docker and Docker Compose (for running the application)

### Installation

```bash
# Install dependencies
npm install

# Install Playwright browsers (install all or just one)
npx playwright install          # Install all browsers
npx playwright install chromium # Install only Chromium (recommended for quick start)
```

### Running Tests

#### Run All Tests

```bash
npm test
```

This will:
1. Start the application using Docker Compose
2. Wait for all services to be healthy
3. Run all E2E tests across Chromium, Firefox, and WebKit

#### Run Tests Without Starting the App

If the application is already running:

```bash
npx playwright test
```

#### Run Specific Test Suites

```bash
# Run only health tests
npx playwright test e2e-tests/01-health.spec.ts

# Run admin page tests
npx playwright test e2e-tests/02-admin-page.spec.ts

# Run monitoring page tests
npx playwright test e2e-tests/03-monitoring-page.spec.ts

# Run only on Chromium (faster for local dev)
npx playwright test --project=chromium
```

#### Run Tests in Headed Mode

To see the browser while tests run:

```bash
npm run test:headed
```

#### Run Tests in UI Mode

For interactive debugging:

```bash
npm run test:ui
```

#### Run Tests in Debug Mode

```bash
npm run test:debug
```

### View Test Reports

After running tests, view the HTML report:

```bash
npm run test:report
```

## Test Structure

The E2E test suite is organized in the `e2e-tests/` directory:

- **00-setup-validation.spec.ts** - Validates Playwright setup
- **01-health.spec.ts** - Health endpoint and basic API tests
- **02-admin-page.spec.ts** - Admin page UI and functionality
- **03-monitoring-page.spec.ts** - Monitoring page and real-time features
- **04-api-endpoints.spec.ts** - API endpoint validation
- **05-navigation.spec.ts** - Page navigation and reload tests
- **06-admin-features.spec.ts** - Detailed admin feature tests
- **07-monitoring-features.spec.ts** - Detailed monitoring and SignalR tests
- **08-error-handling.spec.ts** - Error handling, performance, and security
- **09-integration.spec.ts** - Full user journey integration tests

## Test Coverage

The test suite comprehensively covers:

✅ **Admin Page Features**
- File management (upload, view, delete)
- Prompt management (create, edit, delete)
- Group management and organization
- Pagination and search functionality
- Responsive design across viewports

✅ **Monitoring Page Features**
- Real-time request monitoring
- SignalR WebSocket connections
- Request statistics and metrics
- Connection status indicators
- Historical request data

✅ **Navigation & Page Reloading**
- Page navigation between admin and monitoring
- Browser back/forward navigation
- Page reload functionality
- Hard refresh with cache clearing
- Multiple consecutive reloads

✅ **API Endpoints**
- Health checks
- History endpoints
- Admin API endpoints
- Monitoring API endpoints
- Error handling for invalid requests

✅ **Error Handling & Edge Cases**
- Network failures and recovery
- Slow API responses
- 404 and 500 error handling
- Invalid input validation
- CORS error handling

✅ **Performance & Security**
- Page load time benchmarks
- Memory leak prevention
- Input sanitization
- XSS protection
- Secure headers validation

✅ **Responsive Design**
- Mobile viewports (320x568, 375x667)
- Tablet viewports (768x1024)
- Desktop viewports (1366x768, 1920x1080)
- Ultra-wide displays (3840x2160)

## Configuration

Test configuration is in `playwright.config.ts`:

```typescript
{
  testDir: './e2e-tests',
  fullyParallel: false,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  use: {
    baseURL: 'http://localhost:5000',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  }
}
```

## Test Artifacts

All test artifacts are excluded from Git via `.gitignore`:

- `node_modules/` - NPM dependencies
- `playwright-report/` - HTML test reports
- `test-results/` - Screenshots, videos, and traces
- `playwright/.cache/` - Browser binaries

## Continuous Integration

The tests are designed for CI/CD environments:

- Automatic Docker Compose startup
- Health check validation before tests
- Retry on failure (CI only)
- Sequential execution on CI
- Fail-fast on `test.only` usage

### Example CI Configuration

```yaml
- name: Install dependencies
  run: npm install

- name: Install Playwright browsers
  run: npx playwright install --with-deps

- name: Run E2E tests
  run: npm test
```

## Troubleshooting

### Tests Timeout Waiting for Application

If tests timeout waiting for the application to start:

1. Check Docker Compose is running: `docker-compose ps`
2. Check service health: `docker-compose ps`
3. View logs: `docker-compose logs`
4. Increase timeout in `playwright.config.ts` if needed

### Flaky Tests

If tests are flaky:

1. Ensure application is fully loaded before assertions
2. Use `waitForLoadState('networkidle')` for dynamic content
3. Increase specific test timeouts if needed
4. Check for race conditions in the application

### Browser Not Installed

If you see browser installation errors:

```bash
npx playwright install chromium
npx playwright install firefox
npx playwright install webkit
```

## Best Practices

When writing new E2E tests:

1. **Use descriptive test names** that explain what is being tested
2. **Group related tests** using `test.describe()`
3. **Wait for elements** before interacting with them
4. **Clean up after tests** if they create persistent data
5. **Make tests idempotent** - they should work in any order
6. **Use page object patterns** for complex flows
7. **Avoid hard-coded timeouts** - use Playwright's auto-waiting
8. **Test user journeys** not just individual features

## Running Tests Locally vs CI

**Local Development:**
- Reuses existing server if running
- No retries by default
- Parallel execution
- Faster feedback loop

**CI Environment:**
- Starts fresh server
- 2 retries on failure
- Sequential execution
- Comprehensive reporting

## Contributing

When adding new features to the application:

1. Add corresponding E2E tests
2. Ensure tests pass locally
3. Update test documentation if needed
4. Include test coverage in PR description

## Support

For test-related issues:

- Check the [E2E Tests README](e2e-tests/README.md)
- Review test output and screenshots
- Examine trace files for failed tests
- Check Playwright documentation: https://playwright.dev/

## Version

- **Playwright Version**: 1.58.2
- **Supported Browsers**: Chromium, Firefox, WebKit
- **Node.js**: v18+ required
