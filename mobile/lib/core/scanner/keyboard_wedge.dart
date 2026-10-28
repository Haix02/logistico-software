/// Tells a scanner gun (or a handheld in keyboard mode) from a person typing: a scanner "types" a whole code in a fast
/// burst and ends it with Enter; people are much slower between keys.
class KeyboardWedge {
  KeyboardWedge({this.maxGap = const Duration(milliseconds: 60), this.minLength = 4});

  final Duration maxGap;
  final int minLength;
  final StringBuffer _buffer = StringBuffer();
  DateTime? _last;

  /// A printable character arrived at [at].
  void character(String char, DateTime at) {
    if (_last != null && at.difference(_last!) > maxGap) _buffer.clear(); // too slow: someone typing
    _buffer.write(char);
    _last = at;
  }

  /// Enter arrived at [at]: the code, when what came before was a scanner's burst.
  String? enter(DateTime at) {
    final code = _buffer.toString().trim();
    final fast = _last != null && at.difference(_last!) <= maxGap;
    _buffer.clear();
    _last = null;
    return fast && code.length >= minLength ? code : null;
  }
}
