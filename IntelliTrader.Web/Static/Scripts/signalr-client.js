/**
 * IntelliTrader SignalR Client
 * Handles real-time communication with the trading hub for live updates.
 */
var IntelliTraderSignalR = (function () {
    var connection = null;
    var isConnected = false;
    var reconnectAttempts = 0;
    var maxReconnectAttempts = 10;
    var reconnectDelay = 1000; // Start with 1 second
    var callbacks = {
        onStatusUpdate: [],
        onPriceUpdate: [],
        onTickerUpdate: [],
        onTradeExecuted: [],
        onPositionChanged: [],
        onHealthStatus: [],
        onBalanceUpdate: [],
        onTrailingStatus: [],
        onTradingPairsUpdate: [],
        onConnected: [],
        onDisconnected: [],
        onReconnecting: [],
        onError: []
    };

    /**
     * Initialize and connect to the SignalR hub.
     */
    function connect() {
        if (connection) {
            return Promise.resolve();
        }

        connection = new signalR.HubConnectionBuilder()
            .withUrl("/trading-hub")
            .withAutomaticReconnect({
                nextRetryDelayInMilliseconds: function (retryContext) {
                    // Exponential backoff with jitter
                    if (retryContext.previousRetryCount >= maxReconnectAttempts) {
                        return null; // Stop reconnecting
                    }
                    var delay = Math.min(1000 * Math.pow(2, retryContext.previousRetryCount), 30000);
                    return delay + Math.random() * 1000;
                }
            })
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        // Register event handlers
        registerHubEventHandlers();

        return connection.start()
            .then(function () {
                isConnected = true;
                reconnectAttempts = 0;
                console.log("SignalR: Connected to trading hub");
                triggerCallbacks("onConnected");
            })
            .catch(function (err) {
                console.error("SignalR: Connection failed", err);
                triggerCallbacks("onError", err);
                scheduleReconnect();
                return Promise.reject(err);
            });
    }

    /**
     * Disconnect from the SignalR hub.
     */
    function disconnect() {
        if (connection) {
            return connection.stop()
                .then(function () {
                    connection = null;
                    isConnected = false;
                    console.log("SignalR: Disconnected");
                    triggerCallbacks("onDisconnected");
                });
        }
        return Promise.resolve();
    }

    /**
     * Register all hub event handlers.
     */
    function registerHubEventHandlers() {
        // Status updates (balance, rating, trailing, health)
        connection.on("StatusUpdate", function (data) {
            triggerCallbacks("onStatusUpdate", data);
            updateStatusBar(data);
        });

        // Price updates for specific pairs
        connection.on("PriceUpdate", function (data) {
            triggerCallbacks("onPriceUpdate", data);
        });

        // Full ticker updates
        connection.on("TickerUpdate", function (data) {
            triggerCallbacks("onTickerUpdate", data);
        });

        // Trade execution notifications
        connection.on("TradeExecuted", function (data) {
            triggerCallbacks("onTradeExecuted", data);
            showTradeNotification(data);
        });

        // Position changes (add, update, remove)
        connection.on("PositionChanged", function (data) {
            triggerCallbacks("onPositionChanged", data);
        });

        // Health status updates
        connection.on("HealthStatus", function (data) {
            triggerCallbacks("onHealthStatus", data);
            updateHealthStatus(data);
        });

        // Balance updates
        connection.on("BalanceUpdate", function (data) {
            triggerCallbacks("onBalanceUpdate", data);
            updateBalance(data);
        });

        // Trailing status updates
        connection.on("TrailingStatus", function (data) {
            triggerCallbacks("onTrailingStatus", data);
            updateTrailingStatus(data);
        });

        // Trading pairs update
        connection.on("TradingPairsUpdate", function (data) {
            triggerCallbacks("onTradingPairsUpdate", data);
        });

        // Connection state handlers
        connection.onreconnecting(function (error) {
            isConnected = false;
            console.log("SignalR: Reconnecting...", error);
            setConnectionStatus("reconnecting");
            triggerCallbacks("onReconnecting", error);
        });

        connection.onreconnected(function (connectionId) {
            isConnected = true;
            reconnectAttempts = 0;
            console.log("SignalR: Reconnected", connectionId);
            setConnectionStatus("connected");
            triggerCallbacks("onConnected", connectionId);
        });

        connection.onclose(function (error) {
            isConnected = false;
            console.log("SignalR: Connection closed", error);
            setConnectionStatus("disconnected");
            triggerCallbacks("onDisconnected", error);
            scheduleReconnect();
        });
    }

    /**
     * Schedule a reconnection attempt.
     */
    function scheduleReconnect() {
        if (reconnectAttempts >= maxReconnectAttempts) {
            console.error("SignalR: Max reconnect attempts reached");
            return;
        }

        reconnectAttempts++;
        var delay = Math.min(reconnectDelay * Math.pow(2, reconnectAttempts - 1), 30000);

        console.log("SignalR: Reconnecting in " + delay + "ms (attempt " + reconnectAttempts + ")");

        setTimeout(function () {
            if (!isConnected && connection) {
                connection.start()
                    .then(function () {
                        isConnected = true;
                        reconnectAttempts = 0;
                        console.log("SignalR: Reconnected");
                        setConnectionStatus("connected");
                        triggerCallbacks("onConnected");
                    })
                    .catch(function (err) {
                        console.error("SignalR: Reconnection failed", err);
                        scheduleReconnect();
                    });
            }
        }, delay);
    }

    /**
     * Subscribe to updates for a specific trading pair.
     */
    function subscribeToPair(pair) {
        if (isConnected && connection) {
            return connection.invoke("SubscribeToPair", pair);
        }
        return Promise.reject(new Error("Not connected"));
    }

    /**
     * Unsubscribe from updates for a specific trading pair.
     */
    function unsubscribeFromPair(pair) {
        if (isConnected && connection) {
            return connection.invoke("UnsubscribeFromPair", pair);
        }
        return Promise.reject(new Error("Not connected"));
    }

    /**
     * Request a status update from the server.
     */
    function requestStatus() {
        if (isConnected && connection) {
            return connection.invoke("RequestStatus");
        }
        return Promise.reject(new Error("Not connected"));
    }

    /**
     * Request trading pairs data from the server.
     */
    function requestTradingPairs() {
        if (isConnected && connection) {
            return connection.invoke("RequestTradingPairs");
        }
        return Promise.reject(new Error("Not connected"));
    }

    /**
     * Request health status from the server.
     */
    function requestHealthStatus() {
        if (isConnected && connection) {
            return connection.invoke("RequestHealthStatus");
        }
        return Promise.reject(new Error("Not connected"));
    }

    /**
     * Register a callback for a specific event.
     */
    function on(event, callback) {
        if (callbacks[event]) {
            callbacks[event].push(callback);
        }
    }

    /**
     * Remove a callback for a specific event.
     */
    function off(event, callback) {
        if (callbacks[event]) {
            var index = callbacks[event].indexOf(callback);
            if (index > -1) {
                callbacks[event].splice(index, 1);
            }
        }
    }

    /**
     * Trigger all callbacks for a specific event.
     */
    function triggerCallbacks(event, data) {
        if (callbacks[event]) {
            callbacks[event].forEach(function (callback) {
                try {
                    callback(data);
                } catch (err) {
                    console.error("SignalR: Callback error for " + event, err);
                }
            });
        }
    }

    /**
     * Update the status bar with received data.
     */
    function updateStatusBar(data) {
        if (!data) return;

        // Update balance
        if (data.Balance !== undefined) {
            var accountBalance = $("#accountBalance");
            accountBalance.text(data.Balance.toFixed(8));
        }

        // Update global rating
        if (data.GlobalRating !== undefined) {
            var globalRating = $("#globalRating");
            globalRating.text(data.GlobalRating);
            if (parseFloat(data.GlobalRating) > 0) {
                globalRating.removeClass("text-warning").addClass("text-success");
            } else {
                globalRating.removeClass("text-success").addClass("text-warning");
            }
        }

        // Update trailing counts
        updateTrailingStatus(data);

        // Update health checks
        updateHealthStatus(data);

        // Update log entries
        if (data.LogEntries && data.LogEntries.length > 0) {
            var logEntries = $("#logEntries");
            logEntries.empty();
            data.LogEntries.forEach(function(entry) {
                $("<div>").text(entry).appendTo(logEntries);
            });
        }

        setConnectionStatus("connected");
    }

    /**
     * Update balance display.
     */
    function updateBalance(data) {
        if (!data || data.Balance === undefined) return;
        var accountBalance = $("#accountBalance");
        accountBalance.text(data.Balance.toFixed(8));
    }

    /**
     * Update trailing status display.
     */
    function updateTrailingStatus(data) {
        if (!data) return;

        if (data.TrailingBuys !== undefined) {
            var trailingBuys = $("#trailingBuys");
            trailingBuys.text(data.TrailingBuys.length);
            trailingBuys.attr("title", "Buys:\r\n" + data.TrailingBuys.join("\r\n"));
        }

        if (data.TrailingSells !== undefined) {
            var trailingSells = $("#trailingSells");
            trailingSells.text(data.TrailingSells.length);
            trailingSells.attr("title", "Sells:\r\n" + data.TrailingSells.join("\r\n"));
        }

        if (data.TrailingSignals !== undefined) {
            var trailingSignals = $("#trailingSignals");
            trailingSignals.text(data.TrailingSignals.length);
            trailingSignals.attr("title", "Signals:\r\n" + data.TrailingSignals.join("\r\n"));
        }
    }

    /**
     * Update health status display.
     */
    function updateHealthStatus(data) {
        if (!data) return;

        var healthChecks = $("#healthChecks");

        if (data.TradingSuspended !== undefined) {
            if (data.TradingSuspended) {
                healthChecks.removeClass("badge-success").addClass("badge-danger");
                healthChecks.text("OFF");
            } else {
                healthChecks.removeClass("badge-danger").addClass("badge-success");
                healthChecks.text("ON");
            }
        }

        if (data.HealthChecks && data.HealthChecks.length > 0) {
            var checks = data.HealthChecks.slice().sort(function (a, b) {
                return a.Name > b.Name ? 1 : -1;
            });
            healthChecks.attr("title", checks.map(function (check) {
                var dateStr = check.LastUpdated ? new Date(check.LastUpdated).toTimeString().split(" ")[0] : "N/A";
                return check.Name + ": " + dateStr + (check.Failed ? " (Failed)" : " (OK)");
            }).join("\r\n"));
        }
    }

    /**
     * Show a notification for trade execution.
     */
    function showTradeNotification(data) {
        if (!data) return;

        var message = data.Side + " order for " + data.Pair + " @ " + data.AveragePrice;
        console.log("SignalR: Trade executed - " + message);

        // Could show a toast notification here
        // For now, just log to console
    }

    /**
     * Set the connection status indicator.
     */
    function setConnectionStatus(status) {
        var refreshIcon = $("#statusRefreshIcon");
        var warningIcon = $("#statusWarningIcon");

        if (status === "connected") {
            refreshIcon.stop().fadeIn(200).fadeOut(200);
            warningIcon.stop().hide();
        } else if (status === "reconnecting") {
            refreshIcon.stop().show().addClass("fa-spin");
            warningIcon.stop().hide();
        } else if (status === "disconnected") {
            refreshIcon.stop().hide().removeClass("fa-spin");
            warningIcon.stop().show();
        } else {
            refreshIcon.stop().hide().removeClass("fa-spin");
            warningIcon.stop().hide();
        }
    }

    /**
     * Check if connected.
     */
    function getIsConnected() {
        return isConnected;
    }

    /**
     * Get the connection object (for advanced usage).
     */
    function getConnection() {
        return connection;
    }

    // Public API
    return {
        connect: connect,
        disconnect: disconnect,
        subscribeToPair: subscribeToPair,
        unsubscribeFromPair: unsubscribeFromPair,
        requestStatus: requestStatus,
        requestTradingPairs: requestTradingPairs,
        requestHealthStatus: requestHealthStatus,
        on: on,
        off: off,
        isConnected: getIsConnected,
        getConnection: getConnection
    };
})();
