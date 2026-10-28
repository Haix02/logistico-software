import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// The cash the courier holds: what to hand in at the office and what was collected today.
class CashPage extends ConsumerWidget {
  const CashPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    return RefreshIndicator(
      onRefresh: () => ref.refresh(courierRunProvider.future),
      child: AsyncBody(
        value: ref.watch(courierRunProvider),
        onRetry: () => ref.invalidate(courierRunProvider),
        data: (run) => ListView(padding: const EdgeInsets.all(12), children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Text(s.cashToHandIn, style: TextStyle(color: scheme.onSurfaceVariant)),
                const SizedBox(height: 8),
                Text(f.money(run.cashToHandIn), style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w800, color: scheme.success)),
                const SizedBox(height: 4),
                Text(s.parcelsN(run.parcelsToHandIn), style: const TextStyle(fontWeight: FontWeight.w700)),
                const SizedBox(height: 12),
                Text(s.handInHint, style: TextStyle(color: scheme.onSurfaceVariant)),
              ]),
            ),
          ),
          const SizedBox(height: 10),
          KpiGrid(children: [
            Kpi(label: s.cashToday, value: f.money(run.cashToday), icon: Icons.payments_outlined),
            Kpi(label: s.deliveredToday, value: f.number(run.deliveredToday), icon: Icons.check_circle_outline, color: scheme.success),
          ]),
        ]),
      ),
    );
  }
}
