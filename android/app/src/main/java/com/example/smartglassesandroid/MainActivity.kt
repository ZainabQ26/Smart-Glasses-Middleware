package com.example.smartglassesandroid

import android.Manifest
import android.graphics.Bitmap
import android.util.Base64
import java.io.ByteArrayOutputStream
import android.app.Activity
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.media.AudioFormat
import android.media.AudioRecord
import android.media.MediaRecorder
import android.os.Bundle
import android.print.PrintAttributes
import android.print.PrintManager
import android.speech.RecognizerIntent
import android.view.View
import android.webkit.JavascriptInterface
import android.webkit.PermissionRequest
import android.webkit.WebChromeClient
import android.webkit.WebView
import android.webkit.WebViewClient
import android.widget.Button
import android.widget.ImageButton
import android.widget.Toast
import androidx.activity.OnBackPressedCallback
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import java.util.Locale

class MainActivity : AppCompatActivity() {
    lateinit var webView: WebView
    private lateinit var btnAdmin: Button
    private lateinit var btnCustom: Button
    private lateinit var btnCannDir: Button
    private lateinit var btnVoiceNav: ImageButton

    // The page is ALWAYS loaded from http://localhost:5000, which Chromium treats as a secure
    // context (so navigator.mediaDevices / camera works). LocalForwarder relays that port to the PC.
    private val localBase = "http://localhost:5000"
    private val localUrl = "$localBase/"
    private var currentUrlToLoad = localUrl
    // Optional hints only. The real PC address is auto-discovered on whatever Wi-Fi the phone is on (see discoverLanHosts).
    private val defaultPcHosts = ""
    private var localForwarder: LocalForwarder? = null
    private var pendingWebPermission: PermissionRequest? = null

    private var printWebView: WebView? = null
    private var printHtmlBuilder = StringBuilder()
    private var pendingPrintJobName = "SmartGlasses_Document"

    @Volatile private var isRecording = false
    private var audioRecord: AudioRecord? = null
    private var recordingThread: Thread? = null

    private val requestPermissionLauncher = registerForActivityResult(
        ActivityResultContracts.RequestMultiplePermissions()
    ) { _ ->
        // Now that Android permissions are decided, answer any WebView request that was waiting.
        pendingWebPermission?.let { req ->
            pendingWebPermission = null
            runOnUiThread { req.grant(req.resources) }
        }
    }

        private val takePictureLauncher = registerForActivityResult(
        ActivityResultContracts.TakePicturePreview()
    ) { bitmap ->
        if (bitmap != null) {
            try {
                val outputStream = ByteArrayOutputStream()
                bitmap.compress(Bitmap.CompressFormat.JPEG, 92, outputStream)
                val byteArray = outputStream.toByteArray()
                val base64 = Base64.encodeToString(byteArray, Base64.NO_WRAP)
                webView.evaluateJavascript("window.onNativePhotoCaptured && window.onNativePhotoCaptured('data:image/jpeg;base64,$base64')", null)
            } catch (e: Exception) {
                e.printStackTrace()
                webView.evaluateJavascript("window.onNativePhotoError && window.onNativePhotoError('Failed to process image: ${e.message}')", null)
            }
        } else {
            webView.evaluateJavascript("window.onNativePhotoError && window.onNativePhotoError('Photo capture cancelled')", null)
        }
    }

