// DTO contracts mirrored from the ASP.NET Core API (enums are serialized as strings).

export type ProductKind = 'Key' | 'Account' | 'Gift'
export type OrderStatus = 'AwaitingPayment' | 'Paid' | 'Completed' | 'Cancelled' | 'Expired' | 'Refunded'
export type PaymentMethod = 'Balance' | 'Card' | 'Erip' | 'Stripe'
export type PaymentStatus = 'Pending' | 'Succeeded' | 'Failed' | 'Cancelled'
export type PaymentPurpose = 'Order' | 'Deposit'
export type ReviewStatus = 'Published' | 'PendingModeration' | 'Rejected'
export type TicketStatus = 'Open' | 'WaitingForCustomer' | 'WaitingForSupport' | 'Closed'
export type TicketPriority = 'Low' | 'Normal' | 'High'
export type TicketCategory = 'General' | 'Payment' | 'KeyActivation' | 'Refund' | 'Account'
export type StockItemStatus = 'Available' | 'Reserved' | 'Sold'
export type BalanceTransactionType = 'Deposit' | 'Purchase' | 'Refund' | 'Cashback' | 'Bonus' | 'AdminAdjustment'
export type NotificationType = 'System' | 'OrderCompleted' | 'PriceDrop' | 'BackInStock' | 'SupportReply' | 'Promo' | 'LowStock' | 'Cashback'
export type AutomationKind = 'Scheduled' | 'EventDriven'
export type AutomationRunStatus = 'Running' | 'Succeeded' | 'Failed' | 'Skipped'
export type AutomationTrigger = 'Schedule' | 'Manual' | 'Event'
export type PromotionStatus = 'Scheduled' | 'Active' | 'Finished' | 'Cancelled'
export type UserRole = 'Customer' | 'Admin'

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number }

export interface User {
  id: number; username: string; email: string; avatarUrl?: string | null; role: string; balance: number
  emailConfirmed: boolean; twoFactorEnabled: boolean; createdAt: string
}

export interface TokenResponse { requiresTwoFactor: boolean; accessToken?: string; expiresAt?: string; user?: User }

export interface Lookup { id: number; name: string; slug?: string | null }

export interface GameCard {
  id: number; slug: string; title: string; coverImageUrl?: string | null; headerImageUrl?: string | null
  platform: string; genres: string[]; price?: number | null; originalPrice?: number | null; maxDiscount: number
  averageRating: number; ratingsCount: number; inStock: boolean; isFeatured: boolean
}

export interface Product {
  id: number; kind: ProductKind; edition: string; price: number; discountPercent: number; finalPrice: number
  isAvailable: boolean; inStock: number
}

export interface GameDetail {
  id: number; slug: string; title: string; shortDescription?: string; description?: string; releaseDate?: string
  ageRating?: string; systemRequirements?: string; coverImageUrl?: string; headerImageUrl?: string; trailerUrl?: string
  isAvailable: boolean; isFeatured: boolean; developer: Lookup; publisher: Lookup; platform: Lookup; genres: Lookup[]
  products: Product[]; averageRating: number; ratingsCount: number; ratingDistribution: number[]; salesCount: number
  inWishlist: boolean; owned: boolean; promotion?: { id: number; name: string; discountPercent: number; endsAt: string } | null
}

export interface PromotionBanner { id: number; name: string; description?: string; discountPercent: number; startsAt: string; endsAt: string; bannerImageUrl?: string; productsCount: number }

export interface Home {
  featured: GameCard[]; newReleases: GameCard[]; topSellers: GameCard[]; onSale: GameCard[]
  promotions: PromotionBanner[]; stats: { games: number; keysDelivered: number; customers: number; averageRating: number }
}

export interface CartLine {
  productId: number; gameId: number; gameSlug: string; gameTitle: string; imageUrl?: string; platform: string
  kind: ProductKind; edition: string; price: number; discountPercent: number; finalPrice: number; quantity: number
  inStock: number; lineTotal: number
}
export interface Cart { items: CartLine[]; totalQuantity: number; subtotal: number; total: number; savings: number }

