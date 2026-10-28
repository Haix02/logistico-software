import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/preferences.dart';

/// Switches between Arabic and English (the other language's own name, as on the web console).
class LanguageButton extends ConsumerWidget {
  const LanguageButton({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final arabic = Localizations.localeOf(context).languageCode == 'ar';
    return TextButton.icon(
      onPressed: () => ref.read(preferencesProvider.notifier).setLocale(Locale(arabic ? 'en' : 'ar')),
      icon: const Icon(Icons.translate, size: 18),
      label: Text(arabic ? 'English' : 'العربية'),
    );
  }
}
