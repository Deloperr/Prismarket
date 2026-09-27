import { useState, type CSSProperties } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { Download, FileSpreadsheet, FileText, FileType, Plus, Sparkles, Trash2 } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { download, errorMessage } from '@/api/client'
import { Field, Loader, Modal, Pagination, StatusBadge, Switch, fmtDate } from '@/components/ui'

function toLocalInput(d: Date) {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}

function PromotionModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient()
  const [search, setSearch] = useState('')
  const { data: games } = useQuery({ queryKey: ['admin', 'games', search, 1], queryFn: () => api.admin.games(search, 1) })
  const [form, setForm] = useState({
    name: '', description: '', discountPercent: 25,
    startsAt: toLocalInput(new Date(Date.now() + 5 * 60_000)), endsAt: toLocalInput(new Date(Date.now() + 7 * 86_400_000)),
    productIds: [] as number[],
  })
  const toggle = (id: number) => setForm((f) => ({ ...f, productIds: f.productIds.includes(id) ? f.productIds.filter((x) => x !== id) : [...f.productIds, id] }))

  return (
    <Modal open wide onClose={onClose} title="Новая акция">
      <form className="stack" onSubmit={async (e) => {
        e.preventDefault()
        try {
          await api.admin.createPromotion({ ...form, startsAt: new Date(form.startsAt).toISOString(), endsAt: new Date(form.endsAt).toISOString() })
          toast.success('Акция запланирована — скидки включатся автоматически')
          qc.invalidateQueries({ queryKey: ['admin', 'promotions'] })
          onClose()
        } catch (err) { toast.error(errorMessage(err)) }
      }}>
        <div className="grid-2">
          <Field label="Название"><input className="input" required value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} /></Field>
          <Field label="Скидка, %"><input className="input" type="number" min={1} max={95} value={form.discountPercent} onChange={(e) => setForm({ ...form, discountPercent: Number(e.target.value) })} /></Field>
          <Field label="Начало"><input className="input" type="datetime-local" value={form.startsAt} onChange={(e) => setForm({ ...form, startsAt: e.target.value })} /></Field>
          <Field label="Окончание"><input className="input" type="datetime-local" value={form.endsAt} onChange={(e) => setForm({ ...form, endsAt: e.target.value })} /></Field>
        </div>
        <Field label="Описание"><input className="input" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></Field>
        <Field label={`Товары (${form.productIds.length} выбрано)`}>
          <input className="input" placeholder="Поиск игры" value={search} onChange={(e) => setSearch(e.target.value)} />
        </Field>
        <div className="stack" style={{ maxHeight: 260, overflowY: 'auto', '--gap': '6px' } as CSSProperties}>
          {games?.items.flatMap((g) => g.products.map((p) => (
            <label key={p.id} className={clsx('edition', form.productIds.includes(p.id) && 'selected')} style={{ padding: 10 }}>
              <input type="checkbox" checked={form.productIds.includes(p.id)} onChange={() => toggle(p.id)} />
              <span className="grow">{g.title} <span className="subtle">({p.edition})</span></span>
              <span className="subtle">{p.price} BYN</span>
            </label>
          )))}
        </div>
        <button className="btn primary">Запланировать</button>
      </form>
    </Modal>
  )
}

