import { create } from 'zustand'
import { persist } from 'zustand/middleware'

export type Theme = 'dark' | 'light'
export type Currency = 'BYN' | 'USD' | 'EUR' | 'RUB'

interface PrefsState {
  theme: Theme
  currency: Currency
  toggleTheme: () => void
  setCurrency: (c: Currency) => void
}

export const usePrefs = create<PrefsState>()(
  persist(
    (set, get) => ({
      theme: 'dark',
      currency: 'BYN',
      toggleTheme: () => {
        const theme: Theme = get().theme === 'dark' ? 'light' : 'dark'
        document.documentElement.dataset.theme = theme
        set({ theme })
      },
      setCurrency: (currency) => set({ currency }),
    }),
    { name: 'pm-prefs' },
  ),
)
