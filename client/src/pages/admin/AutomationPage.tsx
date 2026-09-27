import { useState, type CSSProperties } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import {
  Activity, AlarmClock, Archive, BellRing, Bot, CalendarClock, Coins, DatabaseBackup, ExternalLink, FileBarChart, Mail,
  MessageSquareWarning, Play, RefreshCw, Settings2, ShoppingCart, Sparkles, Timer, TrendingDown, Trash2, UserRoundCheck, Warehouse, Zap,
} from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { AutomationJob } from '@/api/types'
import { useAuth } from '@/store/auth'
import { Field, Loader, Modal, Pagination, StatusBadge, Switch, fmtDate, relTime } from '@/components/ui'
import { Kpi } from './DashboardPage'

const icons: Record<string, JSX.Element> = {
  'order-expiry': <Timer size={20} />,
  'email-outbox': <Mail size={20} />,
  'promotions-scheduler': <Sparkles size={20} />,
  'stock-monitor': <Warehouse size={20} />,
  'wishlist-price-alerts': <TrendingDown size={20} />,
  'abandoned-cart': <ShoppingCart size={20} />,
  'currency-rates': <Coins size={20} />,
  'sales-report': <FileBarChart size={20} />,
  'support-autoclose': <AlarmClock size={20} />,
  'featured-rotation': <RefreshCw size={20} />,
  'win-back': <UserRoundCheck size={20} />,
  cleanup: <Trash2 size={20} />,
  'db-backup': <DatabaseBackup size={20} />,
  'key-delivery': <Zap size={20} />,
  cashback: <Coins size={20} />,
  'review-moderation': <MessageSquareWarning size={20} />,
  'support-bot': <Bot size={20} />,
}

/** Human readable description of common CRON expressions. */
export function describeCron(cron?: string | null): string {
  if (!cron) return 'по событию'
  const [min, hour, dom, , dow] = cron.split(' ')
  if (cron === '* * * * *') return 'каждую минуту'
  if (min.startsWith('*/') && hour === '*') return `каждые ${min.slice(2)} мин`
  if (hour === '*' && /^\d+$/.test(min)) return min === '0' ? 'каждый час' : `каждый час в :${min.padStart(2, '0')}`
  if (hour.startsWith('*/') && /^\d+$/.test(min)) return `каждые ${hour.slice(2)} ч`
  const days = ['вс', 'пн', 'вт', 'ср', 'чт', 'пт', 'сб']
  if (/^\d+$/.test(hour) && /^\d+$/.test(min)) {
    const time = `${hour.padStart(2, '0')}:${min.padStart(2, '0')}`
    if (dom === '*' && dow === '*') return `ежедневно в ${time}`
    if (dom === '*' && /^\d$/.test(dow)) return `по ${days[Number(dow)]} в ${time}`
  }
  return cron
}

