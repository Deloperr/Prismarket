import { useCallback, useMemo } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { toast } from 'sonner'
import { api } from '@/api/endpoints'
import { errorMessage } from '@/api/client'
import type { Cart, CartLine, GameCard, GameDetail, Product } from '@/api/types'
import { useAuth } from '@/store/auth'
import { useGuestCart } from '@/store/guestCart'
import { usePrefs, type Currency } from '@/store/prefs'

/* ---------------- Money / currency ---------------- */

const symbols: Record<Currency, string> = { BYN: 'Br', USD: '$', EUR: '€', RUB: '₽' }

export function useMoney() {
  const currency = usePrefs((s) => s.currency)
  const { data: rates } = useQuery({ queryKey: ['rates'], queryFn: api.currency.rates, staleTime: 30 * 60_000 })

  return useCallback(
    (amountByn: number | null | undefined) => {
      if (amountByn == null) return '—'
      const rate = rates?.find((r) => r.code === currency)?.rateToByn ?? 1
      const value = currency === 'BYN' ? amountByn : amountByn / rate
      const formatted = new Intl.NumberFormat('ru-RU', {
        minimumFractionDigits: currency === 'RUB' ? 0 : 2,
        maximumFractionDigits: currency === 'RUB' ? 0 : 2,
      }).format(value)
      return currency === 'USD' || currency === 'EUR' ? `${symbols[currency]}${formatted}` : `${formatted} ${symbols[currency]}`
    },
    [rates, currency],
  )
}

/* ---------------- Cart (server for users, localStorage for guests) ---------------- */

const emptyCart: Cart = { items: [], totalQuantity: 0, subtotal: 0, total: 0, savings: 0 }

export function useCart() {
  const qc = useQueryClient()
  const user = useAuth((s) => s.user)
  const guest = useGuestCart()

  const server = useQuery({ queryKey: ['cart'], queryFn: api.cart.get, enabled: !!user })

  const cart: Cart = useMemo(() => {
    if (user) return server.data ?? emptyCart
    const subtotal = guest.items.reduce((s, i) => s + i.price * i.quantity, 0)
    const total = guest.items.reduce((s, i) => s + i.lineTotal, 0)
    return {
      items: guest.items,
      totalQuantity: guest.items.reduce((s, i) => s + i.quantity, 0),
      subtotal,
      total,
      savings: subtotal - total,
    }
  }, [user, server.data, guest.items])

  const onServer = (fn: () => Promise<Cart>) =>
    fn()
      .then((c) => qc.setQueryData(['cart'], c))
      .catch((e) => {
        toast.error(errorMessage(e))
        throw e
      })

  const add = useCallback(
    async (game: Pick<GameDetail, 'id' | 'slug' | 'title' | 'headerImageUrl' | 'coverImageUrl'> & { platform: string }, product: Product, quantity = 1) => {
      if (user) {
        await onServer(() => api.cart.add(product.id, quantity))
      } else {
        const line: Omit<CartLine, 'quantity' | 'lineTotal'> = {
          productId: product.id, gameId: game.id, gameSlug: game.slug, gameTitle: game.title,
          imageUrl: game.headerImageUrl ?? game.coverImageUrl ?? undefined, platform: game.platform, kind: product.kind,
          edition: product.edition, price: product.price, discountPercent: product.discountPercent,
          finalPrice: product.finalPrice, inStock: product.inStock,
        }
        guest.add(line, quantity)
      }
      toast.success(`«${game.title}» в корзине`)
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [user, guest],
  )

  const setQuantity = (productId: number, quantity: number) =>
    user ? onServer(() => api.cart.setQuantity(productId, quantity)) : guest.setQuantity(productId, quantity)
  const remove = (productId: number) => (user ? onServer(() => api.cart.remove(productId)) : guest.remove(productId))

  return { cart, isLoading: !!user && server.isLoading, add, setQuantity, remove }
}

/** Merge the guest cart into the server cart right after login. */
export async function mergeGuestCart() {
  const { items, clear } = useGuestCart.getState()
  if (items.length === 0) return
  try {
    await api.cart.merge(items.map((i) => ({ productId: i.productId, quantity: i.quantity })))
    clear()
  } catch {
    /* keep the guest cart if merge failed */
  }
}

/* ---------------- Wishlist ---------------- */

export function useWishlist() {
  const qc = useQueryClient()
  const user = useAuth((s) => s.user)
  const { data: ids = [] } = useQuery({ queryKey: ['wishlist-ids'], queryFn: api.wishlist.ids, enabled: !!user })
  const set = useMemo(() => new Set(ids), [ids])

  const toggle = useMutation({
    mutationFn: async (game: Pick<GameCard, 'id' | 'title'>) => {
      if (!user) throw new Error('Войдите, чтобы добавлять игры в список желаемого')
      if (set.has(game.id)) {
        await api.wishlist.remove(game.id)
        return { game, added: false }
      }
      await api.wishlist.add(game.id)
      return { game, added: true }
    },
    onSuccess: ({ game, added }) => {
      qc.setQueryData<number[]>(['wishlist-ids'], (old = []) => (added ? [...old, game.id] : old.filter((x) => x !== game.id)))
      qc.invalidateQueries({ queryKey: ['wishlist'] })
      toast.success(added ? `«${game.title}» в списке желаемого — сообщим о скидке` : 'Удалено из списка желаемого')
    },
    onError: (e) => toast.error(errorMessage(e)),
  })

  return { has: (id: number) => set.has(id), toggle: toggle.mutate, count: ids.length }
}
