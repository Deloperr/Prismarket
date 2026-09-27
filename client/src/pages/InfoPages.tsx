import { useEffect, useState, type CSSProperties } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Bot, ChevronDown, Compass, Cpu, KeyRound, ShieldCheck, Sparkles, Timer, Wallet, Zap } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import { useAuth } from '@/store/auth'
import { Avatar, Empty, Stars, fmtDate } from '@/components/ui'

export function AboutPage() {
  const user = useAuth((s) => s.user)
  const qc = useQueryClient()
  const { data } = useQuery({ queryKey: ['site-reviews'], queryFn: () => api.reviews.site(12) })
  const { data: mine } = useQuery({ queryKey: ['my-site-review'], queryFn: api.reviews.mySite, enabled: !!user })
  const [rating, setRating] = useState(5)
  const [comment, setComment] = useState('')
  useEffect(() => { if (mine) { setRating(mine.rating); setComment(mine.comment ?? '') } }, [mine])

  const automations = [
    { icon: <Zap size={20} />, title: 'Мгновенная выдача', text: 'Ключи попадают в библиотеку и на почту сразу после оплаты — без участия оператора.' },
    { icon: <Timer size={20} />, title: 'Резерв и автоотмена', text: 'Ключ бронируется на 15 минут; неоплаченный заказ отменяется, ключ возвращается в продажу.' },
    { icon: <Sparkles size={20} />, title: 'Распродажи по расписанию', text: 'Скидки включаются и выключаются точно по времени акции.' },
    { icon: <Wallet size={20} />, title: 'Кэшбэк', text: 'Процент от каждого заказа автоматически возвращается на баланс.' },
    { icon: <Bot size={20} />, title: 'Бот поддержки', text: 'Классифицирует обращения, отвечает на типовые вопросы и назначает ответственного.' },
    { icon: <Cpu size={20} />, title: 'Контроль склада', text: 'Следим за остатками ключей, скрываем закончившиеся товары и сообщаем о поступлении.' },
  ]

  return (
    <div className="stack" style={{ '--gap': '28px' } as CSSProperties}>
      <section className="glass pad" style={{ padding: 40 }}>
        <h1>О <span className="gradient-text">Prismarket</span></h1>
        <p className="muted" style={{ fontSize: 17, maxWidth: 760, marginTop: 12 }}>
          Prismarket — магазин цифровых ключей, в котором рутину делают автоматизации: от выдачи ключей и отмены неоплаченных заказов
          до отчётов о продажах и напоминаний о скидках. Проект разработан на ASP.NET Core, PostgreSQL и React как дипломная работа и pet-проект.
        </p>
      </section>

      <section>
        <h2 style={{ marginBottom: 16 }}>Что работает само</h2>
        <div className="grid-3">
          {automations.map((a) => (
            <div key={a.title} className="glass pad-sm feature">
              <div className="ico">{a.icon}</div>
              <div><b>{a.title}</b><p className="subtle">{a.text}</p></div>
            </div>
          ))}
        </div>
      </section>

      <section className="stack">
        <div className="row between wrap">
          <h2>Отзывы о магазине</h2>
          {data && <span className="row"><span className="display" style={{ fontSize: 28 }}>{data.average.toFixed(1)}</span><Stars value={data.average} size={18} /><span className="subtle">({data.count})</span></span>}
        </div>
        {user && (
          <form className="glass pad stack" onSubmit={async (e) => {
            e.preventDefault()
            try { await api.reviews.upsertSite(rating, comment); toast.success('Спасибо за отзыв!'); qc.invalidateQueries({ queryKey: ['site-reviews'] }) }
            catch (err) { toast.error(errorMessage(err)) }
          }}>
            <div className="row between"><b>{mine ? 'Ваш отзыв' : 'Оцените магазин'}</b><Stars value={rating} size={22} onChange={setRating} /></div>
            <textarea className="textarea" placeholder="Расскажите о своём опыте" value={comment} onChange={(e) => setComment(e.target.value)} />
            <button className="btn primary" style={{ alignSelf: 'flex-start' }}>{mine ? 'Обновить' : 'Отправить'}</button>
          </form>
        )}
        <div className="grid-3">
          {data?.latest.map((r) => (
            <div key={r.id} className="glass pad-sm stack" style={{ '--gap': '8px' } as CSSProperties}>
              <div className="row"><Avatar name={r.username} url={r.avatarUrl} /><div className="grow"><b>{r.username}</b><div className="subtle">{fmtDate(r.createdAt, false)}</div></div><Stars value={r.rating} /></div>
              <p className="muted">{r.comment}</p>
            </div>
          ))}
        </div>
      </section>
    </div>
  )
}

