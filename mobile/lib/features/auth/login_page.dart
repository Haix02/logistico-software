import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/preferences.dart';
import '../../core/session.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';
import 'language_button.dart';

/// Sign-in with the account the company created (no sign-up: accounts come from the Team page).
class LoginPage extends ConsumerStatefulWidget {
  const LoginPage({super.key});

  @override
  ConsumerState<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends ConsumerState<LoginPage> {
  final _email = TextEditingController(), _password = TextEditingController(), _code = TextEditingController();
  bool _busy = false, _needsCode = false, _hidden = true;
  String? _error;

  @override
  void dispose() {
    for (final c in [_email, _password, _code]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _signIn() async {
    if (_email.text.trim().isEmpty || _password.text.isEmpty) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(sessionProvider.notifier).signIn(_email.text, _password.text, twoFactorCode: _needsCode ? _code.text : null);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _needsCode = _needsCode || e.code == 'auth.two_factor_required';
        _error = errorText(context, e);
      });
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    final server = ref.watch(preferencesProvider).value?.serverUrl ?? '';
    return Scaffold(
      appBar: AppBar(actions: const [LanguageButton()]),
      body: SafeArea(
        child: AutofillGroup(
          child: ListView(padding: const EdgeInsets.all(24), children: [
            Text(s.signInTitle, style: Theme.of(context).textTheme.headlineMedium?.copyWith(fontWeight: FontWeight.w800)),
            const SizedBox(height: 6),
            Text(Uri.tryParse(server)?.host ?? server, textDirection: TextDirection.ltr, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
            const SizedBox(height: 28),
            TextField(
              controller: _email,
              keyboardType: TextInputType.emailAddress,
              textDirection: TextDirection.ltr,
              autofillHints: const [AutofillHints.email, AutofillHints.username],
              decoration: InputDecoration(labelText: s.email, prefixIcon: const Icon(Icons.alternate_email)),
              textInputAction: TextInputAction.next,
            ),
            const SizedBox(height: 14),
            TextField(
              controller: _password,
              obscureText: _hidden,
              textDirection: TextDirection.ltr,
              autofillHints: const [AutofillHints.password],
              decoration: InputDecoration(
                labelText: s.password,
                prefixIcon: const Icon(Icons.lock_outline),
                suffixIcon: IconButton(onPressed: () => setState(() => _hidden = !_hidden), icon: Icon(_hidden ? Icons.visibility_outlined : Icons.visibility_off_outlined)),
              ),
              onSubmitted: (_) => _signIn(),
            ),
            if (_needsCode) ...[
              const SizedBox(height: 14),
              TextField(
                controller: _code,
                keyboardType: TextInputType.number,
                textDirection: TextDirection.ltr,
                autofillHints: const [AutofillHints.oneTimeCode],
                decoration: InputDecoration(labelText: s.twoFactorCode, prefixIcon: const Icon(Icons.pin_outlined)),
                onSubmitted: (_) => _signIn(),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 14),
              Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
            ],
            const SizedBox(height: 24),
            FilledButton(
              onPressed: _busy ? null : _signIn,
              child: _busy ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2)) : Text(s.signIn),
            ),
            const SizedBox(height: 8),
            TextButton(onPressed: () => ref.read(preferencesProvider.notifier).clearServer(), child: Text(s.changeServer)),
          ]),
        ),
      ),
    );
  }
}
