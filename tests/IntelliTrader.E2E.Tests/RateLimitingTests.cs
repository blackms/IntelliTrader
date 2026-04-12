using FluentAssertions;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace IntelliTrader.E2E.Tests
{
    /// <summary>
    /// End-to-end tests verifying that rate limiting middleware is active.
    /// Health endpoints are exempt from rate limiting (DisableRateLimiting)
    /// while authenticated API endpoints are subject to fixed-window limits.
    /// </summary>
    public class RateLimitingTests : IDisposable
    {
        private readonly TestWebHostFactory _factory;
        private readonly HttpClient _client;

        public RateLimitingTests()
        {
            // Each test gets its own factory to avoid rate limit state leaking
            _factory = new TestWebHostFactory();
            _client = _factory.Client;
        }

        [Fact]
        public async Task HealthEndpoints_AreExemptFromRateLimiting()
        {
            // Health endpoints have DisableRateLimiting() so they should
            // always return 200 regardless of how many requests are made.
            for (int i = 0; i < 100; i++)
            {
                var response = await _client.GetAsync("/api/health");
                response.StatusCode.Should().Be(HttpStatusCode.OK,
                    because: $"health endpoint should never be rate limited (request {i + 1})");
            }
        }

        [Fact]
        public async Task LivenessProbe_IsExemptFromRateLimiting()
        {
            for (int i = 0; i < 100; i++)
            {
                var response = await _client.GetAsync("/health/live");
                response.StatusCode.Should().Be(HttpStatusCode.OK,
                    because: $"liveness probe should never be rate limited (request {i + 1})");
            }
        }

        [Fact]
        public async Task ReadinessProbe_IsExemptFromRateLimiting()
        {
            // Ready endpoint returns 200 or 503 based on health, but never 429
            for (int i = 0; i < 100; i++)
            {
                var response = await _client.GetAsync("/health/ready");
                var statusCode = (int)response.StatusCode;
                statusCode.Should().BeOneOf(new[] { 200, 503 },
                    because: $"readiness probe should never be rate limited (request {i + 1})");
            }
        }

        public void Dispose()
        {
            _factory?.Dispose();
        }
    }
}
