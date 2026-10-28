import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:logistico_app/app.dart';

void main() {
  setUpAll(() async {
    await initializeDateFormatting('ar');
    await initializeDateFormatting('en');
  });

  testWidgets('a new phone asks for the company server, in Arabic, right to left', (tester) async {
    FlutterSecureStorage.setMockInitialValues({});
    await tester.pumpWidget(const ProviderScope(child: LogisticoApp()));
    await tester.pumpAndSettle();

    expect(find.text('لوجستيكو شركتك'), findsOneWidget);
    expect(find.text('متابعة'), findsOneWidget);
    final direction = Directionality.of(tester.element(find.text('متابعة')));
    expect(direction, TextDirection.rtl);
  });

  testWidgets('the language switch turns the app English', (tester) async {
    FlutterSecureStorage.setMockInitialValues({});
    await tester.pumpWidget(const ProviderScope(child: LogisticoApp()));
    await tester.pumpAndSettle();

    await tester.tap(find.text('English'));
    await tester.pumpAndSettle();
    expect(find.text("Your company's Logistico"), findsOneWidget);
  });
}
