import { create } from 'zustand'
import type { User } from '@/api/types'

interface AuthState {
  accessToken: string | null
  user: User | null
  initialized: boolean
  setSession: (token: string, user: User) => void
  setUser: (user: User) => void
  clear: () => void
  markInitialized: () => void
}

/** Access token lives only in memory; the refresh token is an http-only cookie. */
export const useAuth = create<AuthState>((set) => ({
  accessToken: null,
  user: null,
  initialized: false,
  setSession: (accessToken, user) => set({ accessToken, user }),
  setUser: (user) => set({ user }),
  clear: () => set({ accessToken: null, user: null }),
  markInitialized: () => set({ initialized: true }),
}))

export const useIsAdmin = () => useAuth((s) => s.user?.role === 'Admin')
