import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/config.dart';
import '../../core/data.dart';
import '../../core/models.dart';
import '../../core/scanner/camera_scan_page.dart';
import '../../core/scanner/scanner.dart';
import '../../core/session.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// The hub station on a handheld: receive parcels onto a shelf, give them to a courier, check their route — or count a
/// shelf. Every scan (built-in scanner, scanner gun or camera) goes straight to the server; the answer shows big.
class HubPage extends ConsumerStatefulWidget {
  const HubPage({super.key});

  @override
  ConsumerState<HubPage> createState() => _HubPageState();
}

class _HubPageState extends ConsumerState<HubPage> {
  late bool _counting = !ref.read(configProvider).has(Modules.managerHubScan);
  String _operation = 'Receive';
  String? _hub, _courier;
  final _shelf = TextEditingController();
  final List<ScanResult> _results = [];
  bool _busy = false;

  ShelfSummary? _check;
  final List<ShelfLine> _lines = [];

  @override
  void dispose() {
    _shelf.dispose();
    super.dispose();
  }

  Future<void> _scan(String code) async {
    if (_busy || _hub == null) return;
    if (_counting) return _count(code);
    if (_operation == 'Dispatch' && _courier == null) {
      showError(context, ApiException(S.of(context).chooseCourier));
      return;
    }
    setState(() => _busy = true);
    final s = S.of(context);
    try {
      final json = await ref.read(apiProvider)!.post('api/v1/warehouse/scan', {
        'operation': _operation,
        'trackingNumber': code,
        'hubCode': _hub,
        'warehouseBin': _operation == 'Receive' && _shelf.text.trim().isNotEmpty ? _shelf.text.trim().toUpperCase() : null,
        'courierName': _operation == 'Dispatch' ? _courier : null,
      });
      _show(ScanResult(json as Json));
    } on ApiException catch (e) {
      // A refused scan still answers with what happened.
      _show(ScanResult({'ok': false, 'trackingNumber': code, 'message': e.message.isEmpty ? s.errorUnexpected : e.message}));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _show(ScanResult result) {
    result.ok ? HapticFeedback.mediumImpact() : HapticFeedback.heavyImpact();
    if (mounted) setState(() => _results.insert(0, result));
  }

  Future<void> _startCount() async {
    if (_hub == null) return;
    setState(() => _busy = true);
    try {
      final json = await ref.read(apiProvider)!.post('api/v1/shelf-checks', {'hubCode': _hub, 'bin': _shelf.text.trim().isEmpty ? null : _shelf.text.trim()}) as Json;
      setState(() {
        _check = ShelfSummary(json['summary'] as Json);
        _lines.clear();
      });
    } on ApiException catch (e) {
      if (mounted) showError(context, e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _count(String code) async {
    final check = _check;
    if (check == null || check.finished) return;
    setState(() => _busy = true);
    try {
      final json = await ref.read(apiProvider)!.post('api/v1/shelf-checks/${check.id}/scan', {'trackingNumber': code}) as Json;
      final line = ShelfLine(json['line'] as Json);
      line.state == 'Found' ? HapticFeedback.mediumImpact() : HapticFeedback.heavyImpact();
      setState(() {
        _check = ShelfSummary(json['summary'] as Json);
        _lines
          ..removeWhere((l) => l.trackingNumber == line.trackingNumber)
          ..insert(0, line);
      });
    } on ApiException catch (e) {
      if (mounted) showError(context, e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _finishCount() async {
    final check = _check;
    if (check == null) return;
    try {
      final json = await ref.read(apiProvider)!.post('api/v1/shelf-checks/${check.id}/finish') as Json;
      setState(() {
        _check = ShelfSummary(json['summary'] as Json);
        for (final l in (json['lines'] as List).cast<Json>().map(ShelfLine.new).where((l) => l.state == 'Missing')) {
          _lines.add(l);
        }
      });
    } on ApiException catch (e) {
      if (mounted) showError(context, e);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context), config = ref.watch(configProvider);
    final language = Localizations.localeOf(context).languageCode;
    final hubs = ref.watch(hubsProvider).value ?? const [];
    _hub ??= hubs.firstOrNull?.code;
    final both = config.has(Modules.managerHubScan) && config.has(Modules.managerShelfCheck);
    return ScanListener(
      onScan: _scan,
      child: Scaffold(
        floatingActionButton: config.cameraScanning
            ? FloatingActionButton(
                onPressed: () async {
                  final code = await CameraScanPage.open(context);
                  if (code != null) _scan(code);
                },
                tooltip: s.scanCamera,
                child: const Icon(Icons.qr_code_scanner),
              )
            : null,
        body: ListView(padding: const EdgeInsets.fromLTRB(12, 8, 12, 96), children: [
          if (both)
            SegmentedButton<bool>(
              segments: [ButtonSegment(value: false, label: Text(s.scan), icon: const Icon(Icons.qr_code_scanner)), ButtonSegment(value: true, label: Text(s.shelfCheck), icon: const Icon(Icons.checklist))],
              selected: {_counting},
              onSelectionChanged: (v) => setState(() => _counting = v.first),
            ),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(
            initialValue: _hub,
            decoration: InputDecoration(labelText: s.hub, prefixIcon: const Icon(Icons.warehouse_outlined)),
            items: [for (final h in hubs) DropdownMenuItem(value: h.code, child: Text('${h.tag} · ${h.name(language)}'))],
            onChanged: (v) => setState(() => _hub = v),
          ),
          const SizedBox(height: 10),
          if (!_counting) ...[
            SegmentedButton<String>(
              segments: [
                ButtonSegment(value: 'Receive', label: Text(s.opReceive)),
                ButtonSegment(value: 'Dispatch', label: Text(s.opDispatch)),
                ButtonSegment(value: 'Verify', label: Text(s.opVerify)),
              ],
              selected: {_operation},
              onSelectionChanged: (v) => setState(() => _operation = v.first),
            ),
            const SizedBox(height: 10),
            if (_operation == 'Receive')
              TextField(controller: _shelf, textDirection: TextDirection.ltr, decoration: InputDecoration(labelText: '${s.shelf} (${s.optional})', hintText: 'A-01-1', prefixIcon: const Icon(Icons.shelves))),
            if (_operation == 'Dispatch')
              DropdownButtonFormField<String>(
                initialValue: _courier,
                decoration: InputDecoration(labelText: s.chooseCourier, prefixIcon: const Icon(Icons.delivery_dining_outlined)),
                items: [for (final c in ref.watch(couriersProvider).value ?? const <CourierItem>[]) DropdownMenuItem(value: c.label, child: Text(c.label))],
                onChanged: (v) => setState(() => _courier = v),
              ),
            const SizedBox(height: 12),
            _Hint(busy: _busy),
            for (final r in _results.take(30)) _ResultTile(r),
          ] else ...[
            if (_check == null || _check!.finished) ...[
              TextField(controller: _shelf, textDirection: TextDirection.ltr, decoration: InputDecoration(labelText: '${s.shelf} (${s.optional})', hintText: s.wholeHub, prefixIcon: const Icon(Icons.shelves))),
              const SizedBox(height: 10),
              FilledButton.icon(onPressed: _busy ? null : _startCount, icon: const Icon(Icons.play_arrow), label: Text(s.startCheck)),
            ],
            if (_check case final c?) ...[
              const SizedBox(height: 12),
              Text('${c.number} · ${c.bin ?? s.wholeHub}', style: const TextStyle(fontWeight: FontWeight.w800)),
              const SizedBox(height: 8),
              KpiGrid(children: [
                Kpi(label: s.expected, value: f.number(c.expected), icon: Icons.inventory_2_outlined),
                Kpi(label: s.found, value: f.number(c.found), icon: Icons.check_circle_outline, color: Theme.of(context).colorScheme.success),
                Kpi(label: c.finished ? s.missing : f.domain('ShelfState', 'Expected'), value: f.number(c.finished ? c.missing : c.pending), icon: Icons.help_outline,
                    color: c.finished && c.missing > 0 ? Theme.of(context).colorScheme.error : null),
                Kpi(label: s.wrongShelf, value: f.number(c.wrongShelf), icon: Icons.swap_horiz, color: c.wrongShelf > 0 ? Theme.of(context).colorScheme.warning : null),
              ]),
              if (!c.finished) ...[
                const SizedBox(height: 10),
                _Hint(busy: _busy),
                OutlinedButton.icon(onPressed: _finishCount, icon: const Icon(Icons.flag_outlined), label: Text(s.finishCheck)),
              ],
              for (final l in _lines.take(60))
                ListTile(
                  dense: true,
                  leading: Icon(l.state == 'Found' ? Icons.check_circle : Icons.warning_amber, color: l.state == 'Found' ? Theme.of(context).colorScheme.success : Theme.of(context).colorScheme.warning),
                  title: Text(l.trackingNumber, textDirection: TextDirection.ltr),
                  subtitle: Text([f.domain('ShelfState', l.state), ?l.systemBin].join(' · ')),
                ),
            ],
          ],
        ]),
      ),
    );
  }
}

class _Hint extends StatelessWidget {
  const _Hint({required this.busy});

  final bool busy;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 8),
        child: Row(children: [
          busy ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2)) : Icon(Icons.qr_code_scanner, color: Theme.of(context).colorScheme.secondary),
          const SizedBox(width: 10),
          Expanded(child: Text(S.of(context).scanHint, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant))),
        ]),
      );
}

class _ResultTile extends StatelessWidget {
  const _ResultTile(this.result);

  final ScanResult result;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final color = result.ok ? scheme.success : scheme.error;
    return Card(
      color: color.withValues(alpha: 0.08),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10), side: BorderSide(color: color.withValues(alpha: 0.4))),
      child: ListTile(
        leading: Icon(result.ok ? Icons.check_circle : Icons.error, color: color),
        title: Text(result.trackingNumber, textDirection: TextDirection.ltr, style: const TextStyle(fontWeight: FontWeight.w800)),
        subtitle: Text(result.message),
      ),
    );
  }
}
