# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

This repository contains two distinct projects:

1. **IntelliTrader** - A .NET Core cryptocurrency trading bot for Binance exchange with virtual/live trading, DCA support, signal-based buying, and web dashboard
2. **WhatsappAgent** - A Python autonomous WhatsApp agent using OpenAI GPT with RAG for automated responses

## IntelliTrader (.NET Core)

### Build & Run Commands

```bash
# Build
dotnet build IntelliTrader.sln

# Run (Windows)
Start-IntelliTrader.bat
# Or directly:
dotnet bin/Debug/netcoreapp2.1/IntelliTrader.dll

# Encrypt API keys for live trading
dotnet bin/Debug/netcoreapp2.1/IntelliTrader.dll --encrypt --path keys.bin --publickey YOUR_API_KEY --privatekey YOUR_API_SECRET
```

### Architecture

**Solution Structure** - Multi-project .NET Core 2.1 solution with Autofac DI:

- `IntelliTrader/` - Main executable, configuration files, entry point (`Program.cs`)
- `IntelliTrader.Core/` - Core services, interfaces, models, timed tasks (`Application.cs` manages DI container)
- `IntelliTrader.Trading/` - Buy/sell/swap order execution
- `IntelliTrader.Signals.Base/TradingView/` - Signal acquisition from TradingView
- `IntelliTrader.Rules/` - Rule engine for trading decisions
- `IntelliTrader.Exchange.Base/Binance/` - Exchange API abstraction
- `IntelliTrader.Web/` - ASP.NET Core dashboard (default port 7000)
- `IntelliTrader.Backtesting/` - Historical snapshot replay

**Key Services** (singletons via Autofac):
- `ICoreService` - Orchestrator, manages all timed tasks
- `ITradingService` - Buy/sell/swap operations
- `ISignalsService` - Signal aggregation and ratings
- `IRulesService` - Rule condition evaluation
- `IExchangeService` - Binance API wrapper

**Data Flow**:
1. `TradingViewSignalPollingTimedTask` fetches signals every 7s
2. `BinanceTickersMonitorTimedTask` updates prices every 1s
3. `SignalRulesTimedTask` evaluates buy conditions every 3s
4. `TradingRulesTimedTask` evaluates sell/DCA conditions every 3s
5. `TradingTimedTask` executes pending orders every 1s

### Configuration

All JSON configs in `IntelliTrader/config/` with hot-reload support:
- `core.json` - Health checks, debug mode, instance name
- `trading.json` - Market, exchange, buy/sell/DCA parameters, virtual trading toggle
- `signals.json` - TradingView signal definitions
- `rules.json` - Signal rules (buy triggers) and trading rules (sell/DCA triggers)
- `web.json` - Web interface port and settings
- `notification.json` - Telegram alerts

### Key Patterns

- **Timed Tasks**: `HighResolutionTimedTask` for concurrent polling operations
- **Rule Engine**: Conditions checked via `IRulesService.CheckConditions()` with signal-specific and pair-specific conditions
- **Plugin Architecture**: Modules auto-discovered from `IntelliTrader.*.dll` assemblies via `AppModule : Module`

---

## WhatsappAgent (Python)

### Commands

```bash
# Install
pip install -e ".[dev]"

# Run
whatsapp-agent

# Tests
pytest
pytest tests/test_models.py::test_function_name -v
pytest --cov=src --cov-report=term-missing

# Linting & Type Checking
ruff check src tests
black --check src tests
mypy src

# Format
black src tests
ruff check --fix src tests
```

### Architecture

Event-driven system in `WhatsappAgent/src/`:

- `browser_controller.py` - Playwright/Chromium automation for WhatsApp Web
- `message_processor.py` - Message parsing, context management (max 50 messages/chat)
- `response_generator.py` - OpenAI GPT response generation
- `rag_system.py` - ChromaDB vector store with OpenAI embeddings
- `orchestrator.py` - Sub-agent coordination (web search, document search, reasoning)
- `models.py` - Pydantic data models with validation

**Data Flow**:
1. Browser Controller detects WhatsApp messages
2. Message Processor extracts fields, checks exclusions
3. RAG System retrieves relevant knowledge (top 5, min score 0.7)
4. Sub-Agent Orchestrator delegates if RAG insufficient
5. Response Generator creates response with GPT + context
6. Browser Controller sends with human-like typing delays

### Configuration

YAML config in `WhatsappAgent/config/config.yaml` with hot-reload:
- `excluded_contacts` - Senders to ignore
- `active_hours` - Operating window (start_hour, end_hour)
- `typing_speed` - Per-character delay range (min_ms, max_ms)
- `response_delay` - Pre-response delay range (min_s, max_s)

### Testing

pytest with Hypothesis property-based testing:
- `default` profile: 100 examples, 5s deadline
- `ci` profile: 200 examples, 10s deadline
- `debug` profile: 10 examples, no deadline
