import { useRef, useState, type CSSProperties } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import {
  BadgeCheck, Camera, CheckCircle2, Coins, Gamepad2, Heart, KeyRound, Library, MailWarning, Receipt, ShieldCheck, Wallet,
} from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { PaymentMethod, TwoFactorSetup } from '@/api/types'
import { useAuth } from '@/store/auth'
import { useMoney } from '@/hooks/useStore'
import { Cover } from '@/components/GameCard'
import { Avatar, Empty, Field, Loader, Modal, Pagination, StatusBadge, Tabs, fmtDate } from '@/components/ui'
import { SecretValue } from './OrderPage'

type Tab = 'library' | 'orders' | 'balance' | 'security'

const txLabels: Record<string, string> = {
  Deposit: 'Пополнение', Purchase: 'Покупка', Refund: 'Возврат', Cashback: 'Кэшбэк', Bonus: 'Бонус', AdminAdjustment: 'Корректировка',
}

function LibraryTab() {
  const qc = useQueryClient()
  const { data, isLoading } = useQuery({ queryKey: ['library'], queryFn: api.profile.library })
  const [filter, setFilter] = useState<'all' | 'new'>('all')
  if (isLoading) return <Loader />
  if (!data?.length) return <Empty icon={<Library size={28} />} title="Библиотека пуста" text="Купленные ключи появятся здесь сразу после оплаты." action={<Link to="/catalog" className="btn primary">В каталог</Link>} />
  const items = filter === 'new' ? data.filter((i) => !i.isActivated) : data

  return (
    <div className="stack">
      <Tabs value={filter} onChange={setFilter} items={[{ value: 'all', label: `Все (${data.length})` }, { value: 'new', label: `Не активированы (${data.filter((i) => !i.isActivated).length})` }]} />
      {items.map((i) => (
        <div key={i.id} className="glass pad-sm row wrap fade-in">
          <Link to={`/game/${i.gameSlug}`} className="thumb-cover" style={{ width: 140, height: 64 }}><Cover src={i.imageUrl} title={i.gameTitle} /></Link>
          <div className="grow" style={{ minWidth: 180 }}>
            <b>{i.gameTitle}</b>
            <div className="subtle">{i.edition} · {i.platform} · {fmtDate(i.purchasedAt, false)} · {i.orderNumber}</div>
          </div>
          {i.keyValue && <SecretValue value={i.keyValue} />}
          {i.accountLogin && <div className="stack" style={{ gap: 6 }}><SecretValue value={i.accountLogin} /><SecretValue value={i.accountPassword ?? ''} /></div>}
          <button
            className={clsx('btn sm', i.isActivated && 'ghost')}
            onClick={async () => { await api.profile.activate(i.id, !i.isActivated); qc.invalidateQueries({ queryKey: ['library'] }) }}
          >
            {i.isActivated ? <><CheckCircle2 size={14} /> Активирован</> : 'Отметить активированным'}
          </button>
        </div>
      ))}
    </div>
  )
}

function OrdersTab() {
  const [page, setPage] = useState(1)
  const money = useMoney()
  const { data, isLoading } = useQuery({ queryKey: ['orders', page], queryFn: () => api.orders.list(page) })
  if (isLoading) return <Loader />
  if (!data?.items.length) return <Empty icon={<Receipt size={28} />} title="Заказов пока нет" />
  return (
    <div className="glass pad table-wrap">
      <table className="table">
        <thead><tr><th>Номер</th><th>Дата</th><th>Товары</th><th>Сумма</th><th>Статус</th></tr></thead>
        <tbody>
          {data.items.map((o) => (
            <tr key={o.id}>
              <td><Link to={`/orders/${o.number}`} className="gradient-text"><b>{o.number}</b></Link></td>
              <td className="subtle">{fmtDate(o.createdAt)}</td>
              <td>{o.items.map((i) => i.gameTitle).join(', ')}</td>
              <td><b>{money(o.total)}</b></td>
              <td><StatusBadge status={o.status} /></td>
            </tr>
          ))}
        </tbody>
      </table>
      <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />
    </div>
  )
}

