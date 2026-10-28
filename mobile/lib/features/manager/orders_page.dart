import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/scanner/scanner.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'order_page.dart';

/// Find any order by tracking number, customer, phone or shop — or scan its label.
class ManagerOrdersPage extends ConsumerStatefulWidget {
  const ManagerOrdersPage({super.key});

  @override
  ConsumerState<ManagerOrdersPage> createState() => _ManagerOrdersPageState();
}

class _ManagerOrdersPageState extends ConsumerState<ManagerOrdersPage> {
  final _search = TextEditingController();
  String _q = '';
  Timer? _debounce;

  @override
  void dispose() {
    _debounce?.cancel();
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context);
    return ScanListener(
      onScan: (code) {
        _search.text = code;
        setState(() => _q = code);
      },
      child: Column(children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
          child: TextField(
            controller: _search,
            decoration: InputDecoration(hintText: s.searchOrders, prefixIcon: const Icon(Icons.search), isDense: true),
            onChanged: (text) {
              _debounce?.cancel();
              _debounce = Timer(const Duration(milliseconds: 350), () => setState(() => _q = text.trim()));
            },
          ),
        ),
        Expanded(
          child: _q.isEmpty
              ? EmptyState(icon: Icons.qr_code_scanner, text: s.scanHint)
              : AsyncBody(
                  value: ref.watch(shipmentSearchProvider(_q)),
                  onRetry: () => ref.invalidate(shipmentSearchProvider(_q)),
                  data: (rows) => rows.isEmpty
                      ? EmptyState(icon: Icons.search_off, text: s.noOrders)
                      : ListView.separated(
                          padding: const EdgeInsets.fromLTRB(12, 0, 12, 24),
                          itemCount: rows.length,
                          separatorBuilder: (_, _) => const SizedBox(height: 8),
                          itemBuilder: (_, i) {
                            final r = rows[i];
                            return Card(
                              clipBehavior: Clip.antiAlias,
                              child: ListTile(
                                title: Row(children: [Expanded(child: Text(r.customer, style: const TextStyle(fontWeight: FontWeight.w700))), StatusPill(r.status)]),
                                subtitle: Text([r.trackingNumber, f.place(r.governorate), ?r.shop, if (r.cod != null) f.money(r.cod)].join(' · ')),
                                onTap: () => OrderPage.open(context, r.id),
                              ),
                            );
                          },
                        ),
                ),
        ),
      ]),
    );
  }
}
