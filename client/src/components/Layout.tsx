import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { Link, NavLink, Navigate, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import {
  Bell, Heart, LayoutDashboard, LifeBuoy, LogOut, Menu, Moon, Search, ShoppingBag, Sun, User as UserIcon, Wallet,
} from 'lucide-react'
import { api } from '@/api/endpoints'
import { useAuth } from '@/store/auth'
import { usePrefs, type Currency } from '@/store/prefs'
import { useCart, useMoney } from '@/hooks/useStore'
import { useRealtimeConnection } from '@/hooks/useRealtime'
import { Avatar, Loader, relTime } from './ui'

export function Logo({ size = 30 }: { size?: number }) {
  return (
    <span className="logo">
      <svg width={size} height={size} viewBox="0 0 64 64" aria-hidden>
        <defs>
          <linearGradient id="pm-g" x1="0" y1="0" x2="1" y2="1">
            <stop offset="0" stopColor="#7c8cff" />
            <stop offset=".55" stopColor="#b18cff" />
            <stop offset="1" stopColor="#ffb8a0" />
          </linearGradient>
        </defs>
        <path d="M32 8 56 52H8Z" fill="rgba(124,140,255,.12)" stroke="url(#pm-g)" strokeWidth="4.5" strokeLinejoin="round" />
        <path d="M32 8 38 52" stroke="url(#pm-g)" strokeWidth="3" opacity=".7" />
        <path d="M2 34h18M44 30l18-6M44 36l18 2M44 42l18 10" stroke="url(#pm-g)" strokeWidth="3" strokeLinecap="round" />
      </svg>
      <span>
        Prism<span className="gradient-text">market</span>
      </span>
    </span>
  )
}

function Backdrop() {
  return (
    <div className="backdrop" aria-hidden>
      <div className="grid" />
      <div className="orb o1" />
      <div className="orb o2" />
      <div className="orb o3" />
    </div>
  )
}

function useClickOutside(onOutside: () => void) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    const handler = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && onOutside()
    document.addEventListener('mousedown', handler)
    return () => document.removeEventListener('mousedown', handler)
  }, [onOutside])
  return ref
}

