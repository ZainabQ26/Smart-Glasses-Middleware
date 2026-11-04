package com.example.smartglassesandroid

import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.ServerSocket
import java.net.Socket

/**
 * Relays 127.0.0.1:[listenPort] on the phone to the middleware PC on [targetPort].
 *
 * Why: Chromium/WebView only exposes navigator.mediaDevices (camera/mic) on a "secure context".
 * A LAN IP over http:// is NOT secure, but http://localhost IS. By loading the app from
 * http://localhost:5000 and forwarding the bytes to the PC, the camera works with no flags,
 * no certificates and no ADB tricks, on any network. The PC host list is re-checked for every
 * new connection, so switching Wi-Fi networks just works.
 */
class LocalForwarder(
    private val listenPort: Int,
    private val targetPort: Int,
    private val hostsProvider: () -> List<String>
) {
    @Volatile private var running = false
    @Volatile private var lastGoodHost: String? = null
    private var server: ServerSocket? = null

    fun start() {
        if (running) return
        running = true
        Thread {
            try {
                val s = ServerSocket()
                s.reuseAddress = true
                s.bind(InetSocketAddress(InetAddress.getByName("127.0.0.1"), listenPort))
                server = s
                while (running) {
                    val client = s.accept()
                    Thread { handle(client) }.start()
                }
            } catch (e: Exception) {
                if (running) e.printStackTrace()
            }
        }.start()
    }

    fun stop() {
        running = false
        try { server?.close() } catch (_: Exception) {}
    }

    private fun connectUpstream(): Socket? {
        val hosts = hostsProvider()
        val ordered = (listOfNotNull(lastGoodHost) + hosts).distinct()
        for (h in ordered) {
            try {
                val sock = Socket()
                sock.connect(InetSocketAddress(h, targetPort), 1500)
                lastGoodHost = h
                return sock
            } catch (_: Exception) {
            }
        }
        return null
    }

    private fun handle(client: Socket) {
        val upstream = connectUpstream()
        if (upstream == null) {
            try { client.close() } catch (_: Exception) {}
            return
        }
        val closeBoth = {
            try { client.close() } catch (_: Exception) {}
            try { upstream.close() } catch (_: Exception) {}
        }
        Thread { pipe(client, upstream, closeBoth) }.start()
        Thread { pipe(upstream, client, closeBoth) }.start()
    }

    private fun pipe(from: Socket, to: Socket, onDone: () -> Unit) {
        try {
            val input = from.getInputStream()
            val output = to.getOutputStream()
            val buf = ByteArray(16 * 1024)
            while (true) {
                val n = input.read(buf)
                if (n < 0) break
                output.write(buf, 0, n)
                output.flush()
            }
        } catch (_: Exception) {
        } finally {
            onDone()
        }
    }
}
