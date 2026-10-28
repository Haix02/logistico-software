import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/preferences.dart';
import '../../l10n/gen/app_localizations.dart';
import 'language_button.dart';

/// First start: the address of the company's Logistico server, checked before it is kept.
class ServerPage extends ConsumerStatefulWidget {
  const ServerPage({super.key});

  @override
  ConsumerState<ServerPage> createState() => _ServerPageState();
}

class _ServerPageState extends ConsumerState<ServerPage> {
  final _address = TextEditingController(text: 'https://');
  bool _checking = false;
  String? _error;

  @override
  void dispose() {
    _address.dispose();
    super.dispose();
  }

  Future<void> _continue() async {
    final s = S.of(context);
    var url = _address.text.trim();
    if (!url.startsWith('http://') && !url.startsWith('https://')) url = 'https://$url';
    url = url.replaceAll(RegExp(r'/+$'), '');
    setState(() {
      _checking = true;
      _error = null;
    });
    try {
      // A Logistico server answers who is signed in, even for nobody.
      final answer = await Dio(BaseOptions(connectTimeout: const Duration(seconds: 10))).get('$url/api/v1/account/me');
      if (answer.data is! Map || !(answer.data as Map).containsKey('isAuthenticated')) throw const FormatException();
      await ref.read(preferencesProvider.notifier).setServer(url);
    } catch (_) {
      if (mounted) setState(() => _error = s.serverInvalid);
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    return Scaffold(
      appBar: AppBar(actions: const [LanguageButton()]),
      body: SafeArea(
        child: ListView(padding: const EdgeInsets.all(24), children: [
          const _Logo(),
          const SizedBox(height: 32),
          Text(s.serverTitle, style: Theme.of(context).textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800)),
          const SizedBox(height: 8),
          Text(s.serverHint, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
          const SizedBox(height: 24),
          TextField(
            controller: _address,
            keyboardType: TextInputType.url,
            textDirection: TextDirection.ltr,
            autocorrect: false,
            decoration: InputDecoration(labelText: s.serverAddress, errorText: _error, prefixIcon: const Icon(Icons.dns_outlined)),
            onSubmitted: (_) => _continue(),
          ),
          const SizedBox(height: 20),
          FilledButton(
            onPressed: _checking ? null : _continue,
            child: _checking ? const SizedBox.square(dimension: 20, child: CircularProgressIndicator(strokeWidth: 2)) : Text(s.serverContinue),
          ),
        ]),
      ),
    );
  }
}

class _Logo extends StatelessWidget {
  const _Logo();

  @override
  Widget build(BuildContext context) => Row(children: [
        Transform.rotate(
          angle: 0.785398,
          child: Container(width: 26, height: 26, decoration: BoxDecoration(color: Theme.of(context).colorScheme.secondary, borderRadius: BorderRadius.circular(5))),
        ),
        const SizedBox(width: 14),
        Text(S.of(context).appTitle, style: const TextStyle(fontSize: 26, fontWeight: FontWeight.w800)),
      ]);
}
