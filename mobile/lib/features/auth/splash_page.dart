import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/preferences.dart';
import '../../core/session.dart';
import '../../core/version.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// While the session starts; if the server cannot be reached, says so with a retry.
class SplashPage extends ConsumerWidget {
  const SplashPage({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(sessionProvider);
    final s = S.of(context);
    return Scaffold(
      body: Center(
        child: session.hasError
            ? Padding(
                padding: const EdgeInsets.all(24),
                child: Column(mainAxisSize: MainAxisSize.min, children: [
                  const Icon(Icons.cloud_off_outlined, size: 44),
                  const SizedBox(height: 12),
                  Text(errorText(context, session.error!), textAlign: TextAlign.center),
                  const SizedBox(height: 16),
                  FilledButton(onPressed: () => ref.invalidate(sessionProvider), child: Text(s.retry)),
                  TextButton(onPressed: () => ref.read(preferencesProvider.notifier).clearServer(), child: Text(s.changeServer)),
                ]),
              )
            : const CircularProgressIndicator(),
      ),
    );
  }
}

/// The company requires a newer app (minimum version in its Mobile app settings).
class UpdateRequiredPage extends StatelessWidget {
  const UpdateRequiredPage({super.key, required this.minimum});

  final String minimum;

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(mainAxisAlignment: MainAxisAlignment.center, children: [
            const Icon(Icons.system_update_outlined, size: 56),
            const SizedBox(height: 16),
            Text(s.updateTitle, style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800)),
            const SizedBox(height: 8),
            Text(s.updateBody(minimum), textAlign: TextAlign.center),
            const SizedBox(height: 8),
            Text(appVersion, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
          ]),
        ),
      ),
    );
  }
}
