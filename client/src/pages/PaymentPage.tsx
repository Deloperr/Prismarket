import { useState, type CSSProperties } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { CreditCard, Landmark, Lock, ShieldAlert } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import { useMoney } from '@/hooks/useStore'
import { Empty, Field, Loader, StatusBadge } from '@/components/ui'

/** Built-in test payment gateway (emulates bank acquiring / ERIP without real money). */
export default function PaymentPage() {
  const { paymentId } = useParams()
  const id = Number(paymentId)
  const money = useMoney()
  const navigate = useNavigate()
  const qc = useQueryClient()
  const [busy, setBusy] = useState(false)
  const [card, setCard] = useState({ number: '4242 4242 4242 4242', exp: '12/29', cvc: '123', name: 'PRISMARKET DEMO' })

  const { data: payment, isLoading, refetch } = useQuery({
    queryKey: ['payment', id],
    queryFn: () => api.payments.get(id),
    refetchInterval: (q) => (q.state.data?.status === 'Pending' ? 4000 : false),
  })

  if (isLoading) return <Loader />
  if (!payment) return <Empty icon={<ShieldAlert size={28} />} title="Платёж не найден" />

  const finish = () => {
    qc.invalidateQueries()
    if (payment.orderNumber) navigate(`/orders/${payment.orderNumber}`)
    else navigate('/profile?tab=balance')
  }

  const confirm = async (success: boolean) => {
    setBusy(true)
    try {
      const result = await api.payments.testConfirm(id, success)
      if (result.status === 'Succeeded') {
        toast.success(payment.purpose === 'Deposit' ? 'Баланс пополнен' : 'Оплата прошла — ключи уже в библиотеке!')
        finish()
      } else {
        toast.error('Платёж отклонён банком (тест)')
        await refetch()
      }
    } catch (e) {
      toast.error(errorMessage(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div style={{ maxWidth: 520, margin: '0 auto' }} className="stack fade-in">
      <div className="glass pad stack">
        <div className="row between">
          <span className="row muted"><Lock size={16} /> Тестовый платёжный шлюз</span>
          <StatusBadge status={payment.status} />
        </div>
        <div>
          <span className="subtle">{payment.purpose === 'Deposit' ? 'Пополнение баланса' : `Оплата заказа ${payment.orderNumber}`}</span>
          <div className="display" style={{ fontSize: 40 }}>{money(payment.amount)}</div>
        </div>

        {payment.status === 'Pending' && payment.method === 'Card' && (
          <form className="stack" onSubmit={(e) => { e.preventDefault(); confirm(true) }}>
            <div className="glass pad-sm stack" style={{ background: 'var(--grad-soft)', '--gap': '12px' } as CSSProperties}>
              <div className="row between"><CreditCard size={22} /><span className="subtle">VISA · TEST</span></div>
              <Field label="Номер карты"><input className="input mono" value={card.number} onChange={(e) => setCard({ ...card, number: e.target.value })} /></Field>
              <div className="grid-2">
                <Field label="Срок"><input className="input mono" value={card.exp} onChange={(e) => setCard({ ...card, exp: e.target.value })} /></Field>
                <Field label="CVC"><input className="input mono" value={card.cvc} onChange={(e) => setCard({ ...card, cvc: e.target.value })} /></Field>
              </div>
              <Field label="Владелец"><input className="input" value={card.name} onChange={(e) => setCard({ ...card, name: e.target.value })} /></Field>
            </div>
            <button className="btn primary lg block" disabled={busy}>Оплатить {money(payment.amount)}</button>
            <button type="button" className="btn ghost" disabled={busy} onClick={() => confirm(false)}>Симулировать отказ банка</button>
          </form>
        )}

        {payment.status === 'Pending' && payment.method === 'Erip' && (
          <div className="stack">
            <div className="notice"><Landmark size={18} /><span>{payment.instructions}</span></div>
            <p className="subtle">Статус обновится автоматически после поступления оплаты. В демо-режиме можно симулировать платёж.</p>
            <button className="btn primary lg block" disabled={busy} onClick={() => confirm(true)}>Я оплатил (симуляция)</button>
          </div>
        )}

        {payment.status === 'Pending' && payment.method === 'Stripe' && (
          <div className="notice">Ожидаем подтверждение от Stripe… Страница обновится автоматически.</div>
        )}

        {payment.status === 'Succeeded' && <button className="btn primary lg block" onClick={finish}>Продолжить</button>}
        {(payment.status === 'Failed' || payment.status === 'Cancelled') && payment.orderNumber && (
          <button className="btn lg block" onClick={() => navigate(`/orders/${payment.orderNumber}`)}>Выбрать другой способ оплаты</button>
        )}
      </div>
      <p className="subtle" style={{ textAlign: 'center' }}>Это демонстрационная страница: реальные деньги не списываются.</p>
    </div>
  )
}
