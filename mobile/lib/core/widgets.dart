import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../l10n/gen/app_localizations.dart';
import 'api/api_client.dart';
import 'format.dart';
import 'theme.dart';

/// Formatting in the app's language.
Fmt fmtOf(BuildContext context) => Fmt(Localizations.localeOf(context).languageCode);

/// The message to show for any failure.
String errorText(BuildContext context, Object error) {
  final s = S.of(context);
  if (error is ApiException) return error.message.isNotEmpty ? error.message : (error.status == null ? s.errorNetwork : s.errorUnexpected);
  return s.errorUnexpected;
}

void showError(BuildContext context, Object error) =>
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(errorText(context, error))));

void showDone(BuildContext context, String message) => ScaffoldMessenger.of(context)
    .showSnackBar(SnackBar(content: Text(message), backgroundColor: Theme.of(context).colorScheme.success));

/// Loading, failure (with a retry) or the data, the same way on every screen.
class AsyncBody<T> extends StatelessWidget {
  const AsyncBody({super.key, required this.value, required this.data, this.onRetry});

  final AsyncValue<T> value;
  final Widget Function(T data) data;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) => switch (value) {
        AsyncData(:final value) => data(value),
        AsyncError(:final error) when !value.isLoading => Center(
            child: Padding(
              padding: const EdgeInsets.all(24),
              child: Column(mainAxisSize: MainAxisSize.min, children: [
                Icon(Icons.cloud_off_outlined, size: 40, color: Theme.of(context).colorScheme.muted),
                const SizedBox(height: 12),
                Text(errorText(context, error), textAlign: TextAlign.center),
                if (onRetry != null) ...[
                  const SizedBox(height: 16),
                  OutlinedButton.icon(onPressed: onRetry, icon: const Icon(Icons.refresh), label: Text(S.of(context).retry)),
                ],
              ]),
            ),
          ),
        _ => value.hasValue ? data(value.requireValue) : const Center(child: CircularProgressIndicator()),
      };
}

class EmptyState extends StatelessWidget {
  const EmptyState({super.key, required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 56, horizontal: 32),
        child: Column(children: [
          Icon(icon, size: 44, color: Theme.of(context).colorScheme.muted),
          const SizedBox(height: 12),
          Text(text, textAlign: TextAlign.center, style: TextStyle(color: Theme.of(context).colorScheme.onSurfaceVariant)),
        ]),
      );
}

/// A shipment status as a coloured pill, named as on the web console.
class StatusPill extends StatelessWidget {
  const StatusPill(this.status, {super.key});

  final String status;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final color = switch (status) {
      'Delivered' => scheme.success,
      'DeliveryFailed' || 'Cancelled' || 'CustomsHold' => scheme.error,
      'ReturnInitiated' || 'ReturnInTransit' || 'ReturnAtHub' || 'Returned' => scheme.warning,
      'OutForDelivery' => scheme.secondary,
      _ => scheme.onSurfaceVariant,
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
      decoration: BoxDecoration(color: color.withValues(alpha: 0.12), borderRadius: BorderRadius.circular(99)),
      child: Text(fmtOf(context).domain('Status', status), style: TextStyle(color: color, fontSize: 12, fontWeight: FontWeight.w700)),
    );
  }
}

/// One figure with its label, as on the dashboard.
class Kpi extends StatelessWidget {
  const Kpi({super.key, required this.label, required this.value, required this.icon, this.color, this.footnote});

  final String label, value;
  final IconData icon;
  final Color? color;
  final String? footnote;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
          Row(children: [
            Expanded(child: Text(label, style: TextStyle(color: theme.colorScheme.onSurfaceVariant, fontSize: 13))),
            Icon(icon, size: 18, color: color ?? theme.colorScheme.onSurfaceVariant),
          ]),
          const SizedBox(height: 8),
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: AlignmentDirectional.centerStart,
            child: Text(value, style: theme.textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.w800, color: color)),
          ),
          if (footnote != null) ...[
            const SizedBox(height: 4),
            Text(footnote!, style: TextStyle(color: theme.colorScheme.onSurfaceVariant, fontSize: 12)),
          ],
        ]),
      ),
    );
  }
}

/// Figures two per row, any count.
class KpiGrid extends StatelessWidget {
  const KpiGrid({super.key, required this.children});

  final List<Widget> children;

  @override
  Widget build(BuildContext context) => LayoutBuilder(builder: (context, box) {
        final columns = box.maxWidth > 560 ? 3 : 2;
        final width = (box.maxWidth - (columns - 1) * 10) / columns;
        return Wrap(spacing: 10, runSpacing: 10, children: [for (final c in children) SizedBox(width: width, child: c)]);
      });
}

class SectionTitle extends StatelessWidget {
  const SectionTitle(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsetsDirectional.fromSTEB(4, 20, 4, 8),
        child: Text(text, style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800)),
      );
}

/// The company's message to everyone (Mobile app settings).
class AnnouncementBanner extends StatelessWidget {
  const AnnouncementBanner(this.text, {super.key});

  final String text;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Material(
      color: scheme.warning.withValues(alpha: 0.14),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 10),
        child: Row(children: [
          Icon(Icons.campaign_outlined, color: scheme.warning, size: 20),
          const SizedBox(width: 10),
          Expanded(child: Text(text, style: const TextStyle(fontWeight: FontWeight.w700))),
        ]),
      ),
    );
  }
}
