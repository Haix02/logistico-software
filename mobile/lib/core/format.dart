import 'package:intl/intl.dart';

import '../l10n/domain_texts.g.dart';

/// Numbers, money and dates the way the web console writes them (Western digits in both languages).
class Fmt {
  Fmt(this.language);

  final String language;

  bool get _ar => language == 'ar';

  String number(num value) => NumberFormat.decimalPattern('en').format(value);

  String money(num? value) => value == null ? '—' : '${number(value.round())} ${_ar ? 'د.ع' : 'IQD'}';

  /// "45.3M IQD" for dashboards.
  String compactMoney(num value) =>
      value.abs() >= 1e6 ? '${(value / 1e6).toStringAsFixed(1)}${_ar ? ' مليون' : 'M'} ${_ar ? 'د.ع' : 'IQD'}' : money(value);

  String dateTime(DateTime? value) => value == null ? '—' : DateFormat('d MMM y · HH:mm', _ar ? 'ar' : 'en').format(value.toLocal());

  String time(DateTime value) => DateFormat('HH:mm', 'en').format(value.toLocal());

  /// A domain word from the server's own texts: status, failure reason, receiver, alert…
  String domain(String prefix, String? value) => value == null ? '—' : domainTexts[language]?['${prefix}_$value'] ?? value;

  String place(String? code) {
    for (final (c, en, ar) in governorates) {
      if (c == code) return _ar ? ar : en;
    }
    return code ?? '—';
  }

  /// "0770 123 4567" for a stored "+9647701234567".
  static String phone(String? stored) {
    if (stored == null) return '—';
    final digits = stored.replaceAll(RegExp(r'\D'), '');
    final local = digits.startsWith('964') ? '0${digits.substring(3)}' : digits;
    return local.length == 11 ? '${local.substring(0, 4)} ${local.substring(4, 7)} ${local.substring(7)}' : stored;
  }
}
