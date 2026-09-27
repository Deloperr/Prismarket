import { useEffect, useState, type CSSProperties } from 'react'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import clsx from 'clsx'
import { SearchX, SlidersHorizontal } from 'lucide-react'
import { api } from '@/api/endpoints'
import { GameGrid } from '@/components/GameCard'
import { Empty, Pagination, Switch } from '@/components/ui'

const sorts = [
  { value: 'popular', label: 'Популярные' },
  { value: 'new', label: 'Новинки' },
  { value: 'price_asc', label: 'Сначала дешёвые' },
  { value: 'price_desc', label: 'Сначала дорогие' },
  { value: 'discount', label: 'По размеру скидки' },
  { value: 'rating', label: 'По рейтингу' },
  { value: 'title', label: 'По названию' },
]

export default function CatalogPage() {
  const [params, setParams] = useSearchParams()
  const [search, setSearch] = useState(params.get('search') ?? '')
  const [showFilters, setShowFilters] = useState(true)

  const query = {
    search: params.get('search') || undefined,
    genreId: params.get('genreId') || undefined,
    platformId: params.get('platformId') || undefined,
    minPrice: params.get('minPrice') || undefined,
    maxPrice: params.get('maxPrice') || undefined,
    onSale: params.get('onSale') === 'true' || undefined,
    inStock: params.get('inStock') === 'true' || undefined,
    sort: params.get('sort') || 'popular',
    page: Number(params.get('page') ?? 1),
    pageSize: 24,
  }

  useEffect(() => setSearch(params.get('search') ?? ''), [params])

  const update = (patch: Record<string, string | undefined>) => {
    const next = new URLSearchParams(params)
    Object.entries(patch).forEach(([k, v]) => (v ? next.set(k, v) : next.delete(k)))
    if (!('page' in patch)) next.delete('page')
    setParams(next)
  }

  const { data: genres = [] } = useQuery({ queryKey: ['genres'], queryFn: api.catalog.genres, staleTime: 600_000 })
  const { data: platforms = [] } = useQuery({ queryKey: ['platforms'], queryFn: api.catalog.platforms, staleTime: 600_000 })
  const { data, isLoading, isFetching } = useQuery({
    queryKey: ['games', query],
    queryFn: () => api.catalog.games(query),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <div className="row between wrap" style={{ marginBottom: 24 }}>
        <div>
          <h1>Каталог</h1>
          <p className="muted">{data ? `${data.totalCount} игр` : 'Загрузка…'}{isFetching && ' · обновляем…'}</p>
        </div>
        <div className="row">
          <button className="btn" onClick={() => setShowFilters((v) => !v)}><SlidersHorizontal size={16} /> Фильтры</button>
          <select className="select" style={{ width: 220 }} value={query.sort} onChange={(e) => update({ sort: e.target.value })}>
            {sorts.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
        </div>
      </div>

      <div className="catalog-layout" style={showFilters ? undefined : { gridTemplateColumns: '1fr' }}>
        {showFilters && (
          <aside className="glass pad filters stack" style={{ '--gap': '20px' } as CSSProperties}>
            <form onSubmit={(e) => { e.preventDefault(); update({ search: search.trim() || undefined }) }}>
              <input className="input" placeholder="Название, студия…" value={search} onChange={(e) => setSearch(e.target.value)} />
            </form>

            <div className="stack" style={{ '--gap': '10px' } as CSSProperties}>
              <b className="small">Жанр</b>
              <div className="row wrap" style={{ gap: 6 }}>
                <button className={clsx('chip', !query.genreId && 'active')} onClick={() => update({ genreId: undefined })}>Все</button>
                {genres.map((g) => (
                  <button key={g.id} className={clsx('chip', query.genreId === String(g.id) && 'active')} onClick={() => update({ genreId: String(g.id) })}>
                    {g.name}
                  </button>
                ))}
              </div>
            </div>

            <div className="stack" style={{ '--gap': '10px' } as CSSProperties}>
              <b className="small">Платформа</b>
              <select className="select" value={query.platformId ?? ''} onChange={(e) => update({ platformId: e.target.value || undefined })}>
                <option value="">Любая</option>
                {platforms.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </div>

            <div className="stack" style={{ '--gap': '10px' } as CSSProperties}>
              <b className="small">Цена, BYN</b>
              <div className="row">
                <input className="input" type="number" min={0} placeholder="от" defaultValue={query.minPrice}
                  onBlur={(e) => update({ minPrice: e.target.value || undefined })} />
                <input className="input" type="number" min={0} placeholder="до" defaultValue={query.maxPrice}
                  onBlur={(e) => update({ maxPrice: e.target.value || undefined })} />
              </div>
            </div>

            <label className="row between">
              <span>Только со скидкой</span>
              <Switch checked={!!query.onSale} onChange={(v) => update({ onSale: v ? 'true' : undefined })} />
            </label>
            <label className="row between">
              <span>Только в наличии</span>
              <Switch checked={!!query.inStock} onChange={(v) => update({ inStock: v ? 'true' : undefined })} />
            </label>

            <button className="btn ghost" onClick={() => setParams(new URLSearchParams())}>Сбросить фильтры</button>
          </aside>
        )}

        <div>
          {data && data.items.length === 0 ? (
            <Empty icon={<SearchX size={28} />} title="Ничего не нашлось" text="Попробуйте изменить фильтры или поисковый запрос." />
          ) : (
            <GameGrid games={data?.items} loading={isLoading} count={12} />
          )}
          {data && <Pagination page={data.page} totalPages={data.totalPages} onChange={(p) => update({ page: String(p) })} />}
        </div>
      </div>
    </>
  )
}
