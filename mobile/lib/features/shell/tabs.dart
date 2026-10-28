import '../../core/config.dart';

/// The tabs of the app's bottom bar.
enum AppTab { senderHome, senderOrders, senderBook, senderPayouts, courierRun, courierCash, managerToday, managerAlerts, managerOrders, managerHub, managerMoney }

/// The tabs for an account, from the modules its company switched on (the server already left out what the role may
/// not use). Hub scanning and shelf checks share one tab; scanning on the run is part of the run.
List<AppTab> tabsFor(AppConfig config) => [
      if (config.kind == 'sender') ...[
        if (config.has(Modules.senderHome)) AppTab.senderHome,
        if (config.has(Modules.senderOrders)) AppTab.senderOrders,
        if (config.has(Modules.senderBook)) AppTab.senderBook,
        if (config.has(Modules.senderPayouts)) AppTab.senderPayouts,
      ],
      if (config.kind == 'courier') ...[
        if (config.has(Modules.courierRun)) AppTab.courierRun,
        if (config.has(Modules.courierCash)) AppTab.courierCash,
      ],
      if (config.kind == 'manager') ...[
        if (config.has(Modules.managerDashboard)) AppTab.managerToday,
        if (config.has(Modules.managerAlerts)) AppTab.managerAlerts,
        if (config.has(Modules.managerOrders)) AppTab.managerOrders,
        if (config.has(Modules.managerHubScan) || config.has(Modules.managerShelfCheck)) AppTab.managerHub,
        if (config.has(Modules.managerFinance)) AppTab.managerMoney,
      ],
    ];