function PromoCodeModal({ onClose }: { onClose: () => void }) {
  const qc = useQueryClient()
  const [form, setForm] = useState({ code: '', discountPercent: 10, maxUses: '', expiresAt: '', isActive: true })
  return (
    <Modal open onClose={onClose} title="Новый промокод">
      <form className="stack" onSubmit={async (e) => {
        e.preventDefault()
        try {
          await api.admin.createPromoCode({
            code: form.code, discountPercent: form.discountPercent, isActive: form.isActive,
            maxUses: form.maxUses ? Number(form.maxUses) : null, expiresAt: form.expiresAt ? new Date(form.expiresAt).toISOString() : null,
          })
          qc.invalidateQueries({ queryKey: ['admin', 'promo-codes'] })
          toast.success('Промокод создан')
          onClose()
        } catch (err) { toast.error(errorMessage(err)) }
      }}>
        <Field label="Код"><input className="input mono" required value={form.code} onChange={(e) => setForm({ ...form, code: e.target.value.toUpperCase() })} /></Field>
        <div className="grid-2">
          <Field label="Скидка, %"><input className="input" type="number" min={1} max={100} value={form.discountPercent} onChange={(e) => setForm({ ...form, discountPercent: Number(e.target.value) })} /></Field>
          <Field label="Лимит использований"><input className="input" type="number" min={1} value={form.maxUses} placeholder="∞" onChange={(e) => setForm({ ...form, maxUses: e.target.value })} /></Field>
        </div>
        <Field label="Действует до"><input className="input" type="datetime-local" value={form.expiresAt} onChange={(e) => setForm({ ...form, expiresAt: e.target.value })} /></Field>
        <label className="row between"><span>Активен</span><Switch checked={form.isActive} onChange={(v) => setForm({ ...form, isActive: v })} /></label>
        <button className="btn primary">Создать</button>
      </form>
    </Modal>
  )
}

export function PromotionsPage() {
  const qc = useQueryClient()
  const [modal, setModal] = useState<'promotion' | 'code' | null>(null)
  const [page, setPage] = useState(1)
  const { data: promotions, isLoading } = useQuery({ queryKey: ['admin', 'promotions'], queryFn: api.admin.promotions, refetchInterval: 30_000 })
  const { data: codes } = useQuery({ queryKey: ['admin', 'promo-codes', page], queryFn: () => api.admin.promoCodes(page) })

  return (
    <div className="stack" style={{ '--gap': '22px' } as CSSProperties}>
      <div className="row between wrap">
        <div><h1>Акции и промокоды</h1><p className="muted small">Скидки включаются и выключаются автоматизацией «Планировщик распродаж» точно по расписанию.</p></div>
        <div className="row">
          <button className="btn" onClick={() => setModal('code')}><Plus size={15} /> Промокод</button>
          <button className="btn primary" onClick={() => setModal('promotion')}><Sparkles size={15} /> Акция</button>
        </div>
      </div>

      {isLoading ? <Loader /> : (
        <div className="grid-2">
          {promotions?.map((p) => (
            <div key={p.id} className="glass pad-sm stack" style={{ '--gap': '8px' } as CSSProperties}>
              <div className="row between"><b>{p.name}</b><StatusBadge status={p.status} /></div>
              <span className="display gradient-text" style={{ fontSize: 28 }}>−{p.discountPercent}%</span>
              <span className="subtle">{fmtDate(p.startsAt)} → {fmtDate(p.endsAt)}</span>
              <div className="row wrap" style={{ gap: 4 }}>{p.products.map((x) => <span key={x.productId} className="badge">{x.gameTitle}</span>)}</div>
              {(p.status === 'Scheduled' || p.status === 'Active') && (
                <button className="btn sm ghost danger" style={{ alignSelf: 'flex-start' }} onClick={async () => {
                  await api.admin.cancelPromotion(p.id); qc.invalidateQueries({ queryKey: ['admin', 'promotions'] }); toast.success('Акция отменена, цены восстановлены')
                }}>Отменить</button>
              )}
            </div>
          ))}
        </div>
      )}

      <section className="glass pad table-wrap">
        <h3 style={{ marginBottom: 12 }}>Промокоды</h3>
        <table className="table">
          <thead><tr><th>Код</th><th>Скидка</th><th>Использован</th><th>Действует до</th><th>Источник</th><th>Владелец</th><th>Статус</th><th /></tr></thead>
          <tbody>
            {codes?.items.map((c) => (
              <tr key={c.id}>
                <td className="mono"><b>{c.code}</b></td>
                <td>{c.discountPercent}%</td>
                <td>{c.usedCount}{c.maxUses ? ` / ${c.maxUses}` : ''}</td>
                <td className="subtle">{fmtDate(c.expiresAt)}</td>
                <td>{c.source === 'win-back' ? <span className="badge info">автоматизация</span> : <span className="badge">вручную</span>}</td>
                <td>{c.owner ?? <span className="subtle">все</span>}</td>
                <td>{c.isActive ? <span className="badge success">Активен</span> : <span className="badge">Выключен</span>}</td>
                <td><button className="btn icon sm ghost" onClick={async () => { await api.admin.deletePromoCode(c.id); qc.invalidateQueries({ queryKey: ['admin', 'promo-codes'] }) }}><Trash2 size={14} /></button></td>
              </tr>
            ))}
          </tbody>
        </table>
        {codes && <Pagination page={codes.page} totalPages={codes.totalPages} onChange={setPage} />}
      </section>

      {modal === 'promotion' && <PromotionModal onClose={() => setModal(null)} />}
      {modal === 'code' && <PromoCodeModal onClose={() => setModal(null)} />}
    </div>
  )
}

