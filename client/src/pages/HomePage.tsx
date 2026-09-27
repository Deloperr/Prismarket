import { useEffect, useState, type CSSProperties } from 'react'
import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { ArrowRight, BadgePercent, Bell, Clock3, Sparkles, Wallet, Zap } from 'lucide-react'
import { api } from '@/api/endpoints'
import { useAuth } from '@/store/auth'
import { useMoney } from '@/hooks/useStore'
import { Cover, GameGrid } from '@/components/GameCard'
import { Stars } from '@/components/ui'

function Countdown({ to }: { to: string }) {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [])
  const diff = Math.max(0, new Date(to).getTime() - now)
  const d = Math.floor(diff / 86_400_000)
  const h = Math.floor((diff / 3_600_000) % 24)
  const m = Math.floor((diff / 60_000) % 60)
  const s = Math.floor((diff / 1000) % 60)
  return <span className="mono">{d > 0 && `${d}д `}{String(h).padStart(2, '0')}:{String(m).padStart(2, '0')}:{String(s).padStart(2, '0')}</span>
}

export default function HomePage() {
  const money = useMoney()
  const user = useAuth((s) => s.user)
  const { data, isLoading } = useQuery({ queryKey: ['home'], queryFn: api.catalog.home })
  const { data: recommended } = useQuery({
    queryKey: ['recommendations', user?.id],
    queryFn: () => api.catalog.recommendations(4),
    enabled: !!user,
  })

  const [slide, setSlide] = useState(0)
  const featured = data?.featured ?? []
  useEffect(() => {
    if (featured.length < 2) return
    const t = setInterval(() => setSlide((s) => (s + 1) % featured.length), 6000)
    return () => clearInterval(t)
  }, [featured.length])
  const hero = featured[slide]

  return (
    <>
      <section className="hero">
        <div className="glass hero-copy">
          <span className="badge accent" style={{ alignSelf: 'flex-start' }}><Sparkles size={13} /> Ключи за секунды</span>
          <h1>
            Игры, которые <span className="gradient-text">преломляют</span> реальность
          </h1>
          <p className="muted" style={{ fontSize: 17, maxWidth: 520 }}>
            Лицензионные ключи Steam, GOG и Xbox с мгновенной автоматической выдачей. Кэшбэк с каждой покупки и уведомления о скидках на игры из вашего вишлиста.
          </p>
          <div className="row wrap">
            <Link to="/catalog" className="btn primary lg">Открыть каталог <ArrowRight size={18} /></Link>
            <Link to="/catalog?onSale=true" className="btn lg">Скидки дня</Link>
          </div>
          {data && (
            <div className="hero-stats">
              <div><b>{data.stats.games}</b><span className="subtle">игр в каталоге</span></div>
              <div><b>{data.stats.keysDelivered.toLocaleString('ru-RU')}</b><span className="subtle">ключей выдано</span></div>
              <div><b>{data.stats.averageRating.toFixed(1)} ★</b><span className="subtle">оценка магазина</span></div>
            </div>
          )}
        </div>

        <Link to={hero ? `/game/${hero.slug}` : '/catalog'} className="glass hero-feature">
          {hero ? (
            <>
              <Cover key={hero.id} src={hero.headerImageUrl} fallbackSrc={hero.coverImageUrl} title={hero.title} className="fade-in" />
              <div className="glass overlay">
                <div className="row between wrap">
                  <div>
                    <span className="subtle">Хит продаж · {hero.platform}</span>
                    <h3 style={{ marginTop: 4 }}>{hero.title}</h3>
                  </div>
                  <div className="price">
                    {hero.maxDiscount > 0 && <span className="badge discount">−{hero.maxDiscount}%</span>}
                    <span className="now">{money(hero.price)}</span>
                  </div>
                </div>
                <div className="row" style={{ marginTop: 12, gap: 6 }}>
                  {featured.map((f, i) => (
                    <span key={f.id} onClick={(e) => { e.preventDefault(); setSlide(i) }}
                      style={{ height: 4, flex: 1, borderRadius: 4, cursor: 'pointer', background: i === slide ? 'var(--grad)' : 'var(--glass-3)' }} />
                  ))}
                </div>
              </div>
            </>
          ) : (
            <div className="skeleton" style={{ position: 'absolute', inset: 0 }} />
          )}
        </Link>
      </section>

      <section className="features">
        {[
          { icon: <Zap size={20} />, title: 'Мгновенная выдача', text: 'Ключ в библиотеке и на почте сразу после оплаты' },
          { icon: <Wallet size={20} />, title: 'Кэшбэк 3%', text: 'Автоматически на баланс с каждого заказа' },
          { icon: <Bell size={20} />, title: 'Следим за ценами', text: 'Сообщим, когда игра из вишлиста подешевеет' },
          { icon: <BadgePercent size={20} />, title: 'Распродажи', text: 'Акции включаются и выключаются сами по расписанию' },
        ].map((f) => (
          <div key={f.title} className="glass pad-sm feature">
            <div className="ico">{f.icon}</div>
            <div>
              <b>{f.title}</b>
              <p className="subtle">{f.text}</p>
            </div>
          </div>
        ))}
      </section>

      {data?.promotions.map((p) => (
        <section key={p.id} className="glass pad" style={{ marginTop: 28, background: 'var(--grad-soft)' }}>
          <div className="row between wrap">
            <div>
              <span className="badge">{new Date(p.startsAt) > new Date() ? 'Скоро' : 'Акция идёт'}</span>
              <h2 style={{ marginTop: 10 }}>{p.name} · −{p.discountPercent}%</h2>
              <p className="muted">{p.description} · {p.productsCount} игр</p>
            </div>
            <div className="stack" style={{ alignItems: 'flex-end', '--gap': '6px' } as CSSProperties}>
              <span className="subtle"><Clock3 size={13} /> {new Date(p.startsAt) > new Date() ? 'До старта' : 'До конца'}</span>
              <span className="display" style={{ fontSize: 26 }}>
                <Countdown to={new Date(p.startsAt) > new Date() ? p.startsAt : p.endsAt} />
              </span>
            </div>
          </div>
        </section>
      ))}

      {user && recommended && recommended.length > 0 && (
        <>
          <div className="section-title"><h2>Рекомендуем вам</h2><Link to="/catalog">Все игры →</Link></div>
          <GameGrid games={recommended} />
        </>
      )}

      <div className="section-title"><h2>Скидки</h2><Link to="/catalog?onSale=true">Все скидки →</Link></div>
      <GameGrid games={data?.onSale} loading={isLoading} count={4} />

      <div className="section-title"><h2>Хиты продаж</h2><Link to="/catalog?sort=popular">Смотреть все →</Link></div>
      <GameGrid games={data?.topSellers} loading={isLoading} />

      <div className="section-title"><h2>Новинки</h2><Link to="/catalog?sort=new">Смотреть все →</Link></div>
      <GameGrid games={data?.newReleases} loading={isLoading} />

      <section className="glass pad" style={{ marginTop: 48 }}>
        <div className="row between wrap">
          <div>
            <h2>Нам доверяют</h2>
            <p className="muted">{data?.stats.customers ?? '…'} покупателей, средняя оценка магазина</p>
          </div>
          <div className="row">
            <span className="display" style={{ fontSize: 40 }}>{data?.stats.averageRating.toFixed(1) ?? '—'}</span>
            <Stars value={data?.stats.averageRating ?? 0} size={20} />
          </div>
          <Link to="/about" className="btn">Читать отзывы</Link>
        </div>
      </section>
    </>
  )
}
