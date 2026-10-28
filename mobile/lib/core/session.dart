import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'api/api_client.dart';
import 'config.dart';

/// Where the app stands: which server, whether someone is signed in, and what the app shows for them.
sealed class Session {
  const Session();
}

/// The phone does not know the company's server yet.
class NeedsServer extends Session {
  const NeedsServer();
}

class SignedOut extends Session {
  const SignedOut();
}

class SignedIn extends Session {
  const SignedIn(this.config);

  final AppConfig config;
}

class SessionController extends AsyncNotifier<Session> {
  @override
  Future<Session> build() async {
    // A failed refresh anywhere in the app ends the session.
    ref.listen(signedOutSignalProvider, (_, _) => state = const AsyncData(SignedOut()));
    final api = ref.watch(apiProvider);
    if (api == null) return const NeedsServer();
    if (!await ref.read(tokenStoreProvider).hasTokens()) return const SignedOut();
    try {
      return SignedIn(await _config(api));
    } on ApiException catch (e) {
      if (e.isUnauthorized) return const SignedOut();
      rethrow;
    }
  }

  Future<void> signIn(String email, String password, {String? twoFactorCode}) async {
    final api = ref.read(apiProvider)!;
    await api.signIn(email.trim(), password, twoFactorCode: twoFactorCode);
    state = AsyncData(SignedIn(await _config(api)));
  }

  /// Reads the app's settings again (the company may have changed what this account sees).
  Future<void> reload() async {
    final api = ref.read(apiProvider);
    if (api == null || state.value is! SignedIn) return;
    state = AsyncData(SignedIn(await _config(api)));
  }

  Future<void> signOut() async {
    await ref.read(tokenStoreProvider).clear();
    state = const AsyncData(SignedOut());
  }

  static Future<AppConfig> _config(ApiClient api) async => AppConfig.fromJson(await api.get('api/v1/app/config') as Map<String, dynamic>);
}

final sessionProvider = AsyncNotifierProvider<SessionController, Session>(SessionController.new);

/// The signed-in account's config (only read below the signed-in shell).
final configProvider = Provider<AppConfig>((ref) {
  final session = ref.watch(sessionProvider).value;
  if (session is SignedIn) return session.config;
  throw StateError('No one is signed in');
});
