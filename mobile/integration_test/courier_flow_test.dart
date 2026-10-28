import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';
import 'package:logistico_app/main.dart' as app;

/// On a simulator or device against a real server: a courier signs in, finds today's run, delivers one parcel with a
/// signature. Prints `SHOT:name` where a screenshot is useful (tool/run_device_test.py takes them).
/// `flutter test integration_test -d DEVICE --dart-define=SERVER=http://localhost:5210`
void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  Future<void> wait(WidgetTester tester, [int seconds = 3]) async {
    for (var i = 0; i < seconds * 4; i++) {
      await tester.pump(const Duration(milliseconds: 250));
    }
  }

  Future<void> shot(WidgetTester tester, String name) async {
    debugPrint('SHOT:$name');
    await wait(tester, 4); // the screenshot is taken while the screen holds still
  }

  testWidgets('a courier signs in, sees the run and delivers with a signature', (tester) async {
    app.main();
    await wait(tester, 4);

    await tester.enterText(find.byType(TextField), const String.fromEnvironment('SERVER', defaultValue: 'http://localhost:5311'));
    await tester.tap(find.text('متابعة'));
    await wait(tester, 4);
    await shot(tester, 'login');

    await tester.enterText(find.byType(TextField).at(0), 'courier@logistico.local');
    await tester.enterText(find.byType(TextField).at(1), 'Logistico#2026');
    await tester.tap(find.text('دخول'));
    await wait(tester, 6);
    expect(find.text('جولتي'), findsWidgets);
    expect(find.textContaining('أحمد كريم'), findsWidgets);
    await shot(tester, 'run');

    await tester.tap(find.textContaining('أحمد كريم').first);
    await wait(tester, 2);
    await shot(tester, 'stop');
    await tester.tap(find.widgetWithText(FilledButton, 'تم التسليم'));
    await wait(tester, 3);
    await tester.tap(find.text('أحد أفراد العائلة'));
    await wait(tester, 1);
    await tester.enterText(find.byType(TextField).last, 'أم أحمد');
    await wait(tester, 1);
    final pad = tester.getRect(find.byType(CustomPaint).last);
    final gesture = await tester.startGesture(pad.centerLeft + const Offset(30, 0));
    for (var i = 1; i <= 12; i++) {
      await gesture.moveBy(Offset(18, i.isEven ? -14 : 14));
      await tester.pump(const Duration(milliseconds: 16));
    }
    await gesture.up();
    await wait(tester, 2);
    await shot(tester, 'deliver');

    await tester.tap(find.widgetWithText(FilledButton, 'تم التسليم').last);
    await wait(tester, 5);
    expect(find.textContaining('أحمد كريم'), findsNothing); // delivered: off the run
    await shot(tester, 'after');
  });
}
