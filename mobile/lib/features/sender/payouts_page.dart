import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/models.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// Every payout the company made to the shop.
class PayoutsPage extends ConsumerWidget {
  const PayoutsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => RefreshIndicator(
        onRefresh: () => ref.refresh(senderHomeProvider.future),
        child: AsyncBody(
          value: ref.watch(senderHomeProvider),
          onRetry: () => ref.invalidate(senderHomeProvider),
          data: (home) => home.payouts.isEmpty
              ? ListView(children: [EmptyState(icon: Icons.account_balance_wallet_outlined, text: S.of(context).noPayouts)])
              : ListView.separated(
                  padding: const EdgeInsets.all(12),
                  itemCount: home.payouts.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 8),
                  itemBuilder: (_, i) => PayoutTile(home.payouts[i]),
                ),
        ),
      );
}

class PayoutTile extends StatelessWidget {
  const PayoutTile(this.payout, {super.key});

  final Payout payout;

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context);
    return Card(
      child: ListTile(
        leading: const Icon(Icons.receipt_long_outlined),
        title: Text(f.money(payout.net), style: TextStyle(fontWeight: FontWeight.w800, color: Theme.of(context).colorScheme.success)),
        subtitle: Text('${payout.number} · ${s.payoutOrders(payout.orders)}\n${f.dateTime(payout.settledAt)} · ${f.domain('Payout', payout.method)}'),
        isThreeLine: true,
      ),
    );
  }
}