function SettingsModal({ job, onClose }: { job: AutomationJob; onClose: () => void }) {
  const qc = useQueryClient()
  const [cron, setCron] = useState(job.cron ?? '')
  const [values, setValues] = useState<Record<string, unknown>>(job.settings)
  const save = useMutation({
    mutationFn: () => api.admin.updateAutomation(job.key, { cron: job.kind === 'Scheduled' ? cron : undefined, settings: values }),
    onSuccess: () => { toast.success('Настройки сохранены'); qc.invalidateQueries({ queryKey: ['admin', 'automation'] }); onClose() },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <Modal open onClose={onClose} title={job.name}>
      <form className="stack" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <p className="muted small">{job.description}</p>
        {job.kind === 'Scheduled' && (
          <Field label={`Расписание (CRON) — ${describeCron(cron)}`}>
            <input className="input mono" value={cron} onChange={(e) => setCron(e.target.value)} placeholder="*/15 * * * *" />
          </Field>
        )}
        {job.settingDefinitions.map((d) => (
          <Field key={d.key} label={d.label}>
            {d.type === 'bool' ? (
              <Switch checked={Boolean(values[d.key])} onChange={(v) => setValues({ ...values, [d.key]: v })} />
            ) : (
              <input className="input" type={d.type === 'string' ? 'text' : 'number'} step={d.type === 'decimal' ? '0.1' : '1'}
                value={String(values[d.key] ?? '')}
                onChange={(e) => setValues({ ...values, [d.key]: d.type === 'string' ? e.target.value : Number(e.target.value) })} />
            )}
          </Field>
        ))}
        <button className="btn primary" disabled={save.isPending}>Сохранить</button>
      </form>
    </Modal>
  )
}

function JobCard({ job, onConfigure, onHistory }: { job: AutomationJob; onConfigure: () => void; onHistory: () => void }) {
  const qc = useQueryClient()
  const toggle = useMutation({
    mutationFn: (isEnabled: boolean) => api.admin.updateAutomation(job.key, { isEnabled }),
    onSuccess: (_, enabled) => { toast.success(enabled ? 'Автоматизация включена' : 'Автоматизация выключена'); qc.invalidateQueries({ queryKey: ['admin', 'automation'] }) },
    onError: (e) => toast.error(errorMessage(e)),
  })
  const run = useMutation({
    mutationFn: () => api.admin.runAutomation(job.key),
    onSuccess: () => {
      toast.success('Задача поставлена в очередь')
      setTimeout(() => qc.invalidateQueries({ queryKey: ['admin', 'automation'] }), 2500)
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <div className={clsx('glass pad-sm auto-card fade-in', !job.isEnabled && 'muted')}>
      <div className="head">
        <div className="ico">{icons[job.key] ?? <Activity size={20} />}</div>
        <div className="grow">
          <div className="row between">
            <b>{job.name}</b>
            <Switch checked={job.isEnabled} onChange={(v) => toggle.mutate(v)} disabled={toggle.isPending} />
          </div>
          <p className="subtle" style={{ marginTop: 4 }}>{job.description}</p>
        </div>
      </div>
      <div className="row wrap small" style={{ gap: 8 }}>
        <span className="badge"><CalendarClock size={12} /> {describeCron(job.cron)}</span>
        {job.lastStatus && <StatusBadge status={job.lastStatus} />}
        {job.lastRunAt && <span className="subtle">{relTime(job.lastRunAt)}</span>}
      </div>
      <div className="row small subtle" style={{ gap: 16 }}>
        <span><span className={clsx('pulse', !job.isEnabled && 'off')} /> {job.runs24h} запусков/24ч</span>
        <span>{job.items24h} объектов</span>
        {job.failed24h > 0 && <span style={{ color: 'var(--danger)' }}>{job.failed24h} ошибок</span>}
      </div>
      <div className="row">
        {job.kind === 'Scheduled' && (
          <button className="btn sm primary" onClick={() => run.mutate()} disabled={run.isPending}><Play size={14} /> Запустить</button>
        )}
        <button className="btn sm" onClick={onConfigure}><Settings2 size={14} /> Настроить</button>
        <button className="btn sm ghost" onClick={onHistory}><Archive size={14} /> История</button>
      </div>
    </div>
  )
}

export default function AutomationPage() {
  const token = useAuth((s) => s.accessToken)
  const [configuring, setConfiguring] = useState<AutomationJob | null>(null)
  const [filter, setFilter] = useState<{ key?: string; status?: string; page: number }>({ page: 1 })
  const { data: jobs, isLoading } = useQuery({ queryKey: ['admin', 'automation', 'jobs'], queryFn: api.admin.automation, refetchInterval: 10_000 })
  const { data: overview } = useQuery({ queryKey: ['admin', 'automation', 'overview'], queryFn: api.admin.automationOverview, refetchInterval: 10_000 })
  const { data: runs } = useQuery({ queryKey: ['admin', 'automation', 'runs', filter], queryFn: () => api.admin.automationRuns(filter), refetchInterval: 5_000 })

  if (isLoading || !jobs) return <Loader />
  const scheduled = jobs.filter((j) => j.kind === 'Scheduled')
  const events = jobs.filter((j) => j.kind === 'EventDriven')

  return (
    <div className="stack" style={{ '--gap': '22px' } as CSSProperties}>
      <div className="row between wrap">
        <div>
          <h1>Автоматизация</h1>
          <p className="muted">Фоновые задачи Hangfire по расписанию и правила, срабатывающие на события в магазине.</p>
        </div>
        <a className="btn" href={`/hangfire?access_token=${token}`} target="_blank" rel="noreferrer"><ExternalLink size={15} /> Панель Hangfire</a>
      </div>

      {overview && (
        <div className="grid-4">
          <Kpi label="Активно автоматизаций" value={`${overview.enabled} / ${overview.totalJobs}`} />
          <Kpi label="Запусков за 24 ч" value={overview.runs24h} />
          <Kpi label="Обработано объектов" value={overview.items24h} />
          <Kpi label="Ошибок за 24 ч" value={overview.failed24h} />
        </div>
      )}

      <section className="stack">
        <h2 className="row"><CalendarClock size={22} /> По расписанию</h2>
        <div className="grid-2">
          {scheduled.map((j) => <JobCard key={j.key} job={j} onConfigure={() => setConfiguring(j)} onHistory={() => setFilter({ key: j.key, page: 1 })} />)}
        </div>
      </section>

      <section className="stack">
        <h2 className="row"><BellRing size={22} /> По событиям</h2>
        <div className="grid-2">
          {events.map((j) => <JobCard key={j.key} job={j} onConfigure={() => setConfiguring(j)} onHistory={() => setFilter({ key: j.key, page: 1 })} />)}
        </div>
      </section>

      <section className="glass pad table-wrap">
        <div className="row between wrap" style={{ marginBottom: 12 }}>
          <h3>Журнал запусков <span className="pulse" style={{ marginLeft: 6 }} /></h3>
          <div className="row">
            <select className="select" style={{ width: 260 }} value={filter.key ?? ''} onChange={(e) => setFilter({ ...filter, key: e.target.value || undefined, page: 1 })}>
              <option value="">Все автоматизации</option>
              {jobs.map((j) => <option key={j.key} value={j.key}>{j.name}</option>)}
            </select>
            <select className="select" style={{ width: 160 }} value={filter.status ?? ''} onChange={(e) => setFilter({ ...filter, status: e.target.value || undefined, page: 1 })}>
              <option value="">Любой статус</option>
              <option value="Succeeded">Успешно</option>
              <option value="Failed">Ошибка</option>
              <option value="Running">Выполняется</option>
            </select>
          </div>
        </div>
        <table className="table">
          <thead><tr><th>Время</th><th>Автоматизация</th><th>Запуск</th><th>Статус</th><th>Объектов</th><th>Длительность</th><th>Результат</th></tr></thead>
          <tbody>
            {runs?.items.map((r) => (
              <tr key={r.id}>
                <td className="subtle" style={{ whiteSpace: 'nowrap' }}>{fmtDate(r.startedAt)}</td>
                <td><b>{r.jobName}</b></td>
                <td><span className="badge">{r.trigger === 'Schedule' ? 'расписание' : r.trigger === 'Manual' ? 'вручную' : 'событие'}</span></td>
                <td><StatusBadge status={r.status} /></td>
                <td>{r.itemsProcessed}</td>
                <td className="subtle">{r.durationMs != null ? `${Math.round(r.durationMs)} мс` : '—'}</td>
                <td className="small" style={{ maxWidth: 360 }}>{r.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
        {runs && <Pagination page={runs.page} totalPages={runs.totalPages} onChange={(page) => setFilter({ ...filter, page })} />}
      </section>

      {configuring && <SettingsModal job={configuring} onClose={() => setConfiguring(null)} />}
    </div>
  )
}
