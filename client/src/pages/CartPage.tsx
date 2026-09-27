import { useState, type CSSProperties } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { CreditCard, Landmark, Minus, Plus, ShoppingBag, Tag, Trash2, Wallet, Globe } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { PaymentMethod, PromoPreview } from '@/api/types'
import { useAuth } from '@/store/auth'
import { useCart, useMoney } from '@/hooks/useStore'
import { Cover } from '@/components/GameCard'
import { Empty, Loader } from '@/components/ui'

const methodIcons: Record<PaymentMethod, JSX.Element> = {
  Balance: <Wallet size={18} />, Card: <CreditCard size={18} />, Erip: <Landmark size={18} />, Stripe: <Globe size={18} />,
}

export default function CartPage() {
  const { cart, isLoading, setQuantity, remove } = useCart()
  const money = useMoney()
  const user = useAuth((s) => s.user)
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [method, setMethod] = useState<PaymentMethod>('Card')
  const [promoInput, setPromoInput] = useState('')
  const [promo, setPromo] = useState<PromoPreview | null>(null)
  const [busy, setBusy] = useState(false)
  const { data: methods = [] } = useQuery({ queryKey: ['payment-methods'], queryFn: api.payments.methods, staleTime: 600_000 })

  if (isLoading) return <Loader />
  if (cart.items.length === 0)
    return <Empty icon={<ShoppingBag size={28} />} title="Корзина пуста" text="Загляните в каталог — там много интересного." action={<Link to="/catalog" className="btn primary">В каталог</Link>} />

  const applyPromo = async () => {
    try {
      setPromo(await api.cart.promo(promoInput))
      toast.success('Промокод применён')
    } catch (e) {
      setPromo(null)
      toast.error(errorMessage(e))
    }
  }

  const total = promo ? cart.total - Math.round(cart.total * promo.discountPercent) / 100 : cart.total
  const notEnoughBalance = method === 'Balance' && (user?.balance ?? 0) < total

  const checkout = async () => {
    setBusy(true)
    try {
      const result = await api.orders.checkout(method, promo?.code)
      qc.invalidateQueries({ queryKey: ['cart'] })
      if (result.payment.status === 'Succeeded') {
        toast.success('Заказ оплачен — ключи уже в библиотеке!')
        qc.invalidateQueries()
        navigate(`/orders/${result.order.number}`)
      } else if (result.payment.redirectUrl?.startsWith('http') && !result.payment.redirectUrl.startsWith(window.location.origin)) {
        window.location.href = result.payment.redirectUrl
      } else {
        navigate(`/pay/${result.payment.id}`)
      }
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <h1 style={{ marginBottom: 24 }}>Корзина</h1>
      <div className="game-layout" style={{ marginTop: 0 }}>
        <div className="stack">
          {cart.items.map((i) => (
            <div key={i.productId} className="glass pad-sm row fade-in" style={{ alignItems: 'stretch' }}>
              <Link to={`/game/${i.gameSlug}`} className="thumb-cover" style={{ width: 150, minHeight: 72 }}>
                <Cover src={i.imageUrl} title={i.gameTitle} />
              </Link>
              <div className="grow stack" style={{ '--gap': '4px' } as CSSProperties}>
                <Link to={`/game/${i.gameSlug}`}><b>{i.gameTitle}</b></Link>
                <span className="subtle">{i.edition} · {i.platform}</span>
                {i.quantity > i.inStock && <span className="badge danger">В наличии только {i.inStock}</span>}
              </div>
              <div className="row">
                <button className="btn icon sm" onClick={() => setQuantity(i.productId, i.quantity - 1)} aria-label="Меньше"><Minus size={14} /></button>
                <b style={{ width: 22, textAlign: 'center' }}>{i.quantity}</b>
                <button className="btn icon sm" disabled={i.quantity >= Math.min(10, i.inStock)} onClick={() => setQuantity(i.productId, i.quantity + 1)} aria-label="Больше"><Plus size={14} /></button>
              </div>
              <div style={{ width: 120, textAlign: 'right', alignSelf: 'center' }}>
                <div className="price" style={{ justifyContent: 'flex-end' }}><span className="now" style={{ fontSize: 17 }}>{money(i.lineTotal)}</span></div>
                {i.discountPercent > 0 && <span className="subtle" style={{ textDecoration: 'line-through' }}>{money(i.price * i.quantity)}</span>}
              </div>
              <button className="btn icon sm ghost" onClick={() => remove(i.productId)} aria-label="Удалить" style={{ alignSelf: 'center' }}><Trash2 size={16} /></button>
            </div>
          ))}
        </div>

        <aside className="glass pad buy-box stack">
          <h3>Оформление</h3>
          <div className="stack small" style={{ '--gap': '8px' } as CSSProperties}>
            <div className="row between"><span className="muted">Товаров</span><span>{cart.totalQuantity}</span></div>
            <div className="row between"><span className="muted">Без скидок</span><span>{money(cart.subtotal)}</span></div>
            {cart.savings > 0 && <div className="row between"><span className="muted">Скидки</span><span style={{ color: 'var(--success)' }}>−{money(cart.savings)}</span></div>}
            {promo && <div className="row between"><span className="muted">Промокод {promo.code}</span><span style={{ color: 'var(--success)' }}>−{promo.discountPercent}%</span></div>}
          </div>
          <div className="row between"><b>К оплате</b><span className="display" style={{ fontSize: 26 }}>{money(total)}</span></div>

          {user ? (
            <>
              <div className="row">
                <div style={{ position: 'relative', flex: 1 }}>
                  <input className="input" placeholder="Промокод" value={promoInput} onChange={(e) => setPromoInput(e.target.value.toUpperCase())} />
                </div>
                <button className="btn" onClick={applyPromo} disabled={!promoInput}><Tag size={15} /></button>
              </div>

              <div className="stack" style={{ '--gap': '8px' } as CSSProperties}>
                <b className="small">Способ оплаты</b>
                {methods.map((m) => (
                  <div key={m.method} className={clsx('edition', method === m.method && 'selected')} onClick={() => setMethod(m.method)}>
                    {methodIcons[m.method]}
                    <div className="grow">
                      <b>{m.name}</b>
                      <div className="subtle">{m.method === 'Balance' ? `Доступно: ${money(user.balance)}` : m.description}</div>
                    </div>
                  </div>
                ))}
              </div>
              {notEnoughBalance && <p className="error-text">Недостаточно средств на балансе — пополните его в профиле или выберите другой способ.</p>}
              {!user.emailConfirmed && <p className="error-text">Подтвердите e-mail, чтобы оформить заказ (ссылка в письме).</p>}
              <button className="btn primary lg block" onClick={checkout} disabled={busy || notEnoughBalance || !user.emailConfirmed}>
                {busy ? 'Оформляем…' : `Оплатить ${money(total)}`}
              </button>
              <p className="subtle">Ключи резервируются за вами на 15 минут. Неоплаченный заказ отменится автоматически.</p>
            </>
          ) : (
            <>
              <p className="muted small">Войдите, чтобы оформить заказ — корзина сохранится.</p>
              <Link to="/login?returnUrl=/cart" className="btn primary lg block">Войти и оформить</Link>
            </>
          )}
        </aside>
      </div>
    </>
  )
}
