import { useEffect, useRef, useState, type FormEvent, type ReactNode } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { CheckCircle2, KeyRound, MailCheck, ShieldCheck, XCircle } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { TokenResponse } from '@/api/types'
import { useAuth } from '@/store/auth'
import { mergeGuestCart } from '@/hooks/useStore'
import { Logo } from '@/components/Layout'
import { Field, Loader } from '@/components/ui'

function AuthCard({ title, subtitle, children, footer }: { title: string; subtitle?: string; children: ReactNode; footer?: ReactNode }) {
  return (
    <div style={{ maxWidth: 440, margin: '24px auto' }} className="fade-in">
      <div className="glass pad stack" style={{ padding: 32 }}>
        <div style={{ textAlign: 'center' }} className="stack">
          <div style={{ margin: '0 auto' }}><Logo size={40} /></div>
          <h2 style={{ marginTop: 8 }}>{title}</h2>
          {subtitle && <p className="muted small">{subtitle}</p>}
        </div>
        {children}
      </div>
      {footer && <p className="muted small" style={{ textAlign: 'center', marginTop: 16 }}>{footer}</p>}
    </div>
  )
}

function useCompleteLogin() {
  const setSession = useAuth((s) => s.setSession)
  const qc = useQueryClient()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  return async (r: TokenResponse) => {
    setSession(r.accessToken!, r.user!)
    await mergeGuestCart()
    qc.invalidateQueries()
    const returnUrl = params.get('returnUrl')
    navigate(returnUrl && returnUrl.startsWith('/') ? returnUrl : '/')
  }
}

export function LoginPage() {
  const complete = useCompleteLogin()
  const [login, setLogin] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [needCode, setNeedCode] = useState(false)
  const [busy, setBusy] = useState(false)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setBusy(true)
    try {
      const r = await api.auth.login({ login, password, twoFactorCode: needCode ? code : undefined })
      if (r.requiresTwoFactor) {
        setNeedCode(true)
        toast.info('Введите код из приложения-аутентификатора')
      } else {
        toast.success(`С возвращением, ${r.user!.username}!`)
        await complete(r)
      }
    } catch (err) {
      toast.error(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard title="Вход в аккаунт" subtitle="Рады видеть вас снова" footer={<>Нет аккаунта? <Link to="/register" className="gradient-text"><b>Зарегистрироваться</b></Link></>}>
      <form className="stack" onSubmit={submit}>
        <Field label="E-mail или имя пользователя">
          <input className="input" autoComplete="username" value={login} onChange={(e) => setLogin(e.target.value)} required disabled={needCode} />
        </Field>
        <Field label="Пароль">
          <input className="input" type="password" autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} required disabled={needCode} />
        </Field>
        {needCode && (
          <Field label="Код 2FA">
            <input className="input mono" inputMode="numeric" maxLength={6} autoFocus value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))} placeholder="000000" />
          </Field>
        )}
        <button className="btn primary lg block" disabled={busy}>{needCode ? <><ShieldCheck size={18} /> Подтвердить</> : 'Войти'}</button>
        <Link to="/forgot-password" className="subtle" style={{ textAlign: 'center' }}>Забыли пароль?</Link>
        <div className="notice small">
          <KeyRound size={16} />
          <span>Демо-доступ: <b>demo / Demo123!</b> · администратор: <b>admin / Admin123!</b></span>
        </div>
      </form>
    </AuthCard>
  )
}

