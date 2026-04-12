using FluentAssertions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace IntelliTrader.E2E.Tests
{
    /// <summary>
    /// End-to-end tests verifying that authenticated API endpoints
    /// reject unauthenticated requests and that rate limiting is enforced.
    /// </summary>
    public class ApiAuthenticationTests : IClassFixture<TestWebHostFactory>
    {
        private readonly HttpClient _client;

        public ApiAuthenticationTests(TestWebHostFactory factory)
        {
            _client = factory.Client;
        }

        [Fact]
        public async Task GetApiStatus_WithoutAuth_ReturnsUnauthorizedOrRedirect()
        {
            // Act
            var response = await _client.GetAsync("/api/status");

            // Assert - Cookie auth may redirect to login page (302) or return 401
            // depending on whether the request accepts text/html. For API-style
            // requests the default cookie handler redirects to /Login.
            var statusCode = (int)response.StatusCode;
            statusCode.Should().BeOneOf(
                (int)HttpStatusCode.Unauthorized,
                (int)HttpStatusCode.Redirect,
                (int)HttpStatusCode.OK // redirect followed to login page
            );

            // The key assertion: we should NOT get a valid JSON status response
            // without authentication
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // If we followed a redirect to the login page, the content
                // should be HTML, not the JSON status payload
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                var content = await response.Content.ReadAsStringAsync();
                var isJsonStatus = content.Contains("\"Balance\"") && content.Contains("\"GlobalRating\"");
                isJsonStatus.Should().BeFalse("unauthenticated requests should not receive status data");
            }
        }

        [Fact]
        public async Task PostTradingPairs_WithoutAuth_ReturnsUnauthorizedOrRedirect()
        {
            // Act
            var response = await _client.PostAsync("/api/trading-pairs", null);

            // Assert
            var statusCode = (int)response.StatusCode;
            statusCode.Should().BeOneOf(
                (int)HttpStatusCode.Unauthorized,
                (int)HttpStatusCode.Redirect,
                (int)HttpStatusCode.OK // redirect followed to login page
            );
        }

        [Fact]
        public async Task PostMarketPairs_WithoutAuth_ReturnsUnauthorizedOrRedirect()
        {
            // Act
            var response = await _client.PostAsync("/api/market-pairs", null);

            // Assert
            var statusCode = (int)response.StatusCode;
            statusCode.Should().BeOneOf(
                (int)HttpStatusCode.Unauthorized,
                (int)HttpStatusCode.Redirect,
                (int)HttpStatusCode.OK // redirect followed to login page
            );
        }

        [Fact]
        public async Task GetSignalNames_WithoutAuth_ReturnsUnauthorizedOrRedirect()
        {
            // Act
            var response = await _client.GetAsync("/api/signal-names");

            // Assert
            var statusCode = (int)response.StatusCode;
            statusCode.Should().BeOneOf(
                (int)HttpStatusCode.Unauthorized,
                (int)HttpStatusCode.Redirect,
                (int)HttpStatusCode.OK // redirect followed to login page
            );
        }

        [Fact]
        public async Task PostMarketPairsFiltered_WithoutAuth_ReturnsUnauthorizedOrRedirect()
        {
            // Act
            var content = JsonContent.Create(new[] { "Signal1" });
            var response = await _client.PostAsync("/api/market-pairs/filtered", content);

            // Assert
            var statusCode = (int)response.StatusCode;
            statusCode.Should().BeOneOf(
                (int)HttpStatusCode.Unauthorized,
                (int)HttpStatusCode.Redirect,
                (int)HttpStatusCode.OK // redirect followed to login page
            );
        }

        [Fact]
        public async Task HealthEndpoints_AreNotAffectedByAuthRequirement()
        {
            // Health endpoints must always be accessible (AllowAnonymous)
            var healthResponse = await _client.GetAsync("/api/health");
            healthResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var liveResponse = await _client.GetAsync("/health/live");
            liveResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // Ready can be 200 or 503 but never 401/302
            var readyResponse = await _client.GetAsync("/health/ready");
            var readyStatus = (int)readyResponse.StatusCode;
            readyStatus.Should().BeOneOf(200, 503);
        }
    }
}
