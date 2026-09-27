import { useState, type CSSProperties } from 'react'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import { Ban, CheckCircle2, Search, ShieldCheck, Trash2, Undo2, XCircle } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { AdminUser } from '@/api/types'
import { useMoney } from '@/hooks/useStore'
import { Avatar, Field, Loader, Modal, Pagination, Stars, StatusBadge, Tabs, fmtDate } from '@/components/ui'

function SearchBox({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) {
  return (
    <div className="header-search" style={{ display: 'block', width: 300 }}>
      <Search size={17} />
      <input className="input" placeholder={placeholder} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

const methodNames: Record<string, string> = { Balance: 'Баланс', Card: 'Карта', Erip: 'ЕРИП', Stripe: 'Stripe' }

export function OrdersPage() {
  const money = useMoney()
  const qc = useQueryClient()
  const [status, setStatus] = useState('')
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'orders', status, search, page],
    queryFn: () => api.admin.orders({ status: status || undefined, search: search || undefined, page }),
    placeholderData: keepPreviousData,
  })

  const act = async (fn: () => Promise<unknown>, msg: string) => {
    try { await fn(); toast.success(msg); qc.invalidateQueries({ queryKey: ['admin'] }) } catch (e) { toast.error(errorMessage(e)) }
  }

  return (
    <div className="stack">
      <div className="row between wrap">
        <h1>Заказы</h1>
        <div className="row">
          <SearchBox value={search} onChange={(v) => { setSearch(v); setPage(1) }} placeholder="Номер, логин или e-mail" />
          <select className="select" style={{ width: 200 }} value={status} onChange={(e) => { setStatus(e.target.value); setPage(1) }}>
            <option value="">Все статусы</option>
            <option value="AwaitingPayment">Ожидают оплаты</option>
            <option value="Completed">Выполнены</option>
            <option value="Expired">Истекли</option>
            <option value="Cancelled">Отменены</option>
            <option value="Refunded">Возвраты</option>
          </select>
        </div>
      </div>
      {isLoading || !data ? <Loader /> : (
        <div className="glass pad table-wrap">
          <table className="table">
            <thead><tr><th>Номер</th><th>Покупатель</th><th>Товары</th><th>Сумма</th><th>Оплата</th><th>Статус</th><th>Создан</th><th /></tr></thead>
            <tbody>
              {data.items.map((o) => (
                <tr key={o.id}>
                  <td><b>{o.number}</b></td>
                  <td>{o.customerName}</td>
                  <td className="small">{o.items.map((i) => i.gameTitle).join(', ')}</td>
                  <td><b>{money(o.total)}</b>{o.promoCode && <div className="subtle">{o.promoCode}</div>}</td>
                  <td>{methodNames[o.paymentMethod]}</td>
                  <td><StatusBadge status={o.status} /></td>
                  <td className="subtle">{fmtDate(o.createdAt)}</td>
                  <td>
                    {o.status === 'AwaitingPayment' && (
                      <button className="btn sm" onClick={() => act(() => api.admin.markPaid(o.number), 'Оплата подтверждена, ключи выданы')}><CheckCircle2 size={14} /> Подтвердить оплату</button>
                    )}
                    {o.status === 'Completed' && (
                      <button className="btn sm ghost" onClick={() => {
                        const reason = prompt('Причина возврата')
                        if (reason) act(() => api.admin.refund(o.number, reason), 'Средства возвращены на баланс')
                      }}><Undo2 size={14} /> Возврат</button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />
        </div>
      )}
    </div>
  )
}

function UserModal({ user, onClose }: { user: AdminUser; onClose: () => void }) {
  const money = useMoney()
  const qc = useQueryClient()
  const { data, refetch } = useQuery({ queryKey: ['admin', 'user', user.id], queryFn: () => api.admin.user(user.id) })
  const [amount, setAmount] = useState(10)
  const [reason, setReason] = useState('Бонус от администрации')
  const u = data?.user ?? user

  const update = async (body: Parameters<typeof api.admin.updateUser>[1]) => {
    try { await api.admin.updateUser(user.id, body); await refetch(); qc.invalidateQueries({ queryKey: ['admin', 'users'] }); toast.success('Сохранено') }
    catch (e) { toast.error(errorMessage(e)) }
  }

  return (
    <Modal open wide onClose={onClose} title={u.username}>
      <div className="stack">
        <div className="row wrap">
          <Avatar name={u.username} />
          <div className="grow"><b>{u.email}</b><div className="subtle">Регистрация {fmtDate(u.createdAt, false)} · последний вход {fmtDate(u.lastLoginAt)}</div></div>
          <span className="display" style={{ fontSize: 22 }}>{money(u.balance)}</span>
        </div>
        <div className="row wrap">
          <button className="btn sm" onClick={() => update({ role: u.role === 'Admin' ? 'Customer' : 'Admin' })}><ShieldCheck size={14} /> {u.role === 'Admin' ? 'Снять права админа' : 'Сделать админом'}</button>
          <button className="btn sm" onClick={() => update({ isActive: !u.isActive })}><Ban size={14} /> {u.isActive ? 'Заблокировать' : 'Разблокировать'}</button>
          {!u.emailConfirmed && <button className="btn sm" onClick={() => update({ emailConfirmed: true })}>Подтвердить e-mail</button>}
        </div>
        <div className="glass pad-sm row wrap">
          <Field label="Изменить баланс (±BYN)"><input className="input" type="number" value={amount} onChange={(e) => setAmount(Number(e.target.value))} style={{ width: 120 }} /></Field>
          <Field label="Причина"><input className="input" value={reason} onChange={(e) => setReason(e.target.value)} /></Field>
          <button className="btn primary" style={{ alignSelf: 'flex-end' }} onClick={async () => {
            try { await api.admin.adjustBalance(user.id, amount, reason); await refetch(); toast.success('Баланс изменён') } catch (e) { toast.error(errorMessage(e)) }
          }}>Применить</button>
        </div>
        <h3>Библиотека ({data?.library.length ?? 0})</h3>
        <div className="row wrap" style={{ gap: 6 }}>{data?.library.map((l) => <span key={l.id} className="badge">{l.gameTitle}</span>)}</div>
        <h3>Последние заказы</h3>
        <table className="table">
          <tbody>
            {data?.orders.slice(0, 8).map((o) => (
              <tr key={o.id}><td><b>{o.number}</b></td><td>{money(o.total)}</td><td><StatusBadge status={o.status} /></td><td className="subtle">{fmtDate(o.createdAt)}</td></tr>
            ))}
          </tbody>
        </table>
      </div>
    </Modal>
  )
}

export function UsersPage() {
  const money = useMoney()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [selected, setSelected] = useState<AdminUser | null>(null)
  const { data, isLoading } = useQuery({ queryKey: ['admin', 'users', search, page], queryFn: () => api.admin.users(search, page), placeholderData: keepPreviousData })

  return (
    <div className="stack">
      <div className="row between wrap"><h1>Пользователи</h1><SearchBox value={search} onChange={(v) => { setSearch(v); setPage(1) }} placeholder="Логин или e-mail" /></div>
      {isLoading || !data ? <Loader /> : (
        <div className="glass pad table-wrap">
          <table className="table">
            <thead><tr><th>Пользователь</th><th>Роль</th><th>Баланс</th><th>Заказы</th><th>Потрачено</th><th>Последний вход</th><th>Статус</th></tr></thead>
            <tbody>
              {data.items.map((u) => (
                <tr key={u.id} style={{ cursor: 'pointer' }} onClick={() => setSelected(u)}>
                  <td><div className="row"><Avatar name={u.username} /><div><b>{u.username}</b><div className="subtle">{u.email}</div></div></div></td>
                  <td>{u.role === 'Admin' ? <span className="badge accent">Админ</span> : <span className="badge">Покупатель</span>}</td>
                  <td>{money(u.balance)}</td>
                  <td>{u.ordersCount}</td>
                  <td>{money(u.totalSpent)}</td>
                  <td className="subtle">{fmtDate(u.lastLoginAt)}</td>
                  <td>
                    {u.isActive ? <span className="badge success">Активен</span> : <span className="badge danger">Заблокирован</span>}
                    {u.twoFactorEnabled && <span className="badge info" style={{ marginLeft: 4 }}>2FA</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />
        </div>
      )}
      {selected && <UserModal user={selected} onClose={() => setSelected(null)} />}
    </div>
  )
}

export function ReviewsModerationPage() {
  const qc = useQueryClient()
  const [status, setStatus] = useState<'PendingModeration' | 'Published' | 'Rejected'>('PendingModeration')
  const [page, setPage] = useState(1)
  const { data, isLoading } = useQuery({ queryKey: ['admin', 'reviews', status, page], queryFn: () => api.admin.reviews(status, page) })
  const act = async (fn: () => Promise<unknown>) => { try { await fn(); qc.invalidateQueries({ queryKey: ['admin', 'reviews'] }) } catch (e) { toast.error(errorMessage(e)) } }

  return (
    <div className="stack">
      <div className="row between wrap">
        <h1>Модерация отзывов</h1>
        <Tabs value={status} onChange={(s) => { setStatus(s); setPage(1) }} items={[
          { value: 'PendingModeration', label: 'На проверке' }, { value: 'Published', label: 'Опубликованы' }, { value: 'Rejected', label: 'Отклонены' },
        ]} />
      </div>
      <p className="muted small">Автомодерация проверяет каждый новый отзыв на стоп-слова и ссылки. Подозрительные отзывы попадают сюда.</p>
      {isLoading || !data ? <Loader /> : data.items.length === 0 ? <div className="glass pad empty"><p className="muted">Здесь пусто</p></div> : data.items.map((r) => (
        <div key={r.id} className="glass pad-sm stack" style={{ '--gap': '8px' } as CSSProperties}>
          <div className="row wrap">
            <b>{r.username}</b><span className="subtle">о «{r.gameTitle}»</span><Stars value={r.rating} />
            <span className="subtle grow">{fmtDate(r.createdAt)}</span>
            {r.moderationNote && <span className="badge warning">{r.moderationNote}</span>}
          </div>
          {r.title && <b>{r.title}</b>}
          <p className="muted">{r.comment}</p>
          <div className="row">
            {r.status !== 'Published' && <button className="btn sm" onClick={() => act(() => api.admin.setReviewStatus(r.id, 'Published'))}><CheckCircle2 size={14} /> Опубликовать</button>}
            {r.status !== 'Rejected' && <button className="btn sm" onClick={() => act(() => api.admin.setReviewStatus(r.id, 'Rejected'))}><XCircle size={14} /> Отклонить</button>}
            <button className="btn sm ghost danger" onClick={() => act(() => api.admin.deleteReview(r.id))}><Trash2 size={14} /> Удалить</button>
          </div>
        </div>
      ))}
      {data && <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />}
    </div>
  )
}

export function AuditPage() {
  const [page, setPage] = useState(1)
  const [entity, setEntity] = useState('')
  const { data, isLoading } = useQuery({ queryKey: ['admin', 'audit', page, entity], queryFn: () => api.admin.audit(page, entity || undefined), placeholderData: keepPreviousData })
  return (
    <div className="stack">
      <div className="row between wrap">
        <div><h1>Журнал аудита</h1><p className="muted small">Изменения записываются автоматически EF Core-интерсептором при сохранении.</p></div>
        <select className="select" style={{ width: 200 }} value={entity} onChange={(e) => { setEntity(e.target.value); setPage(1) }}>
          <option value="">Все сущности</option>
          {['Game', 'Product', 'Order', 'User', 'PromoCode', 'Promotion'].map((e) => <option key={e}>{e}</option>)}
        </select>
      </div>
      {isLoading || !data ? <Loader /> : (
        <div className="glass pad table-wrap">
          <table className="table">
            <thead><tr><th>Время</th><th>Пользователь</th><th>Действие</th><th>Сущность</th><th>Изменения</th><th>IP</th></tr></thead>
            <tbody>
              {data.items.map((a) => (
                <tr key={a.id}>
                  <td className="subtle" style={{ whiteSpace: 'nowrap' }}>{fmtDate(a.createdAt)}</td>
                  <td>{a.username ?? <span className="subtle">система</span>}</td>
                  <td><span className={`badge ${a.action === 'Deleted' ? 'danger' : 'info'}`}>{a.action === 'Deleted' ? 'удаление' : 'изменение'}</span></td>
                  <td>{a.entityName} #{a.entityId}</td>
                  <td className="mono small" style={{ maxWidth: 420, wordBreak: 'break-all' }}>{a.changes}</td>
                  <td className="subtle">{a.ipAddress}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />
        </div>
      )}
    </div>
  )
}
