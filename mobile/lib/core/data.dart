import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'api/api_client.dart';
import 'models.dart';

/// The data each screen reads. Providers are dropped when their screen goes, so memory stays small; screens refresh
/// them with ref.invalidate after an action or a pull.
ApiClient _api(Ref ref) => ref.watch(apiProvider)!;

// Courier
final courierRunProvider = FutureProvider.autoDispose<CourierRun>((ref) async => CourierRun(await _api(ref).get('api/v1/courier/run') as Json));

// Sender
final senderHomeProvider = FutureProvider.autoDispose<SenderHome>((ref) async => SenderHome(await _api(ref).get('api/v1/seller') as Json));

typedef OrdersQuery = ({String view, String q, int page});

final senderOrdersProvider = FutureProvider.autoDispose.family<Page<SenderOrder>, OrdersQuery>((ref, query) async {
  final json = await _api(ref).get('api/v1/seller/orders', query: {'view': query.view, 'q': query.q, 'page': query.page}) as Json;
  return Page([for (final o in (json['items'] as List).cast<Json>()) SenderOrder(o)], json['total'] as int);
});

final quoteProvider = FutureProvider.autoDispose.family<double, String>((ref, destination) async {
  final json = await _api(ref).get('api/v1/seller/quote', query: {'destination': destination, 'weightKg': 1}) as Json;
  return (json['fee'] as num).toDouble();
});

// Office
final overviewProvider = FutureProvider.autoDispose.family<Overview, int>((ref, days) async => Overview(await _api(ref).get('api/v1/analytics/overview', query: {'days': days}) as Json));

final alertsProvider = FutureProvider.autoDispose<(List<Alert>, int)>((ref) async {
  final json = await _api(ref).get('api/v1/automation/alerts', query: {'pageSize': 100}) as Json;
  return ([for (final a in (json['items'] as List).cast<Json>()) Alert(a)], json['critical'] as int? ?? 0);
});

final shipmentSearchProvider = FutureProvider.autoDispose.family<List<ShipmentRow>, String>((ref, q) async {
  final json = await _api(ref).get('api/v1/shipments', query: {'q': q, 'pageSize': 40}) as Json;
  return [for (final s in (json['items'] as List).cast<Json>()) ShipmentRow(s)];
});

final shipmentProvider = FutureProvider.autoDispose.family<ShipmentDetail, String>((ref, id) async => ShipmentDetail(await _api(ref).get('api/v1/shipments/$id') as Json));

final hubsProvider = FutureProvider.autoDispose<List<Hub>>((ref) async {
  final json = await _api(ref).get('api/v1/network/hubs') as Json;
  return [for (final h in (json['hubs'] as List).cast<Json>()) Hub(h)];
});

final couriersProvider = FutureProvider.autoDispose<List<CourierItem>>((ref) async => [for (final c in (await _api(ref).get('api/v1/fleet/lookup') as List).cast<Json>()) CourierItem(c)]);

final financeProvider = FutureProvider.autoDispose<Finance>((ref) async => Finance(await _api(ref).get('api/v1/finance/summary') as Json));
