import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/format.dart';
import '../../core/models.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// The shop's orders: by state, searchable, newest first, a page at a time.
class SenderOrdersPage extends ConsumerStatefulWidget {
  const SenderOrdersPage({super.key});

  @override
  ConsumerState<SenderOrdersPage> createState() => _SenderOrdersPageState();
}

class _SenderOrdersPageState extends ConsumerState<SenderOrdersPage> {
  String _view = 'All', _q = '';
  int _pages = 1;
  Timer? _debounce;

  @override
  void dispose() {
    _debounce?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    final first = ref.watch(senderOrdersProvider((view: _view, q: _q, page: 1)));
    return Column(children: [
      Padding(
        padding: const EdgeInsets.fromLTRB(12, 8, 12, 4),
        child: TextField(
          decoration: InputDecoration(hintText: s.searchOrders, prefixIcon: const Icon(Icons.search), isDense: true),
          onChanged: (text) {
            _debounce?.cancel();
            _debounce = Timer(const Duration(milliseconds: 350), () => setState(() {
              _q = text.trim();
              _pages = 1;
            }));
          },
        ),
      ),
      SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
        child: Row(children: [
          for (final (view, label) in [('All', s.viewAll), ('Active', s.viewActive), ('Delivered', s.viewDelivered), ('Returns', s.viewReturns)])
            Padding(
              padding: const EdgeInsetsDirectional.only(end: 8),
              child: ChoiceChip(label: Text(label), selected: _view == view, onSelected: (_) => setState(() {
                  _view = view;
                  _pages = 1;
                })),
            ),
        ]),
      ),
      Expanded(
        child: RefreshIndicator(
          onRefresh: () async {
            ref.invalidate(senderOrdersProvider);
            setState(() => _pages = 1);
          },
          child: AsyncBody(
            value: first,
            onRetry: () => ref.invalidate(senderOrdersProvider),
            data: (page) {
              if (page.items.isEmpty) return ListView(children: [EmptyState(icon: Icons.inventory_2_outlined, text: s.noOrders)]);
              final orders = <SenderOrder>[
                for (var p = 1; p <= _pages; p++) ...?ref.watch(senderOrdersProvider((view: _view, q: _q, page: p))).value?.items,
              ];
              final more = orders.length < page.total;
              return ListView.builder(
                padding: const EdgeInsets.fromLTRB(12, 4, 12, 24),
                itemCount: orders.length + (more ? 1 : 0),
                itemBuilder: (_, i) => i == orders.length
                    ? TextButton(onPressed: () => setState(() => _pages++), child: Text(s.loadMore))
                    : Padding(padding: const EdgeInsets.only(bottom: 8), child: _OrderCard(orders[i])),
              );
            },
          ),
        ),
      ),
    ]);
  }
}

class _OrderCard extends StatelessWidget {
  const _OrderCard(this.order);

  final SenderOrder order;

  @override
  Widget build(BuildContext context) {
    final f = fmtOf(context), scheme = Theme.of(context).colorScheme, s = S.of(context);
    return Card(
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: () => showModalBottomSheet<void>(context: context, isScrollControlled: true, builder: (_) => _OrderSheet(order)),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Expanded(child: Text(order.customer, style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16))),
              StatusPill(order.status),
            ]),
            const SizedBox(height: 6),
            Text([f.place(order.governorate), if (order.city != null) order.city!].join(' — ')),
            const SizedBox(height: 4),
            Row(children: [
              Text(order.trackingNumber, textDirection: TextDirection.ltr, style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 12)),
              const Spacer(),
              if (order.cod != null) Text(f.money(order.cod), style: const TextStyle(fontWeight: FontWeight.w700)),
              if (order.paidOut) Padding(padding: const EdgeInsetsDirectional.only(start: 6), child: Tooltip(message: s.paidOut, child: Icon(Icons.verified, size: 16, color: scheme.secondary))),
            ]),
          ]),
        ),
      ),
    );
  }
}

class _OrderSheet extends StatelessWidget {
  const _OrderSheet(this.order);

  final SenderOrder order;

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context);
    Widget row(String label, String value) => Padding(
          padding: const EdgeInsets.symmetric(vertical: 6),
          child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
            SizedBox(width: 130, child: Text(label, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant))),
            Expanded(child: Text(value, style: const TextStyle(fontWeight: FontWeight.w700))),
          ]),
        );
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Row(children: [
            Expanded(child: Text(order.trackingNumber, textDirection: TextDirection.ltr, style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800))),
            StatusPill(order.status),
          ]),
          const SizedBox(height: 10),
          row(s.customerName, order.customer),
          row(s.customerPhone, Fmt.phone(order.phone)),
          row(s.governorate, [f.place(order.governorate), if (order.city != null) order.city!].join(' — ')),
          if (order.items != null) row(s.items, order.items!),
          row(s.cod, f.money(order.cod)),
          row(s.deliveryFee, f.money(order.fee)),
          if (order.returnFee != null) row(s.returnFee, f.money(order.returnFee)),
          if (order.attempts > 0) row(s.attempts, '${order.attempts}${order.lastFailure != null ? ' · ${f.domain('Failure', order.lastFailure)}' : ''}'),
          row(s.paidOut, order.paidOut ? '✓' : '—'),
        ]),
      ),
    );
  }
}
