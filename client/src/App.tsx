import { lazy, Suspense, useEffect } from 'react'
import { Route, Routes } from 'react-router-dom'
import { Layout, RequireAuth } from '@/components/Layout'
import { Loader } from '@/components/ui'
import { refreshAccessToken } from '@/api/client'
import { useAuth } from '@/store/auth'
import HomePage from '@/pages/HomePage'
import CatalogPage from '@/pages/CatalogPage'
import GamePage from '@/pages/GamePage'
import CartPage from '@/pages/CartPage'
import { LoginPage, RegisterPage, ForgotPasswordPage, ResetPasswordPage, VerifyEmailPage } from '@/pages/AuthPages'
import PaymentPage from '@/pages/PaymentPage'
import ProfilePage from '@/pages/ProfilePage'
import WishlistPage from '@/pages/WishlistPage'
import { SupportPage, TicketPage } from '@/pages/SupportPages'
import { AboutPage, HelpPage, OfferPage, NotFoundPage } from '@/pages/InfoPages'
import OrderPage from '@/pages/OrderPage'

// Admin bundle is loaded only for administrators.
const AdminRoutes = lazy(() => import('@/pages/admin/AdminRoutes'))

export default function App() {
  const markInitialized = useAuth((s) => s.markInitialized)

  // Restore the session from the http-only refresh cookie.
  useEffect(() => {
    refreshAccessToken().finally(markInitialized)
  }, [markInitialized])

  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
        <Route path="catalog" element={<CatalogPage />} />
        <Route path="game/:slug" element={<GamePage />} />
        <Route path="cart" element={<CartPage />} />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />
        <Route path="forgot-password" element={<ForgotPasswordPage />} />
        <Route path="reset-password" element={<ResetPasswordPage />} />
        <Route path="verify-email" element={<VerifyEmailPage />} />
        <Route path="about" element={<AboutPage />} />
        <Route path="help" element={<HelpPage />} />
        <Route path="offer" element={<OfferPage />} />
        <Route path="pay/:paymentId" element={<RequireAuth><PaymentPage /></RequireAuth>} />
        <Route path="orders/:number" element={<RequireAuth><OrderPage /></RequireAuth>} />
        <Route path="profile" element={<RequireAuth><ProfilePage /></RequireAuth>} />
        <Route path="wishlist" element={<RequireAuth><WishlistPage /></RequireAuth>} />
        <Route path="support" element={<RequireAuth><SupportPage /></RequireAuth>} />
        <Route path="support/:id" element={<RequireAuth><TicketPage /></RequireAuth>} />
        <Route
          path="admin/*"
          element={
            <RequireAuth admin>
              <Suspense fallback={<Loader />}>
                <AdminRoutes />
              </Suspense>
            </RequireAuth>
          }
        />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
