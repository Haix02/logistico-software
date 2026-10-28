import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'core/preferences.dart';
import 'core/session.dart';
import 'core/theme.dart';
import 'l10n/gen/app_localizations.dart';
import 'router.dart';

/// The app: the company's colour, the phone's language choice (Arabic first), light or dark.
class LogisticoApp extends ConsumerWidget {
  const LogisticoApp({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final prefs = ref.watch(preferencesProvider).value ?? const Preferences();
    final session = ref.watch(sessionProvider).value;
    final accent = session is SignedIn ? parseHex(session.config.primaryColor) : null;
    return MaterialApp.router(
      title: session is SignedIn ? session.config.appName : 'Logistico',
      debugShowCheckedModeBanner: false,
      theme: buildTheme(Brightness.light, accent: accent),
      darkTheme: buildTheme(Brightness.dark, accent: accent),
      themeMode: prefs.themeMode,
      locale: prefs.locale,
      supportedLocales: S.supportedLocales,
      localizationsDelegates: const [S.delegate, GlobalMaterialLocalizations.delegate, GlobalWidgetsLocalizations.delegate, GlobalCupertinoLocalizations.delegate],
      routerConfig: ref.watch(routerProvider),
    );
  }
}