function BalanceTab() {
  const money = useMoney()
  const navigate = useNavigate()
  const user = useAuth((s) => s.user)!
  const [page, setPage] = useState(1)
  const [amount, setAmount] = useState(50)
  const [method, setMethod] = useState<PaymentMethod>('Card')
  const { data } = useQuery({ queryKey: ['balance', page], queryFn: () => api.profile.balance(page) })
  const { data: methods = [] } = useQuery({ queryKey: ['payment-methods'], queryFn: api.payments.methods })

  const deposit = async () => {
    try {
      const p = await api.payments.deposit(amount, method)
      if (p.redirectUrl && p.method === 'Stripe') window.location.href = p.redirectUrl
      else navigate(`/pay/${p.id}`)
    } catch (e) { toast.error(errorMessage(e)) }
  }

  return (
    <div className="grid-2" style={{ alignItems: 'start' }}>
      <div className="glass pad stack">
        <span className="muted">Текущий баланс</span>
        <span className="display" style={{ fontSize: 40 }}>{money(user.balance)}</span>
        <p className="subtle">Кэшбэк за каждую покупку начисляется на баланс автоматически. Баланс можно тратить на любые игры.</p>
        <hr className="divider" />
        <b>Пополнить</b>
        <div className="row wrap" style={{ gap: 6 }}>
          {[20, 50, 100, 200].map((v) => <button key={v} className={clsx('chip', amount === v && 'active')} onClick={() => setAmount(v)}>{v} BYN</button>)}
        </div>
        <input className="input" type="number" min={1} max={5000} value={amount} onChange={(e) => setAmount(Number(e.target.value))} />
        <select className="select" value={method} onChange={(e) => setMethod(e.target.value as PaymentMethod)}>
          {methods.filter((m) => m.supportsDeposit).map((m) => <option key={m.method} value={m.method}>{m.name}</option>)}
        </select>
        <button className="btn primary" onClick={deposit} disabled={amount < 1}>Пополнить на {amount} BYN</button>
      </div>
      <div className="glass pad">
        <h3 style={{ marginBottom: 12 }}>История операций</h3>
        {data?.items.length === 0 && <p className="subtle">Операций пока нет</p>}
        <div className="stack" style={{ '--gap': '4px' } as CSSProperties}>
          {data?.items.map((t) => (
            <div key={t.id} className="row between" style={{ padding: '10px 0', borderBottom: '1px solid var(--glass-border)' }}>
              <div>
                <b className="small">{txLabels[t.type]}</b>
                <div className="subtle">{t.description} · {fmtDate(t.createdAt)}</div>
              </div>
              <b style={{ color: t.amount > 0 ? 'var(--success)' : 'var(--text)' }}>{t.amount > 0 ? '+' : ''}{money(t.amount)}</b>
            </div>
          ))}
        </div>
        {data && <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />}
      </div>
    </div>
  )
}

function SecurityTab() {
  const user = useAuth((s) => s.user)!
  const setUser = useAuth((s) => s.setUser)
  const [setup, setSetup] = useState<TwoFactorSetup | null>(null)
  const [code, setCode] = useState('')
  const [disableOpen, setDisableOpen] = useState(false)
  const [password, setPassword] = useState('')
  const [form, setForm] = useState({ currentPassword: '', username: user.username, email: user.email, newPassword: '' })

  const save = useMutation({
    mutationFn: () => api.profile.update({ ...form, newPassword: form.newPassword || undefined }),
    onSuccess: (u) => { setUser(u); toast.success('Данные сохранены'); setForm((f) => ({ ...f, currentPassword: '', newPassword: '' })) },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <div className="grid-2" style={{ alignItems: 'start' }}>
      <form className="glass pad stack" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
        <h3>Данные аккаунта</h3>
        <Field label="Имя пользователя"><input className="input" value={form.username} onChange={(e) => setForm({ ...form, username: e.target.value })} /></Field>
        <Field label="E-mail"><input className="input" type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} /></Field>
        <Field label="Новый пароль (необязательно)"><input className="input" type="password" minLength={8} value={form.newPassword} onChange={(e) => setForm({ ...form, newPassword: e.target.value })} /></Field>
        <Field label="Текущий пароль для подтверждения"><input className="input" type="password" required value={form.currentPassword} onChange={(e) => setForm({ ...form, currentPassword: e.target.value })} /></Field>
        <button className="btn primary" disabled={save.isPending}>Сохранить</button>
      </form>

      <div className="glass pad stack">
        <h3 className="row"><ShieldCheck size={20} /> Двухфакторная аутентификация</h3>
        <p className="muted small">Защитите аккаунт одноразовыми кодами из Google Authenticator, Microsoft Authenticator или Aegis.</p>
        {user.twoFactorEnabled ? (
          <>
            <span className="badge success" style={{ alignSelf: 'flex-start' }}><BadgeCheck size={13} /> Включена</span>
            <button className="btn danger" onClick={() => setDisableOpen(true)}>Отключить 2FA</button>
          </>
        ) : setup ? (
          <div className="stack" style={{ alignItems: 'center' }}>
            <img src={setup.qrCodeDataUri} alt="QR-код для 2FA" style={{ width: 200, borderRadius: 16, background: '#fff', padding: 8 }} />
            <span className="subtle">или ключ: <span className="mono">{setup.secret}</span></span>
            <input className="input mono" placeholder="Код из приложения" maxLength={6} value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} />
            <button className="btn primary block" onClick={async () => {
              try {
                await api.profile.enable2fa(setup.secret, code)
                setUser({ ...user, twoFactorEnabled: true })
                setSetup(null)
                toast.success('2FA включена')
              } catch (e) { toast.error(errorMessage(e)) }
            }}>Подтвердить</button>
          </div>
        ) : (
          <button className="btn primary" onClick={async () => setSetup(await api.profile.setup2fa())}>Включить 2FA</button>
        )}
      </div>

      <Modal open={disableOpen} onClose={() => setDisableOpen(false)} title="Отключить 2FA">
        <div className="stack">
          <Field label="Пароль"><input className="input" type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
          <button className="btn danger" onClick={async () => {
            try {
              await api.profile.disable2fa(password)
              setUser({ ...user, twoFactorEnabled: false })
              setDisableOpen(false)
              toast.success('2FA отключена')
            } catch (e) { toast.error(errorMessage(e)) }
          }}>Отключить</button>
        </div>
      </Modal>
    </div>
  )
}

