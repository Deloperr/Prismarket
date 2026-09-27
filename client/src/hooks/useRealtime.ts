import { useEffect, useRef } from 'react'
import { HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import type { Notification } from '@/api/types'
import { useAuth } from '@/store/auth'

let connection: HubConnection | null = null
const listeners = new Set<(event: string, payload: unknown) => void>()

function ensureConnection(): HubConnection {
  if (connection) return connection
  connection = new HubConnectionBuilder()
    .withUrl('/hubs/store', { accessTokenFactory: () => useAuth.getState().accessToken ?? '' })
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
  for (const event of ['notification', 'order', 'ticket', 'message', 'status']) {
    connection.on(event, (payload: unknown) => listeners.forEach((l) => l(event, payload)))
  }
  return connection
}

/** Global realtime connection: pushes notifications as toasts and refreshes related queries. */
export function useRealtimeConnection() {
  const token = useAuth((s) => s.accessToken)
  const qc = useQueryClient()

  useEffect(() => {
    if (!token) {
      void connection?.stop()
      connection = null
      return
    }
    const conn = ensureConnection()
    if (conn.state === HubConnectionState.Disconnected) conn.start().catch(() => undefined)

    const handler = (event: string, payload: unknown) => {
      if (event === 'notification') {
        const n = payload as Notification
        toast(n.title, { description: n.message })
        qc.invalidateQueries({ queryKey: ['notifications'] })
        qc.invalidateQueries({ queryKey: ['me'] })
      }
      if (event === 'order' || event === 'ticket') qc.invalidateQueries({ queryKey: ['admin'] })
    }
    listeners.add(handler)
    return () => {
      listeners.delete(handler)
    }
  }, [token, qc])
}

/** Subscribe to a support ticket chat. */
export function useTicketChannel(ticketId: number | undefined, onEvent: (event: string, payload: unknown) => void) {
  const token = useAuth((s) => s.accessToken)
  const cb = useRef(onEvent)
  cb.current = onEvent

  useEffect(() => {
    if (!token || !ticketId) return
    const conn = ensureConnection()
    const join = () => conn.invoke('JoinTicket', ticketId).catch(() => undefined)
    if (conn.state === HubConnectionState.Connected) join()
    else if (conn.state === HubConnectionState.Disconnected) conn.start().then(join).catch(() => undefined)
    else conn.onreconnected(() => join())

    const handler = (event: string, payload: unknown) => {
      if (event === 'message' || event === 'status') cb.current(event, payload)
    }
    listeners.add(handler)
    const timer = setInterval(() => {
      if (conn.state === HubConnectionState.Connected) join()
    }, 30_000)
    return () => {
      listeners.delete(handler)
      clearInterval(timer)
      if (conn.state === HubConnectionState.Connected) conn.invoke('LeaveTicket', ticketId).catch(() => undefined)
    }
  }, [ticketId, token])
}
