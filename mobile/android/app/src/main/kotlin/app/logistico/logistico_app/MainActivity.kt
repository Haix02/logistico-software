package app.logistico.logistico_app

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodChannel

/**
 * The bridge to the built-in scanners of Android handhelds (Zebra DataWedge, Urovo, Sunmi, Newland, iData, Chainway…):
 * they hand each code to apps as a broadcast intent. The app tells us which broadcasts to hear (the company's settings);
 * every code goes to Dart on the "app.logistico/scanner" event channel. Scanner guns and keyboard mode need nothing here.
 */
class MainActivity : FlutterActivity() {
    private var sink: EventChannel.EventSink? = null
    private var receiver: BroadcastReceiver? = null
    private var extras: Map<String, String> = emptyMap()

    /** Extra names handhelds commonly use, tried when the configured one is missing. */
    private val commonExtras = listOf(
        "com.symbol.datawedge.data_string", "barcode_string", "data", "value", "SCAN_BARCODE1", "barcodeData", "scannerdata", "barcode",
    )

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        val messenger = flutterEngine.dartExecutor.binaryMessenger

        EventChannel(messenger, "app.logistico/scanner").setStreamHandler(object : EventChannel.StreamHandler {
            override fun onListen(arguments: Any?, events: EventChannel.EventSink?) {
                sink = events
            }

            override fun onCancel(arguments: Any?) {
                sink = null
            }
        })

        MethodChannel(messenger, "app.logistico/scanner/config").setMethodCallHandler { call, result ->
            if (call.method != "configure") return@setMethodCallHandler result.notImplemented()
            @Suppress("UNCHECKED_CAST")
            val pairs = (call.arguments as? List<Map<String, String>>).orEmpty()
            listen(pairs.mapNotNull { p -> p["action"]?.let { it to (p["extra"] ?: "") } })
            result.success(null)
        }
    }

    private fun listen(pairs: List<Pair<String, String>>) {
        receiver?.let { unregisterReceiver(it) }
        receiver = null
        if (pairs.isEmpty()) return
        extras = pairs.toMap()
        val filter = IntentFilter().apply { pairs.map { it.first }.distinct().forEach(::addAction) }
        val listener = object : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                val keys = listOfNotNull(extras[intent.action]) + commonExtras
                val code = keys.firstNotNullOfOrNull { key -> intent.getStringExtra(key)?.takeIf { it.isNotBlank() } } ?: return
                runOnUiThread { sink?.success(code.trim()) }
            }
        }
        // Scanner apps are other apps, so the receiver must accept their broadcasts.
        ContextCompat.registerReceiver(this, listener, filter, ContextCompat.RECEIVER_EXPORTED)
        receiver = listener
    }

    override fun onDestroy() {
        receiver?.let { unregisterReceiver(it) }
        receiver = null
        super.onDestroy()
    }
}
