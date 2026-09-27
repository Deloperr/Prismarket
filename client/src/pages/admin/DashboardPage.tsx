import type { CSSProperties } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { Area, AreaChart, Bar, BarChart, CartesianGrid, Cell, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { AlertTriangle, Bot, KeySquare, LifeBuoy, MessageSquareWarning, TrendingDown, TrendingUp, Users } from 'lucide-react'
import { api } from '@/api/endpoints'
import { useMoney } from '@/hooks/useStore'
import { Loader, StatusBadge, fmtDate, relTime } from '@/components/ui'

export const chartColors = ['#7c8cff', '#b18cff', '#ffb8a0', '#5ee6a8', '#7cc8ff', '#ffcf6b', '#ff7a90', '#a0a8d8']

const tooltipStyle = {
  background: 'var(--bg-2)', border: '1px solid var(--glass-border)', borderRadius: 12, color: 'var(--text)',
}

export function Kpi({ label, value, delta, icon }: { label: string; value: string | number; delta?: number; icon?: JSX.Element }) {
  return (
    <div className="glass pad-sm kpi">
      <span className="row subtle">{icon} {label}</span>
      <span className="value">{value}</span>
      {delta !== undefined && Number.isFinite(delta) && (
        <span className={`delta small ${delta >= 0 ? 'up' : 'down'}`}>
          {delta >= 0 ? <TrendingUp size={14} /> : <TrendingDown size={14} />} {delta >= 0 ? '+' : ''}{delta.toFixed(1)}% к прошлым 30 дням
        </span>
      )}
    </div>
  )
}

const methodNames: Record<string, string> = { Balance: 'Баланс', Card: 'Карта', Erip: 'ЕРИП', Stripe: 'Stripe' }

export default function DashboardPage() {
  const money = useMoney()
  const { data, isLoading } = useQuery({ queryKey: ['admin', 'dashboard'], queryFn: api.admin.dashboard, refetchInterval: 60_000 })
  const { data: automation } = useQuery({ queryKey: ['admin', 'automation-overview'], queryFn: api.admin.automationOverview, refetchInterval: 30_000 })
  if (isLoading || !data) return <Loader />

  const delta = data.revenuePrev30d > 0 ? ((data.revenue30d - data.revenuePrev30d) / data.revenuePrev30d) * 100 : undefined
  const daily = data.daily.map((d) => ({ ...d, label: new Date(d.day).toLocaleDateString('ru-RU', { day: '2-digit', month: '2-digit' }) }))

  return (
    <div className="stack" style={{ '--gap': '20px' } as CSSProperties}>
      <div className="row between wrap">
        <h1>Дашборд</h1>
        <span className="subtle">Данные собираются параллельными SQL-запросами (Dapper) · обновляется каждую минуту</span>
      </div>

      <div className="grid-4">
        <Kpi label="Выручка сегодня" value={money(data.revenueToday)} />
        <Kpi label="Выручка за 30 дней" value={money(data.revenue30d)} delta={delta} />
        <Kpi label="Заказов за 30 дней" value={data.orders30d} />
        <Kpi label="Средний чек" value={money(data.averageCheck30d)} />
      </div>
      <div className="grid-4">
        <Kpi icon={<Users size={14} />} label="Пользователи" value={`${data.totalUsers} (+${data.newUsers30d})`} />
        <Kpi icon={<KeySquare size={14} />} label="Ключей на складе" value={data.keysInStock} />
        <Kpi icon={<LifeBuoy size={14} />} label="Открытых обращений" value={data.openTickets} />
        <Kpi icon={<MessageSquareWarning size={14} />} label="Отзывов на модерации" value={data.pendingReviews} />
      </div>

      <section className="glass pad">
        <h3 style={{ marginBottom: 16 }}>Выручка по дням</h3>
        <div style={{ height: 280 }}>
          <ResponsiveContainer>
            <AreaChart data={daily} margin={{ left: 0, right: 8 }}>
              <defs>
                <linearGradient id="rev" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0%" stopColor="#b18cff" stopOpacity={0.55} />
                  <stop offset="100%" stopColor="#7c8cff" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--glass-border)" vertical={false} />
              <XAxis dataKey="label" stroke="var(--text-3)" fontSize={12} tickLine={false} axisLine={false} />
              <YAxis stroke="var(--text-3)" fontSize={12} tickLine={false} axisLine={false} width={50} />
              <Tooltip contentStyle={tooltipStyle} formatter={(v, name) => [name === 'revenue' ? money(Number(v)) : String(v), name === 'revenue' ? 'Выручка' : 'Заказы']} />
              <Area type="monotone" dataKey="revenue" stroke="#b18cff" strokeWidth={2.5} fill="url(#rev)" />
            </AreaChart>
          </ResponsiveContainer>
        </div>
      </section>

      <div className="grid-2">
        <section className="glass pad">
          <h3 style={{ marginBottom: 16 }}>Топ игр за 30 дней</h3>
          <div style={{ height: 260 }}>
            <ResponsiveContainer>
              <BarChart data={data.topGames} layout="vertical" margin={{ left: 10 }}>
                <XAxis type="number" hide />
                <YAxis type="category" dataKey="title" width={150} stroke="var(--text-2)" fontSize={12} tickLine={false} axisLine={false} />
                <Tooltip contentStyle={tooltipStyle} formatter={(v) => [`${v} шт.`, 'Продано']} cursor={{ fill: 'var(--glass)' }} />
                <Bar dataKey="sold" radius={[0, 8, 8, 0]}>
                  {data.topGames.map((_, i) => <Cell key={i} fill={chartColors[i % chartColors.length]} />)}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
        </section>
        <section className="glass pad">
          <h3 style={{ marginBottom: 16 }}>Способы оплаты и жанры</h3>
          <div className="grid-2" style={{ height: 260 }}>
            <ResponsiveContainer>
              <PieChart>
                <Pie data={data.paymentMethods.map((m) => ({ ...m, name: methodNames[m.method] ?? m.method }))} dataKey="revenue" nameKey="name" innerRadius={50} outerRadius={85} paddingAngle={3}>
                  {data.paymentMethods.map((_, i) => <Cell key={i} fill={chartColors[i % chartColors.length]} stroke="none" />)}
                </Pie>
                <Tooltip contentStyle={tooltipStyle} formatter={(v) => money(Number(v))} />
              </PieChart>
            </ResponsiveContainer>
            <div className="stack" style={{ '--gap': '8px', justifyContent: 'center' } as CSSProperties}>
              {data.genres.slice(0, 6).map((g, i) => {
                const max = data.genres[0]?.revenue || 1
                return (
                  <div key={g.genre}>
                    <div className="row between small"><span>{g.genre}</span><span className="subtle">{money(g.revenue)}</span></div>
                    <div className="progress"><i style={{ width: `${(g.revenue / max) * 100}%`, background: chartColors[i % chartColors.length] }} /></div>
                  </div>
                )
              })}
            </div>
          </div>
        </section>
      </div>

      <div className="grid-2">
        <section className="glass pad">
          <div className="row between" style={{ marginBottom: 12 }}>
            <h3 className="row"><Bot size={18} /> Автоматизация</h3>
            <Link to="/admin/automation" className="btn sm">Все задачи</Link>
          </div>
          {automation && (
            <>
              <div className="row wrap" style={{ gap: 20, marginBottom: 12 }}>
                <span><b>{automation.enabled}</b>/<span className="subtle">{automation.totalJobs} активно</span></span>
                <span><b>{automation.runs24h}</b> <span className="subtle">запусков за 24 ч</span></span>
                <span><b>{automation.items24h}</b> <span className="subtle">объектов обработано</span></span>
                {automation.failed24h > 0 && <span className="badge danger"><AlertTriangle size={12} /> {automation.failed24h} ошибок</span>}
              </div>
              <div className="stack" style={{ '--gap': '6px' } as CSSProperties}>
                {automation.latestRuns.slice(0, 6).map((r) => (
                  <div key={r.id} className="row small" style={{ gap: 8 }}>
                    <StatusBadge status={r.status} />
                    <b style={{ whiteSpace: 'nowrap' }}>{r.jobName}</b>
                    <span className="subtle grow" style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{r.message}</span>
                    <span className="subtle">{relTime(r.startedAt)}</span>
                  </div>
                ))}
              </div>
            </>
          )}
        </section>
        <section className="glass pad">
          <h3 style={{ marginBottom: 12 }}>Остатки: заканчиваются</h3>
          <div className="stack" style={{ '--gap': '8px' } as CSSProperties}>
            {data.lowStock.map((s) => (
              <div key={s.productId} className="row between small">
                <span>{s.gameTitle} <span className="subtle">({s.edition})</span></span>
                <span className={`badge ${s.available === 0 ? 'danger' : s.available <= 5 ? 'warning' : 'success'}`}>
                  {s.available === 0 ? (s.isAvailable ? 'нет' : 'скрыт') : `${s.available} шт.`}
                </span>
              </div>
            ))}
          </div>
        </section>
      </div>

      <section className="glass pad table-wrap">
        <div className="row between" style={{ marginBottom: 8 }}><h3>Последние заказы</h3><Link to="/admin/orders" className="btn sm">Все заказы</Link></div>
        <table className="table">
          <thead><tr><th>Номер</th><th>Покупатель</th><th>Сумма</th><th>Оплата</th><th>Статус</th><th>Создан</th></tr></thead>
          <tbody>
            {data.recentOrders.map((o) => (
              <tr key={o.number}>
                <td><b>{o.number}</b></td><td>{o.username}</td><td>{money(o.total)}</td>
                <td>{methodNames[o.method] ?? o.method}</td><td><StatusBadge status={o.status} /></td><td className="subtle">{fmtDate(o.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  )
}