export function ReportsPage() {
  const today = new Date()
  const [from, setFrom] = useState(new Date(today.getTime() - 30 * 86_400_000).toISOString().slice(0, 10))
  const [to, setTo] = useState(today.toISOString().slice(0, 10))
  const [busy, setBusy] = useState<string | null>(null)

  const run = async (key: string, url: string, params: Record<string, unknown>) => {
    setBusy(key)
    try { await download(url, params) } catch (e) { toast.error(errorMessage(e)) } finally { setBusy(null) }
  }
  const range = { from, to: new Date(new Date(to).getTime() + 86_400_000).toISOString().slice(0, 10) }

  return (
    <div className="stack" style={{ '--gap': '22px' } as CSSProperties}>
      <div><h1>Отчёты и экспорт</h1><p className="muted small">Ежедневный отчёт также формируется и рассылается администраторам автоматически.</p></div>
      <section className="glass pad stack">
        <h3>Отчёт о продажах</h3>
        <div className="row wrap">
          <Field label="С"><input className="input" type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></Field>
          <Field label="По"><input className="input" type="date" value={to} onChange={(e) => setTo(e.target.value)} /></Field>
        </div>
        <div className="row wrap">
          <button className="btn primary" disabled={!!busy} onClick={() => run('pdf', '/admin/reports/sales', { ...range, format: 'Pdf' })}><FileText size={16} /> PDF</button>
          <button className="btn" disabled={!!busy} onClick={() => run('xlsx', '/admin/reports/sales', { ...range, format: 'Excel' })}><FileSpreadsheet size={16} /> Excel</button>
          <button className="btn" disabled={!!busy} onClick={() => run('docx', '/admin/reports/sales', { ...range, format: 'Word' })}><FileType size={16} /> Word</button>
        </div>
      </section>
      <div className="grid-2">
        <section className="glass pad stack">
          <h3>Каталог</h3>
          <p className="subtle">Все игры и издания с ценами, скидками и остатками.</p>
          <div className="row wrap">
            {(['Json', 'Csv', 'Xml'] as const).map((f) => (
              <button key={f} className="btn" disabled={!!busy} onClick={() => run(`games-${f}`, '/admin/export/games', { format: f })}><Download size={15} /> {f.toUpperCase()}</button>
            ))}
          </div>
        </section>
        <section className="glass pad stack">
          <h3>Заказы за период</h3>
          <p className="subtle">Используется выбранный выше период.</p>
          <div className="row wrap">
            {(['Csv', 'Json', 'Xml'] as const).map((f) => (
              <button key={f} className="btn" disabled={!!busy} onClick={() => run(`orders-${f}`, '/admin/export/orders', { ...range, format: f })}><Download size={15} /> {f.toUpperCase()}</button>
            ))}
          </div>
        </section>
      </div>
    </div>
  )
}
