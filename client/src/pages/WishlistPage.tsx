import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { BellRing, Heart } from 'lucide-react'
import { api } from '@/api/endpoints'
import { useMoney } from '@/hooks/useStore'
import { GameCard } from '@/components/GameCard'
import { Empty, Loader } from '@/components/ui'

export default function WishlistPage() {
  const money = useMoney()
  const { data, isLoading } = useQuery({ queryKey: ['wishlist'], queryFn: api.wishlist.get })
  if (isLoading) return <Loader />

  return (
    <>
      <div className="row between wrap" style={{ marginBottom: 20 }}>
        <div>
          <h1>Список желаемого</h1>
          <p className="muted">{data?.length ?? 0} игр</p>
        </div>
        <div className="notice small" style={{ maxWidth: 460 }}>
          <BellRing size={18} />
          <span>Автоматизация следит за ценами каждый час и присылает уведомление и письмо, когда игра дешевеет или снова появляется в наличии.</span>
        </div>
      </div>
      {!data?.length ? (
        <Empty icon={<Heart size={28} />} title="Пока пусто" text="Нажмите на сердечко на карточке игры, чтобы следить за её ценой." action={<Link to="/catalog" className="btn primary">В каталог</Link>} />
      ) : (
        <div className="grid-cards">
          {data.map((w) => (
            <div key={w.id} className="stack" style={{ gap: 6 }}>
              <GameCard game={w.game} />
              {w.priceWhenAdded != null && w.game.price != null && w.game.price < w.priceWhenAdded && (
                <span className="badge success" style={{ alignSelf: 'center' }}>Дешевле на {money(w.priceWhenAdded - w.game.price)}</span>
              )}
            </div>
          ))}
        </div>
      )}
    </>
  )
}
