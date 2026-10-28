/// Typed views of the server's answers (only the fields the app shows). Names follow the API's JSON.
library;

typedef Json = Map<String, dynamic>;

DateTime? _date(Object? v) => v == null ? null : DateTime.parse(v as String);
double? _num(Object? v) => (v as num?)?.toDouble();
List<T> _list<T>(Object? v, T Function(Json) parse) => [for (final e in (v as List? ?? const [])) parse(e as Json)];

class Page<T> {
  Page(this.items, this.total);

  final List<T> items;
  final int total;
}

// ── Courier ──────────────────────────────────────────────────────────────────

class CourierStop {
  CourierStop(Json j)
      : id = j['id'] as String,
        trackingNumber = j['trackingNumber'] as String,
        customer = j['consigneeName'] as String,
        phone = j['consigneePhone'] as String?,
        governorate = j['destinationCode'] as String,
        city = j['city'] as String?,
        address = j['address'] as String?,
        landmark = j['landmark'] as String?,
        items = j['items'] as String?,
        cod = _num(j['codAmount']),
        attempts = j['attempts'] as int? ?? 0,
        shop = j['merchantName'] as String?,
        notes = j['notes'] as String?;

  final String id, trackingNumber, customer, governorate;
  final String? phone, city, address, landmark, items, shop, notes;
  final double? cod;
  final int attempts;
}

class CourierRun {
  CourierRun(Json j)
      : code = j['courierCode'] as String,
        name = j['courierName'] as String,
        deliveredToday = j['deliveredToday'] as int,
        failedToday = j['failedToday'] as int,
        cashToday = _num(j['cashCollectedTodayIqd']) ?? 0,
        cashToHandIn = _num(j['cashToHandInIqd']) ?? 0,
        parcelsToHandIn = j['parcelsToHandIn'] as int? ?? 0,
        stops = _list(j['stops'], CourierStop.new);

  final String code, name;
  final int deliveredToday, failedToday, parcelsToHandIn;
  final double cashToday, cashToHandIn;
  final List<CourierStop> stops;
}

// ── Sender (seller portal) ───────────────────────────────────────────────────

class Payout {
  Payout(Json j)
      : number = j['settlementNumber'] as String,
        net = _num(j['netPayoutAmount']) ?? 0,
        orders = j['shipmentCount'] as int? ?? 0,
        method = j['payoutMethod'] as String?,
        settledAt = _date(j['settledAt'])!;

  final String number;
  final double net;
  final int orders;
  final String? method;
  final DateTime settledAt;
}

class SenderHome {
  SenderHome(Json j)
      : shop = j['merchantName'] as String,
        activeOrders = j['activeOrders'] as int,
        delivered30 = j['deliveredLast30Days'] as int,
        returned30 = j['returnedLast30Days'] as int,
        owed = _num(j['owedIqd']) ?? 0,
        onTheWay = _num(j['onTheWayIqd']) ?? 0,
        paid = _num(j['paidIqd']) ?? 0,
        payouts = _list(j['payouts'], Payout.new);

  final String shop;
  final int activeOrders, delivered30, returned30;
  final double owed, onTheWay, paid;
  final List<Payout> payouts;
}

class SenderOrder {
  SenderOrder(Json j)
      : id = j['id'] as String,
        trackingNumber = j['trackingNumber'] as String,
        status = j['status'] as String,
        customer = j['consigneeName'] as String,
        phone = j['consigneePhone'] as String?,
        governorate = j['destinationCode'] as String,
        city = j['destinationCity'] as String?,
        items = j['itemsDescription'] as String?,
        cod = _num(j['codAmount']),
        fee = _num(j['deliveryFee']),
        returnFee = _num(j['returnFee']),
        attempts = j['deliveryAttempts'] as int? ?? 0,
        lastFailure = j['lastFailureReason'] as String?,
        createdAt = _date(j['createdAt'])!,
        lastStatusAt = _date(j['lastStatusAt'])!,
        paidOut = j['paidOut'] as bool? ?? false;

  final String id, trackingNumber, status, customer, governorate;
  final String? phone, city, items, lastFailure;
  final double? cod, fee, returnFee;
  final int attempts;
  final DateTime createdAt, lastStatusAt;
  final bool paidOut;
}

// ── Office ───────────────────────────────────────────────────────────────────

class Metric {
  Metric(Json j)
      : value = _num(j['value']) ?? 0,
        previous = _num(j['previous']),
        changePercent = _num(j['changePercent']);

  final double value;
  final double? previous, changePercent;
}

class DayVolume {
  DayVolume(Json j)
      : day = DateTime.parse(j['day'] as String),
        booked = j['booked'] as int,
        delivered = j['delivered'] as int;

  final DateTime day;
  final int booked, delivered;
}

class Overview {
  Overview(Json j)
      : booked = Metric(j['booked'] as Json),
        delivered = Metric(j['delivered'] as Json),
        successRate = Metric(j['successRate'] as Json),
        returnRate = Metric(j['returnRate'] as Json),
        cod = Metric(j['codCollectedIqd'] as Json),
        daily = _list(j['daily'], DayVolume.new);

