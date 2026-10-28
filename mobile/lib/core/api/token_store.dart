import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// The tokens the server issued, as its JSON names them.
class TokenSet {
  TokenSet(this.accessToken, this.refreshToken);

  factory TokenSet.fromJson(Map<String, dynamic> json) => TokenSet(json['accessToken'] as String, json['refreshToken'] as String);

  final String accessToken;
  final String refreshToken;
}

/// Tokens in the device keystore, read once and then kept in memory.
class TokenStore {
  TokenStore(this._storage);

  static const _access = 'access_token', _refresh = 'refresh_token';
  final FlutterSecureStorage _storage;
  String? _accessCache;
  bool _loaded = false;

  Future<String?> accessToken() async {
    if (!_loaded) {
      _accessCache = await _storage.read(key: _access);
      _loaded = true;
    }
    return _accessCache;
  }

  Future<String?> refreshToken() => _storage.read(key: _refresh);

  Future<bool> hasTokens() async => await accessToken() != null;

  Future<void> save(TokenSet tokens) async {
    await _storage.write(key: _access, value: tokens.accessToken);
    await _storage.write(key: _refresh, value: tokens.refreshToken);
    _accessCache = tokens.accessToken;
    _loaded = true;
  }

  Future<void> clear() async {
    await _storage.delete(key: _access);
    await _storage.delete(key: _refresh);
    _accessCache = null;
    _loaded = true;
  }
}