export default function ProfilePage() {
  const user = useAuth((s) => s.user)!
  const setUser = useAuth((s) => s.setUser)
  const money = useMoney()
  const [params, setParams] = useSearchParams()
  const tab = (params.get('tab') as Tab) || 'library'
  const fileRef = useRef<HTMLInputElement>(null)
  const { data: stats } = useQuery({ queryKey: ['profile-stats'], queryFn: api.profile.stats })
  useQuery({ queryKey: ['me'], queryFn: async () => { const u = await api.profile.me(); setUser(u); return u } })

  const stat = (icon: JSX.Element, label: string, value: string | number) => (
    <div className="glass pad-sm row">
      <div className="feature"><div className="ico">{icon}</div></div>
      <div><div className="subtle">{label}</div><b style={{ fontSize: 18 }}>{value}</b></div>
    </div>
  )

  return (
    <div className="stack" style={{ '--gap': '24px' } as CSSProperties}>
      <section className="glass pad row wrap" style={{ gap: 20 }}>
        <div style={{ position: 'relative' }}>
          <Avatar name={user.username} url={user.avatarUrl} size="lg" />
          <button className="glass-icon-btn" style={{ position: 'absolute', right: -4, bottom: -4 }} onClick={() => fileRef.current?.click()} aria-label="Сменить аватар">
            <Camera size={15} />
          </button>
          <input ref={fileRef} type="file" accept="image/*" hidden onChange={async (e) => {
            const file = e.target.files?.[0]
            if (!file) return
            try { setUser(await api.profile.avatar(file)); toast.success('Аватар обновлён') } catch (err) { toast.error(errorMessage(err)) }
          }} />
        </div>
        <div className="grow">
          <h2>{user.username}</h2>
          <p className="muted">{user.email} · с нами с {fmtDate(user.createdAt, false)}</p>
        </div>
        <div style={{ textAlign: 'right' }}>
          <span className="subtle">Баланс</span>
          <div className="display" style={{ fontSize: 28 }}>{money(user.balance)}</div>
        </div>
      </section>

      {!user.emailConfirmed && (
        <div className="notice row between wrap">
          <span className="row"><MailWarning size={18} /> Подтвердите e-mail — без этого нельзя оформлять заказы.</span>
          <button className="btn sm" onClick={async () => { await api.auth.resendConfirmation(); toast.success('Письмо отправлено повторно') }}>Отправить письмо ещё раз</button>
        </div>
      )}

      {stats && (
        <div className="grid-4">
          {stat(<Gamepad2 size={20} />, 'Игр в библиотеке', stats.gamesOwned)}
          {stat(<Receipt size={20} />, 'Выполнено заказов', stats.ordersCompleted)}
          {stat(<Coins size={20} />, 'Получено кэшбэка', money(stats.cashbackEarned))}
          {stat(<Heart size={20} />, 'В списке желаемого', stats.wishlistCount)}
        </div>
      )}

      <Tabs value={tab} onChange={(t) => setParams({ tab: t })} items={[
        { value: 'library', label: <><KeyRound size={15} /> Библиотека</> },
        { value: 'orders', label: <><Receipt size={15} /> Заказы</> },
        { value: 'balance', label: <><Wallet size={15} /> Баланс</> },
        { value: 'security', label: <><ShieldCheck size={15} /> Безопасность</> },
      ]} />

      {tab === 'library' && <LibraryTab />}
      {tab === 'orders' && <OrdersTab />}
      {tab === 'balance' && <BalanceTab />}
      {tab === 'security' && <SecurityTab />}
    </div>
  )
}