export interface Payment {
  id: number; purpose: PaymentPurpose; method: PaymentMethod; status: PaymentStatus; amount: number
  redirectUrl?: string | null; instructions?: string | null; orderNumber?: string | null; createdAt: string; completedAt?: string | null
}
export interface PaymentMethodInfo { method: PaymentMethod; name: string; description: string; supportsDeposit: boolean }

export interface OrderItem {
  id: number; productId: number; gameId: number; gameSlug: string; gameTitle: string; imageUrl?: string; kind: ProductKind
  edition: string; unitPrice: number; discountPercent: number; finalPrice: number
  keyValue?: string | null; accountLogin?: string | null; accountPassword?: string | null
}
export interface Order {
  id: number; number: string; status: OrderStatus; paymentMethod: PaymentMethod; subtotal: number; discountAmount: number
  total: number; promoCode?: string | null; createdAt: string; expiresAt: string; paidAt?: string | null
  completedAt?: string | null; cancellationReason?: string | null; items: OrderItem[]; payment?: Payment | null
  customerName?: string | null
}
export interface CheckoutResult { order: Order; payment: Payment }
export interface PromoPreview { code: string; discountPercent: number; subtotal: number; discount: number; total: number }

export interface LibraryItem {
  id: number; gameId: number; gameSlug: string; gameTitle: string; imageUrl?: string; platform: string; kind: ProductKind
  edition: string; keyValue?: string | null; accountLogin?: string | null; accountPassword?: string | null
  accountEmail?: string | null; purchasedAt: string; isActivated: boolean; activatedAt?: string | null; orderNumber: string
}
export interface BalanceTransaction { id: number; amount: number; balanceAfter: number; type: BalanceTransactionType; description?: string; createdAt: string }
export interface ProfileStats { gamesOwned: number; ordersCompleted: number; totalSpent: number; reviews: number; wishlistCount: number; cashbackEarned: number }
export interface TwoFactorSetup { secret: string; qrCodeDataUri: string; otpAuthUri: string }

export interface WishlistItem { id: number; addedAt: string; priceWhenAdded?: number | null; game: GameCard }

export interface Review {
  id: number; gameId: number; gameTitle: string; userId: number; username: string; avatarUrl?: string | null
  rating: number; title?: string | null; comment?: string | null; isVerifiedPurchase: boolean; status: ReviewStatus
  moderationNote?: string | null; createdAt: string; updatedAt?: string | null
}
export interface SiteReview { id: number; username: string; avatarUrl?: string | null; rating: number; comment?: string | null; createdAt: string }
export interface SiteReviewSummary { average: number; count: number; latest: SiteReview[] }

export interface TicketSummary {
  id: number; subject: string; status: TicketStatus; priority: TicketPriority; category: TicketCategory; username: string
  assignedAdmin?: string | null; orderNumber?: string | null; createdAt: string; lastMessageAt: string; unreadCount: number; lastMessage?: string | null
}
export interface SupportMessage { id: number; ticketId: number; senderId?: number | null; senderName: string; isBot: boolean; isFromStaff: boolean; text: string; createdAt: string }
export interface TicketDetail { ticket: TicketSummary; messages: SupportMessage[] }

export interface Notification { id: number; type: NotificationType; title: string; message: string; link?: string | null; isRead: boolean; createdAt: string }
export interface CurrencyRate { code: string; rateToByn: number; updatedAt: string; source: string }

