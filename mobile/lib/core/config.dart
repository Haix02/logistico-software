import 'package:flutter/foundation.dart';

/// The parts of the app the company can switch on, as the server names them.
abstract final class Modules {
  static const senderHome = 'sender.home', senderOrders = 'sender.orders', senderBook = 'sender.book', senderPayouts = 'sender.payouts';
  static const courierRun = 'courier.run', courierScan = 'courier.scan', courierCash = 'courier.cash';
  static const managerDashboard = 'manager.dashboard', managerAlerts = 'manager.alerts', managerOrders = 'manager.orders';
  static const managerHubScan = 'manager.hub_scan', managerShelfCheck = 'manager.shelf_check', managerFinance = 'manager.finance';
}

/// A handheld scanner's broadcast: the intent action and the extra that carries the code.
@immutable
class ScannerIntent {
  const ScannerIntent(this.action, this.extra);

  final String action;
  final String extra;

  Map<String, String> toJson() => {'action': action, 'extra': extra};
}

@immutable
class Account {
  const Account({required this.email, required this.name, required this.role, this.courierCode, this.merchantId});

  factory Account.fromJson(Map<String, dynamic> j) => Account(
        email: j['email'] as String? ?? '',
        name: j['name'] as String? ?? '',
        role: j['role'] as String? ?? '',
        courierCode: j['courierCode'] as String?,
        merchantId: j['merchantId'] as String?,
      );

  final String email, name, role;
  final String? courierCode, merchantId;
}

/// What the app shows for the signed-in account, as decided on the server (GET api/v1/app/config).
@immutable
class AppConfig {
  const AppConfig({
    required this.kind,
    required this.modules,
    required this.account,
    required this.appName,
    this.primaryColor,
    this.cameraScanning = true,
    this.requireSignature = false,
    this.scannerIntents = const [],
    this.announcement,
    this.minimumVersion,
    this.supportPhone,
  });

  factory AppConfig.fromJson(Map<String, dynamic> j) => AppConfig(
        kind: j['kind'] as String,
        modules: (j['modules'] as List).cast<String>(),
        account: Account.fromJson(j['account'] as Map<String, dynamic>),
        appName: j['appName'] as String? ?? 'Logistico',
        primaryColor: j['primaryColor'] as String?,
        cameraScanning: j['cameraScanning'] as bool? ?? true,
        requireSignature: j['requireSignature'] as bool? ?? false,
        scannerIntents: [
          for (final i in (j['scannerIntents'] as List? ?? const []).cast<Map<String, dynamic>>())
            ScannerIntent(i['action'] as String, i['extra'] as String),
        ],
        announcement: j['announcement'] as String?,
        minimumVersion: j['minimumVersion'] as String?,
        supportPhone: j['supportPhone'] as String?,
      );

  /// sender, courier or manager.
  final String kind;
  final List<String> modules;
  final Account account;
  final String appName;
  final String? primaryColor, announcement, minimumVersion, supportPhone;
  final bool cameraScanning, requireSignature;
  final List<ScannerIntent> scannerIntents;

  bool has(String module) => modules.contains(module);
}
