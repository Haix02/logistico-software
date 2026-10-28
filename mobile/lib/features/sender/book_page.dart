import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/data.dart';
import '../../core/widgets.dart';
import '../../l10n/domain_texts.g.dart';
import '../../l10n/gen/app_localizations.dart';

/// "07701234567", "0770 123 4567", "+964 770 123 4567": an Iraqi mobile number, as the server reads them.
bool isIraqiMobile(String text) {
  var digits = text.replaceAll(RegExp(r'[\s\-()]'), '');
  if (digits.startsWith('+964')) digits = '0${digits.substring(4)}';
  if (digits.startsWith('00964')) digits = '0${digits.substring(5)}';
  if (digits.startsWith('964')) digits = '0${digits.substring(3)}';
  return RegExp(r'^07[3-9]\d{8}$').hasMatch(digits);
}

/// A new order at the company's price: the customer, their Iraqi address and the cash to collect.
class BookOrderPage extends ConsumerStatefulWidget {
  const BookOrderPage({super.key});

  @override
  ConsumerState<BookOrderPage> createState() => _BookOrderPageState();
}

class _BookOrderPageState extends ConsumerState<BookOrderPage> {
  final _form = GlobalKey<FormState>();
  final _name = TextEditingController(), _phone = TextEditingController(), _area = TextEditingController(), _address = TextEditingController();
  final _landmark = TextEditingController(), _items = TextEditingController(), _cod = TextEditingController(), _notes = TextEditingController();
  String? _governorate;
  bool _busy = false;

  @override
  void dispose() {
    for (final c in [_name, _phone, _area, _address, _landmark, _items, _cod, _notes]) {
      c.dispose();
    }
    super.dispose();
  }

  String? _required(String? v) => (v == null || v.trim().isEmpty) ? S.of(context).fieldRequired : null;

  Future<void> _book() async {
    if (!_form.currentState!.validate()) return;
    setState(() => _busy = true);
    final s = S.of(context);
    String? clean(TextEditingController c) => c.text.trim().isEmpty ? null : c.text.trim();
    final cod = double.tryParse(_cod.text.replaceAll(',', '').trim());
    try {
      final booked = await ref.read(apiProvider)!.post('api/v1/seller/orders', {
        'serviceType': 'Domestic',
        'consigneeName': _name.text.trim(),
        'consigneePhone': _phone.text.trim(),
        'destinationCode': _governorate,
        'destinationCity': clean(_area),
        'deliveryAddress': _address.text.trim(),
        'landmark': clean(_landmark),
        'itemsDescription': clean(_items),
        'codAmount': cod,
        'codCurrency': cod == null || cod <= 0 ? null : 'IQD',
        'notes': clean(_notes),
        'pieces': 1,
        'grossWeightKg': 1,
      }) as Map<String, dynamic>;
      HapticFeedback.mediumImpact();
      ref.invalidate(senderOrdersProvider);
      ref.invalidate(senderHomeProvider);
      if (!mounted) return;
      showDone(context, s.booked(booked['trackingNumber'] as String));
      _form.currentState!.reset();
      for (final c in [_name, _phone, _area, _address, _landmark, _items, _cod, _notes]) {
        c.clear();
      }
      setState(() => _governorate = null);
    } on ApiException catch (e) {
      if (mounted) showError(context, e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context);
    final arabic = Localizations.localeOf(context).languageCode == 'ar';
    final quote = _governorate == null ? null : ref.watch(quoteProvider(_governorate!));
    InputDecoration label(String text, {bool optional = false, IconData? icon}) =>
        InputDecoration(labelText: optional ? '$text (${s.optional})' : text, prefixIcon: icon == null ? null : Icon(icon));
    return Form(
      key: _form,
      child: ListView(padding: const EdgeInsets.fromLTRB(16, 12, 16, 32), children: [
        TextFormField(controller: _name, decoration: label(s.customerName, icon: Icons.person_outline), validator: _required, textInputAction: TextInputAction.next),
        const SizedBox(height: 12),
        TextFormField(
          controller: _phone,
          keyboardType: TextInputType.phone,
          textDirection: TextDirection.ltr,
          decoration: label(s.customerPhone, icon: Icons.phone_outlined),
          validator: (v) => _required(v) ?? (isIraqiMobile(v!) ? null : s.invalidPhone),
          textInputAction: TextInputAction.next,
        ),
        const SizedBox(height: 12),
        DropdownButtonFormField<String>(
          initialValue: _governorate,
          decoration: label(s.governorate, icon: Icons.map_outlined),
          items: [for (final (code, en, ar) in governorates) DropdownMenuItem(value: code, child: Text(arabic ? ar : en))],
          onChanged: (v) => setState(() => _governorate = v),
          validator: (v) => v == null ? s.fieldRequired : null,
        ),
        const SizedBox(height: 12),
        TextFormField(controller: _area, decoration: label(s.area, optional: true), textInputAction: TextInputAction.next),
        const SizedBox(height: 12),
        TextFormField(controller: _address, decoration: label(s.address, icon: Icons.home_outlined), validator: _required, textInputAction: TextInputAction.next),
        const SizedBox(height: 12),
        TextFormField(controller: _landmark, decoration: label(s.landmark, optional: true, icon: Icons.place_outlined), textInputAction: TextInputAction.next),
        const SizedBox(height: 12),
        TextFormField(controller: _items, decoration: label(s.items, optional: true, icon: Icons.inventory_2_outlined), textInputAction: TextInputAction.next),
        const SizedBox(height: 12),
        TextFormField(
          controller: _cod,
          keyboardType: const TextInputType.numberWithOptions(),
          inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9,]'))],
          textDirection: TextDirection.ltr,
          decoration: label(s.cod, optional: true, icon: Icons.payments_outlined).copyWith(suffixText: arabic ? 'د.ع' : 'IQD'),
        ),
        const SizedBox(height: 12),
        TextFormField(controller: _notes, decoration: label(s.notes, optional: true), maxLines: 2),
        const SizedBox(height: 16),
        if (quote != null)
          Card(
            child: ListTile(
              leading: const Icon(Icons.sell_outlined),
              title: Text(s.deliveryPrice),
              trailing: switch (quote) {
                AsyncData(:final value) => Text(f.money(value), style: const TextStyle(fontWeight: FontWeight.w800, fontSize: 16)),
                AsyncError() => const Icon(Icons.error_outline),
                _ => const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2)),
              },
            ),
          ),
        const SizedBox(height: 16),
        FilledButton.icon(
          onPressed: _busy ? null : _book,
          icon: _busy ? const SizedBox.square(dimension: 18, child: CircularProgressIndicator(strokeWidth: 2)) : const Icon(Icons.add),
          label: Text(s.book),
        ),
      ]),
    );
  }
}
