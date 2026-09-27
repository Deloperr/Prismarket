import { http } from './client'
import type * as T from './types'

const data = <R,>(p: Promise<{ data: R }>) => p.then((r) => r.data)

export const api = {
  auth: {
    login: (body: { login: string; password: string; twoFactorCode?: string }) => data<T.TokenResponse>(http.post('/auth/login', body)),
    register: (body: { username: string; email: string; password: string }) => data<T.TokenResponse>(http.post('/auth/register', body)),
    logout: () => http.post('/auth/logout'),
    confirmEmail: (token: string) => http.post('/auth/confirm-email', null, { params: { token } }),
    resendConfirmation: () => http.post('/auth/resend-confirmation'),
    forgot: (login: string) => http.post('/auth/forgot-password', { login }),
    reset: (token: string, newPassword: string) => http.post('/auth/reset-password', { token, newPassword }),
  },
  catalog: {
    home: () => data<T.Home>(http.get('/catalog/home')),
    games: (params: Record<string, unknown>) => data<T.Paged<T.GameCard>>(http.get('/catalog/games', { params })),
    game: (slug: string) => data<T.GameDetail>(http.get(`/catalog/games/${slug}`)),
    recommendations: (take = 8) => data<T.GameCard[]>(http.get('/catalog/recommendations', { params: { take } })),
    genres: () => data<T.Lookup[]>(http.get('/catalog/genres')),
    platforms: () => data<T.Lookup[]>(http.get('/catalog/platforms')),
    developers: () => data<T.Lookup[]>(http.get('/catalog/developers')),
    publishers: () => data<T.Lookup[]>(http.get('/catalog/publishers')),
  },
  cart: {
    get: () => data<T.Cart>(http.get('/cart')),
    add: (productId: number, quantity = 1) => data<T.Cart>(http.post('/cart/items', { productId, quantity })),
    setQuantity: (productId: number, quantity: number) => data<T.Cart>(http.put(`/cart/items/${productId}`, { quantity })),
    remove: (productId: number) => data<T.Cart>(http.delete(`/cart/items/${productId}`)),
    merge: (items: { productId: number; quantity: number }[]) => data<T.Cart>(http.post('/cart/merge', items)),
    promo: (code: string) => data<T.PromoPreview>(http.post('/cart/promo', { code })),
  },
  orders: {
    checkout: (paymentMethod: T.PaymentMethod, promoCode?: string) => data<T.CheckoutResult>(http.post('/orders/checkout', { paymentMethod, promoCode })),
    list: (page = 1) => data<T.Paged<T.Order>>(http.get('/orders', { params: { page, pageSize: 10 } })),
    get: (number: string) => data<T.Order>(http.get(`/orders/${number}`)),
    cancel: (number: string) => data<T.Order>(http.post(`/orders/${number}/cancel`)),
    pay: (number: string, method: T.PaymentMethod) => data<T.Payment>(http.post(`/orders/${number}/pay`, { method })),
  },
  payments: {
    methods: () => data<T.PaymentMethodInfo[]>(http.get('/payments/methods')),
    deposit: (amount: number, method: T.PaymentMethod) => data<T.Payment>(http.post('/payments/deposit', { amount, method })),
    get: (id: number) => data<T.Payment>(http.get(`/payments/${id}`)),
    testConfirm: (id: number, success: boolean) => data<T.Payment>(http.post(`/payments/${id}/test-confirm`, { success })),
  },
  profile: {
    me: () => data<T.User>(http.get('/profile')),
    stats: () => data<T.ProfileStats>(http.get('/profile/stats')),
    update: (body: { currentPassword: string; username?: string; email?: string; newPassword?: string }) => data<T.User>(http.put('/profile', body)),
    avatar: (file: File) => {
      const form = new FormData()
      form.append('file', file)
      return data<T.User>(http.post('/profile/avatar', form))
    },
    setup2fa: () => data<T.TwoFactorSetup>(http.post('/profile/2fa/setup')),
    enable2fa: (secret: string, code: string) => http.post('/profile/2fa/enable', { secret, code }),
    disable2fa: (password: string) => http.post('/profile/2fa/disable', { password }),
    library: () => data<T.LibraryItem[]>(http.get('/profile/library')),
    activate: (id: number, activated: boolean) => data<T.LibraryItem>(http.post(`/profile/library/${id}/activated`, { activated })),
    balance: (page = 1) => data<T.Paged<T.BalanceTransaction>>(http.get('/profile/balance', { params: { page, pageSize: 15 } })),
  },
  wishlist: {
    get: () => data<T.WishlistItem[]>(http.get('/wishlist')),
    ids: () => data<number[]>(http.get('/wishlist/ids')),
    add: (gameId: number) => http.post(`/wishlist/${gameId}`),
    remove: (gameId: number) => http.delete(`/wishlist/${gameId}`),
  },
  reviews: {
    forGame: (gameId: number, page = 1) => data<T.Paged<T.Review>>(http.get(`/reviews/game/${gameId}`, { params: { page, pageSize: 6 } })),
    mine: (gameId: number) => data<T.Review | ''>(http.get(`/reviews/game/${gameId}/mine`)).then((r) => r || null),
    upsert: (body: { gameId: number; rating: number; title?: string; comment?: string }) => data<T.Review>(http.post('/reviews', body)),
    remove: (id: number) => http.delete(`/reviews/${id}`),
    site: (take = 12) => data<T.SiteReviewSummary>(http.get('/reviews/site', { params: { take } })),
    mySite: () => data<T.SiteReview | ''>(http.get('/reviews/site/mine')).then((r) => r || null),
    upsertSite: (rating: number, comment: string) => data<T.SiteReview>(http.put('/reviews/site', { rating, comment })),
  },
  support: {
    mine: () => data<T.TicketSummary[]>(http.get('/support/tickets')),
    create: (subject: string, message: string, orderNumber?: string) => data<T.TicketDetail>(http.post('/support/tickets', { subject, message, orderNumber })),
    get: (id: number) => data<T.TicketDetail>(http.get(`/support/tickets/${id}`)),
    post: (id: number, text: string) => data<T.SupportMessage>(http.post(`/support/tickets/${id}/messages`, { text })),
    close: (id: number) => http.post(`/support/tickets/${id}/close`),
  },
  notifications: {
    list: () => data<T.Notification[]>(http.get('/notifications')),
    unread: () => data<number>(http.get('/notifications/unread-count')),
    read: (id?: number) => http.post('/notifications/read', null, { params: { id } }),
  },
  currency: { rates: () => data<T.CurrencyRate[]>(http.get('/currency/rates')) },

  admin: {
    dashboard: () => data<T.Dashboard>(http.get('/admin/dashboard')),
    games: (search: string, page: number) => data<T.Paged<T.AdminGame>>(http.get('/admin/games', { params: { search, page, pageSize: 15 } })),
    game: (id: number) => data<T.AdminGame>(http.get(`/admin/games/${id}`)),
    createGame: (body: T.GameInput) => data<T.AdminGame>(http.post('/admin/games', body)),
    updateGame: (id: number, body: T.GameInput) => data<T.AdminGame>(http.put(`/admin/games/${id}`, body)),
    deleteGame: (id: number) => http.delete(`/admin/games/${id}`),
    uploadImage: (id: number, kind: 'cover' | 'header', file: File) => {
      const form = new FormData()
      form.append('file', file)
      return data<T.AdminGame>(http.post(`/admin/games/${id}/image/${kind}`, form))
    },
    keys: (productId: number, page = 1, status?: string) => data<T.Paged<T.KeyRow>>(http.get(`/admin/games/products/${productId}/keys`, { params: { page, pageSize: 10, status } })),
    importKeys: (productId: number, keys: string) => data<T.ImportResult>(http.post(`/admin/games/products/${productId}/keys`, { keys })),
    importKeysFile: (productId: number, file: File) => {
      const form = new FormData()
      form.append('file', file)
      return data<T.ImportResult>(http.post(`/admin/games/products/${productId}/keys/file`, form))
    },
    generateKeys: (productId: number, count: number) => data<T.ImportResult>(http.post(`/admin/games/products/${productId}/keys/generate`, { count })),
    importAccounts: (productId: number, accounts: string) => data<T.ImportResult>(http.post(`/admin/games/products/${productId}/accounts`, { accounts })),
    deleteKey: (keyId: number) => http.delete(`/admin/games/keys/${keyId}`),
    createLookup: (type: 'genres' | 'platforms' | 'developers' | 'publishers', name: string) => data<T.Lookup>(http.post(`/admin/lookups/${type}`, { name })),

    users: (search: string, page: number) => data<T.Paged<T.AdminUser>>(http.get('/admin/users', { params: { search, page, pageSize: 15 } })),
    user: (id: number) => data<T.AdminUserDetail>(http.get(`/admin/users/${id}`)),
    updateUser: (id: number, body: { role?: T.UserRole; isActive?: boolean; emailConfirmed?: boolean }) => data<T.AdminUser>(http.patch(`/admin/users/${id}`, body)),
    adjustBalance: (id: number, amount: number, reason: string) => data<T.AdminUser>(http.post(`/admin/users/${id}/balance`, { amount, reason })),
    audit: (page: number, entity?: string) => data<T.Paged<T.AuditLog>>(http.get('/admin/audit', { params: { page, pageSize: 20, entity } })),

    orders: (params: { status?: string; search?: string; page: number }) => data<T.Paged<T.Order>>(http.get('/admin/orders', { params: { ...params, pageSize: 15 } })),
    refund: (number: string, reason: string) => data<T.Order>(http.post(`/admin/orders/${number}/refund`, { reason })),
    markPaid: (number: string) => data<T.Order>(http.post(`/admin/orders/${number}/mark-paid`)),

    reviews: (status: string | undefined, page: number) => data<T.Paged<T.Review>>(http.get('/admin/reviews', { params: { status, page, pageSize: 15 } })),
    setReviewStatus: (id: number, status: T.ReviewStatus) => data<T.Review>(http.patch(`/admin/reviews/${id}`, { status })),
    deleteReview: (id: number) => http.delete(`/admin/reviews/${id}`),

    tickets: (status: string | undefined, onlyMine: boolean, page: number) => data<T.Paged<T.TicketSummary>>(http.get('/admin/support/tickets', { params: { status, onlyMine, page, pageSize: 20 } })),
    updateTicket: (id: number, body: { status?: T.TicketStatus; priority?: T.TicketPriority; assignToMe?: boolean }) => data<T.TicketDetail>(http.patch(`/admin/support/tickets/${id}`, body)),

    promotions: () => data<T.Promotion[]>(http.get('/admin/promotions')),
    createPromotion: (body: unknown) => data<T.Promotion>(http.post('/admin/promotions', body)),
    cancelPromotion: (id: number) => http.post(`/admin/promotions/${id}/cancel`),
    promoCodes: (page: number) => data<T.Paged<T.PromoCode>>(http.get('/admin/promo-codes', { params: { page, pageSize: 20 } })),
    createPromoCode: (body: unknown) => data<T.PromoCode>(http.post('/admin/promo-codes', body)),
    deletePromoCode: (id: number) => http.delete(`/admin/promo-codes/${id}`),

    automation: () => data<T.AutomationJob[]>(http.get('/admin/automation')),
    automationOverview: () => data<T.AutomationOverview>(http.get('/admin/automation/overview')),
    updateAutomation: (key: string, body: { isEnabled?: boolean; cron?: string; settings?: Record<string, unknown> }) => data<T.AutomationJob>(http.patch(`/admin/automation/${key}`, body)),
    runAutomation: (key: string) => data<{ jobId: string }>(http.post(`/admin/automation/${key}/run`)),
    automationRuns: (params: { key?: string; status?: string; page: number }) => data<T.Paged<T.AutomationRun>>(http.get('/admin/automation/runs', { params: { ...params, pageSize: 20 } })),
  },
}
