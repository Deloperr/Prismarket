import { useEffect, useRef, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { Bot, Send } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { SupportMessage, TicketDetail } from '@/api/types'
import { useAuth } from '@/store/auth'
import { useTicketChannel } from '@/hooks/useRealtime'
import { Loader, StatusBadge, fmtDate } from './ui'

const categoryLabel: Record<string, string> = {
  General: 'Общий вопрос', Payment: 'Оплата', KeyActivation: 'Активация ключа', Refund: 'Возврат', Account: 'Аккаунт',
}

/** Realtime support chat (SignalR) shared by the customer and the admin panel. */
export function TicketChat({ ticketId, staffView = false, actions }: { ticketId: number; staffView?: boolean; actions?: (t: TicketDetail) => JSX.Element }) {
  const qc = useQueryClient()
  const userId = useAuth((s) => s.user?.id)
  const [text, setText] = useState('')
  const bottom = useRef<HTMLDivElement>(null)
  const key = ['ticket', ticketId]
  const { data, isLoading } = useQuery({ queryKey: key, queryFn: () => api.support.get(ticketId) })

  useTicketChannel(ticketId, (event, payload) => {
    if (event === 'message') {
      const message = payload as SupportMessage
      qc.setQueryData<TicketDetail>(key, (old) =>
        old && !old.messages.some((m) => m.id === message.id) ? { ...old, messages: [...old.messages, message] } : old)
    } else qc.invalidateQueries({ queryKey: key })
  })

  useEffect(() => bottom.current?.scrollIntoView({ behavior: 'smooth', block: 'nearest' }), [data?.messages.length])

  if (isLoading || !data) return <Loader />
  const t = data.ticket

  const send = async () => {
    if (!text.trim()) return
    try {
      const message = await api.support.post(ticketId, text)
      setText('')
      qc.setQueryData<TicketDetail>(key, (old) =>
        old && !old.messages.some((m) => m.id === message.id) ? { ...old, messages: [...old.messages, message] } : old)
    } catch (e) { toast.error(errorMessage(e)) }
  }

  return (
    <div className="glass pad stack">
      <div className="row between wrap">
        <div>
          <h2>{t.subject}</h2>
          <p className="subtle">
            #{t.id} · {categoryLabel[t.category]} · приоритет {t.priority === 'High' ? 'высокий' : t.priority === 'Low' ? 'низкий' : 'обычный'}
            {t.orderNumber && ` · заказ ${t.orderNumber}`}{staffView && ` · клиент ${t.username}`}{t.assignedAdmin && ` · ответственный ${t.assignedAdmin}`}
          </p>
        </div>
        <div className="row">
          <StatusBadge status={t.status} />
          {actions?.(data)}
        </div>
      </div>

      <div className="chat">
        {data.messages.map((m) => {
          const mine = m.senderId === userId
          return (
            <div key={m.id} className={clsx('msg', mine ? 'mine' : 'theirs', m.isBot && 'bot')}>
              <div className="row" style={{ gap: 6, marginBottom: 2 }}>
                {m.isBot && <Bot size={14} />}
                <b className="small">{mine ? 'Вы' : m.senderName}</b>
                {m.isFromStaff && !m.isBot && !mine && <span className="badge info" style={{ height: 18 }}>поддержка</span>}
              </div>
              {m.text}
              <div className="meta">{fmtDate(m.createdAt)}</div>
            </div>
          )
        })}
        <div ref={bottom} />
      </div>

      <form className="row" onSubmit={(e) => { e.preventDefault(); send() }}>
        <textarea className="textarea grow" style={{ minHeight: 48, height: 48 }} placeholder={t.status === 'Closed' ? 'Напишите, чтобы открыть обращение снова…' : 'Сообщение…'}
          value={text} onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); send() } }} />
        <button className="btn primary icon" aria-label="Отправить"><Send size={18} /></button>
      </form>
    </div>
  )
}
