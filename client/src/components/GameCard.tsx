import { useState } from 'react'
import { Link } from 'react-router-dom'
import clsx from 'clsx'
import { Heart } from 'lucide-react'
import type { GameCard as Game } from '@/api/types'
import { useMoney, useWishlist } from '@/hooks/useStore'
import { Stars } from './ui'

export function Cover({ src, fallbackSrc, title, className }: { src?: string | null; fallbackSrc?: string | null; title: string; className?: string }) {
  const [stage, setStage] = useState<0 | 1 | 2>(src ? 0 : fallbackSrc ? 1 : 2)
  const current = stage === 0 ? src : stage === 1 ? fallbackSrc : null
  if (!current) return <div className={clsx('cover-fallback', className)}>{title}</div>
  return (
    <img
      className={className}
      src={current}
      alt={title}
      loading="lazy"
      onError={() => setStage((s) => (s === 0 && fallbackSrc ? 1 : 2))}
    />
  )
}

export function GameCard({ game }: { game: Game }) {
  const money = useMoney()
  const wishlist = useWishlist()
  const wished = wishlist.has(game.id)

  return (
    <Link to={`/game/${game.slug}`} className="glass hover game-card fade-in">
      <div className="cover">
        <Cover src={game.coverImageUrl} fallbackSrc={game.headerImageUrl} title={game.title} />
        <div className="tags">
          <span className="badge">{game.platform}</span>
          {game.maxDiscount > 0 && <span className="badge discount">−{game.maxDiscount}%</span>}
        </div>
        <button
          className={clsx('glass-icon-btn wish', wished && 'active')}
          aria-label="В список желаемого"
          onClick={(e) => {
            e.preventDefault()
            wishlist.toggle(game)
          }}
        >
          <Heart size={17} fill={wished ? 'currentColor' : 'none'} />
        </button>
      </div>
      <div className="body">
        <div className="title">{game.title}</div>
        <div className="row subtle" style={{ gap: 6 }}>
          {game.ratingsCount > 0 ? (
            <>
              <Stars value={game.averageRating} size={12} /> <span>{game.averageRating.toFixed(1)}</span>
            </>
          ) : (
            <span>{game.genres.slice(0, 2).join(' · ')}</span>
          )}
        </div>
        <div className="row between" style={{ marginTop: 'auto' }}>
          {game.inStock ? (
            <div className="price">
              <span className="now">{money(game.price)}</span>
              {game.maxDiscount > 0 && game.originalPrice && <span className="old">{money(game.originalPrice)}</span>}
            </div>
          ) : (
            <span className="badge danger">Нет в наличии</span>
          )}
        </div>
      </div>
    </Link>
  )
}

export function GameGrid({ games, loading, count = 8 }: { games?: Game[]; loading?: boolean; count?: number }) {
  if (loading || !games)
    return (
      <div className="grid-cards">
        {Array.from({ length: count }).map((_, i) => (
          <div key={i} className="skeleton" style={{ aspectRatio: '3 / 4.9' }} />
        ))}
      </div>
    )
  return (
    <div className="grid-cards">
      {games.map((g) => (
        <GameCard key={g.id} game={g} />
      ))}
    </div>
  )
}
