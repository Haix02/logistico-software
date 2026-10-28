/// This build's version; keep in step with `version:` in pubspec.yaml (the server can require a minimum).
const String appVersion = '1.0.0';

/// True when [version] is older than [minimum] ("1.2.0" < "1.10.0"); unreadable versions never block.
bool isOlderThan(String version, String? minimum) {
  if (minimum == null) return false;
  final a = _parts(version), b = _parts(minimum);
  if (a == null || b == null) return false;
  for (var i = 0; i < 3; i++) {
    if (a[i] != b[i]) return a[i] < b[i];
  }
  return false;
}

List<int>? _parts(String v) {
  final parts = v.trim().split('.');
  if (parts.length != 3) return null;
  final numbers = parts.map(int.tryParse).toList();
  return numbers.contains(null) ? null : numbers.cast<int>();
}