    private val voiceResultLauncher = registerForActivityResult(
        ActivityResultContracts.StartActivityForResult()
    ) { result ->
        if (result.resultCode == Activity.RESULT_OK) {
            val matches = result.data?.getStringArrayListExtra(RecognizerIntent.EXTRA_RESULTS)
            if (!matches.isNullOrEmpty()) {
                val spokenText = matches[0].lowercase()
                handleVoiceNavigation(spokenText)
            }
        }
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContentView(R.layout.activity_main)
        
        webView = findViewById(R.id.webview)
        btnAdmin = findViewById(R.id.btn_admin)
        btnCustom = findViewById(R.id.btn_custom)
        btnCannDir = findViewById(R.id.btn_cann_dir)
        btnVoiceNav = findViewById(R.id.btn_voice_nav)

        val mainHandler = android.os.Handler(android.os.Looper.getMainLooper())
        var isRetrying = false

        webView.webViewClient = object : WebViewClient() {
            override fun onPageFinished(view: WebView?, url: String?) {
                super.onPageFinished(view, url)
                isRetrying = false
                if (url?.contains("cann-dir.psu.edu") == true || url?.contains(":5000") == true) {
                    btnVoiceNav.visibility = View.VISIBLE
                } else {
                    btnVoiceNav.visibility = View.GONE
                }
            }

            override fun onReceivedError(view: WebView?, request: android.webkit.WebResourceRequest?, error: android.webkit.WebResourceError?) {
                super.onReceivedError(view, request, error)
                // Main page failed (PC unreachable on this network): keep retrying; the forwarder
                // re-checks every known PC address on each new connection.
                if (request?.isForMainFrame == true && !isRetrying && request.url?.host == "localhost") {
                    isRetrying = true
                    mainHandler.postDelayed({
                        isRetrying = false
                        webView.loadUrl(currentUrlToLoad)
                    }, 2000)
                }
            }
        }
        
        webView.webChromeClient = object : WebChromeClient() {
            override fun onPermissionRequest(request: PermissionRequest) {
                runOnUiThread {
                    val needsCamera = request.resources.contains(PermissionRequest.RESOURCE_VIDEO_CAPTURE)
                    val needsMic = request.resources.contains(PermissionRequest.RESOURCE_AUDIO_CAPTURE)
                    val missing = mutableListOf<String>()
                    if (needsCamera && ContextCompat.checkSelfPermission(this@MainActivity, Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) missing += Manifest.permission.CAMERA
                    if (needsMic && ContextCompat.checkSelfPermission(this@MainActivity, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) missing += Manifest.permission.RECORD_AUDIO
                    if (missing.isEmpty()) {
                        request.grant(request.resources)
                    } else {
                        pendingWebPermission = request
                        requestPermissionLauncher.launch(missing.toTypedArray())
                    }
                }
            }
        }

        val settings = webView.settings
        settings.javaScriptEnabled = true
        settings.domStorageEnabled = true
        settings.mediaPlaybackRequiresUserGesture = false
        settings.setSupportZoom(true)
        settings.builtInZoomControls = true
        settings.displayZoomControls = false
        settings.cacheMode = android.webkit.WebSettings.LOAD_NO_CACHE
        webView.clearCache(true)
        
        webView.addJavascriptInterface(WebAppInterface(), "AndroidMic")

        // PC addresses to try (comma-separated). Override without rebuilding:
        //   adb shell am start -n com.example.smartglassesandroid/.MainActivity --es pc_hosts "10.0.0.5,192.168.1.20"
        val prefs = getSharedPreferences("smartglasses", Context.MODE_PRIVATE)
        intent?.getStringExtra("pc_hosts")?.takeIf { it.isNotBlank() }?.let {
            prefs.edit().putString("pc_hosts", it).apply()
        }
        localForwarder = LocalForwarder(5000, 5000) {
            val configured = (prefs.getString("pc_hosts", null) ?: defaultPcHosts)
                .split(",").map { it.trim() }.filter { it.isNotEmpty() }
            (configured + discoverLanHosts(5000)).distinct()
        }.also { it.start() }

        webView.loadUrl(currentUrlToLoad)

        val getBaseUrl = { localBase }

        btnAdmin.setOnClickListener {
            webView.loadUrl("${getBaseUrl()}/admin.html")
        }

        btnCustom.setOnClickListener {
            webView.loadUrl("${getBaseUrl()}/")
        }

        btnCannDir.setOnClickListener {
            webView.loadUrl("https://cann-dir.psu.edu/drug-research/select")
        }

        btnVoiceNav.setOnClickListener {
            startVoiceRecognition()
        }

        val permissions = arrayOf(Manifest.permission.RECORD_AUDIO, Manifest.permission.MODIFY_AUDIO_SETTINGS, Manifest.permission.CAMERA)
        if (permissions.any { ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED }) {
            requestPermissionLauncher.launch(permissions)
        }

        ViewCompat.setOnApplyWindowInsetsListener(findViewById(R.id.main)) { v, insets ->
            val systemBars = insets.getInsets(WindowInsetsCompat.Type.systemBars())
            v.setPadding(systemBars.left, systemBars.top, systemBars.right, systemBars.bottom)
            insets
        }

        onBackPressedDispatcher.addCallback(this, object : OnBackPressedCallback(true) {
            override fun handleOnBackPressed() {
                if (webView.canGoBack()) webView.goBack() else {
                    isEnabled = false
                    onBackPressedDispatcher.onBackPressed()
                }
            }
        })
    }

    private fun executePrintHtml(htmlContent: String, jobName: String) {
        try {
            Toast.makeText(this, "Preparing print dialog...", Toast.LENGTH_SHORT).show()
            val tempWebView = WebView(this)
            this.printWebView = tempWebView

            tempWebView.settings.javaScriptEnabled = true
            tempWebView.settings.domStorageEnabled = true

            tempWebView.webViewClient = object : WebViewClient() {
                private var isPrinted = false

                override fun onPageFinished(view: WebView?, url: String?) {
                    super.onPageFinished(view, url)
                    if (isPrinted) return
                    isPrinted = true
                    try {
                        val printManager = getSystemService(Context.PRINT_SERVICE) as PrintManager
                        val printAdapter = tempWebView.createPrintDocumentAdapter(jobName)
                        printManager.print(jobName, printAdapter, PrintAttributes.Builder().build())
                    } catch (e: Exception) {
                        e.printStackTrace()
                        Toast.makeText(this@MainActivity, "Print error: ${e.message}", Toast.LENGTH_LONG).show()
                    }
                }
            }

            val baseUrl = "$localBase/"

            tempWebView.loadDataWithBaseURL(baseUrl, htmlContent, "text/html", "UTF-8", null)
        } catch (e: Exception) {
            e.printStackTrace()
            Toast.makeText(this, "Print error: ${e.message}", Toast.LENGTH_LONG).show()
        }
    }

    inner class WebAppInterface {
        @JavascriptInterface
        fun startPrintHtml(jobName: String?) {
            pendingPrintJobName = if (!jobName.isNullOrBlank()) jobName else "SmartGlasses_Document"
            printHtmlBuilder = StringBuilder()
        }

        @JavascriptInterface
        fun appendPrintHtml(chunk: String?) {
            if (chunk != null) {
                printHtmlBuilder.append(chunk)
            }
        }

        @JavascriptInterface
        fun finishPrintHtml() {
            val fullHtml = printHtmlBuilder.toString()
            printHtmlBuilder = StringBuilder()
            runOnUiThread {
                executePrintHtml(fullHtml, pendingPrintJobName)
            }
        }

        @JavascriptInterface
        fun printHtml(htmlContent: String) {
            printHtml(htmlContent, "SmartGlasses_Document")
        }

        @JavascriptInterface
        fun printHtml(htmlContent: String, jobName: String?) {
            val safeJobName = if (!jobName.isNullOrBlank()) jobName else "SmartGlasses_Document"
            runOnUiThread {
                executePrintHtml(htmlContent, safeJobName)
            }
        }

        @JavascriptInterface
        fun printDocument() {
            runOnUiThread {
                try {
                    val printManager = getSystemService(Context.PRINT_SERVICE) as PrintManager
                    val printAdapter = webView.createPrintDocumentAdapter("SmartGlasses_Document")
                    printManager.print("SmartGlasses_Document", printAdapter, PrintAttributes.Builder().build())
                } catch (e: Exception) {
                    e.printStackTrace()
                    Toast.makeText(this@MainActivity, "Print error: ${e.message}", Toast.LENGTH_LONG).show()
                }
            }
        }

        @JavascriptInterface
        fun startNativeMic(): Boolean {
            if (isRecording) return true
            if (ContextCompat.checkSelfPermission(this@MainActivity, Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
                // Not granted yet: tell JS so it falls back to the WebView mic (which triggers the permission prompt).
                return false
            }

            val sampleRate = 16000
            val channelConfig = AudioFormat.CHANNEL_IN_MONO
            val audioFormat = AudioFormat.ENCODING_PCM_16BIT
            val minBufSize = AudioRecord.getMinBufferSize(sampleRate, channelConfig, audioFormat)
            val bufferSize = Math.max(minBufSize, 4096)

            // VOICE_RECOGNITION is untuned and is routed to USB/headset mics more reliably than
            // AudioSource.MIC, which some Beam/glasses HALs deliver as all-zero samples.
            val sources = intArrayOf(MediaRecorder.AudioSource.VOICE_RECOGNITION, MediaRecorder.AudioSource.MIC)
            var rec: AudioRecord? = null
            for (src in sources) {
                try {
                    val candidate = AudioRecord(src, sampleRate, channelConfig, audioFormat, bufferSize)
                    if (candidate.state == AudioRecord.STATE_INITIALIZED) {
                        candidate.startRecording()
                        if (candidate.recordingState == AudioRecord.RECORDSTATE_RECORDING) {
                            rec = candidate
                            break
                        }
                    }
                    candidate.release()
                } catch (e: Exception) {
                    e.printStackTrace()
                }
            }
            if (rec == null) return false

            audioRecord = rec
            isRecording = true

            recordingThread = Thread {
                val buffer = ShortArray(1024)
                while (isRecording) {
                    val read = audioRecord?.read(buffer, 0, buffer.size) ?: -1
                    if (read > 0) {
                        val floatSb = StringBuilder(read * 10)
                        floatSb.append("[")
                        for (i in 0 until read) {
                            floatSb.append(buffer[i] / 32768.0f)
                            if (i < read - 1) floatSb.append(",")
                        }
                        floatSb.append("]")
                        val jsonArray = floatSb.toString()
                        runOnUiThread {
                            webView.evaluateJavascript("window.onNativeAudioChunk && window.onNativeAudioChunk($jsonArray)", null)
                        }
                    } else if (read < 0) {
                        // ERROR_DEAD_OBJECT / ERROR_INVALID_OPERATION: stop; the JS watchdog falls back to the web mic.
                        isRecording = false
                    }
                }
            }
            recordingThread?.start()
            return true
        }

        @JavascriptInterface
        fun takePicture() {
            runOnUiThread {
                webView.evaluateJavascript("if (window.__triggerScanDocument) { window.__triggerScanDocument(); }", null)
            }
        }

        @JavascriptInterface
        fun stopNativeMic() {
            isRecording = false
            try {
                audioRecord?.stop()
                audioRecord?.release()
            } catch (e: Exception) {}
            audioRecord = null
        }
    }

    // ---- LAN auto-discovery: find the middleware PC on the phone's current /24 (works on any Wi-Fi) ----
    @Volatile private var discoveredHosts: List<String> = emptyList()
    @Volatile private var lastDiscoveryAt = 0L

    private fun discoverLanHosts(port: Int): List<String> {
        val now = System.currentTimeMillis()
        if (now - lastDiscoveryAt < 20_000) return discoveredHosts
        lastDiscoveryAt = now
        try {
            val prefixes = mutableSetOf<String>()
            java.net.NetworkInterface.getNetworkInterfaces()?.toList().orEmpty().forEach { nif ->
                if (!nif.isUp || nif.isLoopback) return@forEach
                nif.interfaceAddresses.forEach { ia ->
                    val a = ia.address
                    if (a is java.net.Inet4Address && a.isSiteLocalAddress) {
                        prefixes += a.hostAddress!!.substringBeforeLast('.')
                    }
                }
            }
            val found = java.util.concurrent.ConcurrentLinkedQueue<String>()
            val pool = java.util.concurrent.Executors.newFixedThreadPool(48)
            for (p in prefixes) for (i in 1..254) {
                val host = "$p.$i"
                pool.execute {
                    try {
                        java.net.Socket().use { s ->
                            s.connect(java.net.InetSocketAddress(host, port), 350)
                            found += host
                        }
                    } catch (_: Exception) {}
                }
            }
            pool.shutdown()
            pool.awaitTermination(6, java.util.concurrent.TimeUnit.SECONDS)
            if (found.isNotEmpty()) discoveredHosts = found.toList()
        } catch (e: Exception) { e.printStackTrace() }
        return discoveredHosts
    }

    override fun onDestroy() {        super.onDestroy()
        localForwarder?.stop()
        isRecording = false
        try {
            audioRecord?.stop()
            audioRecord?.release()
        } catch (e: Exception) {}
    }

    private fun startVoiceRecognition() {
        val intent = Intent(RecognizerIntent.ACTION_RECOGNIZE_SPEECH).apply {
            putExtra(RecognizerIntent.EXTRA_LANGUAGE_MODEL, RecognizerIntent.LANGUAGE_MODEL_FREE_FORM)
            putExtra(RecognizerIntent.EXTRA_LANGUAGE, Locale.getDefault())
            putExtra(RecognizerIntent.EXTRA_PROMPT, "Say 'admin', 'custom', 'drug research', or 'find [word]'")
        }
        try {
            voiceResultLauncher.launch(intent)
        } catch (e: Exception) {
            Toast.makeText(this, "Voice recognition not supported", Toast.LENGTH_SHORT).show()
        }
    }

    private fun handleVoiceNavigation(command: String) {
        val base = localBase
        when {
            command.contains("take picture") || command.contains("take a picture") || command.contains("capture") -> {
                webView.evaluateJavascript("if (window.__triggerScanDocument) { window.__triggerScanDocument(); }", null)
                Toast.makeText(this, "Opening glasses scanner...", Toast.LENGTH_SHORT).show()
            }
            command.contains("print highlighted") || command.contains("print highlight") -> {
                webView.evaluateJavascript("if (window.__triggerPrintHighlightedPages) { window.__triggerPrintHighlightedPages(); }", null)
                Toast.makeText(this, "Printing highlighted pages...", Toast.LENGTH_SHORT).show()
            }
            command.contains("print document") || command == "print" || command.contains("print pdf") -> {
                webView.evaluateJavascript("if (window.__triggerPrintDocument) { window.__triggerPrintDocument(); }", null)
                Toast.makeText(this, "Printing document...", Toast.LENGTH_SHORT).show()
            }
            command.contains("admin") -> {
                webView.loadUrl("$base/admin.html")
                Toast.makeText(this, "Navigating to Admin", Toast.LENGTH_SHORT).show()
            }
            command.contains("custom") -> {
                webView.loadUrl("$base/")
                Toast.makeText(this, "Navigating to Custom Search", Toast.LENGTH_SHORT).show()
            }
            command.contains("drug research") || (command.contains("drug") && command.contains("research")) -> {
                webView.loadUrl("https://cann-dir.psu.edu/drug-research/select")
                Toast.makeText(this, "Navigating to Drug Research", Toast.LENGTH_SHORT).show()
            }
            command.contains("find the word") || command.startsWith("find ") -> {
                val searchPart = if (command.contains("find the word")) {
                    command.substringAfter("find the word").trim()
                } else {
                    command.substringAfter("find ").trim()
                }
                
                if (searchPart.contains(" and ")) {
                    val words = searchPart.split(" and ")
                    if (words.size >= 2) {
                        highlightWords(words[0].trim(), words[1].trim())
                    }
                } else {
                    highlightWords(searchPart, "")
                }
            }
            command.contains("clear") -> {
                webView.evaluateJavascript("clearHighlights();", null)
                Toast.makeText(this, "Highlights cleared", Toast.LENGTH_SHORT).show()
            }
            ((command.contains("1") || command.contains("one")) && (command.contains("a") || command.contains("b") || command.contains("bee"))) ||
            ((command.contains("2") || command.contains("two")) && command.contains("a")) -> {
                webView.loadUrl("https://cann-dir.psu.edu/drug-research/search")
                val category = if (command.contains("1") || command.contains("one")) "THC" else "CBD"
                Toast.makeText(this, "Navigating to $category Search", Toast.LENGTH_SHORT).show()
            }
            (command.contains("3") || command.contains("three")) && 
            (command.contains("b") || command.contains("bee")) -> {
                webView.loadUrl("https://cann-dir.psu.edu/drug-research/delta9-thccbd-medical-cannabis")
                Toast.makeText(this, "Navigating to THC/CBD Medical Cannabis", Toast.LENGTH_SHORT).show()
            }
            else -> {
                Toast.makeText(this, "Heard: $command", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun highlightWords(word1: String, word2: String) {
        if (word1.isEmpty()) return
        
        val js = """
            (function() {
                function escapeRegExp(string) {
                    return string.replace(/[.*+?^${'$'}${'$'}{}()|[\]\\]/g, '\${'$'}&');
                }

                function highlight(word, color) {
                    if (!word || word.length < 1) return;
                    const regex = new RegExp('(' + escapeRegExp(word) + ')', 'gi');
                    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null, false);
                    let node;
                    const nodes = [];
                    while (node = walker.nextNode()) {
                        if (node.parentElement.tagName !== 'SCRIPT' && 
                            node.parentElement.tagName !== 'STYLE' && 
                            node.parentElement.tagName !== 'MARK') {
                            nodes.push(node);
                        }
                    }
                    nodes.forEach(textNode => {
                        if (regex.test(textNode.nodeValue)) {
                            const span = document.createElement('span');
                            span.innerHTML = textNode.nodeValue.replace(regex, '<mark class="my-highlight" style="background-color: ' + color + '; color: black;">\${'$'}1</mark>');
                            textNode.parentNode.replaceChild(span, textNode);
                        }
                    });
                }
                
                window.clearHighlights = function() {
                    const marks = document.querySelectorAll('.my-highlight');
                    marks.forEach(mark => {
                        const parent = mark.parentNode;
                        const text = document.createTextNode(mark.textContent);
                        parent.replaceChild(text, mark);
                        parent.normalize();
                    });
                };

                window.clearHighlights();
                highlight('$word1', 'yellow');
                if ('$word2' !== '') {
                    highlight('$word2', 'cyan');
                }
            })();
        """.trimIndent()
        
        webView.evaluateJavascript(js, null)
        val msg = if (word2.isNotEmpty()) "Finding: $word1 and $word2" else "Finding: $word1"
        Toast.makeText(this, msg, Toast.LENGTH_SHORT).show()
    }
}
