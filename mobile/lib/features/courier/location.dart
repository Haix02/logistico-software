import 'package:geolocator/geolocator.dart';

/// The phone's position for proof of delivery: asked when a delivery dialog opens, null when refused or not found in
/// time (it never holds up a delivery).
Future<Map<String, double>?> currentPlace() async {
  try {
    if (!await Geolocator.isLocationServiceEnabled()) return null;
    var permission = await Geolocator.checkPermission();
    if (permission == LocationPermission.denied) permission = await Geolocator.requestPermission();
    if (permission == LocationPermission.denied || permission == LocationPermission.deniedForever) return null;
    final p = await Geolocator.getCurrentPosition(locationSettings: const LocationSettings(accuracy: LocationAccuracy.high, timeLimit: Duration(seconds: 8)));
    return {'latitude': p.latitude, 'longitude': p.longitude, 'accuracyMeters': p.accuracy.roundToDouble()};
  } catch (_) {
    return null;
  }
}

/// What the position search has found by now, waiting at most [wait] more.
Future<Map<String, double>?> placeWithin(Future<Map<String, double>?> search, {Duration wait = const Duration(seconds: 3)}) =>
    search.timeout(wait, onTimeout: () => null);
