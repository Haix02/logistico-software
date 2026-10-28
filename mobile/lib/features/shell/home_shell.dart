import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/config.dart';
import '../../core/preferences.dart';
import '../../core/scanner/scanner.dart';
import '../../core/session.dart';
import '../../core/version.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import '../auth/splash_page.dart';
import '../courier/cash_page.dart';
import '../courier/run_page.dart';
import '../manager/alerts_page.dart';
import '../manager/hub_page.dart';
import '../manager/money_page.dart';
import '../manager/orders_page.dart';
import '../manager/today_page.dart';
import '../sender/book_page.dart';
import '../sender/home_page.dart';
import '../sender/orders_page.dart';
import '../sender/payouts_page.dart';
import 'tabs.dart';

/// The signed-in app: the tabs this account's company switched on, kept alive between visits; only the visible one
/// runs (animations, scanning).
class HomeShell extends ConsumerStatefulWidget {
  const HomeShell({super.key});

  @override
  ConsumerState<HomeShell> createState() => _HomeShellState();
}

class _HomeShellState extends ConsumerState<HomeShell> {
  int _index = 0;

  @override
  void initState() {
    super.initState();
    // The company's handhelds: tell the Android scanner bridge which broadcasts to hear.
    WidgetsBinding.instance.addPostFrameCallback((_) => ref.read(scannerProvider).configure(ref.read(configProvider).scannerIntents));
  }

  @override
  Widget build(BuildContext context) {
    final config = ref.watch(configProvider);
    final s = S.of(context);
    if (isOlderThan(appVersion, config.minimumVersion)) return UpdateRequiredPage(minimum: config.minimumVersion!);

    final tabs = tabsFor(config);
    if (tabs.isEmpty) {
      return Scaffold(appBar: AppBar(title: Text(config.appName), actions: [_AccountButton(config)]), body: EmptyState(icon: Icons.apps_outage_outlined, text: s.nothingHere));
    }

    final index = _index.clamp(0, tabs.length - 1);
    return Scaffold(
      appBar: AppBar(title: Text(_label(s, tabs[index])), actions: [_AccountButton(config)]),
      body: Column(children: [
        if (config.announcement case final text?) AnnouncementBanner(text),
        Expanded(
          child: IndexedStack(index: index, children: [
            for (var i = 0; i < tabs.length; i++) TickerMode(enabled: i == index, child: _page(tabs[i])),
          ]),
        ),
      ]),
      bottomNavigationBar: tabs.length < 2
          ? null
          : NavigationBar(
              selectedIndex: index,
              onDestinationSelected: (i) => setState(() => _index = i),
              destinations: [for (final t in tabs) NavigationDestination(icon: Icon(_icon(t)), selectedIcon: Icon(_icon(t, selected: true)), label: _label(s, t))],
            ),
    );
  }

  static Widget _page(AppTab tab) => switch (tab) {
        AppTab.senderHome => const SenderHomePage(),
        AppTab.senderOrders => const SenderOrdersPage(),
        AppTab.senderBook => const BookOrderPage(),
        AppTab.senderPayouts => const PayoutsPage(),
        AppTab.courierRun => const RunPage(),
        AppTab.courierCash => const CashPage(),
        AppTab.managerToday => const TodayPage(),
        AppTab.managerAlerts => const AlertsPage(),
        AppTab.managerOrders => const ManagerOrdersPage(),
        AppTab.managerHub => const HubPage(),
        AppTab.managerMoney => const MoneyPage(),
      };

  static String _label(S s, AppTab tab) => switch (tab) {
        AppTab.senderHome => s.tabHome,
        AppTab.senderOrders || AppTab.managerOrders => s.tabOrders,
        AppTab.senderBook => s.tabNewOrder,
        AppTab.senderPayouts => s.tabPayouts,
        AppTab.courierRun => s.tabRun,
        AppTab.courierCash => s.tabCash,
        AppTab.managerToday => s.tabToday,
        AppTab.managerAlerts => s.tabAlerts,
        AppTab.managerHub => s.tabHub,
        AppTab.managerMoney => s.tabMoney,
      };

