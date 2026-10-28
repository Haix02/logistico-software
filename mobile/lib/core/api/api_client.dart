import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../preferences.dart';
import '../version.dart';
import 'token_store.dart';

/// A refusal from the server (problem details) or a network failure, with the message to show.
class ApiException implements Exception {
  ApiException(this.message, {this.status, this.code});

  final String message;
  final int? status;

  /// The server's problem code ("auth.invalid", "courier.signature_required"…), null for network failures.
  final String? code;

  bool get isUnauthorized => status == 401;

  @override
  String toString() => message;
}

/// The API, with the signed-in account's token, the app's language and version on every call. A 401 refreshes the
/// token once (for all calls waiting on it together) and repeats the call; when that fails the session ends.
class ApiClient {
  ApiClient(String baseUrl, this._tokens, this._onSignedOut, {required String language})
      : _dio = Dio(BaseOptions(
          baseUrl: baseUrl.endsWith('/') ? baseUrl : '$baseUrl/',
          connectTimeout: const Duration(seconds: 15),
          receiveTimeout: const Duration(seconds: 30),
          headers: {'Accept-Language': language == 'ar' ? 'ar-IQ' : 'en-US', 'X-App-Version': appVersion},
        )) {
    _dio.interceptors.add(QueuedInterceptorsWrapper(
      onRequest: (options, handler) async {
        final token = await _tokens.accessToken();
        if (token != null && !options.path.startsWith('api/v1/auth/')) {
          options.headers['Authorization'] = 'Bearer $token';
        }
        handler.next(options);
      },
      onError: (error, handler) async {
        final request = error.requestOptions;
        if (error.response?.statusCode == 401 && !request.path.startsWith('api/v1/auth/') && request.extra['retried'] != true) {
          if (await _refresh()) {
            request.extra['retried'] = true;
            request.headers['Authorization'] = 'Bearer ${await _tokens.accessToken()}';
            try {
              return handler.resolve(await _dio.fetch(request));
            } on DioException catch (again) {
              return handler.next(again);
            }
          }
          _onSignedOut();
        }
        handler.next(error);
      },
    ));
  }

  final Dio _dio;
  final TokenStore _tokens;
  final void Function() _onSignedOut;
  Future<bool>? _refreshing;

  Future<dynamic> get(String path, {Map<String, Object?>? query}) =>
      _send(() => _dio.get(path, queryParameters: _clean(query)));

  Future<dynamic> post(String path, [Object? body]) => _send(() => _dio.post(path, data: body));

  Future<dynamic> put(String path, Object? body) => _send(() => _dio.put(path, data: body));

  /// Signs in and keeps the tokens.
  Future<void> signIn(String email, String password, {String? twoFactorCode}) async {
    final json = await post('api/v1/auth/token', {'email': email, 'password': password, 'twoFactorCode': twoFactorCode});
    await _tokens.save(TokenSet.fromJson(json as Map<String, dynamic>));
  }

  /// One refresh at a time: calls that fail together wait for the same one.
  Future<bool> _refresh() => _refreshing ??= () async {
        try {
          final refresh = await _tokens.refreshToken();
          if (refresh == null) return false;
          final response = await _dio.post('api/v1/auth/refresh', data: {'refreshToken': refresh});
          await _tokens.save(TokenSet.fromJson(response.data as Map<String, dynamic>));
          return true;
        } on DioException {
          await _tokens.clear();
          return false;
        } finally {
          _refreshing = null;
        }
      }();

  Future<dynamic> _send(Future<Response<dynamic>> Function() call) async {
    try {
      return (await call()).data;
    } on DioException catch (e) {
      throw _problem(e);
    }
  }

  static Map<String, Object?>? _clean(Map<String, Object?>? query) =>
      query == null ? null : (Map.of(query)..removeWhere((_, v) => v == null || (v is String && v.isEmpty)));

  static ApiException _problem(DioException e) {
    final data = e.response?.data;
    if (data is Map && data['title'] is String) {
      // Validation problems carry the fields' messages; the first one says it best.
      final errors = data['errors'];
      final first = errors is Map && errors.isNotEmpty ? (errors.values.first as List).firstOrNull as String? : null;
      return ApiException(first ?? data['title'] as String, status: e.response?.statusCode, code: data['code'] as String?);
    }
    return ApiException('', status: e.response?.statusCode);
  }
}

final tokenStoreProvider = Provider<TokenStore>((ref) => TokenStore(ref.read(storageProvider)));

/// Set by the session when a refresh fails, so the app goes back to sign-in.
final signedOutSignalProvider = NotifierProvider<SignedOutSignal, int>(SignedOutSignal.new);

class SignedOutSignal extends Notifier<int> {
  @override
  int build() => 0;

  void raise() => state++;
}

/// The API for the company's server; null until the phone knows which server that is.
final apiProvider = Provider<ApiClient?>((ref) {
  final prefs = ref.watch(preferencesProvider).value;
  final server = prefs?.serverUrl;
  if (server == null) return null;
  return ApiClient(server, ref.read(tokenStoreProvider), () => ref.read(signedOutSignalProvider.notifier).raise(), language: prefs!.locale.languageCode);
});
