import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { CartLine } from '@/api/types'

/** Cart of an anonymous visitor, kept in localStorage and merged into the server cart after login. */
interface GuestCartState {
  items: CartLine[]
  add: (line: Omit<CartLine, 'quantity' | 'lineTotal'>, quantity?: number) => void
  setQuantity: (productId: number, quantity: number) => void
  remove: (productId: number) => void
  clear: () => void
}

const recalc = (l: CartLine): CartLine => ({ ...l, lineTotal: +(l.finalPrice * l.quantity).toFixed(2) })

export const useGuestCart = create<GuestCartState>()(
  persist(
    (set, get) => ({
      items: [],
      add: (line, quantity = 1) => {
        const existing = get().items.find((i) => i.productId === line.productId)
        const items = existing
          ? get().items.map((i) => (i.productId === line.productId ? recalc({ ...i, quantity: Math.min(10, i.quantity + quantity) }) : i))
          : [...get().items, recalc({ ...line, quantity, lineTotal: 0 })]
        set({ items })
      },
      setQuantity: (productId, quantity) =>
        set({
          items: quantity <= 0
            ? get().items.filter((i) => i.productId !== productId)
            : get().items.map((i) => (i.productId === productId ? recalc({ ...i, quantity: Math.min(10, quantity) }) : i)),
        }),
      remove: (productId) => set({ items: get().items.filter((i) => i.productId !== productId) }),
      clear: () => set({ items: [] }),
    }),
    { name: 'pm-guest-cart' },
  ),
)
