import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/data.dart';
import '../../core/format.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// One order for the office: who, where, money, and its journey.
class OrderPage extends ConsumerWidget {
  const OrderPage({super.key, required this.id});

  final String id;

  static Future<void> open(BuildContext context, String id) => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => OrderPage(id: id)));

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    final detail = ref.watch(shipmentProvider(id));
    return Scaffold(
      appBar: AppBar(title: Text(detail.value?.row.trackingNumber ?? '')),
      body: AsyncBody(
        value: detail,
        onRetry: () => ref.invalidate(shipmentProvider(id)),
        data: (d) => ListView(padding: const EdgeInsets.all(12), children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(16),
              child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                Row(children: [Expanded(child: Text(d.row.customer, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w800))), StatusPill(d.row.status)]),
                const SizedBox(height: 8),
                Text([f.place(d.row.governorate), if (d.city != null) d.city!].join(' — '), style: const TextStyle(fontWeight: FontWeight.w700)),
                if (d.address != null) Text(d.address!),
                if (d.landmark != null) Text('📍 ${d.landmark!}', style: TextStyle(color: scheme.onSurfaceVariant)),
                if (d.items != null) Padding(padding: const EdgeInsets.only(top: 6), child: Text('📦 ${d.items!}')),
                const SizedBox(height: 10),
                Wrap(spacing: 16, runSpacing: 6, children: [
                  Text('${s.cod}: ${f.money(d.row.cod)}'),
                  Text('${s.deliveryFee}: ${f.money(d.fee)}'),
                  if (d.row.shop != null) Text(d.row.shop!),
                  if (d.row.courier != null) Text('${s.courier}: ${d.row.courier!}'),
                  if (d.row.shelf != null) Text('${s.shelf}: ${d.row.shelf!}', textDirection: TextDirection.ltr),
                ]),
                if (d.phone != null) ...[
                  const SizedBox(height: 10),
                  OutlinedButton.icon(onPressed: () => launchUrl(Uri(scheme: 'tel', path: d.phone)), icon: const Icon(Icons.call), label: Text(Fmt.phone(d.phone))),
                ],
              ]),
            ),
          ),
          const SizedBox(height: 8),
          Card(
            child: Column(children: [
              for (final e in d.events)
                ListTile(
                  dense: true,
                  leading: Icon(Icons.circle, size: 10, color: scheme.secondary),
                  title: Text(f.domain('Status', e.status), style: const TextStyle(fontWeight: FontWeight.w700)),
                  subtitle: Text([f.dateTime(e.occurredAt), ?e.location, ?e.courier, if (e.failure != null) f.domain('Failure', e.failure)].join(' · ')),
                ),
            ]),
          ),
        ]),
      ),
    );
  }
}
