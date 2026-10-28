import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'core/session.dart';
import 'features/auth/login_page.dart';
import 'features/auth/server_page.dart';
import 'features/auth/splash_page.dart';
import 'features/shell/home_shell.dart';

/// Where the session says the app is: no server yet, signed out, or the signed-in account's app.
final routerProvider = Provider<GoRouter>((ref) {
  final changes = ValueNotifier(0);
  ref.listen(sessionProvider, (_, _) => changes.value++);
  ref.onDispose(changes.dispose);
  return GoRouter(
    initialLocation: '/',
    refreshListenable: changes,
    redirect: (context, state) {
      final session = ref.read(sessionProvider);
      final here = state.matchedLocation;
      final target = switch (session) {
        AsyncData(value: NeedsServer()) => '/server',
        AsyncData(value: SignedOut()) => '/login',
        AsyncData(value: SignedIn()) => '/',
        _ => '/splash', // starting, or the server could not be reached
      };
      return here == target ? null : target;
    },
    routes: [
      GoRoute(path: '/', builder: (_, _) => const HomeShell()),
      GoRoute(path: '/splash', builder: (_, _) => const SplashPage()),
      GoRoute(path: '/server', builder: (_, _) => const ServerPage()),
      GoRoute(path: '/login', builder: (_, _) => const LoginPage()),
    ],
  );
});
