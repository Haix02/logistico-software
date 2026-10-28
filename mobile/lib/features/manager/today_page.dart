import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/data.dart';
import '../../core/models.dart';
import '../../core/theme.dart';
import '../../core/widgets.dart';
import '../../l10n/gen/app_localizations.dart';

/// The day's (or period's) figures, compared like for like with the same stretch before (as on the web dashboard).
class TodayPage extends ConsumerStatefulWidget {
  const TodayPage({super.key});

  @override
  ConsumerState<TodayPage> createState() => _TodayPageState();
}

class _TodayPageState extends ConsumerState<TodayPage> {
  int _days = 1;

  @override
  Widget build(BuildContext context) {
    final s = S.of(context), f = fmtOf(context), scheme = Theme.of(context).colorScheme;
    String? change(Metric m) => m.changePercent == null ? null : s.vsBefore('${m.changePercent! > 0 ? '+' : ''}${m.changePercent!.toStringAsFixed(1)}');
    return RefreshIndicator(
      onRefresh: () => ref.refresh(overviewProvider(_days).future),
      child: ListView(padding: const EdgeInsets.all(12), children: [
        SegmentedButton<int>(
          segments: [ButtonSegment(value: 1, label: Text(s.periodToday)), ButtonSegment(value: 7, label: Text(s.period7)), ButtonSegment(value: 30, label: Text(s.period30))],
          selected: {_days},
          onSelectionChanged: (v) => setState(() => _days = v.first),
        ),
        const SizedBox(height: 12),
        AsyncBody(
          value: ref.watch(overviewProvider(_days)),
          onRetry: () => ref.invalidate(overviewProvider(_days)),
          data: (o) => Column(children: [
            KpiGrid(children: [
              Kpi(label: s.booked1, value: f.number(o.booked.value), icon: Icons.add_shopping_cart, footnote: change(o.booked)),
              Kpi(label: s.viewDelivered, value: f.number(o.delivered.value), icon: Icons.check_circle_outline, color: scheme.success, footnote: change(o.delivered)),
              Kpi(label: s.successRate, value: '${o.successRate.value.toStringAsFixed(1)}%', icon: Icons.verified_outlined),
              Kpi(label: s.returnRate, value: '${o.returnRate.value.toStringAsFixed(1)}%', icon: Icons.undo, color: o.returnRate.value > 10 ? scheme.warning : null),
              Kpi(label: s.codCollected, value: f.compactMoney(o.cod.value), icon: Icons.payments_outlined, footnote: change(o.cod)),
            ]),
            if (o.daily.length > 1) ...[const SizedBox(height: 12), _DailyChart(o.daily)],
          ]),
        ),
      ]),
    );
  }
}

/// Booked and delivered per day as simple paired bars (no chart library needed).
class _DailyChart extends StatelessWidget {
  const _DailyChart(this.days);

  final List<DayVolume> days;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final shown = days.length > 14 ? days.sublist(days.length - 14) : days;
    final top = shown.fold<int>(1, (m, d) => [m, d.booked, d.delivered].reduce((a, b) => a > b ? a : b));
    return Card(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 16, 12, 10),
        child: SizedBox(
          height: 140,
          child: Row(crossAxisAlignment: CrossAxisAlignment.end, children: [
            for (final d in shown)
              Expanded(
                child: Column(mainAxisAlignment: MainAxisAlignment.end, children: [
                  Row(crossAxisAlignment: CrossAxisAlignment.end, mainAxisAlignment: MainAxisAlignment.center, children: [
                    _bar(d.booked / top, scheme.secondary.withValues(alpha: 0.35)),
                    _bar(d.delivered / top, scheme.success),
                  ]),
                  const SizedBox(height: 4),
                  Text('${d.day.day}', style: TextStyle(fontSize: 10, color: scheme.onSurfaceVariant)),
                ]),
              ),
          ]),
        ),
      ),
    );
  }

  static Widget _bar(double share, Color color) =>
      Container(width: 6, height: 4 + share * 100, margin: const EdgeInsets.symmetric(horizontal: 1), decoration: BoxDecoration(color: color, borderRadius: BorderRadius.circular(3)));
}
