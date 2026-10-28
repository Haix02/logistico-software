import 'package:flutter/material.dart';

/// The strokes as SVG path data in a 300 × 150 box ("M x y L x y …"): the format the server accepts and the office
/// draws. Null when nothing was drawn.
String? signaturePath(List<List<Offset>> strokes, Size size) {
  if (strokes.every((s) => s.isEmpty) || size.isEmpty) return null;
  String n(double v) {
    final rounded = (v * 10).round() / 10;
    return rounded == rounded.roundToDouble() ? rounded.toInt().toString() : rounded.toString();
  }

  String point(Offset p) => '${n((p.dx * 300 / size.width).clamp(0, 300))} ${n((p.dy * 150 / size.height).clamp(0, 150))}';
  return strokes
      .where((s) => s.isNotEmpty)
      .map((s) => 'M${point(s.first)} L${(s.length > 1 ? s.skip(1) : s).map(point).join(' L')}')
      .join(' ');
}

class SignatureController extends ChangeNotifier {
  final List<List<Offset>> strokes = [];
  Size size = Size.zero;

  bool get isEmpty => strokes.every((s) => s.isEmpty);

  String? toPath() => signaturePath(strokes, size);

  void clear() {
    strokes.clear();
    notifyListeners();
  }

  void _start(Offset p) {
    strokes.add([p]);
    notifyListeners();
  }

  void _extend(Offset p) {
    strokes.last.add(p);
    notifyListeners();
  }
}

/// A box the receiver signs in with a finger.
class SignaturePad extends StatelessWidget {
  const SignaturePad({super.key, required this.controller, this.height = 150});

  final SignatureController controller;
  final double height;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return LayoutBuilder(builder: (context, box) {
      controller.size = Size(box.maxWidth, height);
      return Container(
        height: height,
        decoration: BoxDecoration(
          color: scheme.surface,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: scheme.outline, style: BorderStyle.solid),
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(8),
          child: GestureDetector(
            behavior: HitTestBehavior.opaque,
            onPanStart: (d) => controller._start(d.localPosition),
            onPanUpdate: (d) => controller._extend(d.localPosition),
            child: CustomPaint(painter: _Ink(controller, scheme.onSurface), size: Size.infinite),
          ),
        ),
      );
    });
  }
}

class _Ink extends CustomPainter {
  _Ink(this.controller, this.color) : super(repaint: controller);

  final SignatureController controller;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final pen = Paint()
      ..color = color
      ..strokeWidth = 2.4
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round
      ..style = PaintingStyle.stroke;
    for (final stroke in controller.strokes) {
      if (stroke.isEmpty) continue;
      final path = Path()..moveTo(stroke.first.dx, stroke.first.dy);
      for (final p in stroke.skip(1)) {
        path.lineTo(p.dx, p.dy);
      }
      if (stroke.length == 1) path.lineTo(stroke.first.dx + 0.1, stroke.first.dy);
      canvas.drawPath(path, pen);
    }
  }

  @override
  bool shouldRepaint(_Ink old) => false; // repaints through the controller
}
