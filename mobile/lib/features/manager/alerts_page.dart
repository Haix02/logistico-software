import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'order_page.dart';

/// Open exceptions (late, stuck, missing, misrouted parcels; late trucks; overdue requests), the worst first.
class AlertsPage extends ConsumerWidget {
  const AlertsPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    return RefreshIndicator(
      onRefresh: () => ref.refresh(alertsProvider.future),
      child: AsyncBody(
        value: ref.watch(alertsProvider),
        onRetry: () => ref.invalidate(alertsProvider),
        data: (data) {
          final (alerts, critical) = data;
          if (alerts.isEmpty) return ListView(children: [EmptyState(icon: Icons.notifications_none, text: s.noAlerts)]);
          return ListView.separated(
            padding: const EdgeInsets.all(12),
            itemCount: alerts.length + 1,
            separatorBuilder: (_, _) => const SizedBox(height: 8),
            itemBuilder: (_, i) {
              if (i == 0) return critical > 0 ? Text(s.critical(critical), style: TextStyle(color: scheme.error, fontWeight: FontWeight.w800)) : const SizedBox.shrink();
              final a = alerts[i - 1];
              final color = switch (a.severity) { 'Critical' => scheme.error, 'Warning' => scheme.warning, _ => scheme.onSurfaceVariant };
              return Card(
                clipBehavior: Clip.antiAlias,
                child: ListTile(
                  leading: Icon(Icons.error_outline, color: color),
                  title: Text(f.domain('Alert', a.kind), style: const TextStyle(fontWeight: FontWeight.w700)),
                  subtitle: Text([
                    ?a.trackingNumber,
                    ?a.transferNumber,
                    if (a.hub != null) a.hub!,
                    if (a.hours != null) '${a.hours} h',
                    ?a.detail,
                  ].join(' · ')),
                  trailing: a.shipmentId == null ? null : const Icon(Icons.chevron_right),
                  onTap: a.shipmentId == null ? null : () => OrderPage.open(context, a.shipmentId!),
                ),
              );
            },
          );
        },
      ),
    );
  }
}
