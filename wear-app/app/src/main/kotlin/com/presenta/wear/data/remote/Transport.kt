package com.presenta.wear.data.remote

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.Response
import okhttp3.WebSocket
import okhttp3.WebSocketListener
import java.util.concurrent.TimeUnit

/** A connected (or connecting) text WebSocket. */
interface WatchSocket {
    /** Queues [text]; false when the socket is closed. */
    fun send(text: String): Boolean

    /** Starts a clean close. */
    fun close(code: Int, reason: String)

    /** Drops the connection immediately. */
    fun cancel()
}

/** Callbacks of a socket. They may arrive on any thread. */
interface SocketListener {
    fun onOpen()

    fun onMessage(text: String)

    fun onClosed(code: Int, reason: String)

    fun onFailure(error: Throwable)
}

/** Creates sockets. The real one uses OkHttp; tests use a scripted fake. */
fun interface SocketFactory {
    fun connect(url: String, listener: SocketListener): WatchSocket
}

/** OkHttp WebSocket transport. Heartbeats are done by the protocol (`ping`/`pong`), not by OkHttp. */
class OkHttpSocketFactory(
    private val client: OkHttpClient = defaultClient(),
) : SocketFactory {
    override fun connect(url: String, listener: SocketListener): WatchSocket {
        val request = Request.Builder().url(url).build()
        val socket = client.newWebSocket(
            request,
            object : WebSocketListener() {
                override fun onOpen(webSocket: WebSocket, response: Response) = listener.onOpen()

                override fun onMessage(webSocket: WebSocket, text: String) = listener.onMessage(text)

                override fun onClosing(webSocket: WebSocket, code: Int, reason: String) {
                    webSocket.close(NORMAL_CLOSURE, null)
                    listener.onClosed(code, reason)
                }

                override fun onClosed(webSocket: WebSocket, code: Int, reason: String) = listener.onClosed(code, reason)

                override fun onFailure(webSocket: WebSocket, t: Throwable, response: Response?) = listener.onFailure(t)
            },
        )
        return object : WatchSocket {
            override fun send(text: String): Boolean = socket.send(text)

            override fun close(code: Int, reason: String) {
                socket.close(code, reason)
            }

            override fun cancel() = socket.cancel()
        }
    }

    companion object {
        const val NORMAL_CLOSURE = 1000

        fun defaultClient(): OkHttpClient = OkHttpClient.Builder()
            .connectTimeout(4, TimeUnit.SECONDS)
            .readTimeout(0, TimeUnit.MILLISECONDS)
            .pingInterval(0, TimeUnit.MILLISECONDS)
            .build()
    }
}
