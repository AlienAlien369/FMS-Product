import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';

/** Payload the server pushes on every committed notification row. */
export interface NotificationPush {
  eventType: string;
  severity: number;
  title: string;
  message: string;
  createdAt: string;
}

const HUB_PATH = '/hubs/notifications';

/** Same base-URL resolution as lib/api.ts — absolute in prod, same-origin in dev (Vite proxies /hubs). */
function hubUrl(): string {
  const apiUrl = import.meta.env.VITE_API_URL;
  return apiUrl ? `${apiUrl.replace(/\/+$/, '')}${HUB_PATH}` : HUB_PATH;
}

let connection: HubConnection | null = null;
let connecting: Promise<void> | null = null;
let retryTimer: ReturnType<typeof setTimeout> | null = null;
let retryDelayMs = 5_000;

function getConnection(): HubConnection {
  let conn = connection;
  if (!conn) {
    conn = new HubConnectionBuilder()
      .withUrl(hubUrl(), {
        // Browsers can't set headers on WebSockets; SignalR's convention is to
        // send the JWT as the access_token query parameter (server reads it).
        accessTokenFactory: () => localStorage.getItem('token') ?? '',
      })
      .withAutomaticReconnect([0, 2_000, 5_000, 10_000, 30_000])
      .configureLogging(LogLevel.Warning)
      .build();
    connection = conn;
  }
  return conn;
}

function scheduleRetry() {
  if (retryTimer) return;
  retryTimer = setTimeout(() => {
    retryTimer = null;
    void start();
  }, retryDelayMs);
  retryDelayMs = Math.min(retryDelayMs * 2, 30_000);
}

/** Connect idempotently. Automatic reconnect covers drops after a successful
 * connect (deploys, network blips); this retry loop covers a failed first
 * handshake (e.g. token set a tick after mount). */
export function start(): Promise<void> {
  const conn = getConnection();
  if (conn.state === HubConnectionState.Connected) return Promise.resolve();
  if (connecting) return connecting;
  connecting = (async () => {
    try {
      await conn.start();
      retryDelayMs = 5_000; // reset backoff on success
    } catch (err) {
      // Aborted by teardown/remount (stop() during a StrictMode remount) is
      // not an outage: the fresh connection from the new mount handles itself.
      // Only retry when THIS connection is still the live one.
      if (connection === conn) {
        console.warn('[notifications] socket connect failed, will retry', err);
        scheduleRetry();
      }
    } finally {
      connecting = null;
    }
  })();
  return connecting;
}

/** Subscribe to instant pushes. Starts the connection if needed; returns an unsubscribe fn. */
export function onNotification(handler: (push: NotificationPush) => void): () => void {
  const conn = getConnection();
  conn.on('notification.new', handler);
  void start();
  return () => conn.off('notification.new', handler);
}

/** Fires whenever the channel (re)establishes — catch up on anything missed while down. */
export function onChannelUp(handler: () => void): () => void {
  const conn = getConnection();
  let disposed = false;
  const guarded = () => {
    if (!disposed) handler();
  };
  conn.on('connected', guarded);
  conn.onreconnected(guarded);
  void start();
  return () => {
    disposed = true;
    conn.off('connected', guarded);
  };
}

/** Tear down on logout. A fresh connection is built on the next start(). */
export function stop(): Promise<void> {
  if (retryTimer) {
    clearTimeout(retryTimer);
    retryTimer = null;
  }
  connecting = null;
  const conn = connection;
  connection = null;
  return conn?.stop() ?? Promise.resolve();
}