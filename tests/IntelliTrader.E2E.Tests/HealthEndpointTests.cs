using FluentAssertions;
using IntelliTrader.Core;
using Moq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace IntelliTrader.E2E.Tests
{
    /// <summary>
    /// End-to-end tests for the anonymous health check endpoints.
    /// These endpoints are used by Docker HEALTHCHECK and Kubernetes probes
    /// and must be accessible without authentication.
    /// </summary>
    [Collection("E2E")]
    public class HealthEndpointTests : IClassFixture<TestWebHostFactory>
    {
        private readonly HttpClient _client;
        private readonly TestWebHostFactory _factory;

        public HealthEndpointTests(TestWebHostFactory factory)
        {
            _factory = factory;
            _client = factory.Client;
        }

        [Fact]
        public async Task GetApiHealth_ReturnsOkWithStatus()
        {
            // Act
            var response = await _client.GetAsync("/api/health");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            json.RootElement.GetProperty("status").GetString().Should().Be("ok");
        }

        [Fact]
        public async Task GetHealthLive_ReturnsOkWithAliveStatus()
        {
            // Act
            var response = await _client.GetAsync("/health/live");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            json.RootElement.GetProperty("status").GetString().Should().Be("alive");
            json.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
        }

        [Fact]
        public async Task GetHealthReady_WhenBotIsReady_ReturnsOk()
        {
            // Arrange - default mocks have Running=true, IsTradingSuspended=false, no failing checks
            _factory.MockCoreService.Setup(s => s.Running).Returns(true);
            _factory.MockTradingService.Setup(s => s.IsTradingSuspended).Returns(false);
            _factory.MockHealthCheckService.Setup(s => s.GetHealthChecks())
                .Returns(new List<IHealthCheck>());

            // Act
            var response = await _client.GetAsync("/health/ready");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            json.RootElement.GetProperty("status").GetString().Should().Be("ready");
            json.RootElement.GetProperty("coreRunning").GetBoolean().Should().BeTrue();
            json.RootElement.GetProperty("tradingSuspended").GetBoolean().Should().BeFalse();
            json.RootElement.GetProperty("failingCheckCount").GetInt32().Should().Be(0);
            json.RootElement.TryGetProperty("checks", out _).Should().BeTrue();
            json.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
        }

        [Fact]
        public async Task GetHealthReady_WhenCoreNotRunning_Returns503()
        {
            // Arrange
            _factory.MockCoreService.Setup(s => s.Running).Returns(false);
            _factory.MockTradingService.Setup(s => s.IsTradingSuspended).Returns(false);
            _factory.MockHealthCheckService.Setup(s => s.GetHealthChecks())
                .Returns(new List<IHealthCheck>());

            // Act
            var response = await _client.GetAsync("/health/ready");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            json.RootElement.GetProperty("status").GetString().Should().Be("not_ready");
            json.RootElement.GetProperty("coreRunning").GetBoolean().Should().BeFalse();
        }

        [Fact]
        public async Task GetHealthReady_WhenTradingSuspended_Returns503()
        {
            // Arrange
            _factory.MockCoreService.Setup(s => s.Running).Returns(true);
            _factory.MockTradingService.Setup(s => s.IsTradingSuspended).Returns(true);
            _factory.MockHealthCheckService.Setup(s => s.GetHealthChecks())
                .Returns(new List<IHealthCheck>());

            // Act
            var response = await _client.GetAsync("/health/ready");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            json.RootElement.GetProperty("status").GetString().Should().Be("not_ready");
            json.RootElement.GetProperty("tradingSuspended").GetBoolean().Should().BeTrue();
        }

        [Fact]
        public async Task GetHealthReady_WhenHealthCheckFailing_Returns503()
        {
            // Arrange
            var failingCheck = new Mock<IHealthCheck>();
            failingCheck.Setup(c => c.Name).Returns("TestCheck");
            failingCheck.Setup(c => c.Failed).Returns(true);
            failingCheck.Setup(c => c.Message).Returns("Something is wrong");
            failingCheck.Setup(c => c.LastUpdated).Returns(System.DateTimeOffset.UtcNow);

            _factory.MockCoreService.Setup(s => s.Running).Returns(true);
            _factory.MockTradingService.Setup(s => s.IsTradingSuspended).Returns(false);
            _factory.MockHealthCheckService.Setup(s => s.GetHealthChecks())
                .Returns(new List<IHealthCheck> { failingCheck.Object });

            // Act
            var response = await _client.GetAsync("/health/ready");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            var content = await response.Content.ReadAsStringAsync();
            var json = JsonDocument.Parse(content);
            json.RootElement.GetProperty("status").GetString().Should().Be("not_ready");
            json.RootElement.GetProperty("failingCheckCount").GetInt32().Should().Be(1);
        }

        [Fact]
        public async Task GetApiHealth_ReturnsSecurityHeaders()
        {
            // Act
            var response = await _client.GetAsync("/api/health");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
            response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        }

        [Fact]
        public async Task GetHealthLive_ReturnsSecurityHeaders()
        {
            // Act
            var response = await _client.GetAsync("/health/live");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            // X-Frame-Options is set on all responses by the security middleware
            response.Headers.Contains("X-Content-Type-Options").Should().BeTrue();
        }
    }
}