  final Metric booked, delivered, successRate, returnRate, cod;
  final List<DayVolume> daily;
}

class Alert {
  Alert(Json j)
      : id = j['id'] as String,
        kind = j['kind'] as String,
        severity = j['severity'] as String,
        shipmentId = j['shipmentId'] as String?,
        trackingNumber = j['trackingNumber'] as String?,
        transferNumber = j['transferNumber'] as String?,
        hub = j['hubCode'] as String?,
        hours = j['hours'] as int?,
        detail = j['detail'] as String?,
        raisedAt = _date(j['raisedAt'])!;

  final String id, kind, severity;
  final String? shipmentId, trackingNumber, transferNumber, hub, detail;
  final int? hours;
  final DateTime raisedAt;
}

class ShipmentRow {
  ShipmentRow(Json j)
      : id = j['id'] as String,
        trackingNumber = j['trackingNumber'] as String,
        status = j['status'] as String,
        customer = j['consigneeName'] as String,
        shop = j['merchantName'] as String?,
        governorate = j['destinationCode'] as String,
        cod = _num(j['codAmount']),
        shelf = j['warehouseBin'] as String?,
        courier = j['courierName'] as String?;

  final String id, trackingNumber, status, customer, governorate;
  final String? shop, shelf, courier;
  final double? cod;
}

class ShipmentEvent {
  ShipmentEvent(Json j)
      : status = j['status'] as String,
        occurredAt = _date(j['occurredAt'])!,
        location = j['location'] as String?,
        courier = j['courierName'] as String?,
        failure = j['failureReason'] as String?,
        notes = j['notes'] as String?;

  final String status;
  final DateTime occurredAt;
  final String? location, courier, failure, notes;
}

class ShipmentDetail {
  ShipmentDetail(Json j)
      : row = ShipmentRow(j),
        phone = j['consigneePhone'] as String?,
        city = j['destinationCity'] as String?,
        address = j['deliveryAddress'] as String?,
        landmark = j['landmark'] as String?,
        items = j['itemsDescription'] as String?,
        fee = _num(j['deliveryFee']),
        events = _list(j['events'], ShipmentEvent.new).reversed.toList();

  final ShipmentRow row;
  final String? phone, city, address, landmark, items;
  final double? fee;
  final List<ShipmentEvent> events;
}

class Hub {
  Hub(Json j)
      : code = j['code'] as String,
        tag = j['routingTag'] as String,
        arabicName = j['arabicName'] as String,
        englishName = j['englishName'] as String;

  final String code, tag, arabicName, englishName;

  String name(String language) => language == 'ar' ? arabicName : englishName;
}

class CourierItem {
  CourierItem(Json j)
      : code = j['code'] as String,
        label = j['displayLabel'] as String? ?? '${j['name']} (${j['code']})';

  final String code, label;
}

class ScanResult {
  ScanResult(Json j)
      : ok = j['ok'] as bool,
        trackingNumber = j['trackingNumber'] as String,
        message = j['message'] as String,
        status = j['status'] as String?;

  final bool ok;
  final String trackingNumber, message;
  final String? status;
}

class ShelfSummary {
  ShelfSummary(Json j)
      : id = j['id'] as String,
        number = j['number'] as String,
        hub = j['hubCode'] as String,
        bin = j['bin'] as String?,
        finished = j['finished'] as bool,
        expected = j['expected'] as int,
        found = j['found'] as int,
        missing = j['missing'] as int,
        wrongShelf = j['wrongShelf'] as int,
        notExpected = j['notExpected'] as int,
        pending = j['pending'] as int;

  final String id, number, hub;
  final String? bin;
  final bool finished;
  final int expected, found, missing, wrongShelf, notExpected, pending;
}

class ShelfLine {
  ShelfLine(Json j)
      : trackingNumber = j['trackingNumber'] as String,
        state = j['state'] as String,
        systemBin = j['systemBin'] as String?,
        systemStatus = j['systemStatus'] as String?;

  final String trackingNumber, state;
  final String? systemBin, systemStatus;
}

class Finance {
  Finance(Json j)
      : pending = _num(j['pendingCodToCollectIqd']) ?? 0,
        withCouriers = _num(j['codWithCouriersIqd']) ?? 0,
        collected = _num(j['unsettledCodCollectedIqd']) ?? 0,
        netPayable = _num(j['unsettledNetPayableIqd']) ?? 0,
        settled = _num(j['totalSettledNetPaidIqd']) ?? 0,
        balances = [
          for (final b in (j['sellerBalances'] as List? ?? const []).cast<Json>())
            (b['merchantName'] as String, _num(b['netPayableIqd']) ?? 0, b['deliveredUnsettledCount'] as int? ?? 0),
        ];

  final double pending, withCouriers, collected, netPayable, settled;
  final List<(String shop, double net, int orders)> balances;
}
