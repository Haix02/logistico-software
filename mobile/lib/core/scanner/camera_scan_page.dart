import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';

import '../../l10n/gen/app_localizations.dart';

/// Scans one label with the camera (for phones without a built-in scanner) and returns its code.
class CameraScanPage extends StatefulWidget {
  const CameraScanPage({super.key});

  static Future<String?> open(BuildContext context) =>
      Navigator.of(context).push<String>(MaterialPageRoute(fullscreenDialog: true, builder: (_) => const CameraScanPage()));

  @override
  State<CameraScanPage> createState() => _CameraScanPageState();
}

class _CameraScanPageState extends State<CameraScanPage> {
  final _controller = MobileScannerController(
    formats: const [BarcodeFormat.code128, BarcodeFormat.qrCode, BarcodeFormat.code39, BarcodeFormat.ean13],
    detectionSpeed: DetectionSpeed.noDuplicates,
  );
  bool _done = false;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text(s.scanCamera),
        actions: [IconButton(onPressed: _controller.toggleTorch, icon: const Icon(Icons.flashlight_on_outlined))],
      ),
      body: Stack(
        fit: StackFit.expand,
        children: [
          MobileScanner(
            controller: _controller,
            onDetect: (capture) {
              final code = capture.barcodes.map((b) => b.rawValue).whereType<String>().firstOrNull;
              if (code == null || _done) return;
              _done = true;
              Navigator.of(context).pop(code);
            },
          ),
          // A frame to aim with: labels are wide.
          Center(
            child: Container(
              width: 290,
              height: 150,
              decoration: BoxDecoration(border: Border.all(color: Colors.white, width: 2), borderRadius: BorderRadius.circular(12)),
            ),
          ),
        ],
      ),
    );
  }
}