function NotificationBell() {
  const [open, setOpen] = useState(false)
  const qc = useQueryClient()
  const navigate = useNavigate()
  const ref = useClickOutside(() => setOpen(false))
  const { data: items = [] } = useQuery({ queryKey: ['notifications'], queryFn: api.notifications.list, refetchInterval: 120_000 })
  const unread = items.filter((n) => !n.isRead).length

  const openItem = async (id: number, link?: string | null) => {
    await api.notifications.read(id)
    qc.invalidateQueries({ queryKey: ['notifications'] })
    setOpen(false)
    if (link) navigate(link)
  }

  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <button className="btn icon" aria-label="Уведомления" onClick={() => setOpen((v) => !v)}>
        <Bell size={18} />
        {unread > 0 && <span className="counter">{unread}</span>}
      </button>
      {open && (
        <div className="glass dropdown" style={{ width: 360 }}>
          <div className="row between" style={{ padding: '6px 10px 10px' }}>
            <b>Уведомления</b>
            {unread > 0 && (
              <button className="btn sm ghost" onClick={async () => { await api.notifications.read(); qc.invalidateQueries({ queryKey: ['notifications'] }) }}>
                Прочитать все
              </button>
            )}
          </div>
          <div style={{ maxHeight: 380, overflowY: 'auto' }}>
            {items.length === 0 && <p className="subtle" style={{ padding: 12 }}>Пока ничего нового</p>}
            {items.map((n) => (
              <button key={n.id} className="item" onClick={() => openItem(n.id, n.link)} style={{ alignItems: 'flex-start', opacity: n.isRead ? 0.6 : 1 }}>
                <span className={clsx('pulse', n.isRead && 'off')} style={{ marginTop: 7 }} />
                <span className="grow">
                  <b style={{ display: 'block' }}>{n.title}</b>
                  <span className="small muted" style={{ fontWeight: 500 }}>{n.message}</span>
                  <span className="subtle" style={{ display: 'block', fontSize: 11 }}>{relTime(n.createdAt)}</span>
                </span>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  )
}

function UserMenu() {
  const [open, setOpen] = useState(false)
  const user = useAuth((s) => s.user)!
  const clear = useAuth((s) => s.clear)
  const qc = useQueryClient()
  const navigate = useNavigate()
  const money = useMoney()
  const ref = useClickOutside(() => setOpen(false))

  const logout = async () => {
    await api.auth.logout().catch(() => undefined)
    clear()
    qc.clear()
    navigate('/')
  }

  return (
    <div ref={ref} style={{ position: 'relative' }}>
      <button className="btn" style={{ paddingLeft: 5 }} onClick={() => setOpen((v) => !v)}>
        <Avatar name={user.username} url={user.avatarUrl} />
        <span className="small" style={{ maxWidth: 110, overflow: 'hidden', textOverflow: 'ellipsis' }}>{user.username}</span>
      </button>
      {open && (
        <div className="glass dropdown" onClick={() => setOpen(false)}>
          <div className="row" style={{ padding: '8px 12px 12px' }}>
            <Wallet size={16} className="muted" />
            <span className="muted small">Баланс:</span>
            <b>{money(user.balance)}</b>
          </div>
          <Link to="/profile"><UserIcon size={16} /> Профиль и библиотека</Link>
          <Link to="/wishlist"><Heart size={16} /> Список желаемого</Link>
          <Link to="/support"><LifeBuoy size={16} /> Поддержка</Link>
          {user.role === 'Admin' && <Link to="/admin"><LayoutDashboard size={16} /> Админ-панель</Link>}
          <hr className="divider" />
          <button className="item" onClick={logout}><LogOut size={16} /> Выйти</button>
        </div>
      )}
    </div>
  )
}

function Header() {
  const user = useAuth((s) => s.user)
  const { theme, toggleTheme, currency, setCurrency } = usePrefs()
  const { cart } = useCart()
  const navigate = useNavigate()
  const location = useLocation()
  const [query, setQuery] = useState('')
  const [mobileOpen, setMobileOpen] = useState(false)

  useEffect(() => setMobileOpen(false), [location.pathname])

  const links = [
    { to: '/', label: 'Главная', end: true },
    { to: '/catalog', label: 'Каталог' },
    { to: '/catalog?onSale=true', label: 'Скидки' },
    { to: '/about', label: 'О нас' },
    { to: '/help', label: 'Помощь' },
  ]

  return (
    <header className="header">
      <div className="container">
        <button className="btn icon ghost mobile-only" onClick={() => setMobileOpen((v) => !v)} aria-label="Меню">
          <Menu size={20} />
        </button>
        <Link to="/"><Logo /></Link>
        <nav className="nav">
          {links.map((l) => (
            <NavLink key={l.to} to={l.to} end={l.end}>{l.label}</NavLink>
          ))}
        </nav>
        <form
          className="header-search"
          onSubmit={(e) => {
            e.preventDefault()
            navigate(`/catalog?search=${encodeURIComponent(query)}`)
          }}
        >
          <Search size={17} />
          <input className="input" placeholder="Найти игру…" value={query} onChange={(e) => setQuery(e.target.value)} />
        </form>
        <div className="header-actions">
          <select className="select" style={{ width: 86, height: 40, borderRadius: 999 }} value={currency} onChange={(e) => setCurrency(e.target.value as Currency)} aria-label="Валюта">
            {(['BYN', 'USD', 'EUR', 'RUB'] as Currency[]).map((c) => <option key={c}>{c}</option>)}
          </select>
          <button className="btn icon" onClick={toggleTheme} aria-label="Сменить тему">
            {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
          </button>
          {user && <NotificationBell />}
          <Link to="/cart" className="btn icon" style={{ position: 'relative' }} aria-label="Корзина">
            <ShoppingBag size={18} />
            {cart.totalQuantity > 0 && <span className="counter">{cart.totalQuantity}</span>}
          </Link>
          {user ? <UserMenu /> : <Link to="/login" className="btn primary">Войти</Link>}
        </div>
      </div>
      {mobileOpen && (
        <nav className="glass mobile-menu">
          {links.map((l) => <Link key={l.to} to={l.to}>{l.label}</Link>)}
          <Link to="/wishlist">Список желаемого</Link>
        </nav>
      )}
    </header>
  )
}

function Footer() {
  return (
    <footer className="footer">
      <div className="container">
        <div className="footer-grid">
          <div className="stack" style={{ '--gap': '12px' } as CSSProperties}>
            <Logo />
            <p className="muted small" style={{ maxWidth: 320 }}>
              Цифровые ключи для Steam, GOG, Xbox и других платформ. Мгновенная автоматическая выдача после оплаты и кэшбэк с каждой покупки.
            </p>
          </div>
          <div>
            <h4>Магазин</h4>
            <Link to="/catalog">Каталог</Link>
            <Link to="/catalog?onSale=true">Скидки</Link>
            <Link to="/catalog?sort=new">Новинки</Link>
          </div>
          <div>
            <h4>Покупателям</h4>
            <Link to="/help">Как активировать ключ</Link>
            <Link to="/support">Поддержка</Link>
            <Link to="/offer">Публичная оферта</Link>
          </div>
          <div>
            <h4>Prismarket</h4>
            <Link to="/about">О магазине и отзывы</Link>
            <a href="https://github.com/Deloperr" target="_blank" rel="noreferrer">GitHub</a>
          </div>
        </div>
        <p className="subtle" style={{ marginTop: 28 }}>© {new Date().getFullYear()} Prismarket. Учебный pet-проект: оплата работает в тестовом режиме.</p>
      </div>
    </footer>
  )
}

export function Layout() {
  useRealtimeConnection()
  const location = useLocation()
  useEffect(() => window.scrollTo(0, 0), [location.pathname])
  return (
    <>
      <Backdrop />
      <Header />
      <main className="container page">
        <Outlet />
      </main>
      <Footer />
    </>
  )
}

export function RequireAuth({ children, admin }: { children: ReactNode; admin?: boolean }) {
  const { user, initialized } = useAuth()
  const location = useLocation()
  if (!initialized) return <Loader />
  if (!user) return <Navigate to={`/login?returnUrl=${encodeURIComponent(location.pathname + location.search)}`} replace />
  if (admin && user.role !== 'Admin') return <Navigate to="/" replace />
  return <>{children}</>
}
