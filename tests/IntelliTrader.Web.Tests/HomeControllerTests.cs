using FluentAssertions;
using Moq;
using Xunit;
using IntelliTrader.Core;
using IntelliTrader.Web.Controllers;
using IntelliTrader.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace IntelliTrader.Web.Tests;

public class HomeControllerTests
{
    private readonly Mock<ICoreService> _coreServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<ISignalsService> _signalsServiceMock;
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<IPasswordService> _passwordServiceMock;
    private readonly Mock<IConfigProvider> _configProviderMock;
    private readonly Mock<ITradingAccount> _accountMock;
    private readonly List<IConfigurableService> _configurableServices;
    private readonly HomeController _sut;

    public HomeControllerTests()
    {
        _coreServiceMock = new Mock<ICoreService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _signalsServiceMock = new Mock<ISignalsService>();
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _passwordServiceMock = new Mock<IPasswordService>();
        _configProviderMock = new Mock<IConfigProvider>();
        _accountMock = new Mock<ITradingAccount>();
        _configurableServices = new List<IConfigurableService>();

        // Setup core config
        var coreConfigMock = new Mock<ICoreConfig>();
        coreConfigMock.Setup(x => x.InstanceName).Returns("TestInstance");
        coreConfigMock.Setup(x => x.PasswordProtected).Returns(false);
        coreConfigMock.Setup(x => x.TimezoneOffset).Returns(0);
        coreConfigMock.Setup(x => x.HealthCheckEnabled).Returns(true);
        _coreServiceMock.Setup(x => x.Config).Returns(coreConfigMock.Object);
        _coreServiceMock.Setup(x => x.Version).Returns("1.0.0");

        // Setup trading config
        var tradingConfigMock = new Mock<ITradingConfig>();
        tradingConfigMock.Setup(x => x.VirtualTrading).Returns(true);
        tradingConfigMock.Setup(x => x.VirtualAccountInitialBalance).Returns(10000m);
        tradingConfigMock.Setup(x => x.Market).Returns("USDT");
        tradingConfigMock.Setup(x => x.Exchange).Returns("Binance");
        tradingConfigMock.Setup(x => x.BuyEnabled).Returns(true);
        tradingConfigMock.Setup(x => x.BuyDCAEnabled).Returns(true);
        tradingConfigMock.Setup(x => x.SellEnabled).Returns(true);
        _tradingServiceMock.Setup(x => x.Config).Returns(tradingConfigMock.Object);
        _tradingServiceMock.Setup(x => x.Account).Returns(_accountMock.Object);
        _tradingServiceMock.Setup(x => x.IsTradingSuspended).Returns(false);

        // Setup account
        _accountMock.Setup(x => x.GetBalance()).Returns(10000m);
        _accountMock.Setup(x => x.GetTradingPairs()).Returns(new List<ITradingPair>());

        // Setup signals service
        _signalsServiceMock.Setup(x => x.GetGlobalRating()).Returns(0.5);
        _signalsServiceMock.Setup(x => x.GetSignalNames()).Returns(new List<string> { "Signal1", "Signal2" });
        _signalsServiceMock.Setup(x => x.GetTrailingSignals()).Returns(new List<string>());

        // Setup health check service
        _healthCheckServiceMock.Setup(x => x.GetHealthChecks()).Returns(new List<IHealthCheck>());

        // Setup logging service
        _loggingServiceMock.Setup(x => x.GetLogEntries()).Returns(new string[0]);

        // Setup password service with default behavior
        _passwordServiceMock.Setup(x => x.VerifyPassword(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        _passwordServiceMock.Setup(x => x.IsLegacyHash(It.IsAny<string>())).Returns(false);
        _passwordServiceMock.Setup(x => x.IsBCryptHash(It.IsAny<string>())).Returns(true);
        _passwordServiceMock.Setup(x => x.HashPassword(It.IsAny<string>())).Returns("$2a$12$hashedpassword");

        // Setup config provider
        _configProviderMock.Setup(x => x.GetSectionJson(It.IsAny<string>())).Returns("{}");

        _sut = new HomeController(
            _coreServiceMock.Object,
            _tradingServiceMock.Object,
            _signalsServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _passwordServiceMock.Object,
            _configProviderMock.Object,
            _configurableServices,
            new UsersConfig());

        // Setup HttpContext for controller
        var httpContext = new DefaultHttpContext();
        _sut.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullCoreService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new HomeController(
            null!,
            _tradingServiceMock.Object,
            _signalsServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _passwordServiceMock.Object,
            _configProviderMock.Object,
            _configurableServices,
            new UsersConfig());

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("coreService");
    }

    [Fact]
    public void Constructor_WithNullTradingService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new HomeController(
            _coreServiceMock.Object,
            null!,
            _signalsServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            _passwordServiceMock.Object,
            _configProviderMock.Object,
            _configurableServices,
            new UsersConfig());

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("tradingService");
    }

    [Fact]
    public void Constructor_WithNullPasswordService_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new HomeController(
            _coreServiceMock.Object,
            _tradingServiceMock.Object,
            _signalsServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            null!,
            _configProviderMock.Object,
            _configurableServices,
            new UsersConfig());

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("passwordService");
    }

