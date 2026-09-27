import { useEffect, useState, type CSSProperties } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query'
import clsx from 'clsx'
import { EyeOff, ImageUp, KeyRound, Plus, Search, Trash2, Upload, Wand2 } from 'lucide-react'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { AdminProduct, GameInput, ProductInput, ProductKind } from '@/api/types'
import { useMoney } from '@/hooks/useStore'
import { Cover } from '@/components/GameCard'
import { Field, Loader, Modal, Pagination, StatusBadge, Switch, fmtDate } from '@/components/ui'

export function GamesPage() {
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const money = useMoney()
  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'games', search, page],
    queryFn: () => api.admin.games(search, page),
    placeholderData: keepPreviousData,
  })

  return (
    <div className="stack">
      <div className="row between wrap">
        <h1>Игры и ключи</h1>
        <div className="row">
          <div className="header-search" style={{ display: 'block', width: 280 }}>
            <Search size={17} />
            <input className="input" placeholder="Поиск" value={search} onChange={(e) => { setSearch(e.target.value); setPage(1) }} />
          </div>
          <Link to="/admin/games/new" className="btn primary"><Plus size={16} /> Добавить игру</Link>
        </div>
      </div>
      {isLoading || !data ? <Loader /> : (
        <div className="glass pad table-wrap">
          <table className="table">
            <thead><tr><th /><th>Игра</th><th>Платформа</th><th>Издания</th><th>Склад</th><th>Продажи</th><th>Статус</th></tr></thead>
            <tbody>
              {data.items.map((g) => {
                const stock = g.products.reduce((s, p) => s + p.available, 0)
                const hidden = g.products.some((p) => p.hiddenByStockMonitor)
                return (
                  <tr key={g.id}>
                    <td><Link to={`/admin/games/${g.id}`} className="thumb-cover" style={{ width: 72, height: 34 }}><Cover src={g.headerImageUrl} title={g.title} /></Link></td>
                    <td><Link to={`/admin/games/${g.id}`}><b>{g.title}</b></Link><div className="subtle">{g.genres.join(', ')}</div></td>
                    <td>{g.platform}</td>
                    <td>{g.products.map((p) => <div key={p.id} className="small">{p.edition}: {money(p.finalPrice)}{p.discountPercent > 0 && <span className="subtle"> (−{p.discountPercent}%)</span>}</div>)}</td>
                    <td><span className={clsx('badge', stock === 0 ? 'danger' : stock <= 5 ? 'warning' : 'success')}>{stock} шт.</span>{hidden && <div className="subtle row" style={{ gap: 4 }}><EyeOff size={12} /> скрыт автоматически</div>}</td>
                    <td>{g.salesCount}</td>
                    <td>{g.isAvailable ? <span className="badge success">В продаже</span> : <span className="badge">Скрыта</span>}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
          <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />
        </div>
      )}
    </div>
  )
}

function KeysModal({ product, onClose }: { product: AdminProduct; onClose: () => void }) {
  const qc = useQueryClient()
  const [page, setPage] = useState(1)
  const [text, setText] = useState('')
  const [count, setCount] = useState(10)
  const isAccount = product.kind === 'Account'
  const { data, refetch } = useQuery({ queryKey: ['admin', 'keys', product.id, page], queryFn: () => api.admin.keys(product.id, page), enabled: !isAccount })

  const done = (r: { added: number; duplicates: number; invalid: number }) => {
    toast.success(`Добавлено: ${r.added}. Дубликатов: ${r.duplicates}. Некорректных: ${r.invalid}`)
    setText('')
    refetch()
    qc.invalidateQueries({ queryKey: ['admin', 'game'] })
  }

  return (
    <Modal open wide onClose={onClose} title={`Склад: ${product.edition}`}>
      <div className="grid-2" style={{ alignItems: 'start' }}>
        <div className="stack">
          <div className="row wrap" style={{ gap: 8 }}>
            <span className="badge success">Свободно: {product.available}</span>
            <span className="badge warning">В резерве: {product.reserved}</span>
            <span className="badge">Продано: {product.sold}</span>
          </div>
          <Field label={isAccount ? 'Аккаунты (login:password[:email[:инфо]] — по одному в строке)' : 'Ключи (по одному в строке, через запятую или ;)'}>
            <textarea className="textarea mono" style={{ minHeight: 180 }} value={text} onChange={(e) => setText(e.target.value)} />
          </Field>
          <button className="btn primary" disabled={!text.trim()} onClick={async () => {
            try { done(isAccount ? await api.admin.importAccounts(product.id, text) : await api.admin.importKeys(product.id, text)) }
            catch (e) { toast.error(errorMessage(e)) }
          }}><Upload size={15} /> Импортировать</button>
          {!isAccount && (
            <>
              <label className="btn">
                <Upload size={15} /> Загрузить .txt / .csv
                <input type="file" accept=".txt,.csv" hidden onChange={async (e) => {
                  const file = e.target.files?.[0]
                  if (file) try { done(await api.admin.importKeysFile(product.id, file)) } catch (err) { toast.error(errorMessage(err)) }
                }} />
              </label>
              <div className="row">
                <input className="input" type="number" min={1} max={500} value={count} onChange={(e) => setCount(Number(e.target.value))} style={{ width: 100 }} />
                <button className="btn grow" onClick={async () => { try { done(await api.admin.generateKeys(product.id, count)) } catch (e) { toast.error(errorMessage(e)) } }}>
                  <Wand2 size={15} /> Сгенерировать демо-ключи
                </button>
              </div>
            </>
          )}
        </div>
        {!isAccount && (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Ключ</th><th>Статус</th><th>Добавлен</th><th /></tr></thead>
              <tbody>
                {data?.items.map((k) => (
                  <tr key={k.id}>
                    <td className="mono small">{k.maskedValue}</td>
                    <td><StatusBadge status={k.status} /></td>
                    <td className="subtle">{fmtDate(k.createdAt, false)}</td>
                    <td>{k.status === 'Available' && (
                      <button className="btn icon sm ghost" onClick={async () => { await api.admin.deleteKey(k.id); refetch() }} aria-label="Удалить"><Trash2 size={14} /></button>
                    )}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {data && <Pagination page={data.page} totalPages={data.totalPages} onChange={setPage} />}
          </div>
        )}
      </div>
    </Modal>
  )
}

const emptyProduct = (): ProductInput => ({ id: null, kind: 'Key', edition: 'Standard', price: 49.9, discountPercent: 0, isAvailable: true, lowStockThreshold: null })

export function GameEditorPage() {
  const { id } = useParams()
  const isNew = !id
  const navigate = useNavigate()
  const qc = useQueryClient()
  const { data: game, isLoading } = useQuery({ queryKey: ['admin', 'game', id], queryFn: () => api.admin.game(Number(id)), enabled: !isNew })
  const { data: genres = [], refetch: refetchGenres } = useQuery({ queryKey: ['genres'], queryFn: api.catalog.genres })
  const { data: platforms = [] } = useQuery({ queryKey: ['platforms'], queryFn: api.catalog.platforms })
  const { data: developers = [], refetch: refetchDevelopers } = useQuery({ queryKey: ['developers'], queryFn: api.catalog.developers })
  const { data: publishers = [], refetch: refetchPublishers } = useQuery({ queryKey: ['publishers'], queryFn: api.catalog.publishers })
  const [keysFor, setKeysFor] = useState<AdminProduct | null>(null)
  const [form, setForm] = useState<GameInput>({
    title: '', shortDescription: '', description: '', releaseDate: null, ageRating: '', systemRequirements: '',
    coverImageUrl: '', headerImageUrl: '', trailerUrl: '', isAvailable: true, developerId: 0, publisherId: 0, platformId: 0,
    genreIds: [], products: [emptyProduct()],
  })

  useEffect(() => {
    if (game) setForm({
      title: game.title, shortDescription: game.shortDescription ?? '', description: game.description ?? '',
      releaseDate: game.releaseDate ?? null, ageRating: game.ageRating ?? '', systemRequirements: game.systemRequirements ?? '',
      coverImageUrl: game.coverImageUrl ?? '', headerImageUrl: game.headerImageUrl ?? '', trailerUrl: game.trailerUrl ?? '',
      isAvailable: game.isAvailable, developerId: game.developerId, publisherId: game.publisherId, platformId: game.platformId,
      genreIds: game.genreIds,
      products: game.products.map((p) => ({ id: p.id, kind: p.kind, edition: p.edition, price: p.price, discountPercent: p.discountPercent, isAvailable: p.isAvailable, lowStockThreshold: p.lowStockThreshold })),
    })
  }, [game])

  if (!isNew && (isLoading || !game)) return <Loader />

  const set = <K extends keyof GameInput>(k: K, v: GameInput[K]) => setForm((f) => ({ ...f, [k]: v }))
  const setProduct = (i: number, patch: Partial<ProductInput>) =>
    set('products', form.products.map((p, idx) => (idx === i ? { ...p, ...patch } : p)))

  const addLookup = async (type: 'genres' | 'developers' | 'publishers') => {
    const name = prompt('Название')
    if (!name) return
    try {
      const created = await api.admin.createLookup(type, name)
      if (type === 'genres') { await refetchGenres(); set('genreIds', [...form.genreIds, created.id]) }
      if (type === 'developers') { await refetchDevelopers(); set('developerId', created.id) }
      if (type === 'publishers') { await refetchPublishers(); set('publisherId', created.id) }
    } catch (e) { toast.error(errorMessage(e)) }
  }

  const save = async () => {
    try {
      const body = { ...form, releaseDate: form.releaseDate || null }
      const saved = isNew ? await api.admin.createGame(body) : await api.admin.updateGame(Number(id), body)
      toast.success('Сохранено')
      qc.invalidateQueries({ queryKey: ['admin'] })
      if (isNew) navigate(`/admin/games/${saved.id}`)
    } catch (e) { toast.error(errorMessage(e)) }
  }

  const upload = async (kind: 'cover' | 'header', file?: File) => {
    if (!file || isNew) return
    try {
      const g = await api.admin.uploadImage(Number(id), kind, file)
      set(kind === 'cover' ? 'coverImageUrl' : 'headerImageUrl', (kind === 'cover' ? g.coverImageUrl : g.headerImageUrl) ?? '')
      toast.success('Изображение загружено')
    } catch (e) { toast.error(errorMessage(e)) }
  }

  return (
    <form className="stack" style={{ '--gap': '20px' } as CSSProperties} onSubmit={(e) => { e.preventDefault(); save() }}>
      <div className="row between wrap">
        <div>
          <Link to="/admin/games" className="subtle">← Все игры</Link>
          <h1>{isNew ? 'Новая игра' : form.title}</h1>
        </div>
        <div className="row">
          {!isNew && <Link to={`/game/${game!.slug}`} className="btn" target="_blank">Открыть на сайте</Link>}
          {!isNew && (
            <button type="button" className="btn danger" onClick={async () => {
              if (!confirm('Удалить игру? Если у неё были продажи, она будет только скрыта.')) return
              await api.admin.deleteGame(Number(id)); qc.invalidateQueries({ queryKey: ['admin'] }); navigate('/admin/games')
            }}><Trash2 size={15} /> Удалить</button>
          )}
          <button className="btn primary">Сохранить</button>
        </div>
      </div>

      <div className="grid-2" style={{ alignItems: 'start' }}>
        <section className="glass pad stack">
          <Field label="Название"><input className="input" required value={form.title} onChange={(e) => set('title', e.target.value)} /></Field>
          <Field label="Короткое описание"><input className="input" maxLength={300} value={form.shortDescription} onChange={(e) => set('shortDescription', e.target.value)} /></Field>
          <Field label="Описание"><textarea className="textarea" style={{ minHeight: 160 }} value={form.description} onChange={(e) => set('description', e.target.value)} /></Field>
          <Field label="Системные требования"><textarea className="textarea" value={form.systemRequirements} onChange={(e) => set('systemRequirements', e.target.value)} /></Field>
          <div className="grid-2">
            <Field label="Дата выхода"><input className="input" type="date" value={form.releaseDate ?? ''} onChange={(e) => set('releaseDate', e.target.value || null)} /></Field>
            <Field label="Возрастной рейтинг"><input className="input" value={form.ageRating} onChange={(e) => set('ageRating', e.target.value)} placeholder="18+" /></Field>
          </div>
          <label className="row between"><span>Показывать в каталоге</span><Switch checked={form.isAvailable} onChange={(v) => set('isAvailable', v)} /></label>
        </section>

        <section className="stack">
          <div className="glass pad stack">
            <div className="grid-2">
              <Field label="Платформа">
                <select className="select" required value={form.platformId || ''} onChange={(e) => set('platformId', Number(e.target.value))}>
                  <option value="">—</option>{platforms.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              </Field>
              <Field label="Разработчик">
                <div className="row">
                  <select className="select" required value={form.developerId || ''} onChange={(e) => set('developerId', Number(e.target.value))}>
                    <option value="">—</option>{developers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                  </select>
                  <button type="button" className="btn icon sm" onClick={() => addLookup('developers')}><Plus size={14} /></button>
                </div>
              </Field>
            </div>
            <Field label="Издатель">
              <div className="row">
                <select className="select" required value={form.publisherId || ''} onChange={(e) => set('publisherId', Number(e.target.value))}>
                  <option value="">—</option>{publishers.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
                <button type="button" className="btn icon sm" onClick={() => addLookup('publishers')}><Plus size={14} /></button>
              </div>
            </Field>
            <div className="stack" style={{ '--gap': '8px' } as CSSProperties}>
              <span className="small muted"><b>Жанры</b></span>
              <div className="row wrap" style={{ gap: 6 }}>
                {genres.map((g) => (
                  <button type="button" key={g.id} className={clsx('chip', form.genreIds.includes(g.id) && 'active')}
                    onClick={() => set('genreIds', form.genreIds.includes(g.id) ? form.genreIds.filter((x) => x !== g.id) : [...form.genreIds, g.id])}>
                    {g.name}
                  </button>
                ))}
                <button type="button" className="chip" onClick={() => addLookup('genres')}><Plus size={13} /></button>
              </div>
            </div>
          </div>

          <div className="glass pad stack">
            <b>Изображения</b>
            {(['cover', 'header'] as const).map((kind) => {
              const key = kind === 'cover' ? 'coverImageUrl' : 'headerImageUrl'
              return (
                <div key={kind} className="row">
                  <div className="thumb-cover" style={{ width: kind === 'cover' ? 48 : 96, height: 64 }}><Cover src={form[key]} title="" /></div>
                  <input className="input grow" placeholder={kind === 'cover' ? 'Обложка 600×900 (URL)' : 'Шапка 460×215 (URL)'} value={form[key]} onChange={(e) => set(key, e.target.value)} />
                  <label className={clsx('btn icon', isNew && 'disabled')} title={isNew ? 'Сначала сохраните игру' : 'Загрузить файл'}>
                    <ImageUp size={16} />
                    <input type="file" accept="image/*" hidden disabled={isNew} onChange={(e) => upload(kind, e.target.files?.[0])} />
                  </label>
                </div>
              )
            })}
          </div>
        </section>
      </div>

      <section className="glass pad stack">
        <div className="row between"><h3>Издания и цены</h3><button type="button" className="btn sm" onClick={() => set('products', [...form.products, emptyProduct()])}><Plus size={14} /> Издание</button></div>
        <div className="table-wrap">
          <table className="table">
            <thead><tr><th>Издание</th><th>Тип</th><th>Цена, BYN</th><th>Скидка, %</th><th>Порог остатка</th><th>В продаже</th><th>Склад</th><th /></tr></thead>
            <tbody>
              {form.products.map((p, i) => {
                const saved = game?.products.find((x) => x.id === p.id)
                return (
                  <tr key={i}>
                    <td><input className="input" value={p.edition} onChange={(e) => setProduct(i, { edition: e.target.value })} /></td>
                    <td>
                      <select className="select" value={p.kind} onChange={(e) => setProduct(i, { kind: e.target.value as ProductKind })}>
                        <option value="Key">Ключ</option><option value="Account">Аккаунт</option><option value="Gift">Подарок</option>
                      </select>
                    </td>
                    <td><input className="input" type="number" step="0.01" min={0} value={p.price} onChange={(e) => setProduct(i, { price: Number(e.target.value) })} style={{ width: 110 }} /></td>
                    <td><input className="input" type="number" min={0} max={100} value={p.discountPercent} onChange={(e) => setProduct(i, { discountPercent: Number(e.target.value) })} style={{ width: 80 }} /></td>
                    <td><input className="input" type="number" min={0} placeholder="по умолч." value={p.lowStockThreshold ?? ''} onChange={(e) => setProduct(i, { lowStockThreshold: e.target.value ? Number(e.target.value) : null })} style={{ width: 110 }} /></td>
                    <td><Switch checked={p.isAvailable} onChange={(v) => setProduct(i, { isAvailable: v })} /></td>
                    <td>
                      {saved ? (
                        <button type="button" className="btn sm" onClick={() => setKeysFor(saved)}>
                          <KeyRound size={14} /> {saved.available}{saved.hiddenByStockMonitor && ' · скрыт'}
                        </button>
                      ) : <span className="subtle">после сохранения</span>}
                    </td>
                    <td>{form.products.length > 1 && <button type="button" className="btn icon sm ghost" onClick={() => set('products', form.products.filter((_, idx) => idx !== i))}><Trash2 size={14} /></button>}</td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </section>

      {keysFor && <KeysModal product={game?.products.find((p) => p.id === keysFor.id) ?? keysFor} onClose={() => setKeysFor(null)} />}
    </form>
  )
}