const faq = [
  { q: 'Как активировать ключ Steam?', a: 'Откройте клиент Steam → «Игры» → «Активировать в Steam…», введите ключ из раздела «Профиль → Библиотека» и следуйте инструкциям.' },
  { q: 'Как активировать ключ GOG?', a: 'Перейдите на gog.com/redeem, войдите в аккаунт и введите код. Игра появится в библиотеке GOG Galaxy.' },
  { q: 'Как активировать код Xbox?', a: 'Откройте microsoft.com/redeem или Microsoft Store → «Использовать код» и введите ключ.' },
  { q: 'Когда придёт ключ?', a: 'Сразу после подтверждения оплаты — ключ появляется в библиотеке и отправляется на e-mail. Обычно это занимает несколько секунд.' },
  { q: 'Сколько действует бронь ключа?', a: '15 минут с момента оформления заказа. Если оплата не поступит, заказ отменится автоматически, а ключ вернётся в продажу.' },
  { q: 'Оплатил, но заказ отменился', a: 'Не волнуйтесь: если оплата пришла после отмены заказа, сумма автоматически зачисляется на ваш баланс Prismarket.' },
  { q: 'Что такое кэшбэк?', a: 'С каждого выполненного заказа (кроме оплаченных балансом) на баланс возвращается процент суммы. Балансом можно оплачивать следующие покупки.' },
  { q: 'Ключ не активируется', a: 'Проверьте платформу и регион. Если ошибка сохраняется — создайте обращение в поддержку с номером заказа и скриншотом, мы заменим ключ.' },
]

export function HelpPage() {
  const [open, setOpen] = useState<number | null>(0)
  return (
    <div className="game-layout" style={{ marginTop: 0 }}>
      <div className="stack">
        <h1>Помощь</h1>
        {faq.map((f, i) => (
          <div key={f.q} className="glass pad-sm" style={{ cursor: 'pointer' }} onClick={() => setOpen(open === i ? null : i)}>
            <div className="row between"><b>{f.q}</b><ChevronDown size={18} style={{ transform: open === i ? 'rotate(180deg)' : undefined, transition: 'transform .2s' }} /></div>
            {open === i && <p className="muted fade-in" style={{ marginTop: 10 }}>{f.a}</p>}
          </div>
        ))}
      </div>
      <aside className="glass pad stack buy-box">
        <h3>Не нашли ответ?</h3>
        <p className="muted small">Напишите в поддержку — бот ответит мгновенно, а администратор подключится при необходимости.</p>
        <Link to="/support" className="btn primary">Написать в поддержку</Link>
        <hr className="divider" />
        <span className="row small muted"><KeyRound size={15} /> Ключи — в «Профиль → Библиотека»</span>
        <span className="row small muted"><ShieldCheck size={15} /> Гарантия замены нерабочего ключа</span>
      </aside>
    </div>
  )
}

export function OfferPage() {
  const sections = [
    ['1. Общие положения', 'Настоящая оферта определяет условия продажи цифровых товаров (ключей активации и аккаунтов) через сайт Prismarket. Оформляя заказ, покупатель принимает условия оферты. Проект является учебным: платежи обрабатываются в тестовом режиме.'],
    ['2. Товар', 'Товаром является код активации или данные доступа к аккаунту для указанной платформы. Товар передаётся в электронном виде в личном кабинете и на e-mail покупателя.'],
    ['3. Оплата и резервирование', 'После оформления заказа товар резервируется на 15 минут. Неоплаченный в срок заказ отменяется автоматически. Средства, поступившие после отмены, зачисляются на внутренний баланс.'],
    ['4. Возврат', 'Возврат возможен, если код не был активирован. Возврат осуществляется на внутренний баланс после проверки администратором.'],
    ['5. Персональные данные', 'Мы храним только данные, необходимые для исполнения заказа: имя пользователя, e-mail и историю покупок. Пароли хранятся в виде хэша bcrypt.'],
  ]
  return (
    <div className="glass pad stack" style={{ maxWidth: 860, margin: '0 auto', padding: 36 }}>
      <h1>Публичная оферта</h1>
      {sections.map(([title, text]) => (
        <section key={title}><h3 style={{ marginBottom: 6 }}>{title}</h3><p className="muted">{text}</p></section>
      ))}
    </div>
  )
}

export function NotFoundPage() {
  return <Empty icon={<Compass size={28} />} title="Страница не найдена" text="Возможно, она переехала или никогда не существовала." action={<Link to="/" className="btn primary">На главную</Link>} />
}
