using FluentAssertions;
using Moq;
using IntelliTrader.Core;
using IntelliTrader.Web.Controllers;
using IntelliTrader.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;

namespace IntelliTrader.Web.Tests;

public class HomeControllerTests
{
    private readonly Mock<ICoreService> _coreServiceMock;
    private readonly Mock<ITradingService> _tradingServiceMock;
    private readonly Mock<ISignalsService> _signalsServiceMock;
    private readonly Mock<ILoggingService> _loggingServiceMock;
    private readonly Mock<IHealthCheckService> _healthCheckServiceMock;
    private readonly Mock<ITradingAccount> _accountMock;
    private readonly HomeController _sut;

    public HomeControllerTests()
    {
        _coreServiceMock = new Mock<ICoreService>();
        _tradingServiceMock = new Mock<ITradingService>();
        _signalsServiceMock = new Mock<ISignalsService>();
        _loggingServiceMock = new Mock<ILoggingService>();
        _healthCheckServiceMock = new Mock<IHealthCheckService>();
        _accountMock = new Mock<ITradingAccount>();

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
        _signalsServiceMock.Setup(x => x.GetGlobalRating()).Returns(0.5m);
        _signalsServiceMock.Setup(x => x.GetSignalNames()).Returns(new List<string> { "Signal1", "Signal2" });
        _signalsServiceMock.Setup(x => x.GetTrailingSignals()).Returns(new List<string>());

        // Setup health check service
        _healthCheckServiceMock.Setup(x => x.GetHealthChecks()).Returns(new List<IHealthCheck>());

        // Setup logging service
        _loggingServiceMock.Setup(x => x.GetLogEntries()).Returns(new string[0]);

        // Create controller with empty configurable services
        var configurableServices = new List<IConfigurableService>();

        _sut = new HomeController(
            _coreServiceMock.Object,
            _tradingServiceMock.Object,
            _signalsServiceMock.Object,
            _loggingServiceMock.Object,
            _healthCheckServiceMock.Object,
            configurableServices);

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
            new List<IConfigurableService>());

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
            new List<IConfigurableService>());

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("tradingService");
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

    #region API Endpoint Tests

    [Fact]
    public void Status_ReturnsJsonWithStatusData()
    {
        // Arrange
        _tradingServiceMock.Setup(x => x.GetTrailingBuys()).Returns(new List<string> { "BTCUSDT" });
        _tradingServiceMock.Setup(x => x.GetTrailingSells()).Returns(new List<string>());

        // Act
        var result = _sut.Status() as JsonResult;

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().NotBeNull();
    }

    [Fact]
    public void SignalNames_ReturnsJsonWithSignalNames()
    {
        // Act
        var result = _sut.SignalNames() as JsonResult;

        // Assert
        result.Should().NotBeNull();
        var signalNames = result!.Value as IEnumerable<string>;
        signalNames.Should().Contain("Signal1");
        signalNames.Should().Contain("Signal2");
    }

    [Fact]
    public void TradingPairs_ReturnsJsonWithTradingPairs()
    {
        // Arrange
        var tradingPairMock = new Mock<ITradingPair>();
        tradingPairMock.Setup(x => x.Pair).Returns("BTCUSDT");
        tradingPairMock.Setup(x => x.DCALevel).Returns(0);
        tradingPairMock.Setup(x => x.CurrentMargin).Returns(5.5m);
        tradingPairMock.Setup(x => x.CurrentPrice).Returns(50000m);
        tradingPairMock.Setup(x => x.AveragePricePaid).Returns(48000m);
        tradingPairMock.Setup(x => x.AverageCostPaid).Returns(480m);
        tradingPairMock.Setup(x => x.CurrentCost).Returns(500m);
        tradingPairMock.Setup(x => x.TotalAmount).Returns(0.01m);
        tradingPairMock.Setup(x => x.OrderDates).Returns(new List<DateTimeOffset> { DateTimeOffset.Now });
        tradingPairMock.Setup(x => x.OrderIds).Returns(new List<string> { "order1" });
        tradingPairMock.Setup(x => x.CurrentAge).Returns(24.5);
        tradingPairMock.Setup(x => x.Metadata).Returns(new OrderMetadata());

        _accountMock.Setup(x => x.GetTradingPairs())
            .Returns(new List<ITradingPair> { tradingPairMock.Object });

        var pairConfigMock = new Mock<IPairConfig>();
        pairConfigMock.Setup(x => x.SellMargin).Returns(5m);
        pairConfigMock.Setup(x => x.Rules).Returns(new List<string>());
        _tradingServiceMock.Setup(x => x.GetPairConfig("BTCUSDT")).Returns(pairConfigMock.Object);
        _tradingServiceMock.Setup(x => x.GetTrailingBuys()).Returns(new List<string>());
        _tradingServiceMock.Setup(x => x.GetTrailingSells()).Returns(new List<string>());

        // Act
        var result = _sut.TradingPairs() as JsonResult;

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().NotBeNull();
    }

    [Fact]
    public void MarketPairs_WithNoSignals_ReturnsNull()
    {
        // Arrange
        _signalsServiceMock.Setup(x => x.GetAllSignals()).Returns((IEnumerable<ISignal>?)null);

        // Act
        var result = _sut.MarketPairs(new List<string>()) as JsonResult;

        // Assert
        result.Should().NotBeNull();
        result!.Value.Should().BeNull();
    }

    #endregion

    #region Trading Action Tests

    [Fact]
    public void Buy_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new BuyInputModel { Pair = "BTCUSDT", Amount = 0.01m };

        // Act
        var result = _sut.Buy(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.Buy(It.Is<BuyOptions>(o =>
            o.Pair == "BTCUSDT" &&
            o.Amount == 0.01m &&
            o.ManualOrder == true &&
            o.IgnoreExisting == true)), Times.Once);
    }

    [Fact]
    public void Buy_WithInvalidModel_ReturnsBadRequest()
    {
        // Arrange
        var model = new BuyInputModel { Pair = "", Amount = 0 };
        _sut.ModelState.AddModelError("Pair", "Trading pair is required");

        // Act
        var result = _sut.Buy(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Sell_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new SellInputModel { Pair = "BTCUSDT", Amount = 0.01m };

        // Act
        var result = _sut.Sell(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.Sell(It.Is<SellOptions>(o =>
            o.Pair == "BTCUSDT" &&
            o.Amount == 0.01m &&
            o.ManualOrder == true)), Times.Once);
    }

    [Fact]
    public void Sell_WithInvalidModel_ReturnsBadRequest()
    {
        // Arrange
        var model = new SellInputModel { Pair = "invalid", Amount = -1 };
        _sut.ModelState.AddModelError("Amount", "Invalid amount");

        // Act
        var result = _sut.Sell(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void Swap_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new SwapInputModel { Pair = "BTCUSDT", Swap = "ETHUSDT" };

        // Act
        var result = _sut.Swap(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.Swap(It.Is<SwapOptions>(o =>
            o.OldPair == "BTCUSDT" &&
            o.NewPair == "ETHUSDT" &&
            o.ManualOrder == true)), Times.Once);
    }

    [Fact]
    public void Swap_WithInvalidModel_ReturnsBadRequest()
    {
        // Arrange
        var model = new SwapInputModel { Pair = "", Swap = "" };
        _sut.ModelState.AddModelError("Pair", "Source trading pair is required");

        // Act
        var result = _sut.Swap(model);

        // Assert
        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void BuyDefault_WithValidModel_ReturnsOkAndCallsTradingService()
    {
        // Arrange
        var model = new BuyDefaultInputModel { Pair = "BTCUSDT" };
        var pairConfigMock = new Mock<IPairConfig>();
        pairConfigMock.Setup(x => x.BuyMaxCost).Returns(100m);
        _tradingServiceMock.Setup(x => x.GetPairConfig("BTCUSDT")).Returns(pairConfigMock.Object);

        // Act
        var result = _sut.BuyDefault(model);

        // Assert
        result.Should().BeOfType<OkResult>();
        _tradingServiceMock.Verify(x => x.Buy(It.Is<BuyOptions>(o =>
            o.Pair == "BTCUSDT" &&
            o.MaxCost == 100m &&
            o.ManualOrder == true)), Times.Once);
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
        _tradingServiceMock.Verify(x => x.SuspendTrading(), Times.Once);
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
        _tradingServiceMock.Verify(x => x.ResumeTrading(), Times.Once);
    }

    #endregion
}
