using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SmartGlassesMiddleware.Services
{
    public class HttpLoggingHandler : DelegatingHandler
    {
        private readonly ILogger _logger;

        public HttpLoggingHandler(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var reqId = Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                _logger.LogInformation("HTTP OUT -> {Method} {Uri} [reqId={ReqId}]", request.Method, request.RequestUri, reqId);
                if (request.Content != null)
                {
                    try
                    {
                        var reqBody = await request.Content.ReadAsStringAsync(cancellationToken);
                        var truncated = reqBody?.Length > 2000 ? reqBody.Substring(0, 2000) + "...(truncated)" : reqBody;
                        _logger.LogDebug("Request Body [reqId={ReqId}]: {Body}", reqId, truncated);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed reading request content for logging [reqId={ReqId}]", reqId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed logging outgoing request [reqId={ReqId}]", reqId);
            }

            var sw = Stopwatch.StartNew();
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();

            try
            {
                _logger.LogInformation("HTTP IN  <- {StatusCode} {Method} {Uri} [reqId={ReqId}] {Ms}ms", (int)response.StatusCode, request.Method, request.RequestUri, reqId, sw.ElapsedMilliseconds);
                if (response.Content != null)
                {
                    try
                    {
                        var respBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        var truncated = respBody?.Length > 2000 ? respBody.Substring(0, 2000) + "...(truncated)" : respBody;
                        _logger.LogDebug("Response Body [reqId={ReqId}]: {Body}", reqId, truncated);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed reading response content for logging [reqId={ReqId}]", reqId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed logging incoming response [reqId={ReqId}]", reqId);
            }

            return response;
        }
    }
}
