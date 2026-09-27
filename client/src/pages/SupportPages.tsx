import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Bot, LifeBuoy, MessageCirclePlus } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import { TicketChat } from '@/components/TicketChat'
import { Field, Loader, StatusBadge, relTime } from '@/components/ui'

export function SupportPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const { data, isLoading } = useQuery({ queryKey: ['tickets'], queryFn: api.support.mine })
  const [form, setForm] = useState({ subject: '', message: '', orderNumber: params.get('order') ?? '' })
  const [busy, setBusy] = useState(false)

  const create = async () => {
    setBusy(true)
    try {
      const t = await api.support.create(form.subject, form.message, form.orderNumber || undefined)
      toast.success('Обращение создано')
      navigate(`/support/${t.ticket.id}`)
    } catch (e) { toast.error(errorMessage(e)) } finally { setBusy(false) }
  }

  return (
    <div className="game-layout" style={{ marginTop: 0 }}>
      <div className="stack">
        <h1>Поддержка</h1>
        {isLoading ? <Loader /> : data?.length === 0 ? (
          <div className="glass pad empty"><div className="ico"><LifeBuoy size={28} /></div><p className="muted">У вас пока нет обращений</p></div>
        ) : data?.map((t) => (
          <Link key={t.id} to={`/support/${t.id}`} className="glass pad-sm hover row">
            <div className="grow">
              <b>{t.subject}</b>
              <div className="subtle" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{t.lastMessage}</div>
            </div>
            {t.unreadCount > 0 && <span className="badge accent">{t.unreadCount}</span>}
            <span className="subtle">{relTime(t.lastMessageAt)}</span>
            <StatusBadge status={t.status} />
          </Link>
        ))}
      </div>
      <form className="glass pad stack buy-box" onSubmit={(e) => { e.preventDefault(); create() }}>
        <h3 className="row"><MessageCirclePlus size={20} /> Новое обращение</h3>
        <div className="notice small"><Bot size={16} /><span>Бот-помощник сразу ответит на типовые вопросы, а обращение автоматически получит категорию и ответственного.</span></div>
        <Field label="Тема"><input className="input" required minLength={3} value={form.subject} onChange={(e) => setForm({ ...form, subject: e.target.value })} /></Field>
        <Field label="Номер заказа (если есть)"><input className="input" placeholder="PM-…" value={form.orderNumber} onChange={(e) => setForm({ ...form, orderNumber: e.target.value })} /></Field>
        <Field label="Сообщение"><textarea className="textarea" required value={form.message} onChange={(e) => setForm({ ...form, message: e.target.value })} /></Field>
        <button className="btn primary" disabled={busy}>Отправить</button>
      </form>
    </div>
  )
}

export function TicketPage() {
  const { id } = useParams()
  const ticketId = Number(id)
  return (
    <div className="stack" style={{ maxWidth: 860, margin: '0 auto' }}>
      <Link to="/support" className="subtle">← Все обращения</Link>
      <TicketChat ticketId={ticketId} actions={(t) => t.ticket.status !== 'Closed'
        ? <button className="btn sm" onClick={async () => { await api.support.close(ticketId); toast.success('Обращение закрыто') }}>Закрыть</button>
        : <></>} />
    </div>
  )
}