    #endregion

    #region View Endpoint Tests

    [Fact]
    public void Index_ReturnsViewResult()
    {
        // Act
        var result = _sut.Index();

        // Assert
        result.Should().BeOfType<ViewResult>();
    }

    [Fact]
    public void Dashboard_ReturnsViewWithModel()
    {
        // Act
        var result = _sut.Dashboard() as ViewResult;

        // Assert
        result.Should().NotBeNull();
        result!.Model.Should().BeOfType<DashboardViewModel>();
        var model = result.Model as DashboardViewModel;
        model!.InstanceName.Should().Be("TestInstance");
        model.Version.Should().Be("1.0.0");
    }

    [Fact]
    public void Market_ReturnsViewWithModel()
    {
        // Act
        var result = _sut.Market() as ViewResult;

        // Assert
        result.Should().NotBeNull();
        result!.Model.Should().BeOfType<MarketViewModel>();
        var model = result.Model as MarketViewModel;
        model!.InstanceName.Should().Be("TestInstance");
    }

    [Fact]
    public void Settings_ReturnsViewWithModel()
    {
        // Act
        var result = _sut.Settings() as ViewResult;

        // Assert
        result.Should().NotBeNull();
        result!.Model.Should().BeOfType<SettingsViewModel>();
        var model = result.Model as SettingsViewModel;
        model!.BuyEnabled.Should().BeTrue();
        model.SellEnabled.Should().BeTrue();
        model.TradingSuspended.Should().BeFalse();
    }

    [Fact]
    public void Log_ReturnsViewWithLogEntries()
    {
        // Arrange
        _loggingServiceMock.Setup(x => x.GetLogEntries())
            .Returns(new[] { "Log entry 1", "Log entry 2" });

        // Act
        var result = _sut.Log() as ViewResult;

        // Assert
        result.Should().NotBeNull();
        result!.Model.Should().BeOfType<LogViewModel>();
        var model = result.Model as LogViewModel;
        model!.LogEntries.Should().HaveCount(2);
    }

    [Fact]
    public void Help_ReturnsViewWithModel()
    {
        // Act
        var result = _sut.Help() as ViewResult;

        // Assert
        result.Should().NotBeNull();
        result!.Model.Should().BeOfType<HelpViewModel>();
    }

    #endregion

    #region Trading Action Tests

    [Fact]
    public async Task Buy_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new BuyInputModel { Pair = "BTCUSDT", Amount = 0.01m };