export function RegisterPage() {
  const complete = useCompleteLogin()
  const [form, setForm] = useState({ username: '', email: '', password: '', confirm: '' })
  const [busy, setBusy] = useState(false)
  const mismatch = form.confirm.length > 0 && form.confirm !== form.password

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (mismatch) return
    setBusy(true)
    try {
      const r = await api.auth.register({ username: form.username, email: form.email, password: form.password })
      toast.success('Аккаунт создан! Проверьте почту, чтобы подтвердить e-mail.')
      await complete(r)
    } catch (err) {
      toast.error(errorMessage(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <AuthCard title="Создать аккаунт" subtitle="Кэшбэк, вишлист и история покупок" footer={<>Уже есть аккаунт? <Link to="/login" className="gradient-text"><b>Войти</b></Link></>}>
      <form className="stack" onSubmit={submit}>
        <Field label="Имя пользователя">
          <input className="input" value={form.username} pattern="[a-zA-Z0-9_.\-]{3,32}" title="3–32 символа: латиница, цифры, _ . -"
            onChange={(e) => setForm({ ...form, username: e.target.value })} required />
        </Field>
        <Field label="E-mail">
          <input className="input" type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} required />
        </Field>
        <Field label="Пароль (минимум 8 символов)">
          <input className="input" type="password" minLength={8} value={form.password} onChange={(e) => setForm({ ...form, password: e.target.value })} required />
        </Field>
        <Field label="Повторите пароль" error={mismatch ? 'Пароли не совпадают' : undefined}>
          <input className="input" type="password" value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} required />
        </Field>
        <p className="subtle">Регистрируясь, вы принимаете условия <Link to="/offer" className="gradient-text">публичной оферты</Link>.</p>
        <button className="btn primary lg block" disabled={busy || mismatch}>Зарегистрироваться</button>
      </form>
    </AuthCard>
  )
}

export function ForgotPasswordPage() {
  const [login, setLogin] = useState('')
  const [sent, setSent] = useState(false)
  return (
    <AuthCard title="Восстановление пароля" subtitle="Отправим ссылку для сброса на почту">
      {sent ? (
        <div className="notice"><MailCheck size={18} /> Если аккаунт существует, письмо уже в пути. Ссылка действует 1 час.</div>
      ) : (
        <form className="stack" onSubmit={async (e) => { e.preventDefault(); await api.auth.forgot(login).catch(() => undefined); setSent(true) }}>
          <Field label="E-mail или имя пользователя"><input className="input" value={login} onChange={(e) => setLogin(e.target.value)} required /></Field>
          <button className="btn primary lg block">Отправить ссылку</button>
        </form>
      )}
    </AuthCard>
  )
}

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const navigate = useNavigate()
  const [password, setPassword] = useState('')
  const token = params.get('token') ?? ''
  return (
    <AuthCard title="Новый пароль">
      <form className="stack" onSubmit={async (e) => {
        e.preventDefault()
        try {
          await api.auth.reset(token, password)
          toast.success('Пароль изменён, войдите с новым паролем')
          navigate('/login')
        } catch (err) { toast.error(errorMessage(err)) }
      }}>
        <Field label="Новый пароль"><input className="input" type="password" minLength={8} value={password} onChange={(e) => setPassword(e.target.value)} required /></Field>
        <button className="btn primary lg block" disabled={!token}>Сохранить</button>
      </form>
    </AuthCard>
  )
}

export function VerifyEmailPage() {
  const [params] = useSearchParams()
  const [state, setState] = useState<'loading' | 'ok' | 'error'>('loading')
  const [message, setMessage] = useState('')
  const user = useAuth((s) => s.user)
  const setUser = useAuth((s) => s.setUser)
  const started = useRef(false)

  useEffect(() => {
    if (started.current) return
    started.current = true
    api.auth.confirmEmail(params.get('token') ?? '')
      .then(() => {
        setState('ok')
        if (user) setUser({ ...user, emailConfirmed: true })
      })
      .catch((e) => { setState('error'); setMessage(errorMessage(e)) })
  }, [params, user, setUser])

  if (state === 'loading') return <Loader />
  return (
    <AuthCard title={state === 'ok' ? 'E-mail подтверждён' : 'Не удалось подтвердить'}>
      <div className="stack" style={{ alignItems: 'center', textAlign: 'center' }}>
        {state === 'ok' ? <CheckCircle2 size={48} color="var(--success)" /> : <XCircle size={48} color="var(--danger)" />}
        <p className="muted">{state === 'ok' ? 'Теперь вы можете совершать покупки.' : message}</p>
        <Link to="/catalog" className="btn primary">За покупками</Link>
      </div>
    </AuthCard>
  )
}
