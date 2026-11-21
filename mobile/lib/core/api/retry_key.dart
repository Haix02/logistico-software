import 'dart:convert';
import 'dart:math';

/// One Idempotency-Key per thing being sent. Sending the same content again (a retry after a lost answer on a weak
/// network) reuses the key, so the server books it once; changed content is a new request with a new key; after a
/// success the next send starts afresh, even with the same content (the same customer booked twice on purpose).
class RetryKey {
  static final _random = Random.secure();

  String? _content;
  String _key = newKey();

  /// 32 random hex characters, like a GUID without dashes.
  static String newKey() => List.generate(16, (_) => _random.nextInt(256).toRadixString(16).padLeft(2, '0')).join();

  String forBody(Object? body) {
    final json = jsonEncode(body);
    if (json != _content) {
      _content = json;
      _key = newKey();
    }
    return _key;
  }

  /// The request went through: the next one is a new intention.
  void done() => _content = null;
}
