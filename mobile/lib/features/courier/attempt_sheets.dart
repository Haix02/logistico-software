import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/data.dart';
import '../../core/models.dart';
import '../../core/session.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'location.dart';
import 'signature_pad.dart';

const _receivers = ['Customer', 'Family', 'Neighbour', 'Workplace', 'Other'];
const _failures = ['NoAnswer', 'CustomerRefused', 'WrongAddress', 'CustomerRescheduled', 'Unreachable', 'Other'];

/// Delivered, with proof: who took it, a signature (required when the company says so) and where the phone is.
class DeliverSheet extends ConsumerStatefulWidget {
  const DeliverSheet({super.key, required this.stop});

  final CourierStop stop;

  @override
  ConsumerState<DeliverSheet> createState() => _DeliverSheetState();
}

class _DeliverSheetState extends ConsumerState<DeliverSheet> {
  final _signature = SignatureController();
  final _name = TextEditingController();
  late final Future<Map<String, double>?> _place = currentPlace();
  Map<String, double>? _found;
  bool _located = false, _busy = false, _signatureMissing = false;
  String _receiver = 'Customer';

  @override
  void initState() {
    super.initState();
    _place.then((p) {
      if (mounted) {
        setState(() {
          _found = p;
          _located = true;
        });
      }
    });
  }

  @override
  void dispose() {
    _signature.dispose();
    _name.dispose();
    super.dispose();
  }

  Future<void> _confirm() async {
    final config = ref.read(configProvider);
    final signature = _signature.toPath();
    if (config.requireSignature && signature == null) {
      setState(() => _signatureMissing = true);
      return;
    }
    setState(() => _busy = true);
    final s = S.of(context);
    try {
      await ref.read(apiProvider)!.post('api/v1/courier/run/${widget.stop.id}/delivered', {
        'receivedBy': _receiver,
        'receiverName': _receiver == 'Customer' ? null : _name.text.trim(),
        'signature': signature,
        'location': await placeWithin(_place),
      });
      HapticFeedback.mediumImpact();
      ref.invalidate(courierRunProvider);
      if (mounted) {
        Navigator.of(context).pop();
        showDone(context, s.deliveredToast(widget.stop.trackingNumber));
      }
    } on ApiException catch (e) {
      if (mounted) showError(context, e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    final required = ref.watch(configProvider).requireSignature;
    final needsName = _receiver != 'Customer';
    final stop = widget.stop;
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, 16 + MediaQuery.viewInsetsOf(context).bottom),
        child: SingleChildScrollView(
          child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
            Text(
              (stop.cod ?? 0) > 0 ? s.collectCash(f.money(stop.cod), stop.customer) : s.confirmDelivered(stop.customer),
              style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800),
            ),
            const SizedBox(height: 14),
            Text(s.whoReceived, style: const TextStyle(fontWeight: FontWeight.w700)),
            const SizedBox(height: 8),
            Wrap(spacing: 8, runSpacing: 8, children: [
              for (final r in _receivers)
                ChoiceChip(label: Text(f.domain('Receiver', r)), selected: _receiver == r, onSelected: (_) => setState(() => _receiver = r)),
            ]),
            if (needsName) ...[
              const SizedBox(height: 12),
              TextField(controller: _name, decoration: InputDecoration(labelText: s.receiverName), onChanged: (_) => setState(() {})),
            ],
            const SizedBox(height: 14),
            Row(children: [
              Text(s.signature, style: const TextStyle(fontWeight: FontWeight.w700)),
              const SizedBox(width: 6),
              Text('(${required ? s.required : s.optional})', style: TextStyle(color: required ? scheme.warning : scheme.onSurfaceVariant)),
              const Spacer(),
              TextButton.icon(onPressed: _signature.clear, icon: const Icon(Icons.backspace_outlined, size: 18), label: Text(s.clearSignature)),
            ]),
            SignaturePad(controller: _signature),
            if (_signatureMissing) Padding(padding: const EdgeInsets.only(top: 6), child: Text(s.signatureNeeded, style: TextStyle(color: scheme.error))),
            const SizedBox(height: 10),
            Row(children: [
              Icon(Icons.place_outlined, size: 18, color: scheme.onSurfaceVariant),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  !_located ? s.locating : _found != null ? s.located(_found!['accuracyMeters']!.round()) : s.noLocation,
                  style: TextStyle(fontSize: 13, color: scheme.onSurfaceVariant),
                ),
              ),
            ]),
            const SizedBox(height: 16),
            FilledButton.icon(
              style: FilledButton.styleFrom(backgroundColor: scheme.success, foregroundColor: Colors.white),
              onPressed: _busy || (needsName && _name.text.trim().isEmpty) ? null : _confirm,
              icon: _busy ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white)) : const Icon(Icons.check),
              label: Text(s.markDelivered),
            ),
          ]),
        ),
      ),
    );
  }
}

/// Not delivered: the reason, and where the courier was.
class FailSheet extends ConsumerStatefulWidget {
  const FailSheet({super.key, required this.stop});

  final CourierStop stop;

  @override
  ConsumerState<FailSheet> createState() => _FailSheetState();
}

class _FailSheetState extends ConsumerState<FailSheet> {
  late final Future<Map<String, double>?> _place = currentPlace();
  bool _busy = false;

  Future<void> _record(String reason) async {
    setState(() => _busy = true);
    final s = S.of(context);
    try {
      await ref.read(apiProvider)!.post('api/v1/courier/run/${widget.stop.id}/failed', {'reason': reason, 'location': await placeWithin(_place)});
      ref.invalidate(courierRunProvider);
      if (mounted) {
        Navigator.of(context).pop();
        showDone(context, s.failedToast(widget.stop.trackingNumber));
      }
    } on ApiException catch (e) {
      if (mounted) showError(context, e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context);
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.stretch, children: [
          Text(s.whyFailed, style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800)),
          const SizedBox(height: 12),
          for (final reason in _failures)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: OutlinedButton(onPressed: _busy ? null : () => _record(reason), child: Text(f.domain('Failure', reason))),
            ),
        ]),
      ),
    );
  }
}
