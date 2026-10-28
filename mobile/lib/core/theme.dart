import 'package:flutter/material.dart';

/// The web console's look (GitLab Pajamas tokens) for Material 3: a quiet grey frame, white panels, a near-black main
/// action, blue for links and focus, colour kept for status. The company's colour (Mobile app settings) is the accent.
abstract final class Palette {
  static const neutral0 = Color(0xFFFFFFFF), neutral10 = Color(0xFFF7F7F5), neutral50 = Color(0xFFECEBEA), neutral100 = Color(0xFFDCDBD9);
  static const neutral200 = Color(0xFFC1BFBE), neutral400 = Color(0xFF8A8888), neutral500 = Color(0xFF747273), neutral600 = Color(0xFF646163);
  static const neutral700 = Color(0xFF4D4B4E), neutral800 = Color(0xFF3A383D), neutral900 = Color(0xFF28272D), neutral950 = Color(0xFF18171D);
  static const blue = Color(0xFF1F75CB), green = Color(0xFF108548), red = Color(0xFFDD2B0E), orange = Color(0xFFC17D10);
}

/// Status colours that read in both themes.
extension StatusColors on ColorScheme {
  Color get success => brightness == Brightness.light ? Palette.green : const Color(0xFF52B87A);
  Color get warning => brightness == Brightness.light ? Palette.orange : const Color(0xFFE9BE74);
  Color get muted => brightness == Brightness.light ? Palette.neutral500 : Palette.neutral400;
}

Color? parseHex(String? hex) {
  if (hex == null || !RegExp(r'^#[0-9a-fA-F]{6}$').hasMatch(hex)) return null;
  return Color(int.parse('FF${hex.substring(1)}', radix: 16));
}

ThemeData buildTheme(Brightness brightness, {Color? accent}) {
  final light = brightness == Brightness.light;
  final brand = accent ?? Palette.blue;
  final scheme = ColorScheme(
    brightness: brightness,
    primary: light ? Palette.neutral800 : Palette.neutral50,
    onPrimary: light ? Palette.neutral0 : Palette.neutral950,
    secondary: brand,
    onSecondary: Palette.neutral0,
    tertiary: brand,
    onTertiary: Palette.neutral0,
    error: Palette.red,
    onError: Palette.neutral0,
    surface: light ? Palette.neutral0 : Palette.neutral900,
    onSurface: light ? Palette.neutral900 : Palette.neutral50,
    onSurfaceVariant: light ? Palette.neutral600 : Palette.neutral200,
    surfaceContainerLowest: light ? Palette.neutral0 : Palette.neutral950,
    surfaceContainerLow: light ? Palette.neutral10 : const Color(0xFF222127),
    surfaceContainer: light ? Palette.neutral50 : Palette.neutral950,
    surfaceContainerHigh: light ? Palette.neutral100 : Palette.neutral800,
    outline: light ? Palette.neutral400 : Palette.neutral500,
    outlineVariant: light ? Palette.neutral100 : Palette.neutral800,
  );
  final shape = RoundedRectangleBorder(borderRadius: BorderRadius.circular(8));
  return ThemeData(
    useMaterial3: true,
    colorScheme: scheme,
    fontFamily: 'Almarai',
    scaffoldBackgroundColor: scheme.surfaceContainer,
    visualDensity: VisualDensity.standard,
    appBarTheme: AppBarTheme(
      backgroundColor: scheme.surfaceContainer,
      foregroundColor: scheme.onSurface,
      elevation: 0,
      scrolledUnderElevation: 0,
      centerTitle: false,
      titleTextStyle: TextStyle(fontFamily: 'Almarai', fontSize: 20, fontWeight: FontWeight.w800, color: scheme.onSurface),
    ),
    cardTheme: CardThemeData(
      color: scheme.surface,
      elevation: 0,
      margin: EdgeInsets.zero,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10), side: BorderSide(color: scheme.outlineVariant)),
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: scheme.surface,
      indicatorColor: brand.withValues(alpha: 0.14),
      surfaceTintColor: Colors.transparent,
      iconTheme: WidgetStateProperty.resolveWith((s) => IconThemeData(color: s.contains(WidgetState.selected) ? brand : scheme.onSurfaceVariant)),
      labelTextStyle: WidgetStateProperty.resolveWith((s) => TextStyle(
            fontFamily: 'Almarai',
            fontSize: 12,
            fontWeight: s.contains(WidgetState.selected) ? FontWeight.w700 : FontWeight.w400,
            color: s.contains(WidgetState.selected) ? scheme.onSurface : scheme.onSurfaceVariant,
          )),
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(minimumSize: const Size(0, 48), shape: shape, textStyle: const TextStyle(fontFamily: 'Almarai', fontWeight: FontWeight.w700, fontSize: 15)),
    ),
    outlinedButtonTheme: OutlinedButtonThemeData(
      style: OutlinedButton.styleFrom(
        minimumSize: const Size(0, 48),
        shape: shape,
        foregroundColor: scheme.onSurface,
        side: BorderSide(color: scheme.outline),
        textStyle: const TextStyle(fontFamily: 'Almarai', fontWeight: FontWeight.w700, fontSize: 15),
      ),
    ),
    textButtonTheme: TextButtonThemeData(style: TextButton.styleFrom(foregroundColor: brand)),
    inputDecorationTheme: InputDecorationTheme(
      filled: true,
      fillColor: scheme.surface,
      border: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: scheme.outline)),
      enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: scheme.outline)),
      focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(8), borderSide: BorderSide(color: brand, width: 2)),
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
    ),
    chipTheme: ChipThemeData(shape: StadiumBorder(side: BorderSide(color: scheme.outlineVariant)), labelStyle: const TextStyle(fontFamily: 'Almarai')),
    listTileTheme: ListTileThemeData(iconColor: scheme.onSurfaceVariant),
    dividerTheme: DividerThemeData(color: scheme.outlineVariant, space: 1),
    snackBarTheme: SnackBarThemeData(behavior: SnackBarBehavior.floating, shape: shape),
    bottomSheetTheme: BottomSheetThemeData(backgroundColor: scheme.surface, showDragHandle: true),
  );
}
