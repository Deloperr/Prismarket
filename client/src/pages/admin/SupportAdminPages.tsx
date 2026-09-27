import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import type { TicketPriority, TicketStatus } from '@/api/types'
import { TicketChat } from '@/components/TicketChat'
import { Loader, Pagination, StatusBadge, Switch, Tabs, relTime } from '@/components/ui'

const categoryLabel: Record<string, string> = { General: 'Общее', Payment: 'Оплата', KeyActivation: 'Активация', Refund: 'Возврат', Account: 'Аккаунт' }

export function SupportAdminPage() {
  const [status, setStatus] = useState<'active' | TicketStatus>('active')
  const [onlyMine, setOnlyMine] = useState(false)
  const [page, setPage] = useState(1)
  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'tickets', status, onlyMine, page],
    queryFn: () => api.admin.tickets(status === 'active' ? undefined : status, onlyMine, page),
    refetchInterval: 15_000,
  })
  const items = status === 'active' ? data?.items.filter((t) => t.status !== 'Closed') : data?.items

  return (
    <div className="stack">
      <div className="row between wrap">
        <h1>Поддержка</h1>
        <div className="row">
          <label className="row small"><Switch checked={onlyMine} onChange={setOnlyMine} /> Только мои</label>
          <Tabs value={status} onChange={(s) => { setStatus(s); setPage(1) }} items={[
            { value: 'active', label: 'Активные' },
            { value: 'WaitingForSupport', label: 'Ждут ответа' },
            { value: 'WaitingForCustomer', label: 'Ждут клиента' },
            { value: 'Closed', label: 'Закрытые' },
          ]} />
        </div>
      </div>
      {isLoading || !items ? <Loader /> : (
        <div className="glass pad table-wrap">
          <table className="table">
            <thead><tr><th>#</th><th>Тема</th><th>Клиент</th><th>Категория</th><th>Приоритет</th><th>Ответственный</th><th>Статус</th><th>Активность</th></tr></thead>
            <tbody>
              {items.map((t) => (
                <tr key={t.id}>
                  <td>{t.id}</td>
                  <td><Link to={`/admin/support/${t.id}`}><b>{t.subject}</b></Link>{t.unreadCount > 0 && <span className="badge accent" style={{ marginLeft: 6 }}>{t.unreadCount}</span>}</td>
                  <td>{t.username}</td>
                  <td>{categoryLabel[t.category]}</td>
                  <td><span className={clsx('badge', t.priority === 'High' && 'danger')}>{t.priority === 'High' ? 'высокий' : t.priority === 'Low' ? 'низкий' : 'обычный'}</span></td>
                  <td>{t.assignedAdmin ?? <span className="subtle">—</span>}</td>
                  <td><StatusBadge status={t.status} /></td>
                  <td className="subtle">{relTime(t.lastMessageAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {data && <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />}
        </div>
      )}
    </div>
  )
}

export function SupportTicketAdminPage() {
  const { id } = useParams()
  const ticketId = Number(id)
  const qc = useQueryClient()
  const update = async (body: { status?: TicketStatus; priority?: TicketPriority; assignToMe?: boolean }) => {
    await api.admin.updateTicket(ticketId, body)
    qc.invalidateQueries({ queryKey: ['ticket', ticketId] })
    toast.success('Обновлено')
  }
  return (
    <div className="stack">
      <Link to="/admin/support" className="subtle">← Все обращения</Link>
      <TicketChat ticketId={ticketId} staffView actions={(t) => (
        <div className="row">
          <button className="btn sm" onClick={() => update({ assignToMe: true })}>Взять себе</button>
          <select className="select" style={{ width: 150, height: 34 }} value={t.ticket.priority} onChange={(e) => update({ priority: e.target.value as TicketPriority })}>
            <option value="Low">Низкий</option><option value="Normal">Обычный</option><option value="High">Высокий</option>
          </select>
          {t.ticket.status !== 'Closed'
            ? <button className="btn sm" onClick={() => update({ status: 'Closed' })}>Закрыть</button>
            : <button className="btn sm" onClick={() => update({ status: 'Open' })}>Открыть</button>}
        </div>
      )} />
    </div>
  )
}
