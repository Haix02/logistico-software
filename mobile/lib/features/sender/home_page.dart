import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'payouts_page.dart';

/// The shop's money and orders at a glance.
class SenderHomePage extends ConsumerWidget {
  const SenderHomePage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    return RefreshIndicator(
      onRefresh: () => ref.refresh(senderHomeProvider.future),
      child: AsyncBody(
        value: ref.watch(senderHomeProvider),
        onRetry: () => ref.invalidate(senderHomeProvider),
        data: (home) => ListView(padding: const EdgeInsets.all(12), children: [
          Text(home.shop, style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800)),
          const SizedBox(height: 10),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(s.owed, style: TextStyle(color: scheme.onSurfaceVariant)),
                const SizedBox(height: 6),
                Text(f.money(home.owed), style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w800, color: scheme.success)),
              ]),
            ),
          ),
          const SizedBox(height: 10),
          KpiGrid(children: [
            Kpi(label: s.onTheWay, value: f.money(home.onTheWay), icon: Icons.local_shipping_outlined),
            Kpi(label: s.paid, value: f.money(home.paid), icon: Icons.account_balance_outlined),
            Kpi(label: s.activeOrders, value: f.number(home.activeOrders), icon: Icons.inventory_2_outlined),
            Kpi(label: s.delivered30, value: f.number(home.delivered30), icon: Icons.check_circle_outline, color: scheme.success),
            Kpi(label: s.returned30, value: f.number(home.returned30), icon: Icons.undo, color: home.returned30 > 0 ? scheme.warning : null),
          ]),
          SectionTitle(s.recentPayouts),
          if (home.payouts.isEmpty) EmptyState(icon: Icons.account_balance_wallet_outlined, text: s.noPayouts),
          for (final p in home.payouts.take(3)) PayoutTile(p),
        ]),
      ),
    );
  }
}
