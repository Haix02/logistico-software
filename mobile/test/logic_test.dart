import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:logistico_app/core/config.dart';
import 'package:logistico_app/core/format.dart';
import 'package:logistico_app/core/scanner/keyboard_wedge.dart';
import 'package:logistico_app/core/version.dart';
import 'package:logistico_app/features/courier/signature_pad.dart';
import 'package:logistico_app/features/sender/book_page.dart';
import 'package:logistico_app/features/shell/tabs.dart';

AppConfig config(String kind, List<String> modules) => AppConfig.fromJson({
      'kind': kind,
      'modules': modules,
      'account': {'email': 'a@b.iq', 'name': 'علي', 'role': 'Courier', 'courierCode': 'Baghdad-01'},
      'appName': 'Basra Express',
      'primaryColor': '#1f75cb',
      'cameraScanning': true,
      'requireSignature': true,
      'scannerIntents': [
        {'action': 'android.intent.ACTION_DECODE_DATA', 'extra': 'barcode_string'},
      ],
      'announcement': 'العيد',
      'minimumVersion': '1.2.0',
      'supportPhone': '0770',
    });

void main() {
  group('version', () {
    test('compares numerically and never blocks on what it cannot read', () {
      expect(isOlderThan('1.2.0', '1.10.0'), isTrue);
      expect(isOlderThan('1.10.0', '1.2.0'), isFalse);
      expect(isOlderThan('1.0.0', '1.0.0'), isFalse);
      expect(isOlderThan('1.0.0', null), isFalse);
      expect(isOlderThan('1.0.0', 'soon'), isFalse);
    });
  });

  group('scanner gun', () {
    final t0 = DateTime(2026, 10, 28, 9);
    test('a fast burst ended by Enter is a code', () {
      final wedge = KeyboardWedge();
      for (var i = 0; i < 13; i++) {
        wedge.character('LX26000000017'[i], t0.add(Duration(milliseconds: i * 8)));
      }
      expect(wedge.enter(t0.add(const Duration(milliseconds: 110))), 'LX26000000017');
    });

    test('a person typing is not a scan, nor are short bursts', () {
      final wedge = KeyboardWedge();
      for (var i = 0; i < 6; i++) {
        wedge.character('abcdef'[i], t0.add(Duration(milliseconds: i * 250)));
      }
      expect(wedge.enter(t0.add(const Duration(milliseconds: 1600))), isNull);
      wedge.character('A', t0);
      wedge.character('B', t0.add(const Duration(milliseconds: 5)));
      expect(wedge.enter(t0.add(const Duration(milliseconds: 10))), isNull);
    });
  });

  group('signature', () {
    test('is path data in a 300 × 150 box, as the server accepts it', () {
      final path = signaturePath([
        [const Offset(0, 0), const Offset(150, 75)],
        [const Offset(600, 300)],
      ], const Size(600, 300))!;
      expect(path, 'M0 0 L75 37.5 M300 150 L300 150');
      expect(RegExp(r'^M[ML0-9 .\-]*$').hasMatch(path), isTrue); // CourierDeliveryRequest.IsPathData
      expect(signaturePath([[]], const Size(600, 300)), isNull);
    });
  });

  group('what each account sees', () {
    test('tabs follow the modules the company switched on', () {
      expect(tabsFor(config('courier', [Modules.courierRun, Modules.courierScan, Modules.courierCash])), [AppTab.courierRun, AppTab.courierCash]);
      expect(tabsFor(config('sender', [Modules.senderOrders, Modules.senderBook])), [AppTab.senderOrders, AppTab.senderBook]);
      expect(tabsFor(config('manager', [Modules.managerDashboard, Modules.managerShelfCheck])), [AppTab.managerToday, AppTab.managerHub]);
      expect(tabsFor(config('manager', [])), isEmpty);
    });

    test('the server\'s config is read in full', () {
      final c = config('courier', [Modules.courierRun]);
      expect((c.appName, c.requireSignature, c.announcement, c.minimumVersion), ('Basra Express', true, 'العيد', '1.2.0'));
      expect(c.account.courierCode, 'Baghdad-01');
      expect(c.scannerIntents.single.extra, 'barcode_string');
    });
  });

  group('formatting', () {
    test('money, phones and domain words as on the web console', () {
      expect(Fmt('ar').money(45000), '45,000 د.ع');
      expect(Fmt('en').money(45000.4), '45,000 IQD');
      expect(Fmt.phone('+9647701234567'), '0770 123 4567');
      expect(Fmt('ar').domain('Status', 'Delivered'), 'تم التسليم');
      expect(Fmt('en').place('IQ-BA'), 'Basra');
    });

    test('Iraqi mobile numbers in the forms people type them', () {
      for (final ok in ['07701234567', '0770 123 4567', '+964 770 123 4567', '009647801112233']) {
        expect(isIraqiMobile(ok), isTrue, reason: ok);
      }
      for (final bad in ['0770123', '06701234567', 'abc']) {
        expect(isIraqiMobile(bad), isFalse, reason: bad);
      }
    });
  });
}
