import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/config.dart';
import '../../core/data.dart';
import '../../core/format.dart';
import '../../core/models.dart';
import '../../core/scanner/camera_scan_page.dart';
import '../../core/scanner/scanner.dart';
import '../../core/session.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'attempt_sheets.dart';

/// The courier's day: every parcel out for delivery with them. Scanning a label (handheld, scanner gun or camera)
/// opens its stop; new parcels appear by themselves.
class RunPage extends ConsumerStatefulWidget {
  const RunPage({super.key});

  @override
  ConsumerState<RunPage> createState() => _RunPageState();
}

class _RunPageState extends ConsumerState<RunPage> {
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _timer = Timer.periodic(const Duration(seconds: 60), (_) {
      if (mounted && TickerMode.valuesOf(context).enabled) ref.invalidate(courierRunProvider);
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  void _openScanned(String code) {
    final run = ref.read(courierRunProvider).value;
    final stop = run?.stops.where((s) => s.trackingNumber.toUpperCase() == code.toUpperCase()).firstOrNull;
    if (stop == null) {
      HapticFeedback.heavyImpact();
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(SnackBar(content: Text(S.of(context).notOnRun(code))));
      return;
    }
    HapticFeedback.selectionClick();
    showModalBottomSheet<void>(context: context, isScrollControlled: true, builder: (_) => _StopSheet(stop));
  }

  @override
  Widget build(BuildContext context) {
    final config = ref.watch(configProvider);
    final run = ref.watch(courierRunProvider);
    final s = S.of(context);
    return ScanListener(
      onScan: config.has(Modules.courierScan) ? _openScanned : (_) {},
      child: Scaffold(
        floatingActionButton: config.has(Modules.courierScan) && config.cameraScanning
            ? FloatingActionButton.extended(
                onPressed: () async {
                  final code = await CameraScanPage.open(context);
                  if (code != null && context.mounted) _openScanned(code);
                },
                icon: const Icon(Icons.qr_code_scanner),
                label: Text(s.scan),
              )
            : null,
        body: RefreshIndicator(
          onRefresh: () => ref.refresh(courierRunProvider.future),
          child: AsyncBody(
            value: run,
            onRetry: () => ref.invalidate(courierRunProvider),
            data: (run) => ListView(padding: const EdgeInsets.fromLTRB(12, 8, 12, 96), children: [
              _DayCard(run),
              if (run.stops.isEmpty) EmptyState(icon: Icons.inventory_2_outlined, text: s.noStops),
              for (final stop in run.stops) Padding(padding: const EdgeInsets.only(top: 10), child: _StopCard(stop)),
            ]),
          ),
        ),
      ),
    );
  }
}

class _DayCard extends StatelessWidget {
  const _DayCard(this.run);

  final CourierRun run;

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    Widget figure(String value, String label, [Color? color]) => Expanded(
          child: Column(children: [
            Text(value, style: TextStyle(fontSize: 20, fontWeight: FontWeight.w800, color: color)),
            const SizedBox(height: 2),
            Text(label, textAlign: TextAlign.center, style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant)),
          ]),
        );
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Text(s.hello(run.name.split(' ').first), style: Theme.of(context).textTheme.titleLarge?.copyWith(fontWeight: FontWeight.w800)),
          const SizedBox(height: 14),
          Row(children: [
            figure(f.number(run.stops.length), s.tabRun),
            figure(f.number(run.deliveredToday), s.deliveredToday, scheme.success),
            figure(f.number(run.failedToday), s.failedToday, run.failedToday > 0 ? scheme.error : null),
          ]),
        ]),
      ),
    );
  }
}

class _StopCard extends StatelessWidget {
  const _StopCard(this.stop);

  final CourierStop stop;

  @override
  Widget build(BuildContext context) => Card(
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: () => showModalBottomSheet<void>(context: context, isScrollControlled: true, builder: (_) => _StopSheet(stop)),
          child: Padding(padding: const EdgeInsets.all(14), child: _StopDetails(stop, compact: true)),
        ),
      );
}

class _StopDetails extends StatelessWidget {
  const _StopDetails(this.stop, {this.compact = false});

  final CourierStop stop;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    final where = [f.place(stop.governorate), if (stop.city != null) stop.city!].join(' — ');
    return Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
      Row(children: [
        Expanded(child: Text(stop.customer, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w800))),
        if (stop.cod case final cod? when cod > 0)
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
            decoration: BoxDecoration(color: scheme.success.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(99)),
            child: Text(f.money(cod), style: TextStyle(color: scheme.success, fontWeight: FontWeight.w800)),
          ),
      ]),
      const SizedBox(height: 6),
      Text(where, style: const TextStyle(fontWeight: FontWeight.w700)),
      if (stop.address != null) Text(stop.address!),
      if (stop.landmark != null) Text('📍 ${stop.landmark!}', style: TextStyle(color: scheme.onSurfaceVariant)),
      if (!compact) ...[
        if (stop.items != null) Padding(padding: const EdgeInsets.only(top: 6), child: Text('📦 ${stop.items!}')),
        if (stop.notes != null) Padding(padding: const EdgeInsets.only(top: 6), child: Text(stop.notes!, style: TextStyle(color: scheme.warning))),
      ],
      const SizedBox(height: 6),
      Text(
        [stop.trackingNumber, if (stop.shop != null) stop.shop!, if (stop.attempts > 0) '${S.of(context).attempts}: ${stop.attempts}'].join(' · '),
        style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
      ),
    ]);
  }
}

/// One stop with everything the courier needs at the door.
class _StopSheet extends ConsumerWidget {
  const _StopSheet(this.stop);

  final CourierStop stop;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context);
    final phone = stop.phone;
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          _StopDetails(stop),
          const SizedBox(height: 16),
          if (phone != null)
            Row(children: [
              Expanded(child: OutlinedButton.icon(onPressed: () => launchUrl(Uri(scheme: 'tel', path: phone)), icon: const Icon(Icons.call), label: Text(s.call))),
              const SizedBox(width: 10),
              Expanded(
                child: OutlinedButton.icon(
                  onPressed: () => launchUrl(Uri.parse('https://wa.me/${phone.replaceAll(RegExp(r'\D'), '')}'), mode: LaunchMode.externalApplication),
                  icon: const Icon(Icons.chat_outlined),
                  label: Text(s.whatsapp),
                ),
              ),
            ]),
          if (phone != null) Padding(padding: const EdgeInsets.only(top: 4), child: Text(Fmt.phone(phone), textAlign: TextAlign.center, textDirection: TextDirection.ltr)),
          const SizedBox(height: 12),
          FilledButton.icon(
            style: FilledButton.styleFrom(backgroundColor: Theme.of(context).colorScheme.success, foregroundColor: Colors.white),
            onPressed: () async {
              Navigator.of(context).pop();
              await showModalBottomSheet<void>(context: context, isScrollControlled: true, builder: (_) => DeliverSheet(stop: stop));
            },
            icon: const Icon(Icons.check),
            label: Text(s.markDelivered),
          ),
          const SizedBox(height: 8),
          TextButton.icon(
            style: TextButton.styleFrom(foregroundColor: Theme.of(context).colorScheme.error),
            onPressed: () async {
              Navigator.of(context).pop();
              await showModalBottomSheet<void>(context: context, isScrollControlled: true, builder: (_) => FailSheet(stop: stop));
            },
            icon: const Icon(Icons.close),
            label: Text(s.markFailed),
          ),
        ]),
      ),
    );
  }
}