  static IconData _icon(AppTab tab, {bool selected = false}) => switch (tab) {
        AppTab.senderHome => selected ? Icons.storefront : Icons.storefront_outlined,
        AppTab.senderOrders || AppTab.managerOrders => selected ? Icons.inventory_2 : Icons.inventory_2_outlined,
        AppTab.senderBook => selected ? Icons.add_box : Icons.add_box_outlined,
        AppTab.senderPayouts || AppTab.managerMoney => selected ? Icons.account_balance_wallet : Icons.account_balance_wallet_outlined,
        AppTab.courierRun => selected ? Icons.local_shipping : Icons.local_shipping_outlined,
        AppTab.courierCash => selected ? Icons.payments : Icons.payments_outlined,
        AppTab.managerToday => selected ? Icons.insights : Icons.insights_outlined,
        AppTab.managerAlerts => selected ? Icons.notifications : Icons.notifications_outlined,
        AppTab.managerHub => selected ? Icons.qr_code_scanner : Icons.qr_code_scanner_outlined,
      };
}

class _AccountButton extends StatelessWidget {
  const _AccountButton(this.config);

  final AppConfig config;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsetsDirectional.only(end: 8),
        child: IconButton(
          tooltip: S.of(context).account,
          onPressed: () => showModalBottomSheet<void>(context: context, isScrollControlled: true, builder: (_) => _AccountSheet(config)),
          icon: CircleAvatar(
            radius: 16,
            backgroundColor: Theme.of(context).colorScheme.secondary,
            child: Text(config.account.name.characters.firstOrNull ?? '?', style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700)),
          ),
        ),
      );
}

class _AccountSheet extends ConsumerWidget {
  const _AccountSheet(this.config);

  final AppConfig config;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context);
    final prefs = ref.watch(preferencesProvider).value;
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(config.account.name, style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800)),
          Text(config.account.email, textDirection: TextDirection.ltr, textAlign: TextAlign.start, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
          const SizedBox(height: 16),
          Text(s.language, style: const TextStyle(fontWeight: FontWeight.w700)),
          const SizedBox(height: 6),
          SegmentedButton<String>(
            segments: const [ButtonSegment(value: 'ar', label: Text('العربية')), ButtonSegment(value: 'en', label: Text('English'))],
            selected: {prefs?.locale.languageCode ?? 'ar'},
            onSelectionChanged: (v) => ref.read(preferencesProvider.notifier).setLocale(Locale(v.first)),
          ),
          const SizedBox(height: 14),
          Text(s.theme, style: const TextStyle(fontWeight: FontWeight.w700)),
          const SizedBox(height: 6),
          SegmentedButton<ThemeMode>(
            segments: [
              ButtonSegment(value: ThemeMode.system, label: Text(s.themeSystem)),
              ButtonSegment(value: ThemeMode.light, label: Text(s.themeLight)),
              ButtonSegment(value: ThemeMode.dark, label: Text(s.themeDark)),
            ],
            selected: {prefs?.themeMode ?? ThemeMode.system},
            onSelectionChanged: (v) => ref.read(preferencesProvider.notifier).setThemeMode(v.first),
          ),
          const SizedBox(height: 16),
          if (config.supportPhone case final phone?)
            OutlinedButton.icon(onPressed: () => launchUrl(Uri(scheme: 'tel', path: phone)), icon: const Icon(Icons.support_agent), label: Text(s.support)),
          const SizedBox(height: 8),
          FilledButton.tonalIcon(
            onPressed: () {
              Navigator.of(context).pop();
              ref.read(sessionProvider.notifier).signOut();
            },
            icon: const Icon(Icons.logout),
            label: Text(s.signOut),
          ),
          const SizedBox(height: 10),
          Text('${config.appName} · $appVersion', textAlign: TextAlign.center, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant, fontSize: 12)),
        ]),
      ),
    );
  }
}
