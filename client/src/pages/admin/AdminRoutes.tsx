import { NavLink, Route, Routes } from 'react-router-dom'
import {
  BarChart3, Bot, FileBarChart, Gamepad2, History, LifeBuoy, MessageSquareWarning, Percent, Receipt, Users,
} from 'lucide-react'
import DashboardPage from './DashboardPage'
import AutomationPage from './AutomationPage'
import { GamesPage, GameEditorPage } from './GamesPages'
import { OrdersPage, UsersPage, ReviewsModerationPage, AuditPage } from './ManagementPages'
import { SupportAdminPage, SupportTicketAdminPage } from './SupportAdminPages'
import { PromotionsPage, ReportsPage } from './MarketingPages'

const nav = [
  { group: 'Обзор' },
  { to: '/admin', label: 'Дашборд', icon: <BarChart3 size={17} />, end: true },
  { to: '/admin/automation', label: 'Автоматизация', icon: <Bot size={17} /> },
  { group: 'Магазин' },
  { to: '/admin/games', label: 'Игры и ключи', icon: <Gamepad2 size={17} /> },
  { to: '/admin/orders', label: 'Заказы', icon: <Receipt size={17} /> },
  { to: '/admin/promotions', label: 'Акции и промокоды', icon: <Percent size={17} /> },
  { group: 'Пользователи' },
  { to: '/admin/users', label: 'Пользователи', icon: <Users size={17} /> },
  { to: '/admin/support', label: 'Поддержка', icon: <LifeBuoy size={17} /> },
  { to: '/admin/reviews', label: 'Модерация отзывов', icon: <MessageSquareWarning size={17} /> },
  { group: 'Данные' },
  { to: '/admin/reports', label: 'Отчёты и экспорт', icon: <FileBarChart size={17} /> },
  { to: '/admin/audit', label: 'Журнал аудита', icon: <History size={17} /> },
] as const

export default function AdminRoutes() {
  return (
    <div className="admin-layout">
      <nav className="glass admin-nav">
        {nav.map((n, i) =>
          'group' in n ? (
            <div key={i} className="group">{n.group}</div>
          ) : (
            <NavLink key={n.to} to={n.to} end={'end' in n ? n.end : undefined}>
              {n.icon} {n.label}
            </NavLink>
          ),
        )}
      </nav>
      <div style={{ minWidth: 0 }}>
        <Routes>
          <Route index element={<DashboardPage />} />
          <Route path="automation" element={<AutomationPage />} />
          <Route path="games" element={<GamesPage />} />
          <Route path="games/new" element={<GameEditorPage />} />
          <Route path="games/:id" element={<GameEditorPage />} />
          <Route path="orders" element={<OrdersPage />} />
          <Route path="users" element={<UsersPage />} />
          <Route path="reviews" element={<ReviewsModerationPage />} />
          <Route path="support" element={<SupportAdminPage />} />
          <Route path="support/:id" element={<SupportTicketAdminPage />} />
          <Route path="promotions" element={<PromotionsPage />} />
          <Route path="reports" element={<ReportsPage />} />
          <Route path="audit" element={<AuditPage />} />
        </Routes>
      </div>
    </div>
  )
}