        // Act
        var result = await _sut.Buy(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.BuyAsync(It.Is<BuyOptions>(o =>
            o.Pair == "BTCUSDT" &&
            o.Amount == 0.01m &&
            o.ManualOrder == true &&
            o.IgnoreExisting == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Buy_WithInvalidModel_ReturnsBadRequest()
    {
        // Arrange
        var model = new BuyInputModel { Pair = "", Amount = 0 };
        _sut.ModelState.AddModelError("Pair", "Trading pair is required");

        // Act
        var result = await _sut.Buy(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Sell_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new SellInputModel { Pair = "BTCUSDT", Amount = 0.01m };

        // Act
        var result = await _sut.Sell(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.SellAsync(It.Is<SellOptions>(o =>
            o.Pair == "BTCUSDT" &&
            o.Amount == 0.01m &&
            o.ManualOrder == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Sell_WithInvalidModel_ReturnsBadRequest()
    {
        // Arrange
        var model = new SellInputModel { Pair = "invalid", Amount = -1 };
        _sut.ModelState.AddModelError("Amount", "Invalid amount");

        // Act
        var result = await _sut.Sell(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Swap_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new SwapInputModel { Pair = "BTCUSDT", Swap = "ETHUSDT" };

        // Act
        var result = await _sut.Swap(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.SwapAsync(It.Is<SwapOptions>(o =>
            o.OldPair == "BTCUSDT" &&
            o.NewPair == "ETHUSDT" &&
            o.ManualOrder == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Swap_WithInvalidModel_ReturnsBadRequest()
    {
        // Arrange
        var model = new SwapInputModel { Pair = "", Swap = "" };
        _sut.ModelState.AddModelError("Pair", "Source trading pair is required");

        // Act
        var result = await _sut.Swap(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task BuyDefault_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new BuyDefaultInputModel { Pair = "BTCUSDT" };
        var pairConfigMock = new Mock<IPairConfig>();
        pairConfigMock.Setup(x => x.BuyMaxCost).Returns(100m);
        _tradingServiceMock.Setup(x => x.GetPairConfig("BTCUSDT")).Returns(pairConfigMock.Object);

        // Act
        var result = await _sut.BuyDefault(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.BuyAsync(It.Is<BuyOptions>(o =>
            o.Pair == "BTCUSDT" &&
            o.MaxCost == 100m &&
            o.ManualOrder == true), It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region Config Action Tests

    [Fact]
    public void SaveConfig_WithValidModel_ReturnsOk()
    {
        // Arrange
        var model = new ConfigUpdateModel
        {
            Name = "trading",
            Definition = "{\"enabled\": true}"
        };

        // Act
        var result = _sut.SaveConfig(model);

        // Assert
        result.Should().BeOfType<OkResult>();
    }

    [Fact]
    public void SaveConfig_WithInvalidJson_ReturnsBadRequest()
    {
        // Arrange
        var model = new ConfigUpdateModel
        {
            Name = "trading",
            Definition = "not valid json"
        };

        // Act
        var result = _sut.SaveConfig(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
        var badRequest = result as BadRequestObjectResult;
        badRequest!.Value.Should().Be("Invalid JSON format in configuration definition");
    }

    [Fact]
    public void SaveConfig_WithInvalidModel_ReturnsBadRequest()
    {
        // Arrange
        var model = new ConfigUpdateModel { Name = "", Definition = "" };
        _sut.ModelState.AddModelError("Name", "Configuration name is required");

        // Act
        var result = _sut.SaveConfig(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region Service Action Tests

    [Fact]
    public void RefreshAccount_CallsAccountRefresh()
    {
        // Act
        var result = _sut.RefreshAccount();

        // Assert
        result.Should().BeOfType<OkResult>();
        _accountMock.Verify(x => x.Refresh(), Times.Once);
    }

    [Fact]
    public void RestartServices_CallsCoreServiceRestart()
    {
        // Act
        var result = _sut.RestartServices();

        // Assert
        result.Should().BeOfType<OkResult>();
        _coreServiceMock.Verify(x => x.Restart(), Times.Once);
    }

    #endregion

    #region Settings POST Tests

    [Fact]
    public void Settings_Post_UpdatesConfigAndReturnsView()
    {
        // Arrange
        var coreConfigMock = new Mock<ICoreConfig>();
        coreConfigMock.SetupProperty(x => x.HealthCheckEnabled);
        _coreServiceMock.Setup(x => x.Config).Returns(coreConfigMock.Object);

        var tradingConfigMock = new Mock<ITradingConfig>();
        tradingConfigMock.SetupProperty(x => x.BuyEnabled);
        tradingConfigMock.SetupProperty(x => x.BuyDCAEnabled);
        tradingConfigMock.SetupProperty(x => x.SellEnabled);
        _tradingServiceMock.Setup(x => x.Config).Returns(tradingConfigMock.Object);

        var model = new SettingsViewModel
        {
            BuyEnabled = false,
            BuyDCAEnabled = false,
            SellEnabled = false,
            TradingSuspended = true,
            HealthCheckEnabled = false
        };

        // Act
        var result = _sut.Settings(model);

        // Assert
        result.Should().BeOfType<ViewResult>();
        _tradingServiceMock.Verify(x => x.SuspendTrading(false), Times.Once);
    }

    [Fact]
    public void Settings_Post_WithTradingNotSuspended_CallsResumeTrading()
    {
        // Arrange
        var coreConfigMock = new Mock<ICoreConfig>();
        coreConfigMock.SetupProperty(x => x.HealthCheckEnabled);
        _coreServiceMock.Setup(x => x.Config).Returns(coreConfigMock.Object);

        var tradingConfigMock = new Mock<ITradingConfig>();
        tradingConfigMock.SetupProperty(x => x.BuyEnabled);
        tradingConfigMock.SetupProperty(x => x.BuyDCAEnabled);
        tradingConfigMock.SetupProperty(x => x.SellEnabled);
        _tradingServiceMock.Setup(x => x.Config).Returns(tradingConfigMock.Object);

        var model = new SettingsViewModel
        {
            BuyEnabled = true,
            BuyDCAEnabled = true,
            SellEnabled = true,
            TradingSuspended = false,
            HealthCheckEnabled = true
        };

        // Act
        var result = _sut.Settings(model);

        // Assert
        result.Should().BeOfType<ViewResult>();
        _tradingServiceMock.Verify(x => x.ResumeTrading(false), Times.Once);
    }

    #endregion
}
