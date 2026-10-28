import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// The device's keystore (Android Keystore, iOS Keychain): tokens and the few settings of this phone.
final storageProvider = Provider<FlutterSecureStorage>((ref) => const FlutterSecureStorage());

/// What this phone remembers: the company's server, the language and the look.
@immutable
class Preferences {
  const Preferences({this.serverUrl, this.locale = const Locale('ar'), this.themeMode = ThemeMode.system});

  final String? serverUrl;
  final Locale locale;
  final ThemeMode themeMode;

  Preferences copyWith({String? serverUrl, Locale? locale, ThemeMode? themeMode}) =>
      Preferences(serverUrl: serverUrl ?? this.serverUrl, locale: locale ?? this.locale, themeMode: themeMode ?? this.themeMode);
}

class PreferencesController extends AsyncNotifier<Preferences> {
  static const _server = 'server_url', _locale = 'locale', _theme = 'theme_mode';

  @override
  Future<Preferences> build() async {
    final storage = ref.read(storageProvider);
    final values = await storage.readAll();
    return Preferences(
      serverUrl: values[_server],
      locale: Locale(values[_locale] ?? 'ar'),
      themeMode: ThemeMode.values.firstWhere((m) => m.name == values[_theme], orElse: () => ThemeMode.system),
    );
  }

  Future<void> setServer(String url) => _save(_server, url, (p) => p.copyWith(serverUrl: url));
  Future<void> setLocale(Locale locale) => _save(_locale, locale.languageCode, (p) => p.copyWith(locale: locale));
  Future<void> setThemeMode(ThemeMode mode) => _save(_theme, mode.name, (p) => p.copyWith(themeMode: mode));

  Future<void> clearServer() async {
    await ref.read(storageProvider).delete(key: _server);
    final current = state.requireValue;
    state = AsyncData(Preferences(locale: current.locale, themeMode: current.themeMode));
  }

  Future<void> _save(String key, String value, Preferences Function(Preferences) change) async {
    await ref.read(storageProvider).write(key: key, value: value);
    state = AsyncData(change(state.requireValue));
  }
}

final preferencesProvider = AsyncNotifierProvider<PreferencesController, Preferences>(PreferencesController.new);
