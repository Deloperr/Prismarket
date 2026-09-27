import { useEffect, useState, type CSSProperties } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { BadgeCheck, CalendarDays, Cpu, Heart, KeyRound, MessageSquare, ShieldCheck, ShoppingBag, UserRound, Zap } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { Product } from '@/api/types'
import { useAuth } from '@/store/auth'
import { useCart, useMoney, useWishlist } from '@/hooks/useStore'
import { Avatar, Empty, Loader, Pagination, Stars, fmtDate } from '@/components/ui'

const kindLabel = { Key: 'Ключ активации', Account: 'Аккаунт', Gift: 'Подарок' } as const

function ReviewForm({ gameId }: { gameId: number }) {
  const qc = useQueryClient()
  const { data: mine } = useQuery({ queryKey: ['my-review', gameId], queryFn: () => api.reviews.mine(gameId) })
  const [rating, setRating] = useState(5)
  const [title, setTitle] = useState('')
  const [comment, setComment] = useState('')

  useEffect(() => {
    if (mine) {
      setRating(mine.rating)
      setTitle(mine.title ?? '')
      setComment(mine.comment ?? '')
    }
  }, [mine])

  const save = useMutation({
    mutationFn: () => api.reviews.upsert({ gameId, rating, title, comment }),
    onSuccess: (r) => {
      qc.invalidateQueries({ queryKey: ['reviews', gameId] })
      qc.invalidateQueries({ queryKey: ['my-review', gameId] })
      qc.invalidateQueries({ queryKey: ['game'] })
      if (r.status === 'PendingModeration') toast.info('Отзыв отправлен на модерацию', { description: r.moderationNote ?? undefined })
      else toast.success('Спасибо за отзыв!')
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return (
    <form className="glass pad stack" onSubmit={(e) => { e.preventDefault(); save.mutate() }}>
      <div className="row between">
        <h3>{mine ? 'Ваш отзыв' : 'Оставить отзыв'}</h3>
        <Stars value={rating} size={22} onChange={setRating} />
      </div>
      {mine?.status === 'PendingModeration' && <div className="notice small">Отзыв ожидает проверки модератором: {mine.moderationNote}</div>}
      <input className="input" placeholder="Заголовок" value={title} maxLength={120} onChange={(e) => setTitle(e.target.value)} />
      <textarea className="textarea" placeholder="Что понравилось, что нет?" value={comment} maxLength={3000} onChange={(e) => setComment(e.target.value)} />
      <button className="btn primary" disabled={save.isPending}>{mine ? 'Обновить отзыв' : 'Опубликовать'}</button>
    </form>
  )
}

export default function GamePage() {
  const { slug = '' } = useParams()
  const navigate = useNavigate()
  const money = useMoney()
  const user = useAuth((s) => s.user)
  const { add } = useCart()
  const wishlist = useWishlist()
  const [selected, setSelected] = useState<Product | null>(null)
  const [reviewPage, setReviewPage] = useState(1)

  const { data: game, isLoading, error } = useQuery({ queryKey: ['game', slug], queryFn: () => api.catalog.game(slug) })
  const { data: reviews } = useQuery({
    queryKey: ['reviews', game?.id, reviewPage],
    queryFn: () => api.reviews.forGame(game!.id, reviewPage),
    enabled: !!game,
  })

  useEffect(() => {
    if (game) setSelected(game.products.find((p) => p.isAvailable && p.inStock > 0) ?? game.products[0] ?? null)
  }, [game])

  if (isLoading) return <Loader />
  if (error || !game) return <Empty icon={<KeyRound size={28} />} title="Игра не найдена" action={<Link to="/catalog" className="btn">В каталог</Link>} />

  const canBuy = !!selected && selected.isAvailable && selected.inStock > 0
  const total = game.ratingDistribution.reduce((a, b) => a + b, 0) || 1
  const wished = wishlist.has(game.id)
  const cartGame = { ...game, platform: game.platform.name }

  return (
    <>
      <section className="game-hero glass">
        <div className="bg" style={{ backgroundImage: `url(${game.headerImageUrl ?? game.coverImageUrl})` }} />
        <div className="content">
          <div className="row wrap" style={{ gap: 8, marginBottom: 12 }}>
            <span className="badge">{game.platform.name}</span>
            {game.genres.map((g) => <Link key={g.id} to={`/catalog?genreId=${g.id}`} className="badge">{g.name}</Link>)}
            {game.ageRating && <span className="badge">{game.ageRating}</span>}
            {game.owned && <span className="badge success"><BadgeCheck size={13} /> В вашей библиотеке</span>}
          </div>
          <h1>{game.title}</h1>
          <p className="muted" style={{ maxWidth: 720, marginTop: 8 }}>{game.shortDescription}</p>
          <div className="row wrap" style={{ marginTop: 14, gap: 18 }}>
            <span className="row" style={{ gap: 6 }}><Stars value={game.averageRating} /> <b>{game.averageRating.toFixed(1)}</b> <span className="subtle">({game.ratingsCount})</span></span>
            <span className="row subtle" style={{ gap: 6 }}><CalendarDays size={14} /> {fmtDate(game.releaseDate, false)}</span>
            <span className="row subtle" style={{ gap: 6 }}><UserRound size={14} /> {game.developer.name}</span>
            <span className="row subtle" style={{ gap: 6 }}><ShoppingBag size={14} /> Продано: {game.salesCount}</span>
          </div>
        </div>
      </section>

      <div className="game-layout">
        <div className="stack" style={{ '--gap': '24px' } as CSSProperties}>
          <section className="glass pad">
            <h2 style={{ marginBottom: 12 }}>Об игре</h2>
            <p className="prose">{game.description}</p>
            <div className="grid-2" style={{ marginTop: 20 }}>
              <div><span className="subtle">Разработчик</span><div><b>{game.developer.name}</b></div></div>
              <div><span className="subtle">Издатель</span><div><b>{game.publisher.name}</b></div></div>
            </div>
          </section>

          {game.systemRequirements && (
            <section className="glass pad">
              <h3 className="row" style={{ marginBottom: 12 }}><Cpu size={18} /> Системные требования</h3>
              <p className="prose small">{game.systemRequirements}</p>
            </section>
          )}

          <section className="stack">
            <div className="row between"><h2>Отзывы</h2><span className="subtle"><MessageSquare size={14} /> {game.ratingsCount}</span></div>
            <div className="glass pad grid-2">
              <div className="stack" style={{ justifyContent: 'center', alignItems: 'center' }}>
                <span className="display" style={{ fontSize: 52 }}>{game.averageRating.toFixed(1)}</span>
                <Stars value={game.averageRating} size={20} />
                <span className="subtle">{game.ratingsCount} оценок</span>
              </div>
              <div className="stack rating-bars" style={{ '--gap': '8px' } as CSSProperties}>
                {[5, 4, 3, 2, 1].map((star) => (
                  <div key={star} className="row">
                    <span className="small" style={{ width: 14 }}>{star}</span>
                    <div className="bar grow"><i style={{ width: `${(game.ratingDistribution[star - 1] / total) * 100}%` }} /></div>
                    <span className="subtle" style={{ width: 28, textAlign: 'right' }}>{game.ratingDistribution[star - 1]}</span>
                  </div>
                ))}
              </div>
            </div>

            {user ? <ReviewForm gameId={game.id} /> : (
              <div className="notice"><span>Чтобы оставить отзыв, <Link to="/login" className="gradient-text"><b>войдите</b></Link>.</span></div>
            )}

            {reviews?.items.map((r) => (
              <article key={r.id} className="glass pad-sm stack" style={{ '--gap': '8px' } as CSSProperties}>
                <div className="row">
                  <Avatar name={r.username} url={r.avatarUrl} />
                  <div className="grow">
                    <b>{r.username}</b>{' '}
                    {r.isVerifiedPurchase && <span className="badge success" style={{ marginLeft: 6 }}><ShieldCheck size={12} /> Покупка подтверждена</span>}
                    <div className="subtle">{fmtDate(r.createdAt, false)}</div>
                  </div>
                  <Stars value={r.rating} />
                </div>
                {r.title && <b>{r.title}</b>}
                {r.comment && <p className="muted">{r.comment}</p>}
              </article>
            ))}
            {reviews && <Pagination page={reviews.page} totalPages={reviews.totalPages} onChange={setReviewPage} />}
          </section>
        </div>

        <aside className="glass pad buy-box stack">
          {game.promotion && (
            <div className="notice small">
              <Zap size={16} /> <span><b>{game.promotion.name}</b> — скидка действует до {fmtDate(game.promotion.endsAt)}</span>
            </div>
          )}
          <div className="stack" style={{ '--gap': '8px' } as CSSProperties}>
            {game.products.map((p) => {
              const disabled = !p.isAvailable || p.inStock === 0
              return (
                <div key={p.id} className={clsx('edition', selected?.id === p.id && 'selected', disabled && 'disabled')}
                  onClick={() => !disabled && setSelected(p)}>
                  <div className="grow">
                    <b>{p.edition}</b>
                    <div className="subtle">{kindLabel[p.kind]} · {disabled ? 'нет в наличии' : p.inStock <= 5 ? `осталось ${p.inStock} шт.` : 'в наличии'}</div>
                  </div>
                  <div style={{ textAlign: 'right' }}>
                    <div className="price" style={{ justifyContent: 'flex-end' }}><span className="now" style={{ fontSize: 16 }}>{money(p.finalPrice)}</span></div>
                    {p.discountPercent > 0 && <span className="old subtle" style={{ textDecoration: 'line-through' }}>{money(p.price)}</span>}
                  </div>
                </div>
              )
            })}
          </div>

          {selected && (
            <div className="row between" style={{ marginTop: 6 }}>
              <span className="muted">Итого</span>
              <div className="price">
                {selected.discountPercent > 0 && <span className="badge discount">−{selected.discountPercent}%</span>}
                <span className="now" style={{ fontSize: 26 }}>{money(selected.finalPrice)}</span>
              </div>
            </div>
          )}

          <button className="btn primary lg block" disabled={!canBuy} onClick={async () => { await add(cartGame, selected!); navigate('/cart') }}>
            <Zap size={18} /> Купить сейчас
          </button>
          <div className="row">
            <button className="btn grow" disabled={!canBuy} onClick={() => add(cartGame, selected!)}><ShoppingBag size={16} /> В корзину</button>
            <button className={clsx('btn icon', wished && 'primary')} onClick={() => wishlist.toggle(game)} aria-label="В список желаемого">
              <Heart size={18} fill={wished ? 'currentColor' : 'none'} />
            </button>
          </div>
          {!canBuy && <p className="subtle">Добавьте игру в список желаемого — мы автоматически сообщим, когда ключи появятся или цена снизится.</p>}
          <hr className="divider" />
          <div className="stack small muted" style={{ '--gap': '8px' } as CSSProperties}>
            <span className="row"><Zap size={15} /> Автоматическая выдача сразу после оплаты</span>
            <span className="row"><ShieldCheck size={15} /> Лицензионный ключ, гарантия замены</span>
            <span className="row"><KeyRound size={15} /> Платформа активации: {game.platform.name}</span>
          </div>
          <p className="subtle">Регион: СНГ · Инструкция по активации — в разделе <Link to="/help" className="gradient-text">Помощь</Link></p>
        </aside>
      </div>
    </>
  )
}
