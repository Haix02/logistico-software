import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/date_symbol_data_local.dart';

import 'app.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await Future.wait([initializeDateFormatting('ar'), initializeDateFormatting('en')]);
  await SystemChrome.setPreferredOrientations([DeviceOrientation.portraitUp]); // handhelds and phones are held upright
  runApp(const ProviderScope(child: LogisticoApp()));
}
