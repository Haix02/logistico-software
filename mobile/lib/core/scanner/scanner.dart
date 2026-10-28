import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config.dart';
import 'keyboard_wedge.dart';

/// Every way a code reaches the app, as one stream: the built-in scanner of an Android handheld (broadcast intents),
/// a scanner gun or a handheld in keyboard mode (fast key bursts), and the camera (pushed by the camera page).
class ScannerHub {
  ScannerHub() {
    HardwareKeyboard.instance.addHandler(_onKey);
    if (defaultTargetPlatform == TargetPlatform.android) {
      _intents = _events.receiveBroadcastStream().listen((code) => add(code as String), onError: (_) {});
    }
  }

  static const _events = EventChannel('app.logistico/scanner');
  static const _control = MethodChannel('app.logistico/scanner/config');

  final _codes = StreamController<String>.broadcast();
  final _wedge = KeyboardWedge();
  StreamSubscription<dynamic>? _intents;
  String? _lastCode;
  DateTime _lastAt = DateTime.fromMillisecondsSinceEpoch(0);

  Stream<String> get codes => _codes.stream;

  /// The broadcasts this company's handhelds send (from the server's app settings).
  Future<void> configure(List<ScannerIntent> intents) async {
    if (defaultTargetPlatform != TargetPlatform.android) return;
    try {
      await _control.invokeMethod('configure', [for (final i in intents) i.toJson()]);
    } on PlatformException {
      // Not a handheld: keyboard and camera still work.
    }
  }

  /// A code from any source; the same code twice within a moment (one scan heard twice) counts once.
  void add(String raw) {
    final code = raw.trim();
    if (code.isEmpty) return;
    final now = DateTime.now();
    if (code == _lastCode && now.difference(_lastAt) < const Duration(milliseconds: 700)) return;
    _lastCode = code;
    _lastAt = now;
    _codes.add(code);
  }

  bool _onKey(KeyEvent event) {
    if (event is! KeyDownEvent) return false;
    // While someone types in a field, the keys are theirs.
    final focused = FocusManager.instance.primaryFocus?.context?.widget;
    if (focused is EditableText) return false;
    if (event.logicalKey == LogicalKeyboardKey.enter || event.logicalKey == LogicalKeyboardKey.numpadEnter) {
      final code = _wedge.enter(_at(event));
      if (code != null) add(code);
      return code != null;
    }
    final char = event.character;
    if (char != null && char.isNotEmpty && char.codeUnitAt(0) >= 32) _wedge.character(char, _at(event));
    return false;
  }

  /// When the key was pressed, as the platform measured it (more exact than now for telling a scanner's burst).
  static DateTime _at(KeyEvent event) =>
      event.timeStamp == Duration.zero ? DateTime.now() : DateTime.fromMicrosecondsSinceEpoch(event.timeStamp.inMicroseconds);

  void dispose() {
    HardwareKeyboard.instance.removeHandler(_onKey);
    _intents?.cancel();
    _codes.close();
  }
}

final scannerProvider = Provider<ScannerHub>((ref) {
  final hub = ScannerHub();
  ref.onDispose(hub.dispose);
  return hub;
});

/// Hands scanned codes to [onScan] while this screen is the one on display (not a hidden tab or a covered page).
class ScanListener extends ConsumerStatefulWidget {
  const ScanListener({super.key, required this.onScan, required this.child});

  final void Function(String code) onScan;
  final Widget child;

  @override
  ConsumerState<ScanListener> createState() => _ScanListenerState();
}

class _ScanListenerState extends ConsumerState<ScanListener> {
  StreamSubscription<String>? _subscription;

  @override
  void initState() {
    super.initState();
    _subscription = ref.read(scannerProvider).codes.listen((code) {
      if (!mounted || !TickerMode.valuesOf(context).enabled || ModalRoute.of(context)?.isCurrent == false) return;
      widget.onScan(code);
    });
  }

  @override
  void dispose() {
    _subscription?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
