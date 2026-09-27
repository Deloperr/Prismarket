import { useEffect, type ReactNode } from 'react'
import clsx from 'clsx'
import { ChevronLeft, ChevronRight, Star, X } from 'lucide-react'
import type { OrderStatus, PaymentStatus, ReviewStatus, TicketStatus, AutomationRunStatus, PromotionStatus } from '@/api/types'

export function Spinner({ size = 26 }: { size?: number }) {
  return <div className="spinner" style={{ width: size, height: size }} />
}

export function Loader() {
  return (
    <div className="center">
      <Spinner />
    </div>
  )
}

export function Empty({ icon, title, text, action }: { icon: ReactNode; title: string; text?: string; action?: ReactNode }) {
  return (
    <div className="glass empty">
      <div className="ico">{icon}</div>
      <h3>{title}</h3>
      {text && <p className="muted" style={{ maxWidth: 420 }}>{text}</p>}
      {action}
    </div>
  )
}

export function Stars({ value, size = 14, onChange }: { value: number; size?: number; onChange?: (v: number) => void }) {
  return (
    <span className="stars" role={onChange ? 'radiogroup' : undefined}>
      {[1, 2, 3, 4, 5].map((i) => (
        <Star
          key={i}
          size={size}
          className={i <= Math.round(value) ? '' : 'off'}
          fill={i <= Math.round(value) ? 'currentColor' : 'none'}
          style={onChange ? { cursor: 'pointer' } : undefined}
          onClick={onChange ? () => onChange(i) : undefined}
        />
      ))}
    </span>
  )
}

export function Modal({ open, onClose, title, children, wide }: { open: boolean; onClose: () => void; title: string; children: ReactNode; wide?: boolean }) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose()
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [open, onClose])
  if (!open) return null
  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <div className={clsx('glass modal', wide && 'wide')} onMouseDown={(e) => e.stopPropagation()} role="dialog" aria-modal>
        <div className="row between" style={{ marginBottom: 18 }}>
          <h3>{title}</h3>
          <button className="btn icon sm ghost" onClick={onClose} aria-label="Закрыть">
            <X size={18} />
          </button>
        </div>
        {children}
      </div>
    </div>
  )
}

export function Tabs<T extends string>({ value, onChange, items }: { value: T; onChange: (v: T) => void; items: { value: T; label: ReactNode }[] }) {
  return (
    <div className="tabs" role="tablist">
      {items.map((i) => (
        <button key={i.value} role="tab" className={clsx(i.value === value && 'active')} onClick={() => onChange(i.value)}>
          {i.label}
        </button>
      ))}
    </div>
  )
}

export function Pagination({ page, totalPages, onChange }: { page: number; totalPages: number; onChange: (p: number) => void }) {
  if (totalPages <= 1) return null
  const pages = Array.from({ length: totalPages }, (_, i) => i + 1).filter((p) => p === 1 || p === totalPages || Math.abs(p - page) <= 2)
  return (
    <div className="row" style={{ justifyContent: 'center', marginTop: 24 }}>
      <button className="btn icon sm" disabled={page <= 1} onClick={() => onChange(page - 1)} aria-label="Назад">
        <ChevronLeft size={16} />
      </button>
      {pages.map((p, i) => (
        <span key={p} className="row" style={{ gap: 6 }}>
          {i > 0 && pages[i - 1] !== p - 1 && <span className="subtle">…</span>}
          <button className={clsx('btn sm', p === page && 'primary')} onClick={() => onChange(p)}>{p}</button>
        </span>
      ))}
      <button className="btn icon sm" disabled={page >= totalPages} onClick={() => onChange(page + 1)} aria-label="Вперёд">
        <ChevronRight size={16} />
      </button>
    </div>
  )
}

export function Switch({ checked, onChange, disabled }: { checked: boolean; onChange: (v: boolean) => void; disabled?: boolean }) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      className={clsx('switch', checked && 'on')}
      onClick={() => onChange(!checked)}
    />
  )
}

export function Field({ label, children, error }: { label: string; children: ReactNode; error?: string }) {
  return (
    <label className="field">
      <span>{label}</span>
      {children}
      {error && <span className="error-text">{error}</span>}
    </label>
  )
}

type AnyStatus = OrderStatus | PaymentStatus | ReviewStatus | TicketStatus | AutomationRunStatus | PromotionStatus

const statusMap: Record<string, [string, string]> = {
  AwaitingPayment: ['Ожидает оплаты', 'warning'],
  Paid: ['Оплачен', 'info'],
  Completed: ['Выполнен', 'success'],
  Cancelled: ['Отменён', 'danger'],
  Expired: ['Истёк', 'danger'],
  Refunded: ['Возврат', 'info'],
  Pending: ['Ожидает', 'warning'],
  Succeeded: ['Успешно', 'success'],
  Failed: ['Ошибка', 'danger'],
  Published: ['Опубликован', 'success'],
  PendingModeration: ['На модерации', 'warning'],
  Rejected: ['Отклонён', 'danger'],
  Open: ['Открыто', 'info'],
  WaitingForCustomer: ['Ждём ответа клиента', 'warning'],
  WaitingForSupport: ['Ждёт поддержку', 'info'],
  Closed: ['Закрыто', ''],
  Running: ['Выполняется', 'info'],
  Skipped: ['Пропущено', ''],
  Scheduled: ['Запланирована', 'info'],
  Active: ['Идёт', 'success'],
  Finished: ['Завершена', ''],
  Available: ['Свободен', 'success'],
  Reserved: ['В резерве', 'warning'],
  Sold: ['Продан', ''],
}

export function StatusBadge({ status }: { status: AnyStatus | string }) {
  const [label, tone] = statusMap[status] ?? [status, '']
  return <span className={clsx('badge', tone)}>{label}</span>
}

export function Avatar({ name, url, size }: { name: string; url?: string | null; size?: 'lg' }) {
  return (
    <div className={clsx('avatar', size)}>
      {url ? <img src={url} alt={name} style={{ width: '100%', height: '100%', objectFit: 'cover' }} /> : name.slice(0, 1).toUpperCase()}
    </div>
  )
}

export const fmtDate = (value?: string | null, withTime = true) =>
  value
    ? new Intl.DateTimeFormat('ru-RU', withTime ? { dateStyle: 'medium', timeStyle: 'short' } : { dateStyle: 'medium' }).format(new Date(value))
    : '—'

export const relTime = (value: string) => {
  const diff = (Date.now() - new Date(value).getTime()) / 1000
  const rtf = new Intl.RelativeTimeFormat('ru', { numeric: 'auto' })
  if (Math.abs(diff) < 60) return 'только что'
  if (Math.abs(diff) < 3600) return rtf.format(-Math.round(diff / 60), 'minute')
  if (Math.abs(diff) < 86400) return rtf.format(-Math.round(diff / 3600), 'hour')
  return rtf.format(-Math.round(diff / 86400), 'day')
}
