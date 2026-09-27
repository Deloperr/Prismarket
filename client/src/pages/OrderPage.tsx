import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Copy, PackageX, Timer } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { Order, PaymentMethod } from '@/api/types'
import { useMoney } from '@/hooks/useStore'
import { Cover } from '@/components/GameCard'
import { Empty, Loader, StatusBadge, fmtDate } from '@/components/ui'

export function copy(text: string) {
  navigator.clipboard.writeText(text).then(() => toast.success('Скопировано'))
}

export function SecretValue({ value }: { value: string }) {
  const [shown, setShown] = useState(false)
  return (
    <span className="key-box">
      <span className="grow" style={{ filter: shown ? 'none' : 'blur(5px)', cursor: 'pointer', userSelect: shown ? 'text' : 'none' }} onClick={() => setShown(true)}>
        {value}
      </span>
      <button className="btn icon sm ghost" onClick={() => copy(value)} aria-label="Копировать"><Copy size={14} /></button>
    </span>
  )
}

function ExpiresIn({ order }: { order: Order }) {
  const [now, setNow] = useState(Date.now())
  useEffect(() => {
    const t = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(t)
  }, [])
  const left = Math.max(0, new Date(order.expiresAt).getTime() - now)
  return <span className="mono">{Math.floor(left / 60000)}:{String(Math.floor((left / 1000) % 60)).padStart(2, '0')}</span>
}

export default function OrderPage() {
  const { number = '' } = useParams()
  const money = useMoney()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { data: order, isLoading, refetch } = useQuery({
    queryKey: ['order', number],
    queryFn: () => api.orders.get(number),
    refetchInterval: (q) => (q.state.data?.status === 'AwaitingPayment' ? 5000 : false),
  })

  if (isLoading) return <Loader />
  if (!order) return <Empty icon={<PackageX size={28} />} title="Заказ не найден" />

  const pay = async (method: PaymentMethod) => {
    try {
      const payment = await api.orders.pay(order.number, method)
      if (payment.status === 'Succeeded') { qc.invalidateQueries(); await refetch() }
      else if (payment.redirectUrl && !payment.redirectUrl.startsWith(window.location.origin) && payment.method === 'Stripe') window.location.href = payment.redirectUrl
      else navigate(`/pay/${payment.id}`)
    } catch (e) { toast.error(errorMessage(e)) }
  }

  return (
    <div className="stack fade-in" style={{ maxWidth: 900, margin: '0 auto' }}>
      <div className="row between wrap">
        <div>
          <span className="subtle">Заказ от {fmtDate(order.createdAt)}</span>
          <h1>{order.number}</h1>
        </div>
        <StatusBadge status={order.status} />
      </div>

      {order.status === 'AwaitingPayment' && (
        <div className="glass pad row between wrap" style={{ background: 'var(--grad-soft)' }}>
          <div className="row"><Timer size={20} /><span>Ключи зарезервированы. Оплатите в течение <b><ExpiresIn order={order} /></b>, иначе заказ отменится автоматически.</span></div>
          <div className="row">
            {order.payment?.redirectUrl && order.payment.status === 'Pending' && <button className="btn primary" onClick={() => navigate(`/pay/${order.payment!.id}`)}>Перейти к оплате</button>}
            <button className="btn" onClick={() => pay('Balance')}>С баланса</button>
            <button className="btn ghost danger" onClick={async () => { await api.orders.cancel(order.number); refetch() }}>Отменить</button>
          </div>
        </div>
      )}
      {order.cancellationReason && <div className="notice">{order.cancellationReason}</div>}

      <div className="glass pad stack">
        {order.items.map((i) => (
          <div key={i.id} className="row wrap" style={{ alignItems: 'center' }}>
            <Link to={`/game/${i.gameSlug}`} className="thumb-cover" style={{ width: 120, height: 56 }}>
              <Cover src={i.imageUrl} title={i.gameTitle} />
            </Link>
            <div className="grow">
              <b>{i.gameTitle}</b>
              <div className="subtle">{i.edition} · {money(i.finalPrice)}</div>
            </div>
            {i.keyValue && <SecretValue value={i.keyValue} />}
            {i.accountLogin && (
              <div className="stack" style={{ gap: 6 }}>
                <SecretValue value={i.accountLogin} />
                <SecretValue value={i.accountPassword ?? ''} />
              </div>
            )}
          </div>
        ))}
        <hr className="divider" />
        <div className="row between"><span className="muted">Сумма</span><span>{money(order.subtotal)}</span></div>
        {order.discountAmount > 0 && <div className="row between"><span className="muted">Промокод {order.promoCode}</span><span style={{ color: 'var(--success)' }}>−{money(order.discountAmount)}</span></div>}
        <div className="row between"><b>Итого</b><span className="display" style={{ fontSize: 22 }}>{money(order.total)}</span></div>
      </div>
      {order.status === 'Completed' && (
        <div className="row wrap">
          <Link to="/profile?tab=library" className="btn primary">Открыть библиотеку</Link>
          <Link to="/help" className="btn">Как активировать ключ</Link>
          <Link to={`/support?order=${order.number}`} className="btn ghost">Проблема с заказом</Link>
        </div>
      )}
    </div>
  )
}