// ----- admin -----
export interface Dashboard {
  revenueToday: number; revenue7d: number; revenue30d: number; revenuePrev30d: number; ordersToday: number; orders30d: number
  averageCheck30d: number; newUsers30d: number; totalUsers: number; keysInStock: number; openTickets: number; pendingReviews: number
  daily: { day: string; orders: number; items: number; revenue: number }[]
  topGames: { gameId: number; title: string; sold: number; revenue: number }[]
  paymentMethods: { method: string; orders: number; revenue: number }[]
  genres: { genre: string; revenue: number }[]
  lowStock: { productId: number; gameTitle: string; edition: string; available: number; isAvailable: boolean }[]
  recentOrders: { number: string; username: string; total: number; status: string; method: string; createdAt: string }[]
}

export interface AdminProduct {
  id: number; kind: ProductKind; edition: string; price: number; discountPercent: number; finalPrice: number
  isAvailable: boolean; hiddenByStockMonitor: boolean; lowStockThreshold?: number | null; available: number; reserved: number; sold: number
}
export interface AdminGame {
  id: number; title: string; slug: string; shortDescription?: string | null; description?: string | null; releaseDate?: string | null
  ageRating?: string | null; systemRequirements?: string | null; coverImageUrl?: string | null; headerImageUrl?: string | null
  trailerUrl?: string | null; isAvailable: boolean; isFeatured: boolean; developerId: number; developer: string
  publisherId: number; publisher: string; platformId: number; platform: string; genreIds: number[]; genres: string[]
  products: AdminProduct[]; salesCount: number; averageRating: number; createdAt: string
}
export interface ProductInput { id?: number | null; kind: ProductKind; edition: string; price: number; discountPercent: number; isAvailable: boolean; lowStockThreshold?: number | null }
export interface GameInput {
  title: string; shortDescription?: string; description?: string; releaseDate?: string | null; ageRating?: string
  systemRequirements?: string; coverImageUrl?: string; headerImageUrl?: string; trailerUrl?: string; isAvailable: boolean
  developerId: number; publisherId: number; platformId: number; genreIds: number[]; products: ProductInput[]
}
export interface KeyRow { id: number; maskedValue: string; status: StockItemStatus; createdAt: string; soldAt?: string | null }
export interface ImportResult { added: number; duplicates: number; invalid: number }

export interface AdminUser {
  id: number; username: string; email: string; role: UserRole; isActive: boolean; emailConfirmed: boolean; twoFactorEnabled: boolean
  balance: number; createdAt: string; lastLoginAt?: string | null; ordersCount: number; totalSpent: number
}
export interface AdminUserDetail { user: AdminUser; library: LibraryItem[]; orders: Order[]; transactions: BalanceTransaction[] }
export interface AuditLog { id: number; userId?: number | null; username?: string | null; action: string; entityName: string; entityId?: string | null; changes?: string | null; ipAddress?: string | null; createdAt: string }

export interface Promotion {
  id: number; name: string; description?: string | null; discountPercent: number; startsAt: string; endsAt: string
  status: PromotionStatus; bannerImageUrl?: string | null; products: { productId: number; gameTitle: string; edition: string; price: number }[]
}
export interface PromoCode { id: number; code: string; discountPercent: number; maxUses?: number | null; usedCount: number; expiresAt?: string | null; isActive: boolean; owner?: string | null; source?: string | null; createdAt: string }

export interface SettingDefinition { key: string; label: string; type: 'int' | 'decimal' | 'bool' | 'string'; defaultValue: unknown; hint?: string | null }
export interface AutomationJob {
  key: string; name: string; description: string; kind: AutomationKind; cron?: string | null; isEnabled: boolean
  settings: Record<string, unknown>; settingDefinitions: SettingDefinition[]; lastRunAt?: string | null
  lastStatus?: AutomationRunStatus | null; runs24h: number; failed24h: number; items24h: number
}
export interface AutomationRun {
  id: number; jobKey: string; jobName: string; trigger: AutomationTrigger; status: AutomationRunStatus; startedAt: string
  finishedAt?: string | null; durationMs?: number | null; itemsProcessed: number; message?: string | null
}
export interface AutomationOverview { totalJobs: number; enabled: number; runs24h: number; failed24h: number; items24h: number; latestRuns: AutomationRun[] }
