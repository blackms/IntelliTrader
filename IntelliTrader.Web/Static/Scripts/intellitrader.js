// Automatically include CSRF anti-forgery token in all AJAX POST requests
$.ajaxPrefilter(function (options, originalOptions, jqXHR) {
    if (options.type.toUpperCase() === 'POST') {
        var token = $('input[name="__RequestVerificationToken"]').val();
        if (token) {
            if (options.data) { options.data += '&'; } else { options.data = ''; }
            options.data += '__RequestVerificationToken=' + encodeURIComponent(token);
        }
    }
});

// Use SignalR for real-time updates if available, fallback to polling
var useSignalR = typeof IntelliTraderSignalR !== "undefined";
var pollingInterval = null;

$(function () {
    if (window.isAuthenticated) {
        if (useSignalR) {
            // Connect to SignalR hub for real-time updates
            initializeSignalR();
        } else {
            // Fallback to polling if SignalR is not available
            initializePolling();
        }
    }
});

/**
 * Initialize SignalR connection for real-time updates.
 */
function initializeSignalR() {
    setStatus("none");

    // Connect to the SignalR hub
    IntelliTraderSignalR.connect()
        .then(function () {
            console.log("IntelliTrader: SignalR connected - using real-time updates");
            // Request initial status
            IntelliTraderSignalR.requestStatus();
        })
        .catch(function (err) {
            console.warn("IntelliTrader: SignalR connection failed, falling back to polling", err);
            initializePolling();
        });

    // Handle connection state changes
    IntelliTraderSignalR.on("onDisconnected", function () {
        // If SignalR disconnects and cannot reconnect, fall back to polling
        if (!IntelliTraderSignalR.isConnected()) {
            console.warn("IntelliTrader: SignalR disconnected, using polling fallback");
            initializePolling();
        }
    });

    IntelliTraderSignalR.on("onConnected", function () {
        // Stop polling when SignalR reconnects
        if (pollingInterval) {
            clearInterval(pollingInterval);
            pollingInterval = null;
            console.log("IntelliTrader: SignalR reconnected, stopped polling");
        }
    });

    // Handle visibility changes - request update when tab becomes visible
    document.addEventListener("visibilitychange", function () {
        if (!document.hidden && IntelliTraderSignalR.isConnected()) {
            IntelliTraderSignalR.requestStatus();
        }
    }, false);
}

/**
 * Initialize polling for status updates (fallback).
 */
function initializePolling() {
    if (pollingInterval) {
        return; // Already polling
    }

    console.log("IntelliTrader: Using polling for status updates");
    setStatus("none");
    updateStatus();

    pollingInterval = setInterval(function () {
        updateStatus();
    }, 5000);

    document.addEventListener("visibilitychange", function () {
        updateStatus();
    }, false);
}

/**
 * Fetch status via HTTP (polling fallback).
 */
function updateStatus() {
    if (document.hidden)
        return;

    // If SignalR is connected, don't poll
    if (useSignalR && IntelliTraderSignalR.isConnected()) {
        return;
    }

    setStatus("refreshing");
    $.get("/Status", function (data) {
        var accountBalance = $("#accountBalance");
        accountBalance.text(data.Balance.toFixed(8));
        var globalRating = $("#globalRating");
        globalRating.text(data.GlobalRating);
        if (parseFloat(data.GlobalRating) > 0) {
            globalRating.removeClass("text-warning");
            globalRating.addClass("text-success");
        }
        else {
            globalRating.removeClass("text-success");
            globalRating.addClass("text-warning");
        }
        var trailingBuys = $("#trailingBuys");
        trailingBuys.text(data.TrailingBuys.length);
        trailingBuys.attr("title", "Buys:\r\n" + data.TrailingBuys.join("\r\n"));
        var trailingSells = $("#trailingSells");
        trailingSells.text(data.TrailingSells.length);
        trailingSells.attr("title", "Sells:\r\n" + data.TrailingSells.join("\r\n"));
        var trailingSignals = $("#trailingSignals");
        trailingSignals.text(data.TrailingSignals.length);
        trailingSignals.attr("title", "Signals:\r\n" + data.TrailingSignals.join("\r\n"));
        var healthChecks = $("#healthChecks");
        if (data.TradingSuspended) {
            healthChecks.removeClass("badge-success");
            healthChecks.addClass("badge-danger");
            healthChecks.text("OFF");
        }
        else {
            healthChecks.removeClass("badge-danger");
            healthChecks.addClass("badge-success");
            healthChecks.text("ON");
        }
        data.HealthChecks.sort(function (a, b) { return a.Name > b.Name; });
        healthChecks.attr("title", data.HealthChecks.map(function (check) { return check.Name + ": " + new Date(check.LastUpdated).toTimeString().split(' ')[0] + (check.Failed ? " (Failed)" : " (OK)"); }).join("\r\n"));
        var logEntries = $("#logEntries");
        logEntries.empty();
        data.LogEntries.forEach(function(entry) {
            $("<div>").text(entry).appendTo(logEntries);
        });
        setStatus("none");
    }).fail(function (data) {
        setStatus("error");
    });
}

/**
 * Set the connection status indicator.
 */
function setStatus(status) {
    if (status == "refreshing") {
        $("#statusRefreshIcon").stop().fadeIn(700).fadeOut(700);
        $("#statusWarningIcon").stop().hide();
    }
    else if (status == "error") {
        $("#statusRefreshIcon").stop().hide();
        $("#statusWarningIcon").stop().show();
    }
    else {
        $("#statusRefreshIcon").stop().hide();
        $("#statusWarningIcon").stop().hide();
    }
}

jQuery.fn.dataTable.Api.register('average()', function () {
    var data = this.flatten();
    var sum = data.reduce(function (a, b) {
        return (a * 1) + (b * 1); // cast values in-case they are strings
    }, 0);
    return sum / data.length;
});

jQuery.fn.dataTable.Api.register('sum()', function () {
    var data = this.flatten();
    var sum = data.reduce(function (a, b) {
        return (a * 1) + (b * 1); // cast values in-case they are strings
    }, 0);
    return sum;
});