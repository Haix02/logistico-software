import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// Cash on delivery from collection to the sellers' payouts.
class MoneyPage extends ConsumerWidget {
  const MoneyPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    return RefreshIndicator(
      onRefresh: () => ref.refresh(financeProvider.future),
      child: AsyncBody(
        value: ref.watch(financeProvider),
        onRetry: () => ref.invalidate(financeProvider),
        data: (m) => ListView(padding: const EdgeInsets.all(12), children: [
          KpiGrid(children: [
            Kpi(label: s.withCouriers, value: f.compactMoney(m.withCouriers), icon: Icons.delivery_dining_outlined, color: m.withCouriers > 0 ? scheme.warning : null),
            Kpi(label: s.collectedUnsettled, value: f.compactMoney(m.collected), icon: Icons.payments_outlined),
            Kpi(label: s.netPayable, value: f.compactMoney(m.netPayable), icon: Icons.storefront_outlined),
            Kpi(label: s.settled, value: f.compactMoney(m.settled), icon: Icons.check_circle_outline, color: scheme.success),
          ]),
          SectionTitle(s.sellerBalances),
          Card(
            child: Column(children: [
              for (final (shop, net, orders) in m.balances.take(30))
                ListTile(title: Text(shop), subtitle: Text(s.payoutOrders(orders)), trailing: Text(f.money(net), style: const TextStyle(fontWeight: FontWeight.w800))),
            ]),
          ),
        ]),
      ),
    );
  }
}
